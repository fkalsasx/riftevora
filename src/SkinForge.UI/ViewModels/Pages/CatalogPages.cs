using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkinForge.Core.Models;
using SkinForge.UI.Localization;
using SkinForge.UI.Services;

namespace SkinForge.UI.ViewModels;

/// <summary>Shared plumbing for pages that show one big, searchable grid of cards.</summary>
public abstract partial class CardGridPage : PageViewModel
{
    private int _version;
    protected CardGridPage(AppServices app, ShellViewModel shell) : base(app, shell)
    {
        App.CatalogChanged += (_, _) => ShellViewModel.RunOnUi(Refresh);
        App.FavoritesChanged += (_, _) => ShellViewModel.RunOnUi(SyncFavorites);
    }

    public IReadOnlyList<CardItem> Items { get; protected set; } = [];
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private bool _isEmpty;
    public string CountText => Count(Items.Count);
    protected abstract string Count(int n);
    protected abstract IReadOnlyList<CardItem> Build();

    protected bool Built { get; private set; }
    public override Task ActivateAsync() { if (!Built) Refresh(); else SyncFavorites(); return Task.CompletedTask; }
    public override void OnLanguageChanged() { Refresh(); base.OnLanguageChanged(); }

    partial void OnSearchChanged(string value)
    {
        var version = ++_version;
        _ = Task.Delay(90).ContinueWith(_ => ShellViewModel.RunOnUi(() => { if (version == _version) Refresh(); }), TaskScheduler.Default);
    }

    protected virtual void Refresh()
    {
        Built = true;
        Items = Build();
        IsEmpty = Items.Count == 0;
        OnPropertyChanged(nameof(Items)); OnPropertyChanged(nameof(CountText));
    }

    private void SyncFavorites()
    {
        foreach (var item in Items) if (item.CanFavorite) item.IsFavorite = App.IsFavorite(item.Ref);
    }

    protected bool Matches(string text) => string.IsNullOrWhiteSpace(Search) || CatalogIndex.Fold(text).Contains(CatalogIndex.Fold(Search));
    [RelayCommand] private void ClearSearch() => Search = "";
}

public sealed partial class DiscoverViewModel : CardGridPage
{
    private int _seed = DateTime.Today.GetHashCode();
    public DiscoverViewModel(AppServices app, ShellViewModel shell) : base(app, shell) { }
    public override string Key => "discover";
    protected override string Count(int n) => Loc.Count("n_skins", n);

    [ObservableProperty] private string _line = "";
    [ObservableProperty] private bool _linesExpanded;
    public IReadOnlyList<LineChip> LineChips =>
        [new LineChip("", Loc.T("discover_all_lines"), 0), .. App.Index.Lines.Take(60).Select(l => new LineChip(l.Name, l.Name, l.Count))];
    public string LinesToggleText => Loc.T(LinesExpanded ? "discover_less" : "discover_more");
    partial void OnLinesExpandedChanged(bool value) => OnPropertyChanged(nameof(LinesToggleText));
    [RelayCommand] private void ToggleLines() => LinesExpanded = !LinesExpanded;
    public string LineSummary => Line.Length == 0 ? Loc.T("discover_all_lines") : Line;

    partial void OnLineChanged(string value) { Refresh(); OnPropertyChanged(nameof(LineSummary)); }

    protected override void Refresh() { base.Refresh(); OnPropertyChanged(nameof(LineChips)); }

    protected override IReadOnlyList<CardItem> Build()
    {
        IEnumerable<OfficialSkin> skins = App.Index.BaseSkins.Where(s => s.Num > 0);
        if (Line.Length > 0) skins = skins.Where(s => string.Equals(App.Index.LineOf(s), Line, StringComparison.OrdinalIgnoreCase) || (App.Index.LineBySkinId.TryGetValue(s.Id, out var l) && l.Equals(Line, StringComparison.OrdinalIgnoreCase)));
        if (!string.IsNullOrWhiteSpace(Search)) skins = skins.Where(s => Matches(s.Name));
        var random = new Random(_seed);
        // A line reads best in release order; the open-ended view is a shuffle.
        var list = Line.Length > 0 || !string.IsNullOrWhiteSpace(Search) ? skins.ToList() : skins.OrderBy(_ => random.Next()).ToList();
        return list.Select(s => CardFactory.Skin(s, App)).ToArray();
    }

