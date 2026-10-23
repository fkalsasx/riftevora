using Serilog;
using SkinForge.Core.Enums;
using SkinForge.Core.Interfaces;
using SkinForge.Core.Models;

namespace SkinForge.Application.Services;

public class ConflictDetector : IConflictDetector
{
    public IReadOnlyList<ModConflict> DetectConflicts(IReadOnlyList<Skin> activeSkins)
    {
        var conflicts = new List<ModConflict>();
        var seenAssetPaths = new Dictionary<string, Skin>(StringComparer.OrdinalIgnoreCase);
        var seenSlots = new Dictionary<string, Skin>(StringComparer.OrdinalIgnoreCase);

        foreach (var skin in activeSkins)
        {
            // 1. Champion Skin Slot Collisions
            var slotKey = $"{skin.Champion}_Slot_{skin.SkinSlot}";
            if (seenSlots.TryGetValue(slotKey, out var existingSlotSkin) && existingSlotSkin.Id != skin.Id)
            {
                conflicts.Add(new ModConflict(
                    targetAssetPath: $"Champion/{skin.Champion}/SkinSlot{skin.SkinSlot}",
                    champion: skin.Champion,
                    skinA: existingSlotSkin,
                    skinB: skin,
                    severity: ConflictSeverity.Blocker,
                    message: $"Both '{existingSlotSkin.Name}' and '{skin.Name}' target {skin.Champion} Skin Slot {skin.SkinSlot}."
                ));
            }
            else
            {
                seenSlots[slotKey] = skin;
            }

            // 2. Real asset path collisions
            foreach (var asset in skin.Assets)
            {
                var normalizedPath = asset.RelativePath.Replace('\\', '/').ToLowerInvariant();

                if (seenAssetPaths.TryGetValue(normalizedPath, out var conflictingSkin) && conflictingSkin.Id != skin.Id)
                {
                    conflicts.Add(new ModConflict(
                        targetAssetPath: asset.RelativePath,
                        champion: skin.Champion,
                        skinA: conflictingSkin,
                        skinB: skin,
                        severity: ConflictSeverity.Blocker,
                        message: $"Conflicting asset path '{asset.RelativePath}' between '{conflictingSkin.Name}' and '{skin.Name}'."
                    ));
                }
                else
                {
                    seenAssetPaths[normalizedPath] = skin;
                }
            }
        }

        if (conflicts.Count > 0)
        {
            Log.Warning("Detected {Count} mod conflicts among {ActiveCount} active skins.", conflicts.Count, activeSkins.Count);
        }

        return conflicts;
    }

    public bool HasConflict(Skin skinA, Skin skinB, out string? conflictingAssetPath)
    {
        conflictingAssetPath = null;

        if (skinA.Id == skinB.Id) return false;

        // Check slots
        if (string.Equals(skinA.Champion, skinB.Champion, StringComparison.OrdinalIgnoreCase) && skinA.SkinSlot == skinB.SkinSlot)
        {
            conflictingAssetPath = $"Skin Slot {skinA.SkinSlot}";
            return true;
        }

        // Check assets
        var setA = new HashSet<string>(skinA.Assets.Select(a => a.RelativePath.Replace('\\', '/').ToLowerInvariant()));
        foreach (var assetB in skinB.Assets)
        {
            var pathB = assetB.RelativePath.Replace('\\', '/').ToLowerInvariant();
            if (setA.Contains(pathB))
            {
                conflictingAssetPath = assetB.RelativePath;
                return true;
            }
        }

        return false;
    }
}
