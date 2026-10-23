using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkinForge.Core.Models;
using SkinForge.Infrastructure.Packaging;
using SkinForge.Infrastructure.Workspace;
using SkinForge.UI.Localization;
using SkinForge.UI.Services;

namespace SkinForge.UI.ViewModels;

public abstract class DrawerViewModel : ObservableObject
{
    public abstract void Refresh();
}

public sealed partial class ChromaChip : ObservableObject
{
    public required OfficialSkin Skin { get; init; }
    public required string Label { get; init; }
    [ObservableProperty] private bool _isSelected;
}

/// <summary>Everything about one champion: big art, lore, every skin, colour variants and the mods that touch it.</summary>
public sealed partial class ChampionDrawer : DrawerViewModel
{
    private readonly ShellViewModel _shell;
    private readonly AppServices _app;
    private int _version;

    public ChampionDrawer(ShellViewModel shell, AppServices app, OfficialChampion champion, string? skinId, string? chromaId = null)
    {
        _shell = shell; _app = app; Champion = champion;
        PreviewUrl = champion.SplashUrl; PreviewTitle = champion.Name;
        ChampionCard = CardFactory.Champion(champion, app);
        Mods = [];
        foreach (var package in app.PackagesFor(champion.Id)) Mods.Add(CardFactory.Mod(package, app));
        _ = LoadSkinsAsync(skinId, chromaId);
    }

    public OfficialChampion Champion { get; }
    public CardItem ChampionCard { get; }
    public string Name => Champion.Name;
    public string TitleLine => Champion.Title;
    public string RoleLabel => CardFactory.RoleName(Champion.Role);
    public string Lore => Champion.Blurb;
    public ObservableCollection<CardItem> Skins { get; } = [];
    public ObservableCollection<ChromaChip> Chromas { get; } = [];
    public ObservableCollection<CardItem> Mods { get; }
    public bool HasChromas => Chromas.Count > 0;
    public bool HasMods => Mods.Count > 0;
    public string SkinCountText => Loc.Count("n_skins", Skins.Count);
    public string ModsHeader => Loc.T("drawer_mods_for", Champion.Name);

    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private CardItem? _selectedSkin;
    [ObservableProperty] private string _previewUrl = "";
    [ObservableProperty] private string _previewTitle = "";
    [ObservableProperty] private string _previewNote = "";
    [ObservableProperty] private bool _isFavorite;
    [ObservableProperty] private bool _championIsFavorite;

    private async Task LoadSkinsAsync(string? skinId, string? chromaId)
    {
        var version = ++_version;
        IsLoading = true;
        var skins = _app.Index.SkinsOf(Champion.Id);
        if (skins.Count == 0) skins = await _app.Catalog.GetChampionSkinsAsync(Champion.Id); // offline-first: only hits the network when the snapshot lacks it
        if (version != _version) return;
        Skins.Clear();
        foreach (var skin in skins.Where(s => s.Rarity != "Kroma")) Skins.Add(CardFactory.Skin(skin, _app));
        OnPropertyChanged(nameof(SkinCountText));
        var start = (skinId != null ? Skins.FirstOrDefault(s => s.Ref == "skin:" + skinId) : null) ?? Skins.FirstOrDefault(s => (s.Payload as OfficialSkin)?.Num == 0) ?? Skins.FirstOrDefault();
        if (start != null) Select(start, chromaId);
        IsLoading = false;
        Refresh();
    }

    [RelayCommand]
    private void SelectSkin(CardItem? card) { if (card != null) Select(card, null); }

    private void Select(CardItem card, string? chromaId)
    {
        SelectedSkin = card;
        foreach (var s in Skins) s.IsSelected = ReferenceEquals(s, card);
        if (card.Payload is not OfficialSkin skin) return;
        PreviewUrl = skin.SplashUrl;
        PreviewTitle = card.Title;
        PreviewNote = Loc.T("drawer_art_note");
        Chromas.Clear();
        foreach (var chroma in _app.Index.ChromasOf(skin)) Chromas.Add(new ChromaChip { Skin = chroma, Label = _app.Index.ChromaLabel(chroma), IsSelected = chroma.Id == chromaId });
        OnPropertyChanged(nameof(HasChromas));
        if (chromaId != null && Chromas.FirstOrDefault(c => c.Skin.Id == chromaId) is { } chip) PreviewNote = Loc.T("drawer_chroma_note", chip.Label);
        IsFavorite = _app.IsFavorite(card.Ref);
        _app.Store.AddRecent(card.Ref);
    }

    [RelayCommand]
    private void SelectChroma(ChromaChip? chip)
    {
        if (chip == null) return;
        foreach (var c in Chromas) c.IsSelected = ReferenceEquals(c, chip);
        PreviewNote = Loc.T("drawer_chroma_note", chip.Label);
        IsFavorite = _app.IsFavorite("skin:" + chip.Skin.Id);
        _app.Store.AddRecent("skin:" + chip.Skin.Id);
    }

    private string CurrentRef => Chromas.FirstOrDefault(c => c.IsSelected) is { } chip ? "skin:" + chip.Skin.Id : SelectedSkin?.Ref ?? ChampionCard.Ref;
    private string CurrentTitle => Chromas.FirstOrDefault(c => c.IsSelected) is { } chip ? chip.Skin.Name : SelectedSkin?.Title ?? Champion.Name;

