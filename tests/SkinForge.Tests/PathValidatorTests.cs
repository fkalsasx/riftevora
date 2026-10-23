using SkinForge.Infrastructure.FileSystem;
using Xunit;

namespace SkinForge.Tests;

public class PathValidatorTests
{
    [Theory]
    [InlineData("assets/characters/ahri/skins/base/ahri.dds", true)]
    [InlineData("assets/sounds/wwise2016/vo.bnk", true)]
    [InlineData("preview.png", true)]
    [InlineData("manifest.json", true)]
    public void IsSafeRelativePath_ShouldAccept_ValidRelativePaths(string path, bool expected)
    {
        var result = PathValidator.IsSafeRelativePath(path, out var error);
        Assert.Equal(expected, result);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("../../Windows/System32/cmd.exe", "Path traversal")]
    [InlineData("..\\..\\Windows\\calc.exe", "Path traversal")]
    [InlineData("/etc/passwd", "Path traversal")]
    [InlineData("C:\\Game\\League.exe", "Path traversal")]
    [InlineData("malicious_script.ps1", "Executable or dangerous")]
    [InlineData("payload.bat", "Executable or dangerous")]
    [InlineData("trojan.exe", "Executable or dangerous")]
    [InlineData("injected.dll", "Executable or dangerous")]
    [InlineData("virus.vbs", "Executable or dangerous")]
    public void IsSafeRelativePath_ShouldReject_DangerousOrTraversalPaths(string path, string expectedErrorSubstring)
    {
        var result = PathValidator.IsSafeRelativePath(path, out var error);
        Assert.False(result);
        Assert.NotNull(error);
        Assert.Contains(expectedErrorSubstring, error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsPathInsideBaseDirectory_ShouldVerify_BoundaryStrictly()
    {
        var baseDir = @"C:\SkinForge\Library";
        var inside = @"C:\SkinForge\Library\skins\ahri\model.dds";
        var outside = @"C:\SkinForge\Other\secret.txt";

        Assert.True(PathValidator.IsPathInsideBaseDirectory(inside, baseDir));
        Assert.False(PathValidator.IsPathInsideBaseDirectory(outside, baseDir));
    }
}
