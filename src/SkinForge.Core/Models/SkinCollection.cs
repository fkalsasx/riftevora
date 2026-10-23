namespace SkinForge.Core.Models;

public class SkinCollection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> SkinIds { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }

    public SkinCollection() { }

    public SkinCollection(string name, string description = "")
    {
        Name = name;
        Description = description;
    }
}
