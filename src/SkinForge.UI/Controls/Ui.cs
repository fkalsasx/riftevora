using System.Windows;
using System.Windows.Media;

namespace SkinForge.UI.Controls;

/// <summary>Attached properties that let templates stay generic while XAML stays terse.</summary>
public static class Ui
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached("Icon", typeof(Geometry), typeof(Ui), new PropertyMetadata(null));
    public static readonly DependencyProperty BadgeProperty = DependencyProperty.RegisterAttached("Badge", typeof(string), typeof(Ui), new PropertyMetadata(null));
    public static readonly DependencyProperty BadgeToneProperty = DependencyProperty.RegisterAttached("BadgeTone", typeof(string), typeof(Ui), new PropertyMetadata("neutral"));
    public static readonly DependencyProperty CompactProperty = DependencyProperty.RegisterAttached("Compact", typeof(bool), typeof(Ui),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
    public static readonly DependencyProperty HintProperty = DependencyProperty.RegisterAttached("Hint", typeof(string), typeof(Ui), new PropertyMetadata(""));

    public static readonly DependencyProperty AutoFocusProperty = DependencyProperty.RegisterAttached("AutoFocus", typeof(bool), typeof(Ui),
        new PropertyMetadata(false, (d, e) =>
        {
            if (d is not FrameworkElement element || e.NewValue is not true) return;
            // Dispatch after layout so the element is focusable when we ask.
            element.Loaded += (_, _) => element.Dispatcher.BeginInvoke(() => { element.Focus(); if (element is System.Windows.Controls.TextBox box) box.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
        }));
    public static bool GetAutoFocus(DependencyObject d) => (bool)d.GetValue(AutoFocusProperty);
    public static void SetAutoFocus(DependencyObject d, bool v) => d.SetValue(AutoFocusProperty, v);

    public static Geometry? GetIcon(DependencyObject d) => (Geometry?)d.GetValue(IconProperty);
    public static void SetIcon(DependencyObject d, Geometry? v) => d.SetValue(IconProperty, v);
    public static string? GetBadge(DependencyObject d) => (string?)d.GetValue(BadgeProperty);
    public static void SetBadge(DependencyObject d, string? v) => d.SetValue(BadgeProperty, v);
    public static string GetBadgeTone(DependencyObject d) => (string)d.GetValue(BadgeToneProperty);
    public static void SetBadgeTone(DependencyObject d, string v) => d.SetValue(BadgeToneProperty, v);
    public static bool GetCompact(DependencyObject d) => (bool)d.GetValue(CompactProperty);
    public static void SetCompact(DependencyObject d, bool v) => d.SetValue(CompactProperty, v);
    /// <summary>Placeholder text for TextBox.</summary>
    public static string GetHint(DependencyObject d) => (string)d.GetValue(HintProperty);
    public static void SetHint(DependencyObject d, string v) => d.SetValue(HintProperty, v);
}
