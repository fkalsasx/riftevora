using System.Collections.Concurrent;
using SkinForge.Core.Models;
using SkinForge.Infrastructure.Catalog;
using SkinForge.Infrastructure.ModProviders;
using SkinForge.Infrastructure.Packaging;
using SkinForge.Infrastructure.SystemInfo;
using SkinForge.Infrastructure.Workspace;

namespace SkinForge.UI.Services;

/// <summary>Everything pages share: one catalog index, one package list, one idea of what is installed.</summary>
public sealed class AppServices
{
    public required DataDragonCatalogProvider Catalog { get; init; }
    public required RealLtkManagerAdapter Ltk { get; init; }
    public required LocalPackageLibrary Library { get; init; }
    public required WorkspaceStore Store { get; init; }
    public required string DataRoot { get; init; }

    private readonly ConcurrentDictionary<string, PackageInspection> _inspections = new();
    private readonly SemaphoreSlim _packageGate = new(1, 1);

    public CatalogIndex Index { get; private set; } = CatalogIndex.Empty;
    public IReadOnlyList<LocalPackage> Packages { get; private set; } = [];
    public IReadOnlyList<PackageConflict> Conflicts { get; private set; } = [];
    public string CatalogPatch { get; private set; } = "—";
    public CatalogState CatalogState => Catalog.State;
    public bool CatalogReady { get; private set; }

    public bool LtkInstalled { get; private set; }
    public string? LtkPath { get; private set; }
    public string? LtkVersion { get; private set; }
    public GameInstallation? Game { get; private set; }

    public event EventHandler? CatalogChanged;
    public event EventHandler? PackagesChanged;
    public event EventHandler? EnvironmentChanged;
    public event EventHandler? FavoritesChanged;
    public event EventHandler? HealthChanged;

    public bool HealthRan { get; set; }
    public int HealthWarnings { get; set; }
    public int HealthFailures { get; set; }
    public void RaiseHealthChanged() => HealthChanged?.Invoke(this, EventArgs.Empty);
    public void RaiseFavoritesChanged() => FavoritesChanged?.Invoke(this, EventArgs.Empty);

    public PackageInspection InspectionOf(LocalPackage package) => _inspections.GetValueOrDefault(package.Id) ?? PackageInspection.Opaque;
    public LocalPackage? Package(string id) => Packages.FirstOrDefault(p => p.Id == id);

    // ----- catalog -----
    public async Task LoadCatalogAsync(CancellationToken ct = default)
    {
        var champions = await Catalog.GetChampionsAsync(ct);
        CatalogPatch = await Catalog.GetCurrentPatchAsync(ct);
        var skins = await Catalog.GetAllSkinsAsync(ct);
        Index = await Task.Run(() => new CatalogIndex(champions, skins), ct);
        CatalogReady = true;
        CatalogChanged?.Invoke(this, EventArgs.Empty);
        // After a patch bump the per-champion skin lists are empty; fill them quietly and re-index when done.
        if (Index.Champions.Count > 0 && Index.Skins.Count < Index.Champions.Count * 2) _ = CompleteSkinsAsync(ct);
    }

    private async Task CompleteSkinsAsync(CancellationToken ct)
    {
        try
        {
            await Catalog.EnsureAllSkinsAsync(null, ct);
            var champions = await Catalog.GetChampionsAsync(ct);
            var skins = await Catalog.GetAllSkinsAsync(ct);
            Index = await Task.Run(() => new CatalogIndex(champions, skins), ct);
            CatalogChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) { }
    }

    public async Task RefreshCatalogAsync(CancellationToken ct = default)
    {
        await Catalog.RefreshCatalogAsync(ct);
        await LoadCatalogAsync(ct);
    }

    // ----- packages -----
    public async Task ReloadPackagesAsync(CancellationToken ct = default)
    {
        await _packageGate.WaitAsync(ct);
        try
        {
            var packages = await Library.LoadAsync();
            var alive = packages.Select(p => p.Id).ToHashSet();
            foreach (var stale in _inspections.Keys.Where(k => !alive.Contains(k)).ToArray()) _inspections.TryRemove(stale, out _);
            await Parallel.ForEachAsync(packages.Where(p => !_inspections.ContainsKey(p.Id)), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
                async (package, token) => _inspections[package.Id] = await PackageInspector.InspectAsync(package, token));
            Packages = packages.OrderByDescending(p => p.ImportedUtc ?? DateTime.MinValue).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
            Conflicts = await Task.Run(() => FindConflicts(Packages), ct);
        }
        finally { _packageGate.Release(); }
        PackagesChanged?.Invoke(this, EventArgs.Empty);
    }

    private IReadOnlyList<PackageConflict> FindConflicts(IReadOnlyList<LocalPackage> packages)
    {
        var found = new List<PackageConflict>();
        for (var i = 0; i < packages.Count; i++)
            for (var j = i + 1; j < packages.Count; j++)
                if (PackageInspector.Compare(packages[i], InspectionOf(packages[i]), packages[j], InspectionOf(packages[j])) is { } conflict) found.Add(conflict);
        return found.OrderByDescending(c => c.Severity).ThenByDescending(c => c.SharedFileCount).ToArray();
    }

    public IReadOnlyList<LocalPackage> PackagesFor(string championId) =>
        Packages.Where(p => InspectionOf(p).Champions.Contains(championId, StringComparer.OrdinalIgnoreCase)).ToArray();

    public bool HasConflict(LocalPackage package) => Conflicts.Any(c => c.First.Id == package.Id || c.Second.Id == package.Id);

    // ----- environment -----
    public async Task RefreshEnvironmentAsync()
    {
        if (Store.Data.LtkPath != null) await Ltk.DetectInstallationAsync(Store.Data.LtkPath);
        var info = await Ltk.GetStatusAsync();
        LtkInstalled = info.IsInstalled; LtkPath = info.ExecutablePath; LtkVersion = info.Version;
        Game = await Task.Run(() => GameLocator.Detect(Store.Data.GamePath));
        EnvironmentChanged?.Invoke(this, EventArgs.Empty);
    }

    // ----- favorites -----
    public bool IsFavorite(string reference) => Store.IsFavorite(reference);
    public bool ToggleFavorite(string reference)
    {
        var now = Store.ToggleFavorite(reference);
        FavoritesChanged?.Invoke(this, EventArgs.Empty);
        return now;
    }
}
