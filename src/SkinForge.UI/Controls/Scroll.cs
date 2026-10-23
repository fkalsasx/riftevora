using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SkinForge.UI.Controls;

/// <summary>Scroll helpers that make nested scrollers behave the way people expect.</summary>
public static class Scroll
{
    public static readonly DependencyProperty ForwardWheelProperty = DependencyProperty.RegisterAttached("ForwardWheel", typeof(bool), typeof(Scroll),
        new PropertyMetadata(false, (d, e) =>
        {
            if (d is not UIElement element) return;
            if (e.NewValue is true) element.PreviewMouseWheel += Forward; else element.PreviewMouseWheel -= Forward;
        }));
    public static bool GetForwardWheel(DependencyObject d) => (bool)d.GetValue(ForwardWheelProperty);
    public static void SetForwardWheel(DependencyObject d, bool v) => d.SetValue(ForwardWheelProperty, v);

    /// <summary>A horizontal shelf inside a vertical page must not swallow the mouse wheel.</summary>
    private static void Forward(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not UIElement element) return;
        e.Handled = true;
        var parent = (element as FrameworkElement)?.Parent as UIElement ?? System.Windows.Media.VisualTreeHelper.GetParent(element) as UIElement;
        parent?.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = sender });
    }
}
