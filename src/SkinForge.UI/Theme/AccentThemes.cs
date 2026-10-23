using System.Windows;
using System.Windows.Media;

namespace SkinForge.UI.Theme;

/// <summary>The single accent colour is the only thing that changes between themes; everything else stays charcoal.</summary>
public static class AccentThemes
{
    public static readonly IReadOnlyList<string> Names = ["gold", "cyan"];

    private sealed record Palette(Color Main, Color Hover, Color Pressed);

    private static readonly Dictionary<string, Palette> All = new()
    {
        ["gold"] = new(Color.FromRgb(0xBA, 0xE9, 0xB1), Color.FromRgb(0xD3, 0xF4, 0xCD), Color.FromRgb(0x8C, 0xBE, 0x88)),
        ["cyan"] = new(Color.FromRgb(0x0A, 0xC8, 0xB9), Color.FromRgb(0x3B, 0xE0, 0xD2), Color.FromRgb(0x08, 0x9A, 0x8E))
    };

    public static void Apply(string name)
    {
        var palette = All.GetValueOrDefault(name) ?? All["gold"];
        var resources = System.Windows.Application.Current.Resources;
        resources["AccentColor"] = palette.Main;
        resources["AccentHoverColor"] = palette.Hover;
        resources["AccentPressedColor"] = palette.Pressed;
        resources["AccentSubtleColor"] = Color.FromArgb(0x1F, palette.Main.R, palette.Main.G, palette.Main.B);
        resources["AccentBorderColor"] = Color.FromArgb(0x4D, palette.Main.R, palette.Main.G, palette.Main.B);
        resources["AccentGlowColor"] = Color.FromArgb(0x38, palette.Main.R, palette.Main.G, palette.Main.B);
    }

    public static Color Swatch(string name) => (All.GetValueOrDefault(name) ?? All["gold"]).Main;
}
