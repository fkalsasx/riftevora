using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SkinForge.UI.Controls;
using SkinForge.UI.Localization;
using SkinForge.UI.ViewModels;

namespace SkinForge.UI;

public partial class DesktopWindow : Window
{
    private static readonly string[] PageShortcuts = ["home", "discover", "champions", "skins", "mods", "collections", "favorites", "tools", "diagnostics"];
    private readonly ShellViewModel _vm;

    public DesktopWindow(ShellViewModel vm)
    {
        InitializeComponent();
        _vm = vm; DataContext = vm;
        vm.PropertyChanged += OnVmChanged;
        Loc.Instance.LanguageChanged += (_, _) => LanguageCode.Text = Loc.Instance.Language.ToUpperInvariant();
        LanguageCode.Text = Loc.Instance.Language.ToUpperInvariant();
        StateChanged += (_, _) => SyncWindowState();
        SizeChanged += (_, _) => ApplyTopbarDensity();
        PreviewKeyDown += OnKey;
        Closing += (_, _) => vm.App.Store.Flush();
        ApplySidebar(vm.IsCompactSidebar, false);
        SyncWindowState();
    }

    public ShellViewModel ViewModel => _vm;

    // ---------- window chrome ----------
    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void OnMaximize(object sender, RoutedEventArgs e) { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }
    private void OnClose(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    // Below ~1180 px the topbar keeps icons and drops labels so nothing is ever clipped or unreachable.
    private void ApplyTopbarDensity()
    {
        var narrow = ActualWidth < 1180;
        Ui.SetCompact(TopbarGrid, narrow);
        ImportLabel.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SyncWindowState()
    {
        var maximized = WindowState == WindowState.Maximized;
        // A borderless window grows past the screen by its resize frame when maximised; pull the content back in.
        Root.Margin = maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        Ui.SetIcon(MaximizeButton, (Geometry)FindResource(maximized ? "Icon.Restore" : "Icon.Maximize"));
    }

    // ---------- reacting to the view model ----------
    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ShellViewModel.IsCompactSidebar): ApplySidebar(_vm.IsCompactSidebar, true); break;
            case nameof(ShellViewModel.CurrentPage): AnimatePage(); break;
            case nameof(ShellViewModel.IsDrawerOpen):
                ToastHost.Margin = new Thickness(0, 0, _vm.IsDrawerOpen ? DrawerPanel.Width + 24 : 24, 24);
                if (_vm.IsDrawerOpen) AnimateDrawer();
                break;
            case nameof(ShellViewModel.IsDialogOpen) when _vm.IsDialogOpen: AnimatePop(DialogScale, DialogHost); break;
            case nameof(ShellViewModel.IsPaletteOpen) when _vm.IsPaletteOpen: OnPaletteOpened(); break;
            case nameof(ShellViewModel.IsSplashVisible) when !_vm.IsSplashVisible: break;
        }
    }

    private void ApplySidebar(bool compact, bool animate)
    {
        var width = compact ? 72d : 208d;
        if (animate && Motion.Enabled)
            SidebarHost.BeginAnimation(WidthProperty, new DoubleAnimation(width, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        else { SidebarHost.BeginAnimation(WidthProperty, null); SidebarHost.Width = width; }
        var visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        BrandText.Visibility = visibility; SectionExplore.Visibility = visibility; SectionSystem.Visibility = visibility; SafetyText.Visibility = visibility; SafetyCard.Visibility = visibility;
        SidebarToggleIcon.Data = (Geometry)FindResource(compact ? "Icon.ChevronsRight" : "Icon.ChevronsLeft");
    }

    private void AnimatePage()
    {
        // Run after the new DataTemplate has attached. A page should open at
        // its heading rather than inherit a previous page's scroll position.
        Dispatcher.BeginInvoke(() => Visuals.Find<ScrollViewer>(PageHost)?.ScrollToTop(), System.Windows.Threading.DispatcherPriority.Loaded);
        PageShift.BeginAnimation(TranslateTransform.YProperty, null);
        PageHost.BeginAnimation(OpacityProperty, null);
        if (!Motion.Enabled) { PageHost.Opacity = 1; return; }
        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        PageShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void AnimateDrawer()
    {
        if (!Motion.Enabled) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        DrawerShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(90, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease });
        DrawerPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
        DrawerScrim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
    }

    private static void AnimatePop(ScaleTransform scale, UIElement element)
    {
        if (!Motion.Enabled) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(190)) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(190)) { EasingFunction = ease });
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
    }

    private void OnPaletteOpened()
    {
        AnimatePop(PaletteScale, PaletteBox);
        Dispatcher.BeginInvoke(() => { PaletteInput.Focus(); PaletteInput.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>Fades the splash away once the first page is ready.</summary>
    public async Task HideSplashAsync()
    {
        if (!Motion.Enabled) { _vm.IsSplashVisible = false; return; }
        var done = new TaskCompletionSource();
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
        fade.Completed += (_, _) => done.TrySetResult();
        Splash.BeginAnimation(OpacityProperty, fade);
        await done.Task;
        _vm.IsSplashVisible = false;
        Splash.BeginAnimation(OpacityProperty, null);
    }

    // ---------- scrims, palette ----------
    private void OnDrawerScrim(object sender, MouseButtonEventArgs e) => _vm.CloseDrawer();
    private void OnDialogScrim(object sender, MouseButtonEventArgs e) { if (_vm.Dialog is ImportDialog { Finished: false }) return; _vm.Dialog = null; }
    private void OnPaletteScrim(object sender, MouseButtonEventArgs e) => _vm.ClosePalette();
    private void OnSafetyClick(object sender, MouseButtonEventArgs e) => _vm.Navigate("diagnostics");

    private void OnPaletteKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down: _vm.Palette.Move(1); PaletteList.ScrollIntoView(PaletteList.SelectedItem); e.Handled = true; break;
            case Key.Up: _vm.Palette.Move(-1); PaletteList.ScrollIntoView(PaletteList.SelectedItem); e.Handled = true; break;
            case Key.Enter: _vm.Palette.Execute(null); e.Handled = true; break;
            case Key.Escape: _vm.ClosePalette(); e.Handled = true; break;
        }
    }

    // ---------- keyboard ----------
    private void OnKey(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if (e.Key == Key.Escape && _vm.CloseTopmost()) { e.Handled = true; return; }
        if (ctrl && e.Key == Key.K) { _vm.OpenPalette(); e.Handled = true; }
        else if (ctrl && e.Key == Key.I) { _vm.ImportMods(); e.Handled = true; }
        else if (ctrl && e.Key == Key.B) { _vm.IsCompactSidebar = !_vm.IsCompactSidebar; e.Handled = true; }
        else if (ctrl && e.Key == Key.F) { FocusPageSearch(); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.None && Keyboard.FocusedElement is not TextBox && !_vm.IsOverlayOpen && e.Key is >= Key.D1 and <= Key.D9)
        {
            var index = e.Key - Key.D1;
            if (index < PageShortcuts.Length) { _vm.Navigate(PageShortcuts[index]); e.Handled = true; }
        }
    }

    private void FocusPageSearch()
    {
        if (_vm.IsOverlayOpen) return;
        var box = Visuals.Find<SearchBox>(PageHost);
        if (box == null) { _vm.OpenPalette(); return; }
        box.Focus(); box.SelectAll();
    }

    // ---------- drag and drop of mod packages ----------
    private static bool HasFiles(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop);

    private void OnDragEnter(object sender, DragEventArgs e) { if (HasFiles(e)) _vm.IsDragging = true; e.Handled = true; }
    private void OnDragOver(object sender, DragEventArgs e) { e.Effects = HasFiles(e) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private void OnDragLeave(object sender, DragEventArgs e) { _vm.IsDragging = false; e.Handled = true; }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        _vm.IsDragging = false;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) await _vm.ImportFilesAsync(files);
        e.Handled = true;
    }
}
