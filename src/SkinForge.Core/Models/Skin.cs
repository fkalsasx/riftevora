using SkinForge.Core.Enums;

namespace SkinForge.Core.Models;

public class Skin
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Champion { get; set; } = string.Empty;
    public int SkinSlot { get; set; } = 0;
    public string Author { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string Description { get; set; } = string.Empty;
    public string GameVersion { get; set; } = string.Empty;
    public ModCategory Category { get; set; } = ModCategory.FullOverhaul;
    public ModStatus Status { get; set; } = ModStatus.Disabled;

    public bool IsInstalled { get; set; } = true;
    public bool IsActive { get; set; } = false;
    public bool IsFavorite { get; set; } = false;

    public long FileSizeBytes { get; set; } = 0;
    public string? PreviewImagePath { get; set; }
    public List<string> Screenshots { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public List<SkinAsset> Assets { get; set; } = [];
    public List<string> Dependencies { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
    public int LoadOrderPriority { get; set; } = 0;

    public Skin() { }

    public Skin(string id, string name, string champion)
    {
        Id = id;
        Name = name;
        Champion = champion;
    }

    public string FormattedSize
    {
        get
        {
            if (FileSizeBytes <= 0) return "0 MB";
            double mb = FileSizeBytes / (1024.0 * 1024.0);
            return $"{mb:F1} MB";
        }
    }
}
