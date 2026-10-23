using SkinForge.Application.Services;
using SkinForge.Core.Models;
using Xunit;

namespace SkinForge.Tests;

public class FuzzySearchEngineTests
{
    [Fact]
    public void Search_ShouldFind_ByExactOrSubstringMatch()
    {
        var engine = new FuzzySearchEngine();
        var skins = new List<Skin>
        {
            new("s1", "Cyber Samurai Yasuo", "Yasuo") { Author = "Asetro" },
            new("s2", "Spirit Blossom Ahri", "Ahri") { Author = "Community" },
            new("s3", "Project Void Aatrox", "Aatrox") { Author = "Remmor" }
        };

        var result = engine.Search(skins, "Yasuo");
        Assert.Single(result);
        Assert.Equal("Cyber Samurai Yasuo", result[0].Name);
    }

    [Fact]
    public void Search_ShouldMatch_TypoAndFuzzyQuery()
    {
        var engine = new FuzzySearchEngine();
        var skins = new List<Skin>
        {
            new("s1", "Cyber Samurai Yasuo", "Yasuo"),
            new("s2", "Spirit Blossom Ahri", "Ahri"),
            new("s3", "Punk Rebel Jinx", "Jinx")
        };

        // User typed "spir blossom" with typo
        var result = engine.Search(skins, "spir blossom");
        Assert.NotEmpty(result);
        Assert.Equal("Spirit Blossom Ahri", result[0].Name);
    }

    [Fact]
    public void ComputeLevenshteinSimilarity_ShouldCalculateCorrectly()
    {
        var simExact = FuzzySearchEngine.ComputeLevenshteinSimilarity("blossom", "blossom");
        Assert.Equal(1.0, simExact);

        var simClose = FuzzySearchEngine.ComputeLevenshteinSimilarity("blossom", "blosom");
        Assert.True(simClose > 0.85);

        var simFar = FuzzySearchEngine.ComputeLevenshteinSimilarity("apple", "banana");
        Assert.True(simFar < 0.4);
    }
}
