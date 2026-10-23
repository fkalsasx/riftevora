using SkinForge.Core.Enums;

namespace SkinForge.Core.Models;

public class SkinAsset
{
    public string RelativePath { get; set; } = string.Empty;
    public string Sha256Hash { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public LoadOrderLayer Layer { get; set; } = LoadOrderLayer.Texture;

    public SkinAsset() { }

    public SkinAsset(string relativePath, string sha256Hash, long sizeBytes, LoadOrderLayer layer = LoadOrderLayer.Texture)
    {
        RelativePath = relativePath;
        Sha256Hash = sha256Hash;
        SizeBytes = sizeBytes;
        Layer = layer;
    }
}
