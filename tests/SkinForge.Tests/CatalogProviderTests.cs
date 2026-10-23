using Microsoft.Extensions.Logging.Abstractions;
using SkinForge.Infrastructure.Catalog;
using Xunit;

namespace SkinForge.Tests;

public class CatalogProviderTests : IDisposable
{
    private readonly string _testCacheDir;
    private readonly DataDragonCatalogProvider _provider;

    public CatalogProviderTests()
    {
        _testCacheDir = Path.Combine(Path.GetTempPath(), "SkinForge_CatalogTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_testCacheDir);

        _provider = new DataDragonCatalogProvider(_testCacheDir, new NullLogger<DataDragonCatalogProvider>());
    }

    [Fact]
    public async Task Catalog_Provides_Real_League_Champions_Without_Mock_Names()
    {
        var champions = await _provider.GetChampionsAsync();

        Assert.NotEmpty(champions);
        Assert.Contains(champions, c => c.Name == "Ahri");
        Assert.Contains(champions, c => c.Name == "Yasuo");
        Assert.Contains(champions, c => c.Name == "Jinx");

        var ahri = champions.First(c => c.Name == "Ahri");
        Assert.Equal("The Nine-Tailed Fox", ahri.Title);
        Assert.Equal("Mage", ahri.Role);
        // Summary responses have no skin count; the detail response is authoritative.
        var skins = await _provider.GetChampionSkinsAsync(ahri.Id);
        Assert.True(skins.Count >= 10);
    }

    [Fact]
    public async Task Champion_Skins_Return_Official_Skins_With_Splashes()
    {
        var skins = await _provider.GetChampionSkinsAsync("Ahri");

        Assert.NotEmpty(skins);
        Assert.Contains(skins, s => s.Name == "Original");
        Assert.Contains(skins, s => s.Name == "Spirit Blossom Ahri" || s.Name == "K/DA Ahri" || s.Name == "Star Guardian Ahri");

        foreach (var skin in skins)
        {
            Assert.StartsWith("https://ddragon.leagueoflegends.com/", skin.SplashUrl);
            Assert.Equal("Riot Data Dragon", skin.Source);
        }
    }

    [Fact]
    public async Task Patch_Check_Returns_Valid_Version()
    {
        var patch = await _provider.GetCurrentPatchAsync();
        Assert.False(string.IsNullOrWhiteSpace(patch));
        Assert.Contains(".", patch);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testCacheDir))
        {
            try { Directory.Delete(_testCacheDir, true); } catch { }
        }
    }
}
