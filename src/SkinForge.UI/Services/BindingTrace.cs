using System.Diagnostics;
using System.Text;
using Serilog;

namespace SkinForge.UI.Services;

/// <summary>
/// WPF swallows binding errors silently. Routing them to the log file makes a typo in a binding show up in the
/// very next run instead of as a mysteriously empty label.
/// </summary>
public sealed class BindingTrace : TraceListener
{
    private readonly StringBuilder _line = new();

    public static void Install()
    {
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(new BindingTrace());
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error | SourceLevels.Warning;
    }

    public override void Write(string? message) => _line.Append(message);

    public override void WriteLine(string? message)
    {
        _line.Append(message);
        var text = _line.ToString().Trim();
        _line.Clear();
        if (text.Length > 0) Log.Warning("Binding: {Message}", text);
    }
}
