using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkinForge.Infrastructure.Packaging;
using SkinForge.Infrastructure.Workspace;
using SkinForge.UI.Localization;
using SkinForge.UI.Services;

namespace SkinForge.UI.ViewModels;

public sealed partial class ModsViewModel : CardGridPage
{
    public ModsViewModel(AppServices app, ShellViewModel shell) : base(app, shell)
    {
        App.PackagesChanged += (_, _) => ShellViewModel.RunOnUi(Refresh);
    }
    public override string Key => "mods";
    protected override string Count(int n) => Loc.Count("n_mods", n);

    [ObservableProperty] private string _sort = "recent";
    public IReadOnlyList<SortOption> SortOptions => new SortOption[] { new("recent", Loc.T("sort_recent")), new("name", Loc.T("sort_name")), new("size", Loc.T("sort_size")) };
    public string TotalSizeText => HealthCheckRunner.FormatBytes(App.Packages.Sum(p => p.SizeBytes));
    public bool HasAnyPackages => App.Packages.Count > 0;
    public bool LtkMissing => !App.LtkInstalled;
    partial void OnSortChanged(string value) => Refresh();

    protected override void Refresh() { base.Refresh(); OnPropertyChanged(nameof(TotalSizeText)); OnPropertyChanged(nameof(HasAnyPackages)); OnPropertyChanged(nameof(LtkMissing)); }
    public override Task ActivateAsync() { Refresh(); return Task.CompletedTask; }

    protected override IReadOnlyList<CardItem> Build()
    {
        IEnumerable<LocalPackage> packages = App.Packages;
        if (!string.IsNullOrWhiteSpace(Search)) packages = packages.Where(p => Matches(p.Name + " " + p.Author + " " + string.Join(' ', App.InspectionOf(p).Champions)));
        packages = Sort switch
        {
            "name" => packages.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
            "size" => packages.OrderByDescending(p => p.SizeBytes),
            _ => packages
        };
        return packages.Select(p => CardFactory.Mod(p, App)).ToArray();
    }

    [RelayCommand] private void Import() => Shell.ImportMods();
    [RelayCommand] private void OpenFolder() => Shell.OpenFolder(App.Library.Root);
}

public sealed partial class CollectionsViewModel : PageViewModel
{
    public CollectionsViewModel(AppServices app, ShellViewModel shell) : base(app, shell)
    {
        App.PackagesChanged += (_, _) => ShellViewModel.RunOnUi(Rebuild);
        App.FavoritesChanged += (_, _) => ShellViewModel.RunOnUi(Rebuild);
    }
    public override string Key => "collections";

    public ObservableCollection<CardItem> Collections { get; } = [];
    [ObservableProperty] private CollectionData? _selected;
    public IReadOnlyList<CardItem> Items { get; private set; } = [];
    public bool HasCollections => Collections.Count > 0;
    public bool IsDetail => Selected != null;
    public bool IsList => Selected == null;
    public string SelectedName => Selected?.Name ?? "";
    public string SelectedCount => Loc.Count("n_items", Items.Count);
    public bool SelectedEmpty => Selected != null && Items.Count == 0;

    public override Task ActivateAsync() { Rebuild(); return Task.CompletedTask; }

    public void Open(string id) { Selected = App.Store.Data.Collections.FirstOrDefault(c => c.Id == id); Rebuild(); }
    partial void OnSelectedChanged(CollectionData? value) { OnPropertyChanged(nameof(IsDetail)); OnPropertyChanged(nameof(IsList)); OnPropertyChanged(nameof(SelectedName)); }

    private void Rebuild()
    {
        Collections.Clear();
        foreach (var c in App.Store.Data.Collections)
        {
            var cards = c.Items.Select(r => CardFactory.FromRef(r, App)).Where(x => x != null).Cast<CardItem>().ToList();
            Collections.Add(new CardItem
            {
                Ref = "collection:" + c.Id, Kind = CardKind.Collection, Title = c.Name, Subtitle = Loc.Count("n_items", cards.Count),
                Count = cards.Count, Payload = new CollectionRef(c.Id), Mosaic = cards.Select(x => x.ImageUrl).Where(u => u.Length > 0).Take(4).ToArray(),
                Meta = c.CreatedUtc.ToLocalTime().ToString("d", Loc.Instance.Culture)
            });
        }
        if (Selected != null)
        {
            Selected = App.Store.Data.Collections.FirstOrDefault(c => c.Id == Selected.Id);
            Items = Selected?.Items.Select(r => CardFactory.FromRef(r, App)).Where(x => x != null).Cast<CardItem>().ToArray() ?? [];
        }
        else Items = [];
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private void Create() => Shell.ShowDialog(new PromptDialog(Loc.T("collection_new_title"), Loc.T("collection_new_message"), Loc.T("collection_name_hint"), "", Loc.T("collection_create"), name =>
    {
        var created = App.Store.CreateCollection(name);
        Shell.Notify(Loc.T("collection_created", created.Name), ToastTone.Success);
        Open(created.Id);
        return Task.CompletedTask;
    }));

    [RelayCommand] private void Back() { Selected = null; Rebuild(); }

    [RelayCommand]
    private void Rename()
    {
        if (Selected == null) return;
        var id = Selected.Id;
        Shell.ShowDialog(new PromptDialog(Loc.T("collection_rename_title"), "", Loc.T("collection_name_hint"), Selected.Name, Loc.T("save"), name =>
        {
            App.Store.RenameCollection(id, name); Rebuild(); return Task.CompletedTask;
        }));
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selected == null) return;
        var id = Selected.Id; var name = Selected.Name;
        Shell.Confirm(Loc.T("collection_delete_title"), Loc.T("collection_delete_message", name), Loc.T("delete"), true, () =>
        {
            App.Store.DeleteCollection(id); Selected = null; Rebuild();
            Shell.Notify(Loc.T("collection_deleted", name), ToastTone.Success);
            return Task.CompletedTask;
        });
    }

