using System.Diagnostics;
using SkinForge.Application.Services;
using SkinForge.Core.Enums;
using SkinForge.Core.Models;
using Xunit;

namespace SkinForge.Tests;

public class PerformanceBenchmarkTests
{
    [Fact]
    public void Benchmark_1000_Skins_Fuzzy_Search_Responds_Sub_15ms()
    {
        var searchEngine = new FuzzySearchEngine();
        var skins = new List<Skin>(1000);

        for (int i = 0; i < 1000; i++)
        {
            var champ = (i % 5) switch
            {
                0 => "Ahri",
                1 => "Yasuo",
                2 => "Jinx",
                3 => "Zed",
                _ => "Aatrox"
            };

            skins.Add(new Skin($"skin-{i}", $"Custom Variant {i} - {champ} Special", champ)
            {
                Author = $"Author_{i % 20}",
                Category = ModCategory.FullOverhaul,
                IsInstalled = true
            });
        }

        var sw = Stopwatch.StartNew();
        var results = searchEngine.Search(skins, "Ahri Special");
        sw.Stop();

        Assert.NotEmpty(results);
        Assert.True(sw.ElapsedMilliseconds < 50, $"Search took {sw.ElapsedMilliseconds}ms, expected < 50ms.");
    }
}
