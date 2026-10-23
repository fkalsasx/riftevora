using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SkinForge.Infrastructure.SystemInfo;
using SkinForge.Infrastructure.Workspace;
using SkinForge.UI.Controls;
using SkinForge.UI.Localization;
using SkinForge.UI.Services;

namespace SkinForge.UI.ViewModels;

public sealed partial class DiagnosticRow : ObservableObject
{
    public required string Id { get; init; }
    public string Title => Loc.T("health_" + Id + "_title");
    [ObservableProperty] private string _detail = "";
    [ObservableProperty] private HealthStatus _status = HealthStatus.Info;
    [ObservableProperty] private bool _isPending = true;
    [ObservableProperty] private string? _fixId;
    public string FixLabel => FixId == null ? "" : Loc.T("fix_" + FixId);
    public string StatusKey => IsPending ? "pending" : Status.ToString().ToLowerInvariant();
    partial void OnStatusChanged(HealthStatus value) => OnPropertyChanged(nameof(StatusKey));
    partial void OnIsPendingChanged(bool value) => OnPropertyChanged(nameof(StatusKey));
    partial void OnFixIdChanged(string? value) => OnPropertyChanged(nameof(FixLabel));
}

public sealed partial class DiagnosticsViewModel : PageViewModel
{
    private static readonly string[] CheckIds =
        ["data-writable", "data-link", "disk", "ltk", "game", "riot-client", "game-running", "catalog", "bundled-art", "online", "art-cache", "logs", "runtime", "packages"];
    private CancellationTokenSource? _cts;

    public DiagnosticsViewModel(AppServices app, ShellViewModel shell) : base(app, shell)
    {
        foreach (var id in CheckIds) Rows.Add(new DiagnosticRow { Id = id });
    }

    public override string Key => "diagnostics";
    public ObservableCollection<DiagnosticRow> Rows { get; } = [];
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _hasRun;
    [ObservableProperty] private double _progress;
    public int Ok => Rows.Count(r => !r.IsPending && r.Status is HealthStatus.Ok or HealthStatus.Info);
    public int Warnings => Rows.Count(r => !r.IsPending && r.Status == HealthStatus.Warning);
    public int Failures => Rows.Count(r => !r.IsPending && r.Status == HealthStatus.Fail);
    public string SummaryTitle => !HasRun ? Loc.T("diag_never_run") : Failures > 0 ? Loc.Count("diag_failures", Failures) : Warnings > 0 ? Loc.Count("diag_warnings", Warnings) : Loc.T("diag_all_ok");
    public string SummaryKey => !HasRun ? "pending" : Failures > 0 ? "fail" : Warnings > 0 ? "warning" : "ok";
    public string SummaryText => HasRun ? Loc.T("diag_summary", Ok, Warnings, Failures) : Loc.T("diag_never_run_text");

    public override Task ActivateAsync() => HasRun || IsRunning ? Task.CompletedTask : RunAsync(false);

    [RelayCommand]
    public async Task Run() => await RunAsync(true);

    public async Task RunAsync(bool animate)
    {
        if (IsRunning) return;
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        IsRunning = true; Progress = 0;
        foreach (var row in Rows) { row.IsPending = true; row.FixId = null; row.Detail = ""; }
        try
        {
            var inputs = new HealthInputs(App.DataRoot, App.Library, App.LtkPath, App.LtkVersion, App.Game, App.CatalogState.ToString(), App.CatalogPatch,
                App.Index.Champions.Count, App.Index.Skins.Count, ArtworkLoader.CacheDirectory, ArtworkLoader.BundledDirectory, Path.Combine(App.DataRoot, "logs"), animate);
            var done = 0;
            await foreach (var item in HealthCheckRunner.RunAsync(inputs, cts.Token))
            {
                var row = Rows.FirstOrDefault(r => r.Id == item.Id);
                if (row != null)
                {
                    var key = "health_" + item.Id + "_" + item.Status.ToString().ToLowerInvariant();
                    var text = Loc.T(key, item.Args.Cast<object?>().ToArray());
                    row.Detail = text; row.Status = item.Status; row.FixId = item.FixId; row.IsPending = false;
                }
                Progress = ++done / (double)CheckIds.Length;
                if (animate && Motion.Enabled) await Task.Delay(70, cts.Token); // let the eye follow the scan
            }
            HasRun = true;
            App.HealthWarnings = Warnings; App.HealthFailures = Failures; App.HealthRan = true;
            App.RaiseHealthChanged();
            App.Store.Log("diagnostics", Loc.T("history_diag_target"), Failures > 0 ? HistoryResult.Error : Warnings > 0 ? HistoryResult.Warning : HistoryResult.Ok, Loc.T("diag_summary", Ok, Warnings, Failures));
        }
        catch (OperationCanceledException) { }
        finally
        {
            IsRunning = false;
            OnPropertyChanged(string.Empty);
        }
    }

