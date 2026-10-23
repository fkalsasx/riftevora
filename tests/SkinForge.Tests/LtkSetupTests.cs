using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkinForge.Infrastructure.ModProviders;
using SkinForge.Infrastructure.SystemInfo;
using Xunit;

namespace SkinForge.Tests;

public sealed class LtkSetupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Riftevora-setup-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Installer = "official installer test bytes"u8.ToArray();
    public LtkSetupTests() => Directory.CreateDirectory(_root);

    private static string Release(string? url = null, string? digest = null, bool prerelease = false, string name = "LTK.Manager_1.30.4_x64-setup.exe") => JsonSerializer.Serialize(new
    {
        draft = false, prerelease,
        assets = new[] { new { name, size = Installer.Length,
            browser_download_url = url ?? "https://github.com/LeagueToolkit/ltk-manager/releases/download/v1.30.4/" + name,
            digest = digest ?? "sha256:" + Convert.ToHexString(SHA256.HashData(Installer)) } }
    });

    private static HttpClient Client(string metadata, byte[]? bytes = null, HttpStatusCode downloadStatus = HttpStatusCode.OK) => new(new Handler((request, _) =>
        Task.FromResult(request.RequestUri!.AbsoluteUri == LtkSetupService.ReleaseApi
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(metadata) }
            : new HttpResponseMessage(downloadStatus) { Content = new ByteArrayContent(bytes ?? Installer) })));

    [Theory]
    [InlineData("https://example.com/installer.exe")]
    [InlineData("https://github.com.evil.test/LeagueToolkit/ltk-manager/releases/download/x/setup.exe")]
    [InlineData("http://github.com/LeagueToolkit/ltk-manager/releases/download/x/setup.exe")]
    [InlineData("https://github.com/another-owner/ltk-manager/releases/download/x/setup.exe")]
    public void Rejects_Unofficial_Installer_Urls(string url) =>
        Assert.Equal("setup_release_invalid", Assert.Throws<LtkSetupException>(() => LtkSetupService.SelectInstaller(Release(url: url))).Message);

    [Theory]
    [InlineData("sha256:unknown")]
    [InlineData("sha512:0007230f8772c846d586f456aedee5846f7dbf4719715940e73bb073ae4eff23")]
    public void Requires_A_Complete_Sha256_Digest(string digest) =>
        Assert.Throws<LtkSetupException>(() => LtkSetupService.SelectInstaller(Release(digest: digest)));

    [Fact] public void Ignores_Preview_Releases() => Assert.Throws<LtkSetupException>(() => LtkSetupService.SelectInstaller(Release(prerelease: true)));

    [Theory]
    [InlineData("{broken")]
    [InlineData("{}")]
    [InlineData("{\"draft\":false,\"prerelease\":false,\"assets\":42}")]
    public void Malformed_Release_Metadata_Returns_An_Actionable_Error(string metadata) =>
        Assert.Equal("setup_release_invalid", Assert.Throws<LtkSetupException>(() => LtkSetupService.SelectInstaller(metadata)).Message);

    [Fact]
    public async Task Verifies_The_Download_Before_Running_The_Installer_And_Cleans_Up()
    {
        using var http = Client(Release());
        var ran = false;
        var setup = new LtkSetupService(http, async (file, _) =>
        {
            Assert.Equal(Installer, await File.ReadAllBytesAsync(file));
            Assert.DoesNotContain(".part", file);
            ran = true; return 0;
        });
        await setup.InstallAsync(_root);
        Assert.True(ran);
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task Hash_Mismatch_Is_Rejected_Without_Executing_Anything()
    {
        using var http = Client(Release(digest: "sha256:" + new string('0', 64)));
        var ran = false;
        var setup = new LtkSetupService(http, (_, _) => { ran = true; return Task.FromResult(0); });
        var error = await Assert.ThrowsAsync<LtkSetupException>(() => setup.InstallAsync(_root));
        Assert.Equal("setup_integrity_failed", error.Message);
        Assert.False(ran); Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task Truncated_Download_Is_Rejected_And_Partial_Is_Removed()
    {
        using var http = Client(Release(), Installer[..^1]);
        var error = await Assert.ThrowsAsync<LtkSetupException>(() => new LtkSetupService(http).DownloadInstallerAsync(_root));
        Assert.Equal("setup_integrity_failed", error.Message);
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task Failed_Installer_Does_Not_Report_Success()
    {
        using var http = Client(Release());
        var error = await Assert.ThrowsAsync<LtkSetupException>(() => new LtkSetupService(http, (_, _) => Task.FromResult(1)).InstallAsync(_root));
        Assert.Equal("setup_install_failed", error.Message);
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task Cancelled_Download_Does_Not_Run_Installer_Or_Leave_A_Partial()
    {
        using var cancellation = new CancellationTokenSource();
        using var http = Client(Release());
        var setup = new LtkSetupService(http, (_, _) => throw new InvalidOperationException("must not run"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => setup.InstallAsync(_root,
            new ProgressSink(p => { if (p.Stage == "download") cancellation.Cancel(); }), cancellation.Token));
        Assert.Empty(Directory.GetFiles(_root));
    }

    private string Game(string name = "League")
    {
        var game = Path.Combine(_root, name); Directory.CreateDirectory(Path.Combine(game, "Game"));
        File.WriteAllText(Path.Combine(game, "Game", "League of Legends.exe"), "");
        return game;
    }

    [Fact]
    public async Task Adds_Game_To_Official_Ltk_Format_Without_Losing_Preferences_And_Backs_Up()
    {
        var file = Path.Combine(_root, "settings.json");
        const string original = """{"leaguePath":null,"theme":"dark","firstRunComplete":true,"accentColor":{"preset":"blue"},"unknownFutureKey":[1,2]}""";
        await File.WriteAllTextAsync(file, original);
        var game = Game();
        await new LtkGameConfiguration(file, () => false).ConfigureAsync(Path.Combine(game, "Game"));
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(file))!;
        Assert.Equal(game, saved["leaguePath"]!.GetValue<string>());
        Assert.Equal("dark", saved["theme"]!.GetValue<string>());
        Assert.Equal(2, saved["unknownFutureKey"]!.AsArray().Count);
        Assert.Equal(original, await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(_root, "*.bak"))));
    }

    [Fact]
    public async Task First_Install_Writes_Required_Defaults_And_Game_Path()
    {
        var file = Path.Combine(_root, "ltk", "settings.json");
        var game = Game();
        await new LtkGameConfiguration(file, () => false).ConfigureAsync(game);
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(file))!;
        Assert.Equal(game, saved["leaguePath"]!.GetValue<string>());
        Assert.False(saved["firstRunComplete"]!.GetValue<bool>());
        Assert.Equal("system", saved["theme"]!.GetValue<string>());
        Assert.IsType<JsonObject>(saved["accentColor"]);
    }

    [Fact]
    public async Task Running_Ltk_Preserves_Settings_And_Returns_Actionable_Error()
    {
        var file = Path.Combine(_root, "settings.json");
        await File.WriteAllTextAsync(file, "{}");
        var error = await Assert.ThrowsAsync<LtkSetupException>(() => new LtkGameConfiguration(file, () => true).ConfigureAsync(Game()));
        Assert.Equal("setup_close_ltk", error.Message);
        Assert.Equal("{}", await File.ReadAllTextAsync(file));
        Assert.Empty(Directory.GetFiles(_root, "*.bak"));
    }

    [Fact]
    public async Task Matching_Game_Path_Needs_No_Restart_And_No_Extra_Backup()
    {
        var file = Path.Combine(_root, "settings.json"); var game = Game();
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new { leaguePath = game.Replace('\\', '/') }));
        await new LtkGameConfiguration(file, () => true).ConfigureAsync(game);
        Assert.Empty(Directory.GetFiles(_root, "*.bak"));
    }

    [Fact]
    public async Task Corrupt_Ltk_Settings_Are_Preserved()
    {
        var file = Path.Combine(_root, "settings.json"); await File.WriteAllTextAsync(file, "{broken");
        var error = await Assert.ThrowsAsync<LtkSetupException>(() => new LtkGameConfiguration(file, () => false).ConfigureAsync(Game()));
        Assert.Equal("setup_config_invalid", error.Message);
        Assert.Equal("{broken", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task Client_Only_Installation_Cannot_Be_Registered_As_Playable_Game()
    {
        var game = Path.Combine(_root, "client-only"); Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "LeagueClient.exe"), "");
        var error = await Assert.ThrowsAsync<LtkSetupException>(() => new LtkGameConfiguration(Path.Combine(_root, "settings.json"), () => false).ConfigureAsync(game));
        Assert.Equal("settings_game_invalid", error.Message);
    }

    [Fact]
    public void Riot_Record_Detects_Custom_Folder_Names_And_Game_Exe()
    {
        var game = Game("My custom install"); var manifest = Path.Combine(_root, "RiotClientInstalls.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new { associated_client = new Dictionary<string, string> { [game] = "riot.exe" } }));
        var found = GameLocator.Detect(manifestPath: manifest);
        Assert.Equal(game, found!.RootDirectory);
        Assert.Equal(game, GameLocator.Normalize(Path.Combine(game, "Game", "League of Legends.exe")));
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct); }
    private sealed class ProgressSink(Action<LtkSetupProgress> action) : IProgress<LtkSetupProgress>
    { public void Report(LtkSetupProgress value) => action(value); }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
