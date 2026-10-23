using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace SkinForge.UI.Controls;

/// <summary>Global switch for non-essential motion; the setting and the OS "animations off" preference both honour it.</summary>
public static class Motion
{
    public static bool Reduced { get; set; }
    public static bool Enabled => !Reduced && SystemParameters.ClientAreaAnimation;
}

/// <summary>
/// Artwork that fills its box like CSS object-fit: cover, with a focal point so faces are not cropped away.
/// Shows a quiet placeholder while loading, fades the picture in, and degrades to an icon when nothing is available.
/// </summary>
public class RemoteArtwork : FrameworkElement
{
    public static readonly DependencyProperty UrlProperty = DependencyProperty.Register(nameof(Url), typeof(string), typeof(RemoteArtwork),
        new PropertyMetadata("", (d, _) => ((RemoteArtwork)d).Reload()));
    public static readonly DependencyProperty FocusXProperty = DependencyProperty.Register(nameof(FocusX), typeof(double), typeof(RemoteArtwork),
        new FrameworkPropertyMetadata(0.5, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FocusYProperty = DependencyProperty.Register(nameof(FocusY), typeof(double), typeof(RemoteArtwork),
        new FrameworkPropertyMetadata(0.2, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ContainProperty = DependencyProperty.Register(nameof(Contain), typeof(bool), typeof(RemoteArtwork),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ImageOpacityProperty = DependencyProperty.Register(nameof(ImageOpacity), typeof(double), typeof(RemoteArtwork),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FallbackIconProperty = DependencyProperty.Register(nameof(FallbackIcon), typeof(Geometry), typeof(RemoteArtwork),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty KeepPreviousProperty = DependencyProperty.Register(nameof(KeepPrevious), typeof(bool), typeof(RemoteArtwork), new PropertyMetadata(false));
    public static readonly DependencyProperty IsLoadedImageProperty = DependencyProperty.Register(nameof(HasImage), typeof(bool), typeof(RemoteArtwork), new PropertyMetadata(false));

    private BitmapSource? _source;
    private BitmapSource? _previous;
    private bool _failed;
    private CancellationTokenSource? _cts;
    private static readonly Brush Placeholder = Freeze(new LinearGradientBrush(Color.FromRgb(0x14, 0x1A, 0x22), Color.FromRgb(0x1B, 0x23, 0x2D), 45));

    public string Url { get => (string)GetValue(UrlProperty); set => SetValue(UrlProperty, value); }
    public double FocusX { get => (double)GetValue(FocusXProperty); set => SetValue(FocusXProperty, value); }
    public double FocusY { get => (double)GetValue(FocusYProperty); set => SetValue(FocusYProperty, value); }
    public bool Contain { get => (bool)GetValue(ContainProperty); set => SetValue(ContainProperty, value); }
    public double ImageOpacity { get => (double)GetValue(ImageOpacityProperty); set => SetValue(ImageOpacityProperty, value); }
    public Geometry? FallbackIcon { get => (Geometry?)GetValue(FallbackIconProperty); set => SetValue(FallbackIconProperty, value); }
    /// <summary>Keep showing the old picture until the new one is ready, then cross-fade (carousels).</summary>
    public bool KeepPrevious { get => (bool)GetValue(KeepPreviousProperty); set => SetValue(KeepPreviousProperty, value); }
    public bool HasImage => (bool)GetValue(IsLoadedImageProperty);

    public RemoteArtwork()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        Loaded += (_, _) => Reload();
        Unloaded += (_, _) => { Cancel(); _source = null; SetValue(IsLoadedImageProperty, false); };
        SizeChanged += (_, e) => { if (_source == null && !_failed && e.PreviousSize.Width < 1 && e.NewSize.Width >= 1) Reload(); };
    }

    private static Brush Freeze(Brush brush) { brush.Freeze(); return brush; }
    private void Cancel() { _cts?.Cancel(); _cts = null; }

    private int DecodeWidth()
    {
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var width = ActualWidth >= 1 ? ActualWidth : 240;
        return (int)(Math.Ceiling(width * dpi / 64d) * 64);
    }

    private void Reload()
    {
        Cancel();
        if (KeepPrevious) _previous = _source ?? _previous;
        _source = KeepPrevious ? _source : null; _failed = false;
        if (!KeepPrevious) SetValue(IsLoadedImageProperty, false);
        InvalidateVisual();
        var url = Url;
        if (!IsLoaded || string.IsNullOrEmpty(url)) return;
        var width = DecodeWidth();
        if (ArtworkLoader.TryGetCached(url, width, out var cached)) { Show(cached!, false); return; }
        var cts = _cts = new CancellationTokenSource();
        _ = LoadAsync(url, width, cts);
    }

    private async Task LoadAsync(string url, int width, CancellationTokenSource cts)
    {
        var image = await ArtworkLoader.GetAsync(url, width, cts.Token);
        // The user may have scrolled on or the card may show another picture by now: a slow earlier answer must never win.
        if (cts.IsCancellationRequested || !IsLoaded) return;
        if (image == null) { _failed = true; InvalidateVisual(); return; }
        Show(image, true);
    }

    private void Show(BitmapSource image, bool animate)
    {
        if (KeepPrevious && _source != null && !ReferenceEquals(_source, image)) _previous = _source;
        _source = image; _failed = false;
        SetValue(IsLoadedImageProperty, true);
        if (animate && Motion.Enabled)
        {
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(KeepPrevious && _previous != null ? 700 : 220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            fade.Completed += (_, _) => { _previous = null; InvalidateVisual(); };
            BeginAnimation(ImageOpacityProperty, fade);
        }
        else { BeginAnimation(ImageOpacityProperty, null); ImageOpacity = 1; _previous = null; }
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Behaves like a stretching Image: size comes from the parent, never from the bitmap.
        return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth; var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        dc.DrawRectangle(Placeholder, null, new Rect(0, 0, w, h));
        if (_source == null)
        {
            if (_failed && FallbackIcon != null)
            {
                var size = Math.Min(Math.Min(w, h) * 0.32, 44);
                var scale = size / 24d;
                dc.PushTransform(new TranslateTransform((w - size) / 2, (h - size) / 2));
                dc.PushTransform(new ScaleTransform(scale, scale));
                dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(0x3B, 0x47, 0x58)), 1.5 / scale * 1.0), FallbackIcon);
                dc.Pop(); dc.Pop();
            }
            return;
        }
        if (_previous != null) DrawCover(dc, _previous, w, h);
        var opacity = ImageOpacity;
        if (opacity < 1) dc.PushOpacity(opacity);
        DrawCover(dc, _source, w, h);
        if (opacity < 1) dc.Pop();
    }

    private void DrawCover(DrawingContext dc, BitmapSource source, double w, double h)
    {
        double sw = source.PixelWidth, sh = source.PixelHeight;
        var scaleFactor = Contain ? Math.Min(w / sw, h / sh) : Math.Max(w / sw, h / sh);
        double dw = sw * scaleFactor, dh = sh * scaleFactor;
        var x = (w - dw) * (Contain ? 0.5 : Math.Clamp(FocusX, 0, 1));
        var y = (h - dh) * (Contain ? 0.5 : Math.Clamp(FocusY, 0, 1));
        dc.DrawImage(source, new Rect(x, y, dw, dh));
    }
}
