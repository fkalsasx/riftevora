using CommunityToolkit.Mvvm.ComponentModel;
using SkinForge.Core.Models;
using SkinForge.Infrastructure.Packaging;
using SkinForge.UI.Localization;
using SkinForge.UI.Services;

namespace SkinForge.UI.ViewModels;

public enum CardKind { Champion, Skin, Mod, Collection }

public sealed record CollectionRef(string Id);

/// <summary>One tile in any grid. Plain data + two live flags; commands live on the shell.</summary>
public sealed partial class CardItem : ObservableObject
{
    public required string Ref { get; init; }
    public CardKind Kind { get; init; }
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string Meta { get; init; } = "";
    public string ImageUrl { get; init; } = "";
    public string Badge { get; init; } = "";
    public string BadgeTone { get; init; } = "accent";
    public string Badge2 { get; init; } = "";
    public double FocusY { get; init; } = 0.12;
    public int Count { get; init; }
    public object? Payload { get; init; }
    public bool IsMod => Kind == CardKind.Mod;
    public bool IsCollection => Kind == CardKind.Collection;
    public bool CanFavorite => Kind != CardKind.Collection;
    public IReadOnlyList<string> Mosaic { get; init; } = [];
    public int MosaicColumns => Mosaic.Count <= 1 ? 1 : 2;

    [ObservableProperty] private bool _isFavorite;
    [ObservableProperty] private bool _hasConflict;
    [ObservableProperty] private bool _isSelected;

    public string FolderLetter => Title.Length > 0 ? char.ToUpper(Title[0], Loc.Instance.Culture).ToString() : "#";
}

/// <summary>Turns catalog records into tiles. One place decides how every kind of thing is titled and captioned.</summary>
public static class CardFactory
{
    public static string LoadingArt(string championId, int num) => $"https://ddragon.leagueoflegends.com/cdn/img/champion/loading/{championId}_{num}.jpg";

    public static string RoleName(string role) => string.IsNullOrEmpty(role) ? "" : Loc.T("role_" + role.ToLowerInvariant());

    public static CardItem Champion(OfficialChampion c, AppServices app) => new()
    {
        Ref = "champion:" + c.Id, Kind = CardKind.Champion, Title = c.Name, Subtitle = c.Title,
        Badge = RoleName(c.Role), BadgeTone = "accent",
        Meta = app.Index.SkinCountOf(c.Id) > 0 ? Loc.Count("n_skins", app.Index.SkinCountOf(c.Id)) : "",
        ImageUrl = LoadingArt(c.Id, 0), Count = app.Index.SkinCountOf(c.Id), Payload = c, FocusY = 0.1,
        IsFavorite = app.IsFavorite("champion:" + c.Id)
    };

    public static string SkinTitle(OfficialSkin s) => s.Num == 0 ? Loc.T("skin_classic", s.ChampionName) : s.Name;

    public static CardItem Skin(OfficialSkin s, AppServices app)
    {
        var chroma = s.Rarity == "Kroma";
        return new CardItem
        {
            Ref = "skin:" + s.Id, Kind = CardKind.Skin, Title = SkinTitle(s), Subtitle = s.ChampionName,
            Badge = chroma ? Loc.T("badge_chroma") : "", BadgeTone = "info",
            Meta = chroma ? Loc.T("chroma_base_art") : (s.Num == 0 ? "" : app.Index.ChromasOf(s) is { Count: > 0 } list ? Loc.Count("n_chromas", list.Count) : ""),
            ImageUrl = s.LoadingUrl, Payload = s, FocusY = 0.1, IsFavorite = app.IsFavorite("skin:" + s.Id)
        };
    }

    public static CardItem Mod(LocalPackage p, AppServices app)
    {
        var inspection = app.InspectionOf(p);
        var champions = inspection.Champions.Select(n => app.Index.Champion(n)?.Name ?? n).Where(n => !n.StartsWith("Map", StringComparison.OrdinalIgnoreCase) && n != "Common").ToArray();
        var subtitle = string.IsNullOrWhiteSpace(p.Author) ? "" : p.Author + (string.IsNullOrWhiteSpace(p.Version) ? "" : " · v" + p.Version);
        return new CardItem
        {
            Ref = "mod:" + p.Id, Kind = CardKind.Mod, Title = p.Name, Subtitle = subtitle,
            Badge = p.Format, BadgeTone = p.Format == "FANTOME" ? "accent" : "info",
            Badge2 = champions.Length switch { 0 => "", 1 => champions[0], _ => champions[0] + " +" + (champions.Length - 1) },
            Meta = p.SizeLabel + (p.ImportedUtc is { } at ? " · " + at.ToLocalTime().ToString("d", Loc.Instance.Culture) : ""),
            ImageUrl = p.PreviewPath ?? "", FocusY = 0.3, Payload = p,
            IsFavorite = app.IsFavorite("mod:" + p.Id), HasConflict = app.HasConflict(p)
        };
    }

    public static CardItem? FromRef(string reference, AppServices app)
    {
        var colon = reference.IndexOf(':');
        if (colon < 0) return null;
        var id = reference[(colon + 1)..];
        return reference[..colon] switch
        {
            "champion" => app.Index.Champion(id) is { } c ? Champion(c, app) : null,
            "skin" => app.Index.Skin(id) is { } s ? Skin(s, app) : null,
            "mod" => app.Package(id) is { } p ? Mod(p, app) : null,
            _ => null
        };
    }
}
