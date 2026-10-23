using System.IO.Compression;
using System.Text;
using SkinForge.Infrastructure.Packaging;
using Xunit;

namespace SkinForge.Tests;

public sealed class LocalPackageLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SkinForge-package-" + Guid.NewGuid().ToString("N"));
    private LocalPackageLibrary Library => new(Path.Combine(_root, "library"));
    private string Package(string entry = "WAD/Ahri.wad.client", string manifest = "{\"Name\":\"Test Mod\",\"Author\":\"Test Author\",\"Version\":\"1.2\"}")
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "package with spaces.fantome");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(zip.CreateEntry("META/info.json").Open())) writer.Write(manifest);
        using (var writer = new StreamWriter(zip.CreateEntry(entry).Open())) writer.Write("Test asset");
        return path;
    }

    [Fact]
    public async Task Import_Preserves_Original_Bytes_And_Official_Fantome_Metadata()
    {
        var path = Package(); var library = Library;
        var imported = await library.ImportAsync(path);
        Assert.Equal("Test Mod", imported.Name); Assert.Equal("Test Author", imported.Author); Assert.Equal("1.2", imported.Version);
        Assert.Equal(await File.ReadAllBytesAsync(path), await File.ReadAllBytesAsync(imported.FilePath));
        await library.VerifyAsync(imported);
        var loaded = Assert.Single(await library.LoadAsync());
        Assert.Equal(imported.Id, loaded.Id);
    }

    [Theory]
    [InlineData("../../outside.txt")]
    [InlineData("WAD/../../../outside.txt")]
    [InlineData("C:/outside.txt")]
    [InlineData("WAD/run.exe")]
    [InlineData("META/run.ps1")]
    public async Task Unsafe_Packages_Are_Rejected_Before_Copy(string entry)
    {
        var path = Package(entry);
        await Assert.ThrowsAsync<InvalidDataException>(() => Library.ImportAsync(path));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "library")));
    }

    [Fact]
    public async Task Importing_Same_Package_Is_Idempotent()
    {
        var path = Package(); var library = Library;
        var first = await library.ImportAsync(path); var second = await library.ImportAsync(path);
        Assert.Equal(first.Id, second.Id); Assert.Single(await library.LoadAsync());
    }

    [Fact]
    public async Task Modified_Package_Cannot_Be_Handed_Off()
    {
        var package = await Library.ImportAsync(Package());
        await File.AppendAllTextAsync(package.FilePath, "modified");
        await Assert.ThrowsAsync<InvalidDataException>(() => Library.VerifyAsync(package));
    }

    [Fact]
    public async Task Invalid_Fantome_Does_Not_Create_Library_Record()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "invalid.fantome");
        await File.WriteAllTextAsync(path, new string('x', 100));
        await Assert.ThrowsAsync<InvalidDataException>(() => Library.ImportAsync(path));
        Assert.Empty(await Library.LoadAsync());
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
