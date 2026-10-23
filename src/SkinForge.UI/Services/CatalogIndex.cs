using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SkinForge.Core.Models;

namespace SkinForge.UI.Services;

public sealed record SkinLine(string Name, int Count);

public sealed record ChromaChoice(OfficialSkin Skin, string Label);

/// <summary>
/// Read-only, search-friendly view of the catalog. Built once off the UI thread, then shared by every page.
/// Chromas are grouped under the skin whose artwork they borrow, so the UI can show "Popstar Ahri → 6 colors".
/// </summary>
public sealed class CatalogIndex
{
    public static CatalogIndex Empty { get; } = new([], []);

    private readonly Dictionary<string, OfficialChampion> _championById;
    private readonly Dictionary<string, OfficialSkin> _skinById;
    private readonly Dictionary<string, List<OfficialSkin>> _chromasByParent = new();
    private readonly Dictionary<string, OfficialSkin> _parentByChroma = new();
    private readonly List<SearchEntry> _entries = [];

    public IReadOnlyList<OfficialChampion> Champions { get; }
    public IReadOnlyList<OfficialSkin> Skins { get; }
    public IReadOnlyList<OfficialSkin> BaseSkins { get; }
    public IReadOnlyList<OfficialSkin> ChromaSkins { get; }
    public IReadOnlyList<SkinLine> Lines { get; }
    public IReadOnlyDictionary<string, string> LineBySkinId => _lineBySkin;
    private readonly Dictionary<string, string> _lineBySkin = new();

    private sealed record SearchEntry(string Folded, string Reference, bool IsChampion, int Weight);

    public CatalogIndex(IReadOnlyList<OfficialChampion> champions, IReadOnlyList<OfficialSkin> skins)
    {
        Champions = champions; Skins = skins;
        _championById = champions.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        _skinById = new Dictionary<string, OfficialSkin>(skins.Count);
        foreach (var s in skins) _skinById[s.Id] = s;
        BaseSkins = skins.Where(s => s.Rarity != "Kroma").ToArray();
        ChromaSkins = skins.Where(s => s.Rarity == "Kroma").ToArray();

        // The catalog mapper gives a chroma its parent's artwork URL, which is the most reliable parent link.
        var baseByArt = new Dictionary<(string, string), OfficialSkin>();
        foreach (var s in BaseSkins) baseByArt.TryAdd((s.ChampionId, s.SplashUrl), s);
        foreach (var chroma in ChromaSkins)
        {
            if (!baseByArt.TryGetValue((chroma.ChampionId, chroma.SplashUrl), out var parent)) continue;
            _parentByChroma[chroma.Id] = parent;
            if (!_chromasByParent.TryGetValue(parent.Id, out var list)) _chromasByParent[parent.Id] = list = [];
            list.Add(chroma);
        }

        foreach (var group in BaseSkins.Where(s => s.Num > 0).GroupBy(s => s.ChampionId)) _baseCount[group.Key] = group.Count();
        var lines = new Dictionary<string, (string Display, int Count)>();
        foreach (var skin in BaseSkins.Where(s => s.Num > 0))
        {
            var line = LineOf(skin);
            if (line.Length == 0) continue;
            _lineBySkin[skin.Id] = line;
            var key = Fold(line);
            lines[key] = lines.TryGetValue(key, out var current) ? (current.Display, current.Count + 1) : (line, 1);
        }
        Lines = lines.Values.Where(l => l.Count >= 6).OrderByDescending(l => l.Count).ThenBy(l => l.Display).Select(l => new SkinLine(l.Display, l.Count)).ToArray();

        foreach (var champion in champions) _entries.Add(new SearchEntry(Fold(champion.Name + " " + champion.Title), "champion:" + champion.Id, true, 2));
        foreach (var skin in BaseSkins) if (skin.Num > 0) _entries.Add(new SearchEntry(Fold(skin.Name), "skin:" + skin.Id, false, 1));
    }

    public OfficialChampion? Champion(string id) => _championById.GetValueOrDefault(id);
    public OfficialSkin? Skin(string id) => _skinById.GetValueOrDefault(id);
    public OfficialSkin? ParentOf(OfficialSkin chroma) => _parentByChroma.GetValueOrDefault(chroma.Id);
    public IReadOnlyList<OfficialSkin> ChromasOf(OfficialSkin skin) => _chromasByParent.TryGetValue(skin.Id, out var list) ? list : [];

    /// <summary>Skins a champion really has: the classic look and chromas are not counted.</summary>
    public int SkinCountOf(string championId) => _baseCount.GetValueOrDefault(championId);
    private readonly Dictionary<string, int> _baseCount = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<OfficialSkin> SkinsOf(string championId) => Skins.Where(s => s.ChampionId.Equals(championId, StringComparison.OrdinalIgnoreCase)).ToArray();

    /// <summary>The label shown on a chroma chip: the colour name in brackets, or the full name for legacy variants.</summary>
    public string ChromaLabel(OfficialSkin chroma)
    {
        var open = chroma.Name.LastIndexOf('(');
        return open >= 0 && chroma.Name.EndsWith(')') ? chroma.Name[(open + 1)..^1] : chroma.Name;
    }

    public string LineOf(OfficialSkin skin)
    {
        var line = Regex.Replace(skin.Name.Replace(skin.ChampionName, "", StringComparison.OrdinalIgnoreCase), @"\s+", " ").Trim(' ', '-', ':', '.');
        if (line.StartsWith("Prestige ", StringComparison.OrdinalIgnoreCase) && line.Length > 9) line = line[9..];
        return line;
    }

    /// <summary>Ranked matches: exact word starts beat substrings; champions beat skins on ties.</summary>
    public IReadOnlyList<string> Search(string query, int limit = 40)
    {
        var q = Fold(query);
        if (q.Length == 0) return [];
        var tokens = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var scored = new List<(int Score, SearchEntry Entry)>();
        foreach (var entry in _entries)
        {
            var score = 0; var ok = true;
            foreach (var token in tokens)
            {
                var at = entry.Folded.IndexOf(token, StringComparison.Ordinal);
                if (at < 0) { ok = false; break; }
                score += at == 0 ? 30 : entry.Folded[at - 1] == ' ' ? 20 : 6;
            }
            if (!ok) continue;
            scored.Add((score + entry.Weight * 4 - Math.Min(entry.Folded.Length, 40) / 8, entry));
        }
        return scored.OrderByDescending(s => s.Score).ThenBy(s => s.Entry.Folded.Length).Take(limit).Select(s => s.Entry.Reference).ToArray();
    }

    /// <summary>Lowercase, accent-free text so "İrelia", "irelia" and "Irelia" are the same word.</summary>
    public static string Fold(string text)
    {
        var decomposed = text.Replace('ı', 'i').Replace('İ', 'I').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) builder.Append(char.ToLowerInvariant(ch));
        return builder.ToString().Trim();
    }
}
