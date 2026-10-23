using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using SkinForge.UI.Controls;
using SkinForge.UI.ViewModels;

namespace SkinForge.UI.Pages;

public partial class DiagnosticsPage : UserControl
{
    public DiagnosticsPage()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is DiagnosticsViewModel old) old.PropertyChanged -= OnChanged;
            if (e.NewValue is DiagnosticsViewModel current) { current.PropertyChanged += OnChanged; Sync(current.IsRunning); }
        };
        Unloaded += (_, _) => { if (DataContext is DiagnosticsViewModel vm) vm.PropertyChanged -= OnChanged; };
    }

    private void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiagnosticsViewModel.IsRunning) && sender is DiagnosticsViewModel vm) Sync(vm.IsRunning);
    }

    /// <summary>A thin accent line sweeps down the list while checks run: motion that means "working".</summary>
    private void Sync(bool running)
    {
        ScannerLine.BeginAnimation(OpacityProperty, null);
        ScannerShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
        if (!running || !Motion.Enabled) { ScannerLine.Opacity = 0; return; }
        ScannerLine.Opacity = 0.9;
        var sweep = new DoubleAnimation(0, 420, TimeSpan.FromMilliseconds(1100)) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }, AutoReverse = true };
        ScannerShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, sweep);
    }
}
