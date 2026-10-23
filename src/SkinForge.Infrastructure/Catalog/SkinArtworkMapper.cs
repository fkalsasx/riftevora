using System.Text.RegularExpressions;
using SkinForge.Core.Models;

namespace SkinForge.Infrastructure.Catalog;

/// <summary>Riot's older chroma names and the historical FiddleSticks CDN casing.</summary>
public static class SkinArtworkMapper
{
    private static readonly Dictionary<string, string> LegacyChromas = new()
    {
        ["Ashe"] = "Worlds 2017 Ashe Chroma|Worlds 2017 Ashe",
        ["Bard"] = "Bard Bloom|Original", ["Blitzcrank"] = "Blitzcrank Steel|Original",
        ["Caitlyn"] = "Caitlyn Rebel|Original", ["Cassiopeia"] = "Cassiopeia Curse|Original",
        ["Darius"] = "Darius Forge|Original", ["Fizz"] = "Fizz Scorch|Original",
        ["Garen"] = "Garen Noble|Original", ["Jax"] = "Nemesis Jax Sand|Nemesis Jax",
        ["Karthus"] = "Grim Reaper Karthus Plague|Grim Reaper Karthus",
        ["LeeSin"] = "Dragon Fist Lee Sin Duel|Dragon Fist Lee Sin",
        ["Leona"] = "Pool Party Leona Dawn|Pool Party Leona", ["Lucian"] = "Lucian Prime|Original",
        ["MasterYi"] = "Headhunter Master Yi Strike|Headhunter Master Yi",
        ["Morgana"] = "Morgana Haunt|Original", ["Nami"] = "Koi Nami Element|Koi Nami",
        ["Nasus"] = "Dreadknight Nasus Plague|Dreadknight Nasus",
        ["Tristana"] = "Rocket Girl Tristana Punk|Rocket Girl Tristana",
        ["Vayne"] = "Dragonslayer Vayne Chaos|Dragonslayer Vayne", ["Zac"] = "Zac Sweet|Original"
    };
    private static string Canonical(string name) => Regex.Replace(name.Trim(), @"\s+", " ").ToLowerInvariant();
    public static List<OfficialSkin> Map(IEnumerable<OfficialSkin> input)
    {
        var skins = input.Select(s => s.ChampionId.Equals("Fiddlesticks", StringComparison.OrdinalIgnoreCase)
            ? s with { SplashUrl = s.SplashUrl.Replace("/Fiddlesticks_", "/FiddleSticks_"), LoadingUrl = s.LoadingUrl.Replace("/Fiddlesticks_", "/FiddleSticks_") } : s).ToList();
        var byName = skins.GroupBy(s => Canonical(s.Name)).ToDictionary(g => g.Key, g => g.First());
        return skins.Select(s =>
        {
            string? parentName = s.Name.Contains(" (") ? s.Name.Split(" (")[0] : null;
            if (LegacyChromas.TryGetValue(s.ChampionId, out var definition))
            {
                var parts = definition.Split('|');
                if (s.Name.StartsWith(parts[0], StringComparison.OrdinalIgnoreCase)) parentName = parts[1];
            }
            return parentName != null && byName.TryGetValue(Canonical(parentName), out var parent) && parent.Id != s.Id
                ? s with { SplashUrl = parent.SplashUrl, LoadingUrl = parent.LoadingUrl, Rarity = "Kroma", ReleaseInfo = "Temel kostüm görseli" } : s;
        }).ToList();
    }
}