    [RelayCommand]
    private void Shuffle() { _seed = Environment.TickCount; Line = ""; Refresh(); Shell.Notify(Loc.T("discover_shuffled"), ToastTone.Info); }

    [RelayCommand] private void PickLine(string? line) => Line = line ?? "";
}

public sealed partial class SkinsViewModel : CardGridPage
{
    public SkinsViewModel(AppServices app, ShellViewModel shell) : base(app, shell) { }
    public override string Key => "skins";
    protected override string Count(int n) => Loc.Count("n_entries", n);

    [ObservableProperty] private string _kind = "all";
    [ObservableProperty] private string _sort = "champion";
    public string[] Sorts { get; } = ["champion", "name", "name_desc"];
    public IReadOnlyList<SortOption> SortOptions => Sorts.Select(s => new SortOption(s, Loc.T("sort_" + s))).ToArray();

    partial void OnKindChanged(string value) => Refresh();
    partial void OnSortChanged(string value) => Refresh();

    protected override IReadOnlyList<CardItem> Build()
    {
        IEnumerable<OfficialSkin> skins = Kind switch { "skins" => App.Index.BaseSkins, "chromas" => App.Index.ChromaSkins, _ => App.Index.Skins };
        if (!string.IsNullOrWhiteSpace(Search)) skins = skins.Where(s => Matches(s.Name + " " + s.ChampionName));
        skins = Sort switch
        {
            "name" => skins.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase),
            "name_desc" => skins.OrderByDescending(s => s.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => skins
        };
        return skins.Select(s => CardFactory.Skin(s, App)).ToArray();
    }
}

public sealed record SortOption(string Key, string Label);
public sealed record LineChip(string Name, string Label, int Count) { public bool HasCount => Count > 0; }

public sealed partial class ChampionsViewModel : CardGridPage
{
    public ChampionsViewModel(AppServices app, ShellViewModel shell) : base(app, shell) { }
    public override string Key => "champions";
    protected override string Count(int n) => Loc.Count("n_champions", n);

    public event Action<int>? ScrollRequested;
    [ObservableProperty] private string _role = "all";
    [ObservableProperty] private string _sort = "name";
    [ObservableProperty] private string _mode = "cards";
    public IReadOnlyList<SortOption> SortOptions => new SortOption[] { new("name", Loc.T("sort_name")), new("name_desc", Loc.T("sort_name_desc")), new("skins", Loc.T("sort_skins")) };
    public IReadOnlyList<LetterChip> Letters { get; private set; } = [];
    public bool IsTable => Mode == "table";

    partial void OnRoleChanged(string value) => Refresh();
    partial void OnSortChanged(string value) => Refresh();
    partial void OnModeChanged(string value) => OnPropertyChanged(nameof(IsTable));

    protected override IReadOnlyList<CardItem> Build()
    {
        IEnumerable<OfficialChampion> champions = App.Index.Champions;
        if (Role != "all") champions = champions.Where(c => c.Tags.Contains(Role));
        if (!string.IsNullOrWhiteSpace(Search)) champions = champions.Where(c => Matches(c.Name + " " + c.Title));
        champions = Sort switch
        {
            "name_desc" => champions.OrderByDescending(c => c.Name, StringComparer.CurrentCultureIgnoreCase),
            "skins" => champions.OrderByDescending(c => App.Index.SkinCountOf(c.Id)).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => champions.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
        };
        var list = champions.Select(c => CardFactory.Champion(c, App)).ToArray();
        var firstIndex = new Dictionary<char, int>();
        for (var i = 0; i < list.Length; i++) firstIndex.TryAdd(char.ToUpperInvariant(CatalogIndex.Fold(list[i].Title)[0]), i);
        Letters = Sort == "name" || Sort == "name_desc"
            ? Enumerable.Range('A', 26).Select(c => (char)c).Select(c => new LetterChip(c.ToString(), firstIndex.GetValueOrDefault(c, -1))).ToArray()
            : [];
        OnPropertyChanged(nameof(Letters));
        return list;
    }

    [RelayCommand] private void Jump(LetterChip? chip) { if (chip is { Index: >= 0 }) ScrollRequested?.Invoke(chip.Index); }
    [RelayCommand] private void SetRole(string? role) => Role = role ?? "all";
}

public sealed record LetterChip(string Letter, int Index)
{
    public bool IsEnabled => Index >= 0;
}