    [RelayCommand]
    private void RemoveFromCollection(CardItem? card)
    {
        if (Selected == null || card == null) return;
        App.Store.ToggleInCollection(Selected.Id, card.Ref);
        Rebuild();
    }

    [RelayCommand] private void OpenCollection(CardItem? card) { if (card?.Payload is CollectionRef r) Open(r.Id); }
}

public sealed partial class FavoritesViewModel : CardGridPage
{
    public FavoritesViewModel(AppServices app, ShellViewModel shell) : base(app, shell)
    {
        App.FavoritesChanged += (_, _) => ShellViewModel.RunOnUi(Refresh);
        App.PackagesChanged += (_, _) => ShellViewModel.RunOnUi(Refresh);
    }
    public override string Key => "favorites";
    protected override string Count(int n) => Loc.Count("n_items", n);

    [ObservableProperty] private string _filter = "all";
    partial void OnFilterChanged(string value) => Refresh();
    public override Task ActivateAsync() { Refresh(); return Task.CompletedTask; }

    protected override IReadOnlyList<CardItem> Build()
    {
        var cards = App.Store.Data.Favorites.Select(r => CardFactory.FromRef(r, App)).Where(c => c != null).Cast<CardItem>();
        cards = Filter switch
        {
            "champions" => cards.Where(c => c.Kind == CardKind.Champion),
            "skins" => cards.Where(c => c.Kind == CardKind.Skin),
            "mods" => cards.Where(c => c.Kind == CardKind.Mod),
            _ => cards
        };
        if (!string.IsNullOrWhiteSpace(Search)) cards = cards.Where(c => Matches(c.Title + " " + c.Subtitle));
        return cards.OrderBy(c => c.Kind).ThenBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}

public sealed class ConflictRow
{
    public required PackageConflict Conflict { get; init; }
    public string FirstName => Conflict.First.Name;
    public string SecondName => Conflict.Second.Name;
    public string FirstMeta => Conflict.First.Author;
    public string SecondMeta => Conflict.Second.Author;
    public bool IsHard => Conflict.Severity == ConflictSeverity.SharedFiles;
    public string Headline => IsHard ? Loc.T("tools_conflict_files", Conflict.SharedFileCount) : Loc.T("tools_conflict_wad");
    public string Detail => string.Join(", ", Conflict.SharedWads);
    public string Samples => string.Join("\n", Conflict.SampleFiles);
    public bool HasSamples => Conflict.SampleFiles.Count > 0;
}

public sealed class PackageRow
{
    public required LocalPackage Package { get; init; }
    public required string Champions { get; init; }
    public required string Files { get; init; }
    public string Name => Package.Name;
    public string Size => Package.SizeLabel;
    public string Format => Package.Format;
}

public sealed partial class ToolsViewModel : PageViewModel
{
    public ToolsViewModel(AppServices app, ShellViewModel shell) : base(app, shell)
    {
        App.PackagesChanged += (_, _) => ShellViewModel.RunOnUi(Rebuild);
    }
    public override string Key => "tools";
    public IReadOnlyList<ConflictRow> Conflicts { get; private set; } = [];
    public IReadOnlyList<PackageRow> Packages { get; private set; } = [];
    public bool HasConflicts => Conflicts.Count > 0;
    public bool NoConflicts => Conflicts.Count == 0 && App.Packages.Count > 1;
    public bool NeedMore => App.Packages.Count < 2;
    public bool HasPackages => Packages.Count > 0;
    public string SummaryKey => HasConflicts ? "warning" : NeedMore ? "info" : "ok";
    public string SummaryTitle => HasConflicts ? Loc.Count("tools_summary_conflicts", Conflicts.Count) : NeedMore ? Loc.T("tools_summary_need_more") : Loc.T("tools_summary_clean");
    public string SummaryText => HasConflicts ? Loc.T("tools_summary_conflicts_text") : NeedMore ? Loc.T("tools_summary_need_more_text") : Loc.T("tools_summary_clean_text", App.Packages.Count);
    [ObservableProperty] private bool _isScanning;

    public override Task ActivateAsync() { Rebuild(); return Task.CompletedTask; }

    private void Rebuild()
    {
        Conflicts = App.Conflicts.Select(c => new ConflictRow { Conflict = c }).ToArray();
        Packages = App.Packages.Select(p => new PackageRow
        {
            Package = p,
            Champions = string.Join(", ", App.InspectionOf(p).Champions.Select(n => App.Index.Champion(n)?.Name ?? n).Take(4)) is { Length: > 0 } s ? s : "—",
            Files = App.InspectionOf(p).CanInspect ? App.InspectionOf(p).EntryCount.ToString("N0", Loc.Instance.Culture) : Loc.T("mod_opaque_short")
        }).ToArray();
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private async Task Scan()
    {
        IsScanning = true;
        try { await Task.Delay(250); await App.ReloadPackagesAsync(); Shell.Notify(Loc.T("tools_scanned", App.Packages.Count), ToastTone.Success); }
        finally { IsScanning = false; }
    }

    [RelayCommand] private async Task OpenPackage(LocalPackage? package) { if (package != null) await Shell.OpenInLtkAsync(package); }
    [RelayCommand] private void InspectPackage(LocalPackage? package) { if (package != null) Shell.OpenMod(package); }
    [RelayCommand] private void OpenFolder() => Shell.OpenFolder(App.Library.Root);
}
