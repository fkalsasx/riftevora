using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SkinForge.Infrastructure.Packaging;
using SkinForge.Infrastructure.ModProviders;
using SkinForge.Infrastructure.SystemInfo;
using SkinForge.Infrastructure.Workspace;
using SkinForge.UI.Controls;
using SkinForge.UI.Localization;
using SkinForge.UI.Services;
using SkinForge.UI.Theme;

namespace SkinForge.UI.ViewModels;

public abstract class PageViewModel : ViewModelBase
{
    protected PageViewModel(AppServices app, ShellViewModel shell) { App = app; Shell = shell; }
    protected AppServices App { get; }
    public ShellViewModel Shell { get; }
    public abstract string Key { get; }
    public string Title => Loc.T("page_" + Key + "_title");
    public string Subtitle => Loc.T("page_" + Key + "_sub");
    /// <summary>Runs every time the page is shown; cheap refreshes only.</summary>
    public virtual Task ActivateAsync() => Task.CompletedTask;
    public virtual void OnLanguageChanged() => OnPropertyChanged(string.Empty);
}

/// <summary>Owns navigation, the modal layer, toasts and every cross-page action.</summary>
public sealed partial class ShellViewModel : ViewModelBase
{
    public const string ReleaseVersion = "3.1.0";
    private readonly Dictionary<string, PageViewModel> _pages = new();

    public ShellViewModel(AppServices app)
    {
        App = app;
        Palette = new PaletteViewModel(this, app);
        App.PackagesChanged += (_, _) => RunOnUi(RefreshBadges);
        App.CatalogChanged += (_, _) => RunOnUi(RefreshBadges);
        App.FavoritesChanged += (_, _) => RunOnUi(RefreshBadges);
        App.EnvironmentChanged += (_, _) => RunOnUi(RefreshBadges);
        App.HealthChanged += (_, _) => RunOnUi(RefreshBadges);
        Loc.Instance.LanguageChanged += (_, _) => RunOnUi(() =>
        {
            foreach (var page in _pages.Values) page.OnLanguageChanged();
            Drawer?.Refresh();
            OnPropertyChanged(string.Empty);
        });
    }

    public AppServices App { get; }
    public PaletteViewModel Palette { get; }
    public ObservableCollection<ToastItem> Toasts { get; } = [];