    [RelayCommand]
    private async Task Fix(DiagnosticRow? row)
    {
        switch (row?.FixId)
        {
            case "clear-image-cache": Shell.Notify(Loc.T("cache_cleared", ArtworkLoader.ClearDiskCache()), ToastTone.Success); break;
            case "choose-ltk": (Shell.Page("settings") as SettingsViewModel)?.ChooseLtkFile(); break;
            case "choose-game": (Shell.Page("settings") as SettingsViewModel)?.ChooseGameFolder(); break;
            case "refresh-catalog": await Shell.RefreshCatalogAsync(); break;
            case "open-data": Shell.OpenFolder(App.DataRoot); break;
            case "open-logs": Shell.OpenFolder(Path.Combine(App.DataRoot, "logs")); break;
        }
        if (row?.FixId is "choose-ltk" or "choose-game" or "refresh-catalog" or "clear-image-cache") await RunAsync(false);
    }

    [RelayCommand] private void ClearCache() { Shell.Notify(Loc.T("cache_cleared", ArtworkLoader.ClearDiskCache()), ToastTone.Success); _ = RunAsync(false); }
}

public sealed class BackupRow
{
    public required string Path { get; init; }
    public string Name => System.IO.Path.GetFileName(Path);
    public required string Size { get; init; }
    public required string When { get; init; }
}

public sealed class HistoryRow
{
    public required HistoryEntry Entry { get; init; }
    public string When => Entry.TimeUtc.ToLocalTime().ToString("g", Loc.Instance.Culture);
    public string ActionText => Loc.T("history_" + Entry.Action.Replace('-', '_'));
    public string Target => Entry.Target;
    public string Detail => Entry.Detail;
    public string ResultKey => Entry.Result.ToString().ToLowerInvariant();
}

public sealed partial class BackupViewModel : PageViewModel
{
    public BackupViewModel(AppServices app, ShellViewModel shell) : base(app, shell) { }
    public override string Key => "backup";
    public string BackupDirectory => Path.Combine(App.DataRoot, "Backups");
    public ObservableCollection<BackupRow> Backups { get; } = [];
    public ObservableCollection<HistoryRow> History { get; } = [];
    [ObservableProperty] private bool _includePackages = true;
    [ObservableProperty] private bool _isWorking;
    [ObservableProperty] private double _workProgress;
    public bool HasBackups => Backups.Count > 0;
    public bool HasHistory => History.Count > 0;
    public string PackagesSizeText => HealthCheckRunner.FormatBytes(App.Packages.Sum(p => p.SizeBytes));

    public override Task ActivateAsync() { Reload(); return Task.CompletedTask; }

