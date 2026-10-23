using System.Text.Json.Serialization;
using SkinForge.Core.Enums;

namespace SkinForge.Core.Models;

public class SkinManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("author")]
    public string Author { get; set; } = "Community";

    [JsonPropertyName("champion")]
    public string Champion { get; set; } = string.Empty;

    [JsonPropertyName("skinSlot")]
    public int SkinSlot { get; set; } = 0;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("gameVersion")]
    public string GameVersion { get; set; } = "14.20";

    [JsonPropertyName("category")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ModCategory Category { get; set; } = ModCategory.FullOverhaul;

    [JsonPropertyName("preview")]
    public string? Preview { get; set; }

    [JsonPropertyName("screenshots")]
    public List<string> Screenshots { get; set; } = [];

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("assets")]
    public List<SkinAsset> Assets { get; set; } = [];

    [JsonPropertyName("dependencies")]
    public List<string> Dependencies { get; set; } = [];
}
