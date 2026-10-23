using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkinForge.UI.Services;
using SkinForge.UI.ViewModels;

namespace SkinForge.UI;

/// <summary>
/// Developer aid: with SKINFORGE_CAPTURE=&lt;folder&gt; the app walks through its screens, saves a PNG of each, and exits.
/// It makes visual regressions reviewable without a human clicking around.
/// </summary>
public static class CaptureRunner
{
    public static async Task RunAsync(DesktopWindow window, ShellViewModel shell, AppServices app, string directory)
    {
        Directory.CreateDirectory(directory);
        if (app.Index.Champions.Count == 0 || app.Index.Skins.Count == 0) throw new InvalidDataException("QA: catalog is empty");
        var checks = new List<string>();
        var champions = (ChampionsViewModel)shell.Page("champions");
        await champions.ActivateAsync(); champions.Search = "Ahri"; await Task.Delay(250);
        if (champions.Items.Count != 1 || champions.Items[0].Title != "Ahri") throw new InvalidDataException("QA: champion search");
        champions.Search = "";
        checks.Add("Champion search returns Ahri");
        var comparison = (CompareViewModel)shell.Page("compare");
        await comparison.ActivateAsync();
        var first = comparison.Left; var second = comparison.Right;
        if (first == null || second == null || first.Id == second.Id) throw new InvalidDataException("QA: comparison choices");
        comparison.SwapCommand.Execute(null);
        if (comparison.Left != second || comparison.Right != first) throw new InvalidDataException("QA: comparison swap");
        comparison.Champion = app.Index.Champions.First(c => c.Id == "Yasuo");
        if (comparison.Left?.ChampionId != "Yasuo" || comparison.Right?.ChampionId != "Yasuo") throw new InvalidDataException("QA: comparison champion switch");
        checks.Add("Comparison selects distinct skins, swaps sides and follows champion");
        var home = (HomeViewModel)shell.Page("home"); home.Rebuild();
        var oldSlide = home.Slide?.SkinId; home.SurpriseCommand.Execute(null);
        if (home.Slide == null || home.Slide.SkinId == oldSlide || app.Index.Skin(home.Slide.SkinId) == null) throw new InvalidDataException("QA: surprise selection");
        checks.Add("Surprise selects a different real skin");
        var favorite = "skin:" + first.Id;
        var wasFavorite = app.IsFavorite(favorite);
        app.ToggleFavorite(favorite); app.Store.Flush();
        using (var persisted = new SkinForge.Infrastructure.Workspace.WorkspaceStore(app.Store.FilePath))
        {
            persisted.Load(); if (persisted.IsFavorite(favorite) == wasFavorite) throw new InvalidDataException("QA: favorites persistence");
        }
        app.ToggleFavorite(favorite);
        checks.Add("Favorite survives reload");
        if (Environment.GetEnvironmentVariable("SKINFORGE_SETUP_QA") == "1")
        {
            if (app.Game == null || !app.LtkInstalled) throw new InvalidDataException("QA: installed League and LTK required");
            // Exercise the real UI command against the detected installation, with a disposable LTK settings file.
            var ltkSettings = Path.Combine(directory, "ltk-test", "settings.json");
            var setupApp = new AppServices
            {
                Catalog = app.Catalog, Ltk = app.Ltk, Library = app.Library, Store = app.Store, DataRoot = app.DataRoot,
                LtkGame = new SkinForge.Infrastructure.ModProviders.LtkGameConfiguration(ltkSettings, () => false)
            };
            var setupShell = new ShellViewModel(setupApp);
            await setupShell.SetUpEnvironmentAsync(true, app.Game.RootDirectory);
            if (setupShell.IsSetupBusy || setupShell.SetupPercent != 100 || setupShell.SetupStatus != SkinForge.UI.Localization.Loc.T("setup_complete"))
                throw new InvalidDataException("QA: add game failed: " + setupShell.SetupStatus);
            using var ltkDocument = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(ltkSettings));
            if (ltkDocument.RootElement.GetProperty("leaguePath").GetString() != app.Game.RootDirectory)
                throw new InvalidDataException("QA: LTK did not receive detected game path");
            await setupShell.SetUpEnvironmentAsync(true, app.Game.RootDirectory);
            if (Directory.GetFiles(Path.GetDirectoryName(ltkSettings)!, "*.bak").Length != 0)
                throw new InvalidDataException("QA: repeated setup changed matching preferences");
            checks.Add("Add game UI detects installed League and LTK, saves the official LTK game preference and is idempotent");
            shell.SetupStatus = setupShell.SetupStatus;
            shell.SetupPercent = 100;
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "checks.json"), System.Text.Json.JsonSerializer.Serialize(checks));
        async Task Shot(string name, int delay = 1500)
        {
            await Task.Delay(delay);
            window.UpdateLayout();
            Save(window, Path.Combine(directory, name + ".png"));
        }

        if (Environment.GetEnvironmentVariable("SKINFORGE_CAPTURE_PAGES") is { Length: > 0 } selectedPages)
        {
            foreach (var key in selectedPages.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (key == "setup") { await shell.RunSetupIfNeededAsync(); await Shot(key); shell.Dialog = null; continue; }
                if (key.StartsWith("settings-"))
                {
                    shell.Navigate("settings");
                    ((SettingsViewModel)shell.Page("settings")).Tab = key[9..];
                    await Shot(key); continue;
                }
                shell.Navigate(key);
                await Shot(key);
            }
            return;
        }

        if (Environment.GetEnvironmentVariable("SKINFORGE_DEMO") is { Length: > 0 } demo && Directory.Exists(demo))
        {
            await shell.ImportFilesAsync(Directory.GetFiles(demo, "*.fantome"));
            await Shot("demo-import", 1400);
            shell.Dialog = null;
            shell.Navigate("mods");
            await Shot("demo-mods", 2000);
            if (app.Packages.Count > 0) { shell.OpenMod(app.Packages[0]); await Shot("demo-mod-drawer", 1500); shell.CloseDrawer(); }
            shell.Navigate("tools");
            await Shot("demo-tools", 1500);
            shell.Navigate("home");
            await Shot("demo-home", 1800);
        }

        foreach (var key in new[] { "home", "compare", "discover", "champions", "skins", "mods", "collections", "favorites", "tools", "diagnostics", "backup", "settings", "about" })
        {
            shell.Navigate(key);
            await Shot(key, key == "diagnostics" ? 4500 : 1800);
        }

        shell.Navigate("champions");
        await Task.Delay(800);
        if (app.Index.Champion("Ahri") is { } ahri)
        {
            shell.OpenChampion(ahri, "103027");
            await Shot("drawer-champion", 2500);
            shell.CloseDrawer();
        }
        shell.OpenPalette();
        shell.Palette.Query = "ahri";
        await Shot("palette", 1200);
        shell.ClosePalette();
        shell.ShowDialog(new RiskDialog("Spirit Blossom Ahri", () => Task.CompletedTask));
        await Shot("dialog-risk", 900);
        shell.Dialog = null;
        shell.IsCompactSidebar = true;
        shell.Navigate("home");
        await Shot("home-compact", 1200);
    }

    private static void Save(Window window, string path)
    {
        if (window.Content is not FrameworkElement root) return;
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap((int)(root.ActualWidth * dpi.DpiScaleX), (int)(root.ActualHeight * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        var backdrop = new DrawingVisual();
        using (var context = backdrop.RenderOpen()) context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x0B, 0x0D, 0x10)), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        bitmap.Render(backdrop);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