    private void Reload()
    {
        Backups.Clear();
        try
        {
            if (Directory.Exists(BackupDirectory))
                foreach (var file in new DirectoryInfo(BackupDirectory).EnumerateFiles("*.zip").OrderByDescending(f => f.LastWriteTimeUtc))
                    Backups.Add(new BackupRow { Path = file.FullName, Size = HealthCheckRunner.FormatBytes(file.Length), When = file.LastWriteTime.ToString("g", Loc.Instance.Culture) });
        }
        catch (IOException) { }
        History.Clear();
        foreach (var e in App.Store.Data.History.Take(120)) History.Add(new HistoryRow { Entry = e });
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private async Task Create()
    {
        Directory.CreateDirectory(BackupDirectory);
        var dialog = new SaveFileDialog { Title = Loc.T("backup_save_title"), Filter = "SkinForge|*.zip", InitialDirectory = BackupDirectory, FileName = $"SkinForge-{DateTime.Now:yyyyMMdd-HHmm}.zip" };
        if (dialog.ShowDialog() != true) return;
        await CreateAtAsync(dialog.FileName, IncludePackages);
    }

    private async Task CreateAtAsync(string path, bool packages)
    {
        IsWorking = true; WorkProgress = 0;
        try
        {
            var manifest = await WorkspaceBackup.CreateAsync(path, App.Store, App.Library.Root, packages, new Progress<double>(p => WorkProgress = p));
            App.Store.Log("backup", Path.GetFileName(path), HistoryResult.Ok, Loc.T("backup_log_detail", manifest.PackageCount));
            Shell.Notify(Loc.T("backup_created", Path.GetFileName(path)), ToastTone.Success, Loc.T("show_in_folder"), () => { Shell.ShowInFolder(path); return Task.CompletedTask; });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.Store.Log("backup", Path.GetFileName(path), HistoryResult.Error, ex.Message);
            Shell.Notify(ex.Message, ToastTone.Error);
        }
        finally { IsWorking = false; Reload(); }
    }

    [RelayCommand]
    private void Restore(BackupRow? row)
    {
        var path = row?.Path;
        if (path == null)
        {
            var picker = new OpenFileDialog { Title = Loc.T("backup_restore_pick"), Filter = "SkinForge|*.zip", InitialDirectory = Directory.Exists(BackupDirectory) ? BackupDirectory : null };
            if (picker.ShowDialog() != true) return;
            path = picker.FileName;
        }
        var chosen = path;
        Shell.Confirm(Loc.T("backup_restore_title"), Loc.T("backup_restore_message", Path.GetFileName(chosen)), Loc.T("backup_restore_confirm"), false, () => RestoreFromAsync(chosen));
    }

    private async Task RestoreFromAsync(string path)
    {
        IsWorking = true;
        try
        {
            var manifest = await WorkspaceBackup.ReadManifestAsync(path);
            // Safety net first: restoring must never be a one-way door.
            Directory.CreateDirectory(BackupDirectory);
            await WorkspaceBackup.CreateAsync(Path.Combine(BackupDirectory, $"before-restore-{DateTime.Now:yyyyMMdd-HHmmss}.zip"), App.Store, App.Library.Root, false);
            var summary = await WorkspaceBackup.RestoreAsync(path, App.Library.Root, true);
            var restored = summary.Workspace; var current = App.Store.Data;
            restored.LtkPath = current.LtkPath; restored.GamePath = current.GamePath; restored.SetupCompleted = current.SetupCompleted; restored.RiskAcceptedUtc = current.RiskAcceptedUtc;
            App.Store.Replace(restored);
            Loc.Instance.SetLanguage(restored.Language);
            Theme.AccentThemes.Apply(restored.Accent);
            Motion.Reduced = restored.ReduceMotion;
            Shell.IsCompactSidebar = restored.CompactSidebar;
            await App.ReloadPackagesAsync();
            App.RaiseFavoritesChanged();
            App.Store.Log("restore", Path.GetFileName(path), HistoryResult.Ok, Loc.T("backup_restore_detail", summary.PackagesRestored, summary.PackagesSkipped));
            Shell.Notify(Loc.T("backup_restored", summary.PackagesRestored), ToastTone.Success);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            App.Store.Log("restore", Path.GetFileName(path), HistoryResult.Error, ex.Message);
            Shell.Notify(ex.Message, ToastTone.Error);
        }
        finally { IsWorking = false; Reload(); }
    }

    [RelayCommand] private void ShowFile(BackupRow? row) { if (row != null) Shell.ShowInFolder(row.Path); }

    [RelayCommand]
    private void DeleteBackup(BackupRow? row)
    {
        if (row == null) return;
        var path = row.Path;
        Shell.Confirm(Loc.T("backup_delete_title"), Loc.T("backup_delete_message", row.Name), Loc.T("delete"), true, () =>
        {
            try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Shell.Notify(ex.Message, ToastTone.Error); }
            Reload();
            return Task.CompletedTask;
        });
    }

    [RelayCommand] private void OpenBackupFolder() => Shell.OpenFolder(BackupDirectory);
    [RelayCommand] private void ClearHistory() => Shell.Confirm(Loc.T("history_clear_title"), Loc.T("history_clear_message"), Loc.T("history_clear"), true, () => { App.Store.ClearHistory(); Reload(); return Task.CompletedTask; });
}

public sealed record ChoiceOption(string Key, string Label);

