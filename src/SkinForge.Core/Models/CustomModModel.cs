namespace SkinForge.Core.Models;

public enum ModCompatibilityStatus
{
    Compatible,
    PossiblyOutdated,
    Unknown,
    Incompatible
}

public class CustomMod
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Champion { get; set; } = string.Empty;
    public string Author { get; set; } = "Community";
    public string Version { get; set; } = "1.0.0";
    public string PackageFormat { get; set; } = ".fantome"; // .fantome or .modpkg
    public string FilePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string HashSha256 { get; set; } = string.Empty;
    public string TargetPatch { get; set; } = string.Empty;
    public ModCompatibilityStatus Compatibility { get; set; } = ModCompatibilityStatus.Unknown;
    public bool IsInstalled { get; set; } = true;
    public bool IsActive { get; set; } = false;
    public bool IsFavorite { get; set; } = false;
    public string? PreviewImagePath { get; set; }
    public DateTime InstalledAt { get; set; } = DateTime.UtcNow;
    public List<string> Dependencies { get; set; } = [];
    public List<SkinAsset> Assets { get; set; } = [];

    public string FormattedSize => FileSizeBytes switch
    {
        > 1024 * 1024 * 1024 => $"{FileSizeBytes / (1024.0 * 1024.0 * 1024.0):F2} GB",
        > 1024 * 1024 => $"{FileSizeBytes / (1024.0 * 1024.0):F1} MB",
        > 1024 => $"{FileSizeBytes / 1024.0:F0} KB",
        _ => $"{FileSizeBytes} B"
    };

    public CustomMod() { }

    public CustomMod(string id, string name, string champion)
    {
        Id = id;
        Name = name;
        Champion = champion;
    }
}
