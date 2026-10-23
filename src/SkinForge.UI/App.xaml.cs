using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using SkinForge.Infrastructure.Catalog;
using SkinForge.Infrastructure.ModProviders;
using SkinForge.Infrastructure.Packaging;
using SkinForge.Infrastructure.Workspace;
using SkinForge.UI.Controls;
using SkinForge.UI.Localization;
using SkinForge.UI.Services;
using SkinForge.UI.Theme;
using SkinForge.UI.ViewModels;

namespace SkinForge.UI;

public partial class App : System.Windows.Application
{
    private DesktopWindow? _window;
    private ShellViewModel? _shell;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var root = Environment.GetEnvironmentVariable("SKINFORGE_DATA") is { Length: > 0 } custom ? custom : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SkinForge");
        Directory.CreateDirectory(root);
        Log.Logger = new LoggerConfiguration().WriteTo.File(Path.Combine(root, "logs", "desktop-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7).CreateLogger();
        Log.Information("Riftevora {Version} starting", ShellViewModel.ReleaseVersion);
        BindingTrace.Install();

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled UI exception");
            args.Handled = true;
            _shell?.Notify(args.Exception.Message, ToastTone.Error);
        };
        TaskScheduler.UnobservedTaskException += (_, args) => { Log.Error(args.Exception, "Unobserved task exception"); args.SetObserved(); };

        var store = new WorkspaceStore(Path.Combine(root, "workspace.json"));
        store.Load();
        // Preferences are applied before the first frame so the window never flashes the wrong language or accent.
        Loc.Instance.SetLanguage(store.Data.Language);
        CultureInfo.CurrentCulture = Loc.Instance.Culture;
        AccentThemes.Apply(store.Data.Accent);
        Motion.Reduced = store.Data.ReduceMotion;
        SkinForge.Infrastructure.SystemInfo.NetworkPolicy.Offline = store.Data.OfflineMode || Environment.GetEnvironmentVariable("SKINFORGE_OFFLINE") == "1";

        var services = new AppServices
        {
            Catalog = new DataDragonCatalogProvider(Path.Combine(root, "Cache"), NullLogger<DataDragonCatalogProvider>.Instance),
            Ltk = new RealLtkManagerAdapter(NullLogger<RealLtkManagerAdapter>.Instance),
            Library = new LocalPackageLibrary(Path.Combine(root, "Packages")),
            Store = store,
            DataRoot = root
        };
        _shell = new ShellViewModel(services) { IsCompactSidebar = store.Data.CompactSidebar };
        _window = new DesktopWindow(_shell) { Language = XmlLanguage.GetLanguage(Loc.Instance.Culture.IetfLanguageTag) };
        Loc.Instance.LanguageChanged += (_, _) => _window.Language = XmlLanguage.GetLanguage(Loc.Instance.Culture.IetfLanguageTag);
        if (Environment.GetEnvironmentVariable("SKINFORGE_SIZE") is { } size && size.Split('x') is [var w, var h] && double.TryParse(w, out var width) && double.TryParse(h, out var height))
        {
            _window.Width = width; _window.Height = height;
        }
        MainWindow = _window;
        var capture = Environment.GetEnvironmentVariable("SKINFORGE_CAPTURE");
        if (capture != null)
        {
            // Render QA snapshots away from the desktop without taking focus.
            _window.ShowActivated = false;
            _window.ShowInTaskbar = false;
            _window.WindowStartupLocation = WindowStartupLocation.Manual;
            _window.Left = -20000;
            _window.Top = -20000;
        }
        _window.Show();

        _ = BootAsync(_window, _shell, services, capture);
    }

    private static async Task BootAsync(DesktopWindow window, ShellViewModel shell, AppServices app, string? capture)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            await app.RefreshEnvironmentAsync();
            await app.LoadCatalogAsync();
            await app.ReloadPackagesAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error(ex, "Startup load failed");
            shell.Notify(Loc.T("boot_failed", ex.Message), ToastTone.Error);
        }
        await shell.StartAsync(app.Store.Data.StartPage);
        shell.IsReady = true;
        // Hold the splash just long enough to read as intentional, never longer than the work needs.
        var wait = TimeSpan.FromMilliseconds(950) - clock.Elapsed;
        if (wait > TimeSpan.Zero && capture == null) await Task.Delay(wait);
        await window.HideSplashAsync();
        Log.Information("Ready in {Elapsed} ms ({Champions} champions, {Skins} skins, {Packages} packages)", clock.ElapsedMilliseconds, app.Index.Champions.Count, app.Index.Skins.Count, app.Packages.Count);

        if (capture != null)
        {
            try { await CaptureRunner.RunAsync(window, shell, app, capture); System.Windows.Application.Current.Shutdown(); }
            catch (Exception ex) { Log.Error(ex, "QA failed"); Directory.CreateDirectory(capture); await File.WriteAllTextAsync(Path.Combine(capture, "failure.txt"), ex.ToString()); System.Windows.Application.Current.Shutdown(2); }
            return;
        }

        _ = Task.Run(() => ArtworkLoader.PruneOlderThan(TimeSpan.FromDays(75)));
        await shell.RunSetupIfNeededAsync();
        _ = RunSilentHealthCheckAsync(shell);
    }

    /// <summary>A quiet check a few seconds after launch so problems surface before the user hits them.</summary>
    private static async Task RunSilentHealthCheckAsync(ShellViewModel shell)
    {
        await Task.Delay(3500);
        if (shell.Page("diagnostics") is not DiagnosticsViewModel diagnostics) return;
        await diagnostics.RunAsync(false);
        if (diagnostics.Failures > 0)
            shell.Notify(Loc.Count("diag_failures", diagnostics.Failures), ToastTone.Warning, Loc.T("tools_open"), () => { shell.Navigate("diagnostics"); return Task.CompletedTask; });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shell?.App.Store.Flush();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
