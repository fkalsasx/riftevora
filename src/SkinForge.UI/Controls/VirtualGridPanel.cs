using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace SkinForge.UI.Controls;

/// <summary>
/// Responsive card grid that only creates the cards you can see. 9,000+ skins scroll as smoothly as 20.
/// Columns grow with the window; card width stretches so rows always fill the space exactly.
/// </summary>
public sealed class VirtualGridPanel : VirtualizingPanel, IScrollInfo
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(nameof(MinItemWidth), typeof(double), typeof(VirtualGridPanel),
        new FrameworkPropertyMetadata(220d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));
    public static readonly DependencyProperty AspectRatioProperty = DependencyProperty.Register(nameof(AspectRatio), typeof(double), typeof(VirtualGridPanel),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));
    public static readonly DependencyProperty ExtraHeightProperty = DependencyProperty.Register(nameof(ExtraHeight), typeof(double), typeof(VirtualGridPanel),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));
    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(nameof(Gap), typeof(double), typeof(VirtualGridPanel),
        new FrameworkPropertyMetadata(16d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));
    public static readonly DependencyProperty FixedHeightProperty = DependencyProperty.Register(nameof(FixedHeight), typeof(double), typeof(VirtualGridPanel),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));
    public static readonly DependencyProperty MaxItemWidthProperty = DependencyProperty.Register(nameof(MaxItemWidth), typeof(double), typeof(VirtualGridPanel),
        new FrameworkPropertyMetadata(double.PositiveInfinity, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    /// <summary>Narrowest a card may become before another column is dropped.</summary>
    public double MinItemWidth { get => (double)GetValue(MinItemWidthProperty); set => SetValue(MinItemWidthProperty, value); }
    public double MaxItemWidth { get => (double)GetValue(MaxItemWidthProperty); set => SetValue(MaxItemWidthProperty, value); }
    /// <summary>Image height as a multiple of card width; <see cref="ExtraHeight"/> is the caption area below it.</summary>
    public double AspectRatio { get => (double)GetValue(AspectRatioProperty); set => SetValue(AspectRatioProperty, value); }
    public double ExtraHeight { get => (double)GetValue(ExtraHeightProperty); set => SetValue(ExtraHeightProperty, value); }
    /// <summary>When above zero, every card is exactly this tall (table-like rows).</summary>
    public double FixedHeight { get => (double)GetValue(FixedHeightProperty); set => SetValue(FixedHeightProperty, value); }
    public double Gap { get => (double)GetValue(GapProperty); set => SetValue(GapProperty, value); }

    private ScrollViewer? _owner;
    private Size _extent, _viewport;
    private double _offsetY, _targetY;
    private int _columns = 1;
    private double _itemW, _itemH;
    private bool _animating;

    // ---------- layout ----------
    private int ItemCount => ItemsControl.GetItemsOwner(this)?.Items.Count ?? 0;

    private void ComputeMetrics(double width)
    {
        var gap = Gap;
        _columns = Math.Max(1, (int)Math.Floor((width + gap) / (MinItemWidth + gap)));
        _itemW = Math.Min(MaxItemWidth, (width - gap * (_columns - 1)) / _columns);
        _itemH = FixedHeight > 0 ? FixedHeight : Math.Round(_itemW * AspectRatio + ExtraHeight);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 960 : availableSize.Width;
        ComputeMetrics(width);
        var count = ItemCount;
        var rows = (int)Math.Ceiling(count / (double)_columns);
        var rowStride = _itemH + Gap;
        var extentH = rows == 0 ? 0 : rows * rowStride - Gap;
        var viewportH = double.IsInfinity(availableSize.Height) ? extentH : availableSize.Height;
        UpdateScrollInfo(new Size(width, viewportH), new Size(width, extentH));

        if (count == 0 || viewportH <= 0)
        {
            CleanUp(-1, -1);
            return new Size(width, Math.Min(extentH, viewportH));
        }

        var firstRow = (int)Math.Floor(_offsetY / rowStride);
        var lastRow = (int)Math.Floor((_offsetY + viewportH) / rowStride);
        var first = Math.Max(0, (firstRow - 1) * _columns);
        var last = Math.Min(count - 1, (lastRow + 2) * _columns - 1);

        IItemContainerGenerator generator = ItemContainerGenerator;
        var start = generator.GeneratorPositionFromIndex(first);
        var childIndex = start.Offset == 0 ? start.Index : start.Index + 1;
        using (generator.StartAt(start, GeneratorDirection.Forward, true))
        {
            for (var item = first; item <= last; item++, childIndex++)
            {
                var child = generator.GenerateNext(out var isNew) as UIElement;
                if (child == null) break;
                if (isNew)
                {
                    if (childIndex >= InternalChildren.Count) AddInternalChild(child); else InsertInternalChild(childIndex, child);
                    generator.PrepareItemContainer(child);
                }
                child.Measure(new Size(_itemW, _itemH));
            }
        }
        CleanUp(first, last);
        return new Size(width, Math.Min(extentH, viewportH));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        IItemContainerGenerator generator = ItemContainerGenerator;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var index = generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            if (index < 0) continue;
            var row = index / _columns; var col = index % _columns;
            InternalChildren[i].Arrange(new Rect(col * (_itemW + Gap), row * (_itemH + Gap) - _offsetY, _itemW, _itemH));
        }
        return finalSize;
    }

    private void CleanUp(int first, int last)
    {
        IItemContainerGenerator generator = ItemContainerGenerator;
        for (var i = InternalChildren.Count - 1; i >= 0; i--)
        {
            var position = new GeneratorPosition(i, 0);
            var index = generator.IndexFromGeneratorPosition(position);
            if (index < first || index > last)
            {
                generator.Remove(position, 1);
                RemoveInternalChildRange(i, 1);
            }
        }
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        switch (args.Action)
        {
            case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
            case System.Collections.Specialized.NotifyCollectionChangedAction.Replace:
                RemoveInternalChildRange(args.Position.Index, args.ItemUICount);
                break;
            case System.Collections.Specialized.NotifyCollectionChangedAction.Move:
                RemoveInternalChildRange(args.OldPosition.Index, args.ItemUICount);
                break;
            case System.Collections.Specialized.NotifyCollectionChangedAction.Reset:
                _offsetY = _targetY = 0;
                break;
        }
        base.OnItemsChanged(sender, args);
        InvalidateMeasure();
    }

    // ---------- IScrollInfo ----------
    private void UpdateScrollInfo(Size viewport, Size extent)
    {
        var changed = viewport != _viewport || extent != _extent;
        _viewport = viewport; _extent = extent;
        var max = Math.Max(0, extent.Height - viewport.Height);
        if (_offsetY > max) { _offsetY = max; _targetY = Math.Min(_targetY, max); changed = true; }
        if (changed) _owner?.InvalidateScrollInfo();
    }

    public bool CanVerticallyScroll { get; set; } = true;
    public bool CanHorizontallyScroll { get; set; }
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => 0;
    public double VerticalOffset => _offsetY;
    public ScrollViewer? ScrollOwner { get => _owner; set => _owner = value; }

    public void SetVerticalOffset(double offset)
    {
        offset = Math.Clamp(offset, 0, Math.Max(0, _extent.Height - _viewport.Height));
        _targetY = offset;
        if (Math.Abs(offset - _offsetY) < 0.01) return;
        _offsetY = offset;
        _owner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    public void SetHorizontalOffset(double offset) { }
    public void LineUp() => Scroll(-56);
    public void LineDown() => Scroll(56);
    public void LineLeft() { }
    public void LineRight() { }
    public void MouseWheelUp() => Scroll(-132);
    public void MouseWheelDown() => Scroll(132);
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }
    public void PageUp() => Scroll(-_viewport.Height * 0.9);
    public void PageDown() => Scroll(_viewport.Height * 0.9);
    public void PageLeft() { }
    public void PageRight() { }

    private void Scroll(double delta)
    {
        _targetY = Math.Clamp(_targetY + delta, 0, Math.Max(0, _extent.Height - _viewport.Height));
        if (!Motion.Enabled) { SetVerticalOffset(_targetY); return; }
        if (_animating) return;
        _animating = true;
        CompositionTarget.Rendering += Step;
    }

    // Exponential ease toward the wheel target: feels like inertia, stops exactly on the pixel.
    private void Step(object? sender, EventArgs e)
    {
        var distance = _targetY - _offsetY;
        if (Math.Abs(distance) < 0.6 || !IsVisible)
        {
            CompositionTarget.Rendering -= Step; _animating = false;
            SetVerticalOffset(_targetY);
            return;
        }
        var target = _targetY;
        SetVerticalOffset(_offsetY + distance * 0.24);
        _targetY = target;
    }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        if (visual is not UIElement child || !InternalChildren.Contains(child)) return rectangle;
        var index = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(InternalChildren.IndexOf(child), 0));
        if (index < 0) return rectangle;
        var top = index / _columns * (_itemH + Gap);
        if (top < _offsetY) SetVerticalOffset(top - Gap);
        else if (top + _itemH > _offsetY + _viewport.Height) SetVerticalOffset(top + _itemH - _viewport.Height + Gap);
        return rectangle;
    }

    /// <summary>Scrolls so the given item index starts the visible area (A–Z jump).</summary>
    public void ScrollToItem(int index)
    {
        if (index < 0) return;
        var top = index / _columns * (_itemH + Gap);
        _targetY = Math.Clamp(top, 0, Math.Max(0, _extent.Height - _viewport.Height));
        if (!Motion.Enabled) SetVerticalOffset(_targetY); else Scroll(0);
    }
}