    [ObservableProperty] private PageViewModel? _currentPage;
    [ObservableProperty] private string _pageKey = "home";
    [ObservableProperty] private bool _isCompactSidebar;
    [ObservableProperty] private DrawerViewModel? _drawer;
    [ObservableProperty] private DialogViewModel? _dialog;
    [ObservableProperty] private LightboxDialog? _lightbox;
    [ObservableProperty] private bool _isPaletteOpen;
    [ObservableProperty] private bool _isSplashVisible = true;
    [ObservableProperty] private bool _isDragging;
    [ObservableProperty] private bool _isReady;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddGameCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadLtkCommand))]
    private bool _isSetupBusy;
    [ObservableProperty] private string _setupStatus = "";
    [ObservableProperty] private double _setupPercent;
    [ObservableProperty] private bool _setupCanCancel;
    private CancellationTokenSource? _setupCancellation;
    public bool HasSetupStatus => !string.IsNullOrWhiteSpace(SetupStatus);
    private bool CanSetUp() => !IsSetupBusy;
    partial void OnSetupStatusChanged(string value) => OnPropertyChanged(nameof(HasSetupStatus));

    // ----- navigation -----
    public string Breadcrumb => CurrentPage?.Title ?? "";
    public bool IsDrawerOpen => Drawer != null;
    public bool IsDialogOpen => Dialog != null;
    public bool IsLightboxOpen => Lightbox != null;
    public bool IsOverlayOpen => IsDrawerOpen || IsDialogOpen || IsPaletteOpen || IsLightboxOpen;
    public bool ShowHeaderSearchHint => !IsCompactSidebar;

    public string ChampionsBadge => App.Index.Champions.Count.ToString();
    public string SkinsBadge => App.Index.BaseSkins.Count == 0 ? "" : App.Index.BaseSkins.Count.ToString("N0", Loc.Instance.Culture);
    public string ModsBadge => App.Packages.Count == 0 ? "" : App.Packages.Count.ToString();
    public string FavoritesBadge => App.Store.Data.Favorites.Count == 0 ? "" : App.Store.Data.Favorites.Count.ToString();
    public string ToolsBadge => App.Conflicts.Count == 0 ? "" : App.Conflicts.Count.ToString();
    public string DiagnosticsBadge => !App.HealthRan ? "" : App.HealthFailures > 0 ? "!" + App.HealthFailures : App.HealthWarnings > 0 ? "!" + App.HealthWarnings : "OK";
    public string DiagnosticsTone => !App.HealthRan ? "neutral" : App.HealthFailures > 0 ? "danger" : App.HealthWarnings > 0 ? "warning" : "success";
    public bool LtkReady => App.LtkInstalled;
    public string LtkPill => App.LtkInstalled ? Loc.T("ltk_pill_ready", App.LtkVersion is { Length: > 0 } v ? "v" + v : "") .Trim() : Loc.T("ltk_pill_missing");
    public string SafetyTitle => App.LtkInstalled ? Loc.T("safe_title_ok") : Loc.T("safe_title_missing");
    public string SafetyText => Loc.T("safe_text");
    public string VersionText => "v" + ReleaseVersion;

    private void RefreshBadges()
    {
        foreach (var name in new[] { nameof(ChampionsBadge), nameof(SkinsBadge), nameof(ModsBadge), nameof(FavoritesBadge), nameof(ToolsBadge), nameof(DiagnosticsBadge), nameof(DiagnosticsTone), nameof(LtkReady), nameof(LtkPill), nameof(SafetyTitle) })
            OnPropertyChanged(name);
    }

    partial void OnPageKeyChanged(string value) => _ = ShowPageAsync(value);
    partial void OnCurrentPageChanged(PageViewModel? value) => OnPropertyChanged(nameof(Breadcrumb));
    partial void OnDrawerChanged(DrawerViewModel? value) { OnPropertyChanged(nameof(IsDrawerOpen)); OnPropertyChanged(nameof(IsOverlayOpen)); }
    partial void OnDialogChanged(DialogViewModel? value) { OnPropertyChanged(nameof(IsDialogOpen)); OnPropertyChanged(nameof(IsOverlayOpen)); }
    partial void OnIsPaletteOpenChanged(bool value) => OnPropertyChanged(nameof(IsOverlayOpen));
    partial void OnLightboxChanged(LightboxDialog? value) { OnPropertyChanged(nameof(IsLightboxOpen)); OnPropertyChanged(nameof(IsOverlayOpen)); }
    partial void OnIsCompactSidebarChanged(bool value) { App.Store.Data.CompactSidebar = value; App.Store.SaveSoon(); }

    private async Task ShowPageAsync(string key)
    {
        if (!_pages.TryGetValue(key, out var page)) _pages[key] = page = CreatePage(key);
        CloseDrawer();
        (CurrentPage as HomeViewModel)?.Deactivate();
        CurrentPage = page;
        try { await page.ActivateAsync(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Serilog.Log.Error(ex, "Page {Page} failed to activate", key);
            Notify(ex.Message, ToastTone.Error);
        }
    }

    private PageViewModel CreatePage(string key) => key switch
    {
        "compare" => new CompareViewModel(App, this),
        "discover" => new DiscoverViewModel(App, this),
        "champions" => new ChampionsViewModel(App, this),
        "skins" => new SkinsViewModel(App, this),
        "mods" => new ModsViewModel(App, this),
        "collections" => new CollectionsViewModel(App, this),
        "favorites" => new FavoritesViewModel(App, this),
        "tools" => new ToolsViewModel(App, this),
        "diagnostics" => new DiagnosticsViewModel(App, this),
        "backup" => new BackupViewModel(App, this),
        "settings" => new SettingsViewModel(App, this),
        "about" => new AboutViewModel(App, this),
        _ => new HomeViewModel(App, this)
    };

    /// <summary>First navigation: shows the configured start page even when it equals the default key.</summary>
    public async Task StartAsync(string key)
    {
        key = key is "home" or "discover" or "champions" or "mods" ? key : "home";
        if (PageKey != key) PageKey = key; else await ShowPageAsync(key);
    }

    public PageViewModel Page(string key) { if (!_pages.TryGetValue(key, out var p)) _pages[key] = p = CreatePage(key); return p; }

    [RelayCommand] public void Navigate(string? key) { if (!string.IsNullOrEmpty(key)) { if (PageKey == key) CloseDrawer(); else PageKey = key; IsPaletteOpen = false; } }
    [RelayCommand] private void ToggleSidebar() => IsCompactSidebar = !IsCompactSidebar;
    [RelayCommand] public void OpenPalette() { if (Dialog != null) return; Palette.Reset(); IsPaletteOpen = true; }
    [RelayCommand] public void ClosePalette() => IsPaletteOpen = false;

    /// <summary>Esc: close the topmost thing only.</summary>
    public bool CloseTopmost()
    {
        if (Lightbox != null) { Lightbox = null; return true; }
        if (IsPaletteOpen) { IsPaletteOpen = false; return true; }
        if (Dialog != null) { Dialog = null; return true; }
        if (Drawer != null) { CloseDrawer(); return true; }
        return false;
    }

    // ----- notifications -----
    public void Notify(string message, ToastTone tone = ToastTone.Info, string? actionLabel = null, Func<Task>? action = null)
    {
        RunOnUi(() =>
        {
            var toast = new ToastItem { Message = message, Tone = tone, ActionLabel = actionLabel, Action = action };
            Toasts.Add(toast);
            while (Toasts.Count > 4) Toasts.RemoveAt(0);
            _ = Task.Delay(tone is ToastTone.Error or ToastTone.Warning ? 10_000 : 6_000).ContinueWith(_ => RunOnUi(() => DismissToast(toast)), TaskScheduler.Default);
        });
    }

    [RelayCommand] public void DismissToast(ToastItem? toast) { if (toast != null) Toasts.Remove(toast); }
    [RelayCommand] private async Task RunToastAction(ToastItem? toast) { if (toast?.Action == null) return; Toasts.Remove(toast); await toast.Action(); }

    public static void RunOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess()) action(); else dispatcher.BeginInvoke(action);
    }

    // ----- modal layer -----
    public void ShowDialog(DialogViewModel dialog)
    {
        IsPaletteOpen = false;
        dialog.Close = () => { if (ReferenceEquals(Dialog, dialog)) Dialog = null; };
        Dialog = dialog;
    }

    public void Confirm(string title, string message, string confirmLabel, bool danger, Func<Task> onConfirm) =>
        ShowDialog(new ConfirmDialog(title, message, confirmLabel, danger, onConfirm));

    // ----- cards -----
    [RelayCommand]
    public void OpenCard(CardItem? card)
    {
        if (card == null) return;
        IsPaletteOpen = false;
        switch (card.Payload)
        {
            case Core.Models.OfficialChampion champion: OpenChampion(champion, null); break;
            case Core.Models.OfficialSkin skin:
                if (App.Index.Champion(skin.ChampionId) is { } owner) OpenChampion(owner, skin.Rarity == "Kroma" ? App.Index.ParentOf(skin)?.Id ?? skin.Id : skin.Id, skin.Rarity == "Kroma" ? skin.Id : null);
                break;
            case LocalPackage package: OpenMod(package); break;
            case CollectionRef collection:
                if (Page("collections") is CollectionsViewModel collections) collections.Open(collection.Id);
                Navigate("collections");
                break;
        }
    }

    public void OpenChampion(Core.Models.OfficialChampion champion, string? skinId, string? chromaId = null)
    {
        App.Store.AddRecent("champion:" + champion.Id);
        if (skinId != null) App.Store.AddRecent("skin:" + skinId);
        Drawer = new ChampionDrawer(this, App, champion, skinId, chromaId);
    }

    public void OpenMod(LocalPackage package)
    {
        App.Store.AddRecent("mod:" + package.Id);
        Drawer = new ModDrawer(this, App, package);
    }

    [RelayCommand] public void CloseDrawer() => Drawer = null;

    [RelayCommand]
    public void ToggleFavorite(CardItem? card)
    {
        if (card == null || !card.CanFavorite) return;
        card.IsFavorite = App.ToggleFavorite(card.Ref);
        Notify(Loc.T(card.IsFavorite ? "fav_added" : "fav_removed", card.Title), ToastTone.Success);
        Drawer?.Refresh();
    }

    [RelayCommand]
    public void AddToCollection(CardItem? card)
    {
        if (card == null || card.Kind == CardKind.Collection) return;
        ShowDialog(new CollectionPickerDialog(App, card.Ref, card.Title, m => Notify(m, ToastTone.Success)));
    }

    public void ShowLightbox(string title, string url)
    {
        var lightbox = new LightboxDialog(title, url);
        lightbox.Close = () => { if (ReferenceEquals(Lightbox, lightbox)) Lightbox = null; };
        Lightbox = lightbox;
    }

    // ----- mods: import, open in LTK -----
    [RelayCommand]
    public void ImportMods()
    {
        var picker = new OpenFileDialog { Title = Loc.T("import_pick_title"), Filter = Loc.T("import_filter") + "|*.fantome;*.modpkg", Multiselect = true };
        if (picker.ShowDialog() == true) _ = ImportFilesAsync(picker.FileNames);
    }

    public async Task ImportFilesAsync(IEnumerable<string> paths)
    {
        if (Dialog is ImportDialog { Finished: false }) return;
        var all = paths.ToList();
        var files = all.Where(p => File.Exists(p) && Path.GetExtension(p).ToLowerInvariant() is ".fantome" or ".modpkg").ToList();
        if (files.Count == 0) { Notify(Loc.T(all.Count > 0 ? "import_unsupported" : "import_none"), ToastTone.Warning); return; }

        var dialog = new ImportDialog { GoToLibrary = () => Navigate("mods") };
        foreach (var file in files) dialog.Items.Add(new ImportItem { Name = Path.GetFileName(file), Status = Loc.T("import_waiting") });
        ShowDialog(dialog);
        IsBusy = true;
        int ok = 0;
        try
        {
            for (var i = 0; i < files.Count; i++)
            {
                var item = dialog.Items[i];
                var progress = new Progress<ImportStage>(stage => { item.Stage = (int)stage + 1; item.Status = Loc.T("import_stage_" + stage.ToString().ToLowerInvariant()); });
                try
                {
                    var package = await App.Library.ImportAsync(files[i], default, progress);
                    item.IsDone = true; item.Stage = 6; item.Status = Loc.T("import_done", package.Name);
                    App.Store.Log("import", package.Name, HistoryResult.Ok, package.Format + " · " + package.SizeLabel);
                    ok++;
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
                {
                    item.Failed = true; item.Error = ex.Message; item.Status = Loc.T("import_failed");
                    App.Store.Log("import", item.Name, HistoryResult.Error, ex.Message);
                }
            }
            await App.ReloadPackagesAsync();
        }
        finally
        {
            IsBusy = false;
            dialog.Finished = true;
            dialog.Summary = Loc.T("import_summary", ok, files.Count);
        }
        if (ok > 0 && App.Conflicts.Count > 0) Notify(Loc.T("import_conflicts_found", App.Conflicts.Count), ToastTone.Warning, Loc.T("tools_open"), () => { Navigate("tools"); return Task.CompletedTask; });
    }

    [RelayCommand]
    public async Task OpenInLtk(CardItem? card)
    {
        if (card?.Payload is LocalPackage package) await OpenInLtkAsync(package);
    }

    public async Task OpenInLtkAsync(LocalPackage package)
    {
        if (!App.LtkInstalled)
        {
            Notify(Loc.T("ltk_missing_toast"), ToastTone.Error, Loc.T("ltk_setup_action"), () => { Navigate("settings"); return Task.CompletedTask; });
            return;
        }
        if (App.Store.Data.RiskAcceptedUtc == null)
        {
            ShowDialog(new RiskDialog(package.Name, async () =>
            {
                App.Store.Data.RiskAcceptedUtc = DateTime.UtcNow; App.Store.SaveSoon();
                await HandOffAsync(package);
            }));
            return;
        }
        await HandOffAsync(package);
    }

    private async Task HandOffAsync(LocalPackage package)
    {
        try
        {
            await App.Library.VerifyAsync(package);
            var mine = App.Conflicts.Where(c => c.First.Id == package.Id || c.Second.Id == package.Id).ToList();
            var result = await App.Ltk.ImportModAsync(package.FilePath);
            if (result.IsSuccess)
            {
                App.Store.AddRecent("mod:" + package.Id);
                App.Store.Log("open-ltk", package.Name, HistoryResult.Ok);
                Notify(Loc.T("ltk_handoff_ok", package.Name), ToastTone.Success);
                if (mine.Count > 0)
                {
                    var other = mine[0].First.Id == package.Id ? mine[0].Second.Name : mine[0].First.Name;
                    Notify(Loc.T("ltk_handoff_conflict", other), ToastTone.Warning, Loc.T("tools_open"), () => { Navigate("tools"); return Task.CompletedTask; });
                }
            }
            else
            {
                App.Store.Log("open-ltk", package.Name, HistoryResult.Error, result.ErrorMessage ?? "");
                Notify(result.ErrorMessage ?? Loc.T("ltk_open_failed"), ToastTone.Error);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            App.Store.Log("open-ltk", package.Name, HistoryResult.Error, ex.Message);
            Notify(ex.Message, ToastTone.Error);
        }
    }

    [RelayCommand]
    public async Task OpenLtkManager()
    {
        if (!App.LtkInstalled) { Navigate("settings"); Notify(Loc.T("ltk_missing_toast"), ToastTone.Warning); return; }
        var result = await App.Ltk.OpenManagerAsync();
        if (!result.IsSuccess) Notify(result.ErrorMessage ?? Loc.T("ltk_open_failed"), ToastTone.Error);
    }

    [RelayCommand]
    public void ShowCardInFolder(CardItem? card) { if (card?.Payload is LocalPackage package) ShowInFolder(package.FilePath); }

    [RelayCommand]
    public void RemoveMod(CardItem? card)
    {
        if (card?.Payload is LocalPackage package) ConfirmRemove(package);
    }

    public void ConfirmRemove(LocalPackage package) =>
        Confirm(Loc.T("remove_title"), Loc.T("remove_message", package.Name), Loc.T("remove_confirm"), true, async () =>
        {
            await App.Library.RemoveAsync(package);
            App.Store.Log("remove", package.Name, HistoryResult.Ok);
            CloseDrawer();
            await App.ReloadPackagesAsync();
            Notify(Loc.T("removed_toast", package.Name), ToastTone.Success);
        });

    public void ShowInFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", path }, UseShellExecute = false }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Notify(ex.Message, ToastTone.Error); }
    }

    public void OpenFolder(string path)
    {
        try { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { Notify(ex.Message, ToastTone.Error); }
    }

    public void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Notify(ex.Message, ToastTone.Error); }
    }

    public async Task RefreshCatalogAsync()
    {
        if (SkinForge.Infrastructure.SystemInfo.NetworkPolicy.Offline) { Notify(Loc.T("offline_blocked"), ToastTone.Warning); return; }
        IsBusy = true;
        try
        {
            await App.RefreshCatalogAsync();
            Notify(App.CatalogState == Core.Models.CatalogState.Loaded ? Loc.T("catalog_refreshed", App.CatalogPatch) : Loc.T("catalog_offline_kept"), App.CatalogState == Core.Models.CatalogState.Loaded ? ToastTone.Success : ToastTone.Warning);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Notify(ex.Message, ToastTone.Error); }
        finally { IsBusy = false; }
    }

    [RelayCommand] private void FindMods() => OpenUrl("https://www.runeforge.dev/");
    [RelayCommand(CanExecute = nameof(CanSetUp))]
    private async Task DownloadLtk() => await SetUpEnvironmentAsync(false);

    [RelayCommand(CanExecute = nameof(CanSetUp))]
    private async Task AddGame() => await SetUpEnvironmentAsync(true);

    [RelayCommand] private void CancelSetup() { if (SetupCanCancel) _setupCancellation?.Cancel(); }

    public async Task SetUpEnvironmentAsync(bool requireGame, string? selectedGame = null)
    {
        if (IsSetupBusy) return;
        IsSetupBusy = true; SetupCanCancel = true; SetupPercent = 0;
        using var cancellation = new CancellationTokenSource();
        _setupCancellation = cancellation;
        var ct = cancellation.Token;
        try
        {
            SetupStatus = Loc.T("setup_progress_detect");
            var game = await Task.Run(() => GameLocator.Detect(selectedGame ?? App.Store.Data.GamePath), ct);
            if (requireGame && (game == null || !File.Exists(Path.Combine(game.RootDirectory, "Game", "League of Legends.exe"))))
            {
                var picker = new OpenFolderDialog { Title = Loc.T("settings_game_pick") };
                if (picker.ShowDialog() != true) { SetupStatus = Loc.T("setup_cancelled"); return; }
                var root = GameLocator.Normalize(picker.FolderName);
                if (root == null || !File.Exists(Path.Combine(root, "Game", "League of Legends.exe")))
                    throw new LtkSetupException("settings_game_invalid");
                game = GameLocator.Detect(root);
            }
            await App.RefreshEnvironmentAsync();
            if (!App.LtkInstalled)
            {
                var progress = new Progress<LtkSetupProgress>(p =>
                {
                    SetupStatus = p.Stage == "download" ? Loc.T("setup_progress_download", (int)p.Percent) : Loc.T("setup_progress_" + p.Stage);
                    SetupPercent = p.Percent;
                    if (p.Stage == "install") SetupCanCancel = false;
                });
                await App.LtkSetup.InstallAsync(Path.Combine(App.DataRoot, "Downloads", "LTK"), progress, ct);
                await App.RefreshEnvironmentAsync();
                if (!App.LtkInstalled) throw new LtkSetupException("setup_install_not_found");
            }
            ct.ThrowIfCancellationRequested();
            App.Store.Data.LtkPath = App.LtkPath;
            if (game != null)
            {
                SetupStatus = Loc.T("setup_progress_configure");
                await App.LtkGame.ConfigureAsync(game.RootDirectory, ct);
                App.Store.Data.GamePath = game.RootDirectory;
            }
            App.Store.Flush();
            await App.RefreshEnvironmentAsync();
            SetupPercent = 100;
            SetupStatus = Loc.T(game != null ? "setup_complete" : "setup_ltk_complete");
            Notify(SetupStatus, ToastTone.Success);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { SetupStatus = Loc.T("setup_cancelled"); }
        catch (LtkSetupException ex) { SetupStatus = Loc.T(ex.Message); Notify(SetupStatus, ToastTone.Warning); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            SetupStatus = Loc.T("setup_failed", ex.Message); Notify(SetupStatus, ToastTone.Error);
        }
        finally
        {
            _setupCancellation = null; SetupCanCancel = false; IsSetupBusy = false;
            await App.RefreshEnvironmentAsync();
        }
    }

    public void ApplyLanguage(string code)
    {
        App.Store.Data.Language = code; App.Store.SaveSoon();
        Loc.Instance.SetLanguage(code);
    }

    [RelayCommand] private void ToggleLanguage() => ApplyLanguage(Loc.Instance.Language == "tr" ? "en" : "tr");

    public void ApplyAccent(string name)
    {
        App.Store.Data.Accent = name; App.Store.SaveSoon();
        AccentThemes.Apply(name);
    }

    public void SetOffline(bool value)
    {
        App.Store.Data.OfflineMode = value; App.Store.SaveSoon();
        SkinForge.Infrastructure.SystemInfo.NetworkPolicy.Offline = value;
        if (!value) ArtworkLoader.RetryFailures();
    }

    public void SetReduceMotion(bool value)
    {
        App.Store.Data.ReduceMotion = value; App.Store.SaveSoon();
        Motion.Reduced = value;
    }

    public async Task RunSetupIfNeededAsync()
    {
        if (App.Store.Data.SetupCompleted) return;
        var setup = new SetupDialog(App, this)
        {
            Recheck = async () => { await App.RefreshEnvironmentAsync(); },
            ChooseLtk = () => { if (Page("settings") is SettingsViewModel s) { s.ChooseLtkFile(); } },
            DownloadLtk = async () => await SetUpEnvironmentAsync(false),
            AddGame = async () => await SetUpEnvironmentAsync(true),
            Done = () => { App.Store.Data.SetupCompleted = true; App.Store.SaveSoon(); }
        };
        App.EnvironmentChanged += (_, _) => RunOnUi(setup.Refresh);
        ShowDialog(setup);
        await Task.CompletedTask;
    }
}
