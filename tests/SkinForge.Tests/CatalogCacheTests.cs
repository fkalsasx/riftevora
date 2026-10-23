using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SkinForge.Core.Models;
using SkinForge.Infrastructure.Catalog;
using Xunit;

namespace SkinForge.Tests;

public sealed class CatalogCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SkinForge-catalog-" + Guid.NewGuid().ToString("N"));
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Noncontiguous_Skin_Ids_Are_Preserved_And_Chroma_Art_Uses_Parent()
    {
        using var http = new HttpClient(new Handler(r => Json(r.RequestUri!.AbsolutePath switch
        {
            "/api/versions.json" => "[\"99.1.1\"]",
            var p when p.EndsWith("/champion.json") => """{"data":{"Ahri":{"id":"Ahri","key":"103","name":"Ahri","title":"the Nine-Tailed Fox","blurb":"Test","tags":["Mage"]}}}""",
            _ => """{"data":{"Ahri":{"skins":[{"id":"103000","num":0,"name":"default"},{"id":"103027","num":27,"name":"Spirit Blossom Ahri"},{"id":"103028","num":28,"name":"Spirit Blossom Ahri (Ruby)"}]}}}"""
        })));
        var catalog = new DataDragonCatalogProvider(_root, NullLogger<DataDragonCatalogProvider>.Instance, http);
        var skins = await catalog.GetChampionSkinsAsync("Ahri");
        Assert.Equal(new[] { 0, 27, 28 }, skins.Select(s => s.Num));
        Assert.EndsWith("Ahri_27.jpg", skins[1].LoadingUrl);
        Assert.Equal(skins[1].SplashUrl, skins[2].SplashUrl);
        Assert.Equal("Kroma", skins[2].Rarity);
        Assert.Empty(skins[1].Chromas); // No invented color metadata.
        Assert.Equal(3, (await catalog.GetChampionDetailsAsync("Ahri"))!.SkinCount);
    }

    [Fact]
    public async Task Offline_Refresh_Keeps_Cached_Patch_And_Real_Skin_Numbers()
    {
        Directory.CreateDirectory(_root);
        var snapshot = new CatalogSnapshot
        {
            Patch = "16.21.1",
            Champions = [new("Ahri", "103", "Ahri", "Nine-Tailed Fox", "Mage", "", "", "", 1, ["Mage"])],
            Skins = new() { ["Ahri"] = [new("103027", 27, "Spirit Blossom Ahri", "Ahri", "Ahri", 0, [], "real/splash/27.jpg", "real/loading/27.jpg", "", "")] }
        };
        await File.WriteAllTextAsync(Path.Combine(_root, "catalog-v2.json"), JsonSerializer.Serialize(snapshot));
        using var http = new HttpClient(new Handler(_ => throw new HttpRequestException("Offline")));
        var catalog = new DataDragonCatalogProvider(_root, NullLogger<DataDragonCatalogProvider>.Instance, http);
        Assert.Equal("16.21.1", await catalog.GetCurrentPatchAsync());
        await catalog.RefreshCatalogAsync();
        Assert.Equal(CatalogState.Offline, catalog.State);
        Assert.Equal(27, Assert.Single(await catalog.GetChampionSkinsAsync("Ahri")).Num);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