public sealed partial class SettingsViewModel : PageViewModel
{
    public SettingsViewModel(AppServices app, ShellViewModel shell) : base(app, shell)
    {
        App.EnvironmentChanged += (_, _) => ShellViewModel.RunOnUi(() => OnPropertyChanged(string.Empty));
        _language = app.Store.Data.Language; _accent = app.Store.Data.Accent; _startPage = app.Store.Data.StartPage;
        _reduceMotion = app.Store.Data.ReduceMotion; _compact = app.Store.Data.CompactSidebar; _offline = app.Store.Data.OfflineMode;
    }
    public override string Key => "settings";

    [ObservableProperty] private string _tab = "general";
    [ObservableProperty] private string _language;
    [ObservableProperty] private string _accent;
    [ObservableProperty] private string _startPage;
    [ObservableProperty] private bool _reduceMotion;
    [ObservableProperty] private bool _compact;
    [ObservableProperty] private bool _offline;
    [ObservableProperty] private string _cacheSize = "…";
    [ObservableProperty] private string _packagesSize = "…";
    [ObservableProperty] private string _logsSize = "…";

    public IReadOnlyList<ChoiceOption> Languages { get; } = [new("tr", "Türkçe"), new("en", "English")];
    public IReadOnlyList<ChoiceOption> StartPages => new ChoiceOption[] { new("home", Loc.T("nav_home")), new("discover", Loc.T("nav_discover")), new("champions", Loc.T("nav_champions")), new("mods", Loc.T("nav_mods")) };
    public IReadOnlyList<string> Accents => Theme.AccentThemes.Names;

    public string LtkPathText => App.LtkPath ?? Loc.T("settings_ltk_none");
    public string LtkVersionText => App.LtkVersion is { Length: > 0 } v ? "v" + v : "—";
    public bool LtkInstalled => App.LtkInstalled;
    public string GamePathText => App.Game?.RootDirectory ?? Loc.T("settings_game_none");
    public string GameVersionText => App.Game?.GameVersion ?? "—";
    public string GameSourceText => App.Game?.Source ?? "";
    public bool GameManual => !string.IsNullOrWhiteSpace(App.Store.Data.GamePath);
    public string DataRootText => App.DataRoot;
    public string PatchText => App.CatalogPatch;
    public string CatalogStateText => Loc.T("catalog_state_" + App.CatalogState.ToString().ToLowerInvariant());
    public string RiskText => App.Store.Data.RiskAcceptedUtc is { } at ? Loc.T("settings_risk_accepted", at.ToLocalTime().ToString("d", Loc.Instance.Culture)) : Loc.T("settings_risk_pending");
    public bool RiskAccepted => App.Store.Data.RiskAcceptedUtc != null;

    [RelayCommand] private void SetLanguage(string? code) { if (code != null) Language = code; }
    [RelayCommand] private void SetStartPage(string? key) { if (key != null) StartPage = key; }

    partial void OnLanguageChanged(string value) { if (value != Loc.Instance.Language) Shell.ApplyLanguage(value); }
    partial void OnAccentChanged(string value) => Shell.ApplyAccent(value);
    partial void OnStartPageChanged(string value) { App.Store.Data.StartPage = value; App.Store.SaveSoon(); }
    partial void OnReduceMotionChanged(bool value) => Shell.SetReduceMotion(value);
    partial void OnCompactChanged(bool value) => Shell.IsCompactSidebar = value;
    partial void OnOfflineChanged(bool value) => Shell.SetOffline(value);
    partial void OnTabChanged(string value) { if (value == "storage") _ = MeasureAsync(); }

    public override void OnLanguageChanged() { Language = Loc.Instance.Language; base.OnLanguageChanged(); }
    public override Task ActivateAsync()
    {
        Compact = Shell.IsCompactSidebar;
        return Tab == "storage" ? MeasureAsync() : Task.CompletedTask;
    }

    private async Task MeasureAsync()
    {
        var (cache, packages, logs) = await Task.Run(() => (ArtworkLoader.DiskCacheBytes(), App.Packages.Sum(p => p.SizeBytes), HealthCheckRunner.DirectorySize(Path.Combine(App.DataRoot, "logs"))));
        CacheSize = HealthCheckRunner.FormatBytes(cache); PackagesSize = HealthCheckRunner.FormatBytes(packages); LogsSize = HealthCheckRunner.FormatBytes(logs);
    }

