using System.IO.Compression;
using System.Text.Json;
using SkinForge.Infrastructure.Packaging;
using SkinForge.Infrastructure.Workspace;
using Xunit;

namespace SkinForge.Tests;

public sealed class WorkspaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SkinForge-workspace-" + Guid.NewGuid().ToString("N"));
    public WorkspaceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void First_Version_Favorites_Survive_Migration_And_Restart()
    {
        var file = Path.Combine(_root, "workspace.json");
        File.WriteAllText(file, """{"Favorites":["champion:Ahri","skin:103027"],"LtkPath":"C:\\LTK\\ltk-manager.exe","ReduceMotion":true}""");
        using (var store = new WorkspaceStore(file))
        {
            store.Load();
            Assert.True(store.IsFavorite("skin:103027"));
            Assert.True(store.Data.ReduceMotion);
            var collection = store.CreateCollection("Ionia");
            store.ToggleInCollection(collection.Id, "skin:103027");
            store.AddRecent("champion:Ahri");
            store.Flush();
        }
        using var restored = new WorkspaceStore(file);
        restored.Load();
        Assert.True(restored.IsFavorite("champion:Ahri"));
        Assert.Equal("skin:103027", Assert.Single(Assert.Single(restored.Data.Collections).Items));
        Assert.Equal("champion:Ahri", Assert.Single(restored.Data.Recents).Ref);
    }

    [Fact]
    public void Corrupt_Preferences_Are_Preserved_For_Recovery()
    {
        var file = Path.Combine(_root, "workspace.json");
        File.WriteAllText(file, "{broken document");
        using var store = new WorkspaceStore(file);
        store.Load();
        Assert.NotNull(store.LoadWarning);
        Assert.Equal("{broken document", File.ReadAllText(Assert.Single(Directory.GetFiles(_root, "*.corrupt-*"))));
    }

    [Fact]
    public async Task Portable_Backup_Restores_Packages_Into_New_Location_With_Valid_Hashes()
    {
        var source = Path.Combine(_root, "input.fantome");
        using (var zip = ZipFile.Open(source, ZipArchiveMode.Create))
        {
            using (var text = new StreamWriter(zip.CreateEntry("META/info.json").Open())) text.Write("""{"Name":"QA Mod","Author":"QA","Version":"1"}""");
            using (var text = new StreamWriter(zip.CreateEntry("WAD/Ahri.wad.client/assets/test.dds").Open())) text.Write("test");
        }
        var firstLibrary = new LocalPackageLibrary(Path.Combine(_root, "first"));
        var original = await firstLibrary.ImportAsync(source);
        using var store = new WorkspaceStore(Path.Combine(_root, "workspace.json"));
        store.ToggleFavorite("skin:103027");
        var backup = Path.Combine(_root, "portable.zip");
        await WorkspaceBackup.CreateAsync(backup, store, firstLibrary.Root, true);
        var secondLibrary = new LocalPackageLibrary(Path.Combine(_root, "second"));
        var result = await WorkspaceBackup.RestoreAsync(backup, secondLibrary.Root, true);
        Assert.Equal(1, result.PackagesRestored);
        Assert.Contains("skin:103027", result.Workspace.Favorites);
        var package = Assert.Single(await secondLibrary.LoadAsync());
        Assert.NotEqual(original.FilePath, package.FilePath);
        await secondLibrary.VerifyAsync(package);
    }

    [Fact]
    public async Task Corrupted_Backup_Is_Rejected_Without_Importing_Packages()
    {
        using var store = new WorkspaceStore(Path.Combine(_root, "workspace.json"));
        var backup = Path.Combine(_root, "backup.zip");
        await WorkspaceBackup.CreateAsync(backup, store, Path.Combine(_root, "missing"), false);
        using (var zip = ZipFile.Open(backup, ZipArchiveMode.Update))
        {
            zip.GetEntry("workspace.json")!.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("workspace.json").Open());
            writer.Write("{}");
        }
        var destination = Path.Combine(_root, "restored");
        await Assert.ThrowsAsync<InvalidDataException>(() => WorkspaceBackup.RestoreAsync(backup, destination, true));
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public async Task Inspector_Detects_Overlapping_Game_Assets_Across_Fantome_Packages()
    {
        var library = new LocalPackageLibrary(Path.Combine(_root, "library"));
        async Task<LocalPackage> Create(string name)
        {
            var path = Path.Combine(_root, name + ".fantome");
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(zip.CreateEntry("META/info.json").Open())) writer.Write(JsonSerializer.Serialize(new { Name = name }));
                using (var writer = new StreamWriter(zip.CreateEntry("WAD/Ahri.wad.client/assets/ahri.dds").Open())) writer.Write(name);
            }
            return await library.ImportAsync(path);
        }
        var first = await Create("first"); var second = await Create("second");
        var a = await PackageInspector.InspectAsync(first); var b = await PackageInspector.InspectAsync(second);
        var conflict = PackageInspector.Compare(first, a, second, b);
        Assert.NotNull(conflict);
        Assert.Equal(ConflictSeverity.SharedFiles, conflict.Severity);
        Assert.Equal(1, conflict.SharedFileCount);
        Assert.Contains("Ahri", a.Champions);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
