using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SkinForge.UI.Controls;

/// <summary>Text box with a search glyph and a one-click clear button. Escape clears too.</summary>
public sealed class SearchBox : TextBox
{
    static SearchBox() => DefaultStyleKeyProperty.OverrideMetadata(typeof(SearchBox), new FrameworkPropertyMetadata(typeof(SearchBox)));

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_Clear") is Button clear)
            clear.Click += (_, _) => { Text = ""; Focus(); };
    }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape && Text.Length > 0) { Text = ""; e.Handled = true; return; }
        base.OnKeyDown(e);
    }
}

public static class Visuals
{
    public static T? Find<T>(DependencyObject? root) where T : DependencyObject
    {
        if (root == null) return null;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (Find<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    public static IEnumerable<T> FindAll<T>(DependencyObject? root) where T : DependencyObject
    {
        if (root == null) yield break;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var deeper in FindAll<T>(child)) yield return deeper;
        }
    }
}
