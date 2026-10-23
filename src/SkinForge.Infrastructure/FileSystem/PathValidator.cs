namespace SkinForge.Infrastructure.FileSystem;

public static class PathValidator
{
    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".js", ".jse",
        ".wsf", ".wsh", ".msc", ".jar", ".scr", ".pif", ".cpl", ".dll",
        ".so", ".dylib", ".sys", ".drv"
    };

    private static readonly HashSet<string> AllowedAssetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dds", ".tga", ".png", ".jpg", ".jpeg", ".skn", ".skl", ".anm",
        ".sco", ".scb", ".wgt", ".mapgeo", ".wad", ".client", ".bnk", ".wpk",
        ".json", ".txt", ".bin", ".preload", ".font", ".dds.cube", ".dds.vol"
    };

    public static bool IsSafeRelativePath(string relativePath, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            errorMessage = "Path cannot be empty.";
            return false;
        }

        // Check for directory traversal patterns
        if (relativePath.Contains("..") || relativePath.StartsWith("/") || relativePath.StartsWith("\\") || relativePath.Contains(':'))
        {
            errorMessage = $"Path traversal sequence detected: '{relativePath}'.";
            return false;
        }

        var ext = Path.GetExtension(relativePath);
        if (string.IsNullOrEmpty(ext))
        {
            // Directory entry or file without extension
            return true;
        }

        if (BlockedExtensions.Contains(ext))
        {
            errorMessage = $"Executable or dangerous file extension is prohibited: '{ext}'.";
            return false;
        }

        return true;
    }

    public static bool IsPathInsideBaseDirectory(string fullPath, string baseDirectory)
    {
        var normalizedBase = Path.GetFullPath(baseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedFull = Path.GetFullPath(fullPath);

        return normalizedFull.StartsWith(normalizedBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               normalizedFull.Equals(normalizedBase, StringComparison.OrdinalIgnoreCase);
    }

    public static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(fileName.Select(c => invalid.Contains(c) ? '_' : c));
    }
}
