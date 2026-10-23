using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SkinForge.UI.Pages;

/// <summary>Full-screen splash viewer: wheel to zoom toward the cursor, drag to pan, double-click to reset.</summary>
public partial class LightboxView : UserControl
{
    private Point _dragStart;
    private Point _panStart;
    private bool _dragging;

    public LightboxView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Reset();
    }

    private void Reset()
    {
        Zoom.ScaleX = Zoom.ScaleY = 1; Pan.X = Pan.Y = 0;
        ZoomText.Text = "100%";
    }

    private void OnReset(object sender, RoutedEventArgs e) => Reset();

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var previous = Zoom.ScaleX;
        var next = Math.Clamp(previous * (e.Delta > 0 ? 1.18 : 1 / 1.18), 1, 6);
        if (Math.Abs(next - previous) < 0.0001) return;
        // Keep the point under the cursor fixed while scaling.
        var center = new Point(Surface.ActualWidth / 2, Surface.ActualHeight / 2);
        var cursor = e.GetPosition(Surface);
        var ratio = next / previous;
        Pan.X = (Pan.X - (cursor.X - center.X) * (ratio - 1) * previous) ;
        Pan.Y = (Pan.Y - (cursor.Y - center.Y) * (ratio - 1) * previous);
        Zoom.ScaleX = Zoom.ScaleY = next;
        if (next <= 1.001) { Pan.X = Pan.Y = 0; }
        ZoomText.Text = $"{next * 100:0}%";
        e.Handled = true;
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { Reset(); return; }
        _dragging = true; _dragStart = e.GetPosition(this); _panStart = new Point(Pan.X, Pan.Y);
        Stage.CaptureMouse();
    }

    private void OnUp(object sender, MouseButtonEventArgs e) { _dragging = false; Stage.ReleaseMouseCapture(); }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || Zoom.ScaleX <= 1.001) return;
        var now = e.GetPosition(this);
        Pan.X = _panStart.X + (now.X - _dragStart.X);
        Pan.Y = _panStart.Y + (now.Y - _dragStart.Y);
    }

    private void OnMiddle(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Middle) Reset(); }
}
