using SkinForge.Core.Enums;

namespace SkinForge.Core.Models;

public class ModConflict
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TargetAssetPath { get; set; } = string.Empty;
    public string Champion { get; set; } = string.Empty;
    public Skin SkinA { get; set; } = null!;
    public Skin SkinB { get; set; } = null!;
    public ConflictSeverity Severity { get; set; } = ConflictSeverity.Blocker;
    public string Message { get; set; } = string.Empty;

    public ModConflict() { }

    public ModConflict(string targetAssetPath, string champion, Skin skinA, Skin skinB, ConflictSeverity severity, string message)
    {
        TargetAssetPath = targetAssetPath;
        Champion = champion;
        SkinA = skinA;
        SkinB = skinB;
        Severity = severity;
        Message = message;
    }
}