    [RelayCommand]
    private void ToggleFavoriteCurrent()
    {
        var now = _app.ToggleFavorite(CurrentRef);
        IsFavorite = now;
        if (SelectedSkin != null && CurrentRef == SelectedSkin.Ref) SelectedSkin.IsFavorite = now;
        _shell.Notify(Loc.T(now ? "fav_added" : "fav_removed", CurrentTitle), ToastTone.Success);
    }

    [RelayCommand]
    private void ToggleFavoriteChampion()
    {
        ChampionIsFavorite = _app.ToggleFavorite(ChampionCard.Ref);
        ChampionCard.IsFavorite = ChampionIsFavorite;
        _shell.Notify(Loc.T(ChampionIsFavorite ? "fav_added" : "fav_removed", Champion.Name), ToastTone.Success);
    }

    [RelayCommand]
    private void CompareCurrent()
    {
        var selected = SelectedSkin?.Payload as OfficialSkin;
        _shell.Navigate("compare");
        if (_shell.Page("compare") is CompareViewModel compare)
        {
            compare.Champion = Champion;
            if (selected != null) compare.Left = compare.Skins.FirstOrDefault(s => s.Id == selected.Id);
        }
    }

    [RelayCommand]
    private void AddCurrentToCollection() =>
        _shell.ShowDialog(new CollectionPickerDialog(_app, CurrentRef, CurrentTitle, m => _shell.Notify(m, ToastTone.Success)));

    [RelayCommand]
    private void OpenLightbox() => _shell.ShowLightbox(PreviewTitle, PreviewUrl);

    [RelayCommand] private void FindMods() => _shell.OpenUrl("https://www.runeforge.dev/");

    [RelayCommand]
    private void StepSkin(string? direction)
    {
        if (SelectedSkin == null || Skins.Count == 0) return;
        var index = Skins.IndexOf(SelectedSkin) + (direction == "next" ? 1 : -1);
        Select(Skins[(index + Skins.Count) % Skins.Count], null);
    }

    public override void Refresh()
    {
        ChampionIsFavorite = _app.IsFavorite(ChampionCard.Ref);
        ChampionCard.IsFavorite = ChampionIsFavorite;
        if (SelectedSkin != null) IsFavorite = _app.IsFavorite(CurrentRef);
        OnPropertyChanged(string.Empty);
    }
}

/// <summary>Details of one imported package, with the actions that matter: hand to LTK, reveal, remove.</summary>
public sealed partial class ModDrawer : DrawerViewModel
{
    private readonly ShellViewModel _shell;
    private readonly AppServices _app;

    public ModDrawer(ShellViewModel shell, AppServices app, LocalPackage package)
    {
        _shell = shell; _app = app; Package = package;
        Card = CardFactory.Mod(package, app);
        Inspection = app.InspectionOf(package);
        Conflicts = app.Conflicts.Where(c => c.First.Id == package.Id || c.Second.Id == package.Id)
            .Select(c => Loc.T(c.Severity == ConflictSeverity.SharedFiles ? "mod_conflict_files" : "mod_conflict_wad", (c.First.Id == package.Id ? c.Second : c.First).Name, c.SharedFileCount)).ToArray();
    }

    public LocalPackage Package { get; }
    public CardItem Card { get; }
    public PackageInspection Inspection { get; }
    public IReadOnlyList<string> Conflicts { get; }
    public bool HasConflicts => Conflicts.Count > 0;
    public string Name => Package.Name;
    public string Author => string.IsNullOrWhiteSpace(Package.Author) ? Loc.T("mod_unknown_author") : Package.Author;
    public string Version => string.IsNullOrWhiteSpace(Package.Version) ? "—" : Package.Version;
    public string SizeText => Package.SizeLabel;
    public string FormatText => Package.Format;
    public string ImportedText => Package.ImportedUtc is { } at ? at.ToLocalTime().ToString("g", Loc.Instance.Culture) : "—";
    public string HashText => Package.Id[..16] + "…" + Package.Id[^8..];
    public string FullHash => Package.Id;
    public string FilesText => Inspection.CanInspect ? Loc.T("mod_files_count", Inspection.EntryCount, HealthCheckRunner.FormatBytes(Inspection.UncompressedBytes)) : Loc.T("mod_opaque");
    public string ChampionsText => Card.Badge2.Length > 0 ? string.Join(", ", Inspection.Champions.Select(n => _app.Index.Champion(n)?.Name ?? n)) : "—";
    public string PreviewUrl => Package.PreviewPath ?? "";
    public bool HasPreview => !string.IsNullOrEmpty(Package.PreviewPath);
    [ObservableProperty] private bool _isFavorite;

    [RelayCommand] private async Task OpenInLtk() => await _shell.OpenInLtkAsync(Package);
    [RelayCommand] private void ShowFile() => _shell.ShowInFolder(Package.FilePath);
    [RelayCommand] private void CopyHash() { try { System.Windows.Clipboard.SetText(Package.Id); _shell.Notify(Loc.T("copied"), ToastTone.Success); } catch (System.Runtime.InteropServices.COMException) { } }

    [RelayCommand]
    private void ToggleFavorite()
    {
        IsFavorite = _app.ToggleFavorite(Card.Ref);
        Card.IsFavorite = IsFavorite;
        _shell.Notify(Loc.T(IsFavorite ? "fav_added" : "fav_removed", Name), ToastTone.Success);
    }

    [RelayCommand] private void AddToCollection() => _shell.AddToCollectionCommand.Execute(Card);

    [RelayCommand] private void Remove() => _shell.ConfirmRemove(Package);

    public override void Refresh() { IsFavorite = _app.IsFavorite(Card.Ref); OnPropertyChanged(string.Empty); }
}
