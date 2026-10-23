using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkinForge.UI.Localization;
using SkinForge.UI.Services;

namespace SkinForge.UI.ViewModels;

public sealed class PaletteItem
{
    public required string Group { get; init; }
    public required string Title { get; init; }
    public string Subtitle { get; init; } = "";
    public string ImageUrl { get; init; } = "";
    public string IconKey { get; init; } = "Icon.Sparkles";
    public Geometry? Icon => System.Windows.Application.Current?.TryFindResource(IconKey) as Geometry;
    public bool HasImage => ImageUrl.Length > 0;
    public bool ShowHeader { get; set; }
    public string Hint { get; init; } = "";
    public required Action Run { get; init; }
}

/// <summary>Ctrl+K: jump to any page, champion, skin or mod from the keyboard.</summary>
public sealed partial class PaletteViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;
    private readonly AppServices _app;
    private int _version;

    public PaletteViewModel(ShellViewModel shell, AppServices app) { _shell = shell; _app = app; }

    public ObservableCollection<PaletteItem> Results { get; } = [];
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private int _selectedIndex;
    public bool HasResults => Results.Count > 0;
    public bool HasQuery => Query.Trim().Length > 0;

    public void Reset() { Query = ""; Rebuild(); }

    partial void OnQueryChanged(string value)
    {
        OnPropertyChanged(nameof(HasQuery));
        var version = ++_version;
        // Typing is faster than rebuilding: wait a beat so only the last keystroke does the work.
        _ = Task.Delay(70).ContinueWith(_ => ShellViewModel.RunOnUi(() => { if (version == _version) Rebuild(); }), TaskScheduler.Default);
    }

    private void Rebuild()
    {
        var items = new List<PaletteItem>();
        var query = Query.Trim();
        var folded = CatalogIndex.Fold(query);

        foreach (var action in Actions().Where(a => folded.Length == 0 || CatalogIndex.Fold(a.Title).Contains(folded) || CatalogIndex.Fold(a.Hint).Contains(folded)))
            items.Add(action);

        if (folded.Length == 0)
        {
            foreach (var recent in _app.Store.Data.Recents.Take(6))
                if (CardFactory.FromRef(recent.Ref, _app) is { } card) items.Add(ToItem(card, Loc.T("palette_recent")));
        }
        else
        {
            var refs = _app.Index.Search(query, 60);
            int champions = 0, skins = 0;
            foreach (var reference in refs)
            {
                if (reference.StartsWith("champion:") && champions < 5 && CardFactory.FromRef(reference, _app) is { } champion) { items.Add(ToItem(champion, Loc.T("palette_champions"))); champions++; }
                else if (reference.StartsWith("skin:") && skins < 9 && CardFactory.FromRef(reference, _app) is { } skin) { items.Add(ToItem(skin, Loc.T("palette_skins"))); skins++; }
            }
            foreach (var package in _app.Packages.Where(p => CatalogIndex.Fold(p.Name + " " + p.Author).Contains(folded)).Take(5))
                if (CardFactory.Mod(package, _app) is { } mod) items.Add(ToItem(mod, Loc.T("palette_mods")));
        }

        var ordered = items.GroupBy(i => i.Group).SelectMany(g => g).ToList();
        string? last = null;
        foreach (var item in ordered) { item.ShowHeader = item.Group != last; last = item.Group; }
        Results.Clear();
        foreach (var item in ordered) Results.Add(item);
        SelectedIndex = Results.Count > 0 ? 0 : -1;
        OnPropertyChanged(nameof(HasResults));
    }

    private PaletteItem ToItem(CardItem card, string group) => new()
    {
        Group = group, Title = card.Title, Subtitle = card.Kind == CardKind.Mod ? card.Subtitle : card.Kind == CardKind.Champion ? card.Subtitle : card.Subtitle,
        ImageUrl = card.ImageUrl, IconKey = card.Kind == CardKind.Mod ? "Icon.Package" : "Icon.Gem",
        Run = () => _shell.OpenCard(card)
    };

    private IEnumerable<PaletteItem> Actions()
    {
        var group = Loc.T("palette_actions");
        PaletteItem Page(string key, string icon) => new() { Group = group, Title = Loc.T("nav_" + key), Hint = Loc.T("page_" + key + "_sub"), IconKey = icon, Subtitle = Loc.T("palette_go"), Run = () => _shell.Navigate(key) };
        yield return Page("home", "Icon.Home");
        yield return Page("discover", "Icon.Compass");
        yield return Page("champions", "Icon.Swords");
        yield return Page("skins", "Icon.Gem");
        yield return Page("mods", "Icon.Sparkles");
        yield return Page("collections", "Icon.Layers");
        yield return Page("favorites", "Icon.Star");
        yield return Page("tools", "Icon.Wrench");
        yield return Page("diagnostics", "Icon.Activity");
        yield return Page("backup", "Icon.History");
        yield return Page("settings", "Icon.Sliders");
        yield return new PaletteItem { Group = group, Title = Loc.T("btn_import_mod"), Subtitle = "Ctrl+I", IconKey = "Icon.Import", Run = () => _shell.ImportMods() };
        yield return new PaletteItem { Group = group, Title = Loc.T("btn_open_ltk"), IconKey = "Icon.External", Run = () => _ = _shell.OpenLtkManager() };
        yield return new PaletteItem { Group = group, Title = Loc.T("palette_toggle_language"), IconKey = "Icon.Globe", Run = () => _shell.ToggleLanguageCommand.Execute(null) };
        yield return new PaletteItem { Group = group, Title = Loc.T("palette_run_diag"), IconKey = "Icon.Activity", Run = () => { _shell.Navigate("diagnostics"); if (_shell.Page("diagnostics") is DiagnosticsViewModel d) d.RunCommand.Execute(null); } };
    }

    public void Move(int delta)
    {
        if (Results.Count == 0) return;
        SelectedIndex = (SelectedIndex + delta + Results.Count) % Results.Count;
    }

    [RelayCommand]
    public void Execute(PaletteItem? item)
    {
        item ??= SelectedIndex >= 0 && SelectedIndex < Results.Count ? Results[SelectedIndex] : null;
        if (item == null) return;
        _shell.ClosePalette();
        item.Run();
    }
}