    [RelayCommand]
    public void ChooseLtkFile()
    {
        var picker = new OpenFileDialog { Title = Loc.T("settings_ltk_pick"), Filter = "LTK Manager|ltk-manager.exe" };
        if (picker.ShowDialog() != true) return;
        _ = ApplyLtkPathAsync(picker.FileName);
    }

    private async Task ApplyLtkPathAsync(string path)
    {
        if (await App.Ltk.DetectInstallationAsync(path)) { App.Store.Data.LtkPath = path; App.Store.SaveSoon(); Shell.Notify(Loc.T("settings_ltk_saved"), ToastTone.Success); }
        else Shell.Notify(Loc.T("settings_ltk_invalid"), ToastTone.Error);
        await App.RefreshEnvironmentAsync();
    }

    [RelayCommand] private async Task Recheck() { await App.RefreshEnvironmentAsync(); Shell.Notify(App.LtkInstalled ? Loc.T("settings_recheck_ok") : Loc.T("settings_recheck_missing"), App.LtkInstalled ? ToastTone.Success : ToastTone.Warning); }
    [RelayCommand] private async Task OpenLtk() => await Shell.OpenLtkManager();
    [RelayCommand] private void DownloadLtk() => Shell.DownloadLtkCommand.Execute(null);

    [RelayCommand]
    public void ChooseGameFolder()
    {
        var picker = new OpenFolderDialog { Title = Loc.T("settings_game_pick") };
        if (picker.ShowDialog() != true) return;
        if (GameLocator.Normalize(picker.FolderName) == null) { Shell.Notify(Loc.T("settings_game_invalid"), ToastTone.Error); return; }
        App.Store.Data.GamePath = picker.FolderName; App.Store.SaveSoon();
        _ = App.RefreshEnvironmentAsync();
    }

    [RelayCommand]
    private void AutoDetectGame() { App.Store.Data.GamePath = null; App.Store.SaveSoon(); _ = App.RefreshEnvironmentAsync(); }

    [RelayCommand]
    private async Task ClearImageCache() { Shell.Notify(Loc.T("cache_cleared", ArtworkLoader.ClearDiskCache()), ToastTone.Success); await MeasureAsync(); }

    [RelayCommand] private async Task RefreshCatalog() => await Shell.RefreshCatalogAsync();
    [RelayCommand] private void OpenData() => Shell.OpenFolder(App.DataRoot);
    [RelayCommand] private void OpenLibraryFolder() => Shell.OpenFolder(App.Library.Root);
    [RelayCommand] private void OpenLogs() => Shell.OpenFolder(Path.Combine(App.DataRoot, "logs"));

    [RelayCommand]
    private void ResetRisk() { App.Store.Data.RiskAcceptedUtc = null; App.Store.SaveSoon(); OnPropertyChanged(nameof(RiskText)); OnPropertyChanged(nameof(RiskAccepted)); }

    [RelayCommand]
    private async Task RunSetup() { App.Store.Data.SetupCompleted = false; await Shell.RunSetupIfNeededAsync(); }
}

public sealed record ShortcutRow(string Keys, string Text);

public sealed partial class AboutViewModel : PageViewModel
{
    public AboutViewModel(AppServices app, ShellViewModel shell) : base(app, shell) { }
    public override string Key => "about";
    public string Version => "Riftevora " + ShellViewModel.ReleaseVersion;
    public string Runtime => System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + " · " + System.Runtime.InteropServices.RuntimeInformation.OSDescription;
    public IReadOnlyList<ShortcutRow> Shortcuts => new ShortcutRow[]
    {
        new("Ctrl + K", Loc.T("shortcut_palette")), new("Ctrl + I", Loc.T("shortcut_import")), new("Ctrl + F", Loc.T("shortcut_search")),
        new("Ctrl + B", Loc.T("shortcut_sidebar")), new("Esc", Loc.T("shortcut_close")), new("1 – 9", Loc.T("shortcut_pages"))
    };
    [RelayCommand] private void OpenLtkRepo() => Shell.OpenUrl("https://github.com/LeagueToolkit/ltk-manager");
    [RelayCommand] private void OpenRuneforge() => Shell.OpenUrl("https://www.runeforge.dev/");
    [RelayCommand] private void OpenDataDragon() => Shell.OpenUrl("https://developer.riotgames.com/docs/lol#data-dragon");
}
