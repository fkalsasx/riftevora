using SkinForge.Core.Interfaces;
using SkinForge.Core.Models;

namespace SkinForge.Application.Services;

public class FuzzySearchEngine : ISearchEngine
{
    public IReadOnlyList<Skin> Search(IEnumerable<Skin> source, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return source.ToList();
        }

        var cleanQuery = query.Trim().ToLowerInvariant();
        var queryTokens = cleanQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var scored = new List<(Skin Skin, double Score)>();

        foreach (var skin in source)
        {
            var score = CalculateMatchScore(skin, cleanQuery, queryTokens);
            if (score > 0)
            {
                scored.Add((skin, score));
            }
        }

        return scored
            .OrderByDescending(x => x.Score)
            .Select(x => x.Skin)
            .ToList();
    }

    private static double CalculateMatchScore(Skin skin, string fullQuery, string[] queryTokens)
    {
        var nameLower = skin.Name.ToLowerInvariant();
        var champLower = skin.Champion.ToLowerInvariant();
        var authorLower = skin.Author.ToLowerInvariant();
        var tagsLower = skin.Tags.Select(t => t.ToLowerInvariant()).ToList();

        // Exact full match
        if (nameLower.Equals(fullQuery, StringComparison.OrdinalIgnoreCase)) return 100.0;
        if (champLower.Equals(fullQuery, StringComparison.OrdinalIgnoreCase)) return 95.0;

        // Substring contains
        if (nameLower.Contains(fullQuery)) return 80.0;
        if (champLower.Contains(fullQuery)) return 75.0;
        if (authorLower.Contains(fullQuery)) return 60.0;

        // Check if all tokens match or fuzzy-match
        double tokenScore = 0;
        int matchedTokens = 0;

        foreach (var token in queryTokens)
        {
            if (nameLower.Contains(token))
            {
                tokenScore += 30;
                matchedTokens++;
            }
            else if (champLower.Contains(token))
            {
                tokenScore += 25;
                matchedTokens++;
            }
            else if (authorLower.Contains(token))
            {
                tokenScore += 15;
                matchedTokens++;
            }
            else if (tagsLower.Any(t => t.Contains(token)))
            {
                tokenScore += 20;
                matchedTokens++;
            }
            else
            {
                // Levenshtein fuzzy match check on words
                var words = nameLower.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Concat(champLower.Split(' ', StringSplitOptions.RemoveEmptyEntries));

                double bestWordSimilarity = 0;
                foreach (var word in words)
                {
                    var similarity = ComputeLevenshteinSimilarity(token, word);
                    if (similarity > bestWordSimilarity)
                    {
                        bestWordSimilarity = similarity;
                    }
                }

                if (bestWordSimilarity >= 0.70) // 70% fuzzy threshold
                {
                    tokenScore += bestWordSimilarity * 20;
                    matchedTokens++;
                }
            }
        }

        if (matchedTokens == queryTokens.Length)
        {
            return tokenScore;
        }

        return matchedTokens > 0 ? tokenScore * 0.4 : 0;
    }

    public static double ComputeLevenshteinSimilarity(string s, string t)
    {
        if (string.IsNullOrEmpty(s)) return string.IsNullOrEmpty(t) ? 1.0 : 0.0;
        if (string.IsNullOrEmpty(t)) return 0.0;

        int n = s.Length;
        int m = t.Length;
        var d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; i++) d[i, 0] = i;
        for (int j = 0; j <= m; j++) d[0, j] = j;

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost
                );
            }
        }

        int distance = d[n, m];
        int maxLength = Math.Max(n, m);
        return 1.0 - ((double)distance / maxLength);
    }
}
