using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkinForge.Core.Models;
using SkinForge.Infrastructure.Workspace;
using SkinForge.UI.Controls;
using SkinForge.UI.Localization;
using SkinForge.UI.Services;

namespace SkinForge.UI.ViewModels;

public sealed record HeroSlide(string SkinId, string Title, string ChampionName, string ImageUrl);

public sealed partial class HomeViewModel : PageViewModel
{
    // Spirit Blossom Ahri ships inside the app, so the first impression never depends on the network.
    private static readonly string[] FeaturedIds = ["103027", "84032", "99007", "222004", "429001", "412005", "236008", "360030", "99015"];
    private static readonly string BundledHero = new Uri(Path.Combine(AppContext.BaseDirectory, "Resources", "Artwork", "hero.jpg")).AbsoluteUri;
    private readonly DispatcherTimer _rotation = new() { Interval = TimeSpan.FromSeconds(9) };
    private readonly List<HeroSlide> _slides = [];
    private bool _slidesBuilt;

    public HomeViewModel(AppServices app, ShellViewModel shell) : base(app, shell)
    {
        _rotation.Tick += (_, _) => Step(1);
        App.CatalogChanged += (_, _) => ShellViewModel.RunOnUi(Rebuild);
        App.PackagesChanged += (_, _) => ShellViewModel.RunOnUi(Rebuild);
        App.FavoritesChanged += (_, _) => ShellViewModel.RunOnUi(Rebuild);
        App.EnvironmentChanged += (_, _) => ShellViewModel.RunOnUi(() => OnPropertyChanged(string.Empty));
    }

    public override string Key => "home";

    [ObservableProperty] private HeroSlide? _slide;
    [ObservableProperty] private int _slideIndex;
    public int SlideCount => _slides.Count;
    public IReadOnlyList<int> SlideDots => Enumerable.Range(0, _slides.Count).ToArray();

    public IReadOnlyList<CardItem> Recents { get; private set; } = [];
    public IReadOnlyList<CardItem> Picks { get; private set; } = [];
    public IReadOnlyList<CardItem> MyMods { get; private set; } = [];
    public bool HasRecents => Recents.Count > 0;
    public bool HasMods => MyMods.Count > 0;
    public bool ShowGuide => App.Packages.Count == 0;
    public string ShelfTitle => HasRecents ? Loc.T("home_recent") : Loc.T("home_picks");
    public string ShelfSub => HasRecents ? Loc.T("home_recent_sub") : Loc.T("home_picks_sub");

    public string ChampionsValue => App.Index.Champions.Count.ToString("N0", Loc.Instance.Culture);
    public string ChampionsSub => Loc.T("metric_champions_sub", App.CatalogPatch);
    public string SkinsValue => App.Index.BaseSkins.Count.ToString("N0", Loc.Instance.Culture);
    public string SkinsSub => Loc.T("metric_skins_sub", App.Index.ChromaSkins.Count.ToString("N0", Loc.Instance.Culture));
    public string ModsValue => App.Packages.Count.ToString("N0", Loc.Instance.Culture);
    public string ModsSub => App.Packages.Count == 0 ? Loc.T("metric_mods_empty") : Loc.T("metric_mods_sub", HealthCheckRunner.FormatBytes(App.Packages.Sum(p => p.SizeBytes)));
    public bool LtkReady => App.LtkInstalled;
    public string EngineValue => App.LtkInstalled ? Loc.T("metric_engine_ready") : Loc.T("metric_engine_missing");
    public string EngineSub => App.LtkInstalled ? (App.LtkVersion is { Length: > 0 } v ? "LTK Manager v" + v : "LTK Manager") : Loc.T("metric_engine_missing_sub");

    public override Task ActivateAsync() { Rebuild(); if (Motion.Enabled && _slides.Count > 1) _rotation.Start(); return Task.CompletedTask; }
    public void Deactivate() => _rotation.Stop();

    public void Rebuild()
    {
        BuildSlides();
        Recents = App.Store.Data.Recents.Select(r => CardFactory.FromRef(r.Ref, App)).Where(c => c != null).Cast<CardItem>().Take(12).ToArray();
        // Day-stable "picks": same suggestions all day, a fresh set tomorrow.
        var random = new Random(DateTime.Today.GetHashCode());
        Picks = App.Index.BaseSkins.Where(s => s.Num > 0).OrderBy(_ => random.Next()).Take(12).Select(s => CardFactory.Skin(s, App)).ToArray();
        MyMods = App.Packages.Take(8).Select(p => CardFactory.Mod(p, App)).ToArray();
        OnPropertyChanged(string.Empty);
    }

    private void BuildSlides()
    {
        if (_slidesBuilt && _slides.Count > 1) return;
        _slides.Clear();
        _slides.Add(new HeroSlide("103027", "Spirit Blossom Ahri", "Ahri", BundledHero));
        foreach (var id in FeaturedIds.Skip(1))
            if (App.Index.Skin(id) is { } skin)
            {
                var url = skin.SplashUrl;
                _slides.Add(new HeroSlide(id, skin.Name, skin.ChampionName, url));
            }
        _slidesBuilt = _slides.Count > 1;
        Slide = _slides[Math.Min(SlideIndex, _slides.Count - 1)];
        OnPropertyChanged(nameof(SlideCount)); OnPropertyChanged(nameof(SlideDots));
    }

    private void Step(int delta)
    {
        if (_slides.Count < 2) return;
        // Skip slides whose artwork has not been fetched (offline): a blank hero is worse than a shorter carousel.
        for (var attempt = 1; attempt <= _slides.Count; attempt++)
        {
            var index = ((SlideIndex + delta * attempt) % _slides.Count + _slides.Count) % _slides.Count;
            var candidate = _slides[index];
            if (index == 0 || ArtworkLoader.TryGetCached(candidate.ImageUrl, 1280, out _) || File.Exists(CachePath(candidate.ImageUrl)))
            {
                SlideIndex = index; Slide = candidate; return;
            }
            _ = ArtworkLoader.GetAsync(candidate.ImageUrl, 1280); // warm it for next time
        }
    }

    private static string CachePath(string url) =>
        Path.Combine(ArtworkLoader.CacheDirectory, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url))) + ".img");

    [RelayCommand] private void NextSlide() { _rotation.Stop(); Step(1); if (Motion.Enabled) _rotation.Start(); }
    [RelayCommand] private void PreviousSlide() { _rotation.Stop(); Step(-1); if (Motion.Enabled) _rotation.Start(); }
    [RelayCommand] private void ShowSlide(int index) { if (index >= 0 && index < _slides.Count) { SlideIndex = index; Slide = _slides[index]; } }

    [RelayCommand]
    private void OpenSlide()
    {
        if (Slide == null || App.Index.Skin(Slide.SkinId) is not { } skin || App.Index.Champion(skin.ChampionId) is not { } champion) return;
        Shell.OpenChampion(champion, skin.Id);
    }

    [RelayCommand] private void Explore() => Shell.Navigate("discover");
    [RelayCommand] private void Import() => Shell.ImportMods();
    [RelayCommand] private void SeeMods() => Shell.Navigate("mods");
    [RelayCommand] private void Compare() => Shell.Navigate("compare");
    [RelayCommand] private void Surprise()
    {
        var choices = App.Index.BaseSkins.Where(s => s.Num > 0 && s.Id != Slide?.SkinId).ToArray();
        if (choices.Length == 0) return;
        var skin = choices[Random.Shared.Next(choices.Length)];
        _rotation.Stop();
        Slide = new HeroSlide(skin.Id, skin.Name, skin.ChampionName, skin.SplashUrl);
    }
}
