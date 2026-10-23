using System.Text.RegularExpressions;
using SkinForge.Core.Models;
using SkinForge.Infrastructure.FileSystem;

namespace SkinForge.Infrastructure.Packaging;

public static class ManifestValidator
{
    private static readonly Regex VersionRegex = new(@"^\d+(\.\d+){0,3}(\-[a-zA-Z0-9\.\-]+)?$", RegexOptions.Compiled);
    private static readonly Regex SafeIdRegex = new(@"^[a-zA-Z0-9_\-\.]+$", RegexOptions.Compiled);

    public static OperationResult Validate(SkinManifest manifest)
    {
        if (manifest == null)
        {
            return OperationResult.Failure("Manifest cannot be null.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Id))
        {
            return OperationResult.Failure("Manifest 'id' is required.");
        }

        if (!SafeIdRegex.IsMatch(manifest.Id))
        {
            return OperationResult.Failure($"Manifest 'id' contains invalid characters: '{manifest.Id}'. Allowed: alphanumeric, hyphen, underscore, dot.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            return OperationResult.Failure("Manifest 'name' is required.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Champion))
        {
            return OperationResult.Failure("Manifest 'champion' is required.");
        }

        if (manifest.SkinSlot < 0)
        {
            return OperationResult.Failure("Manifest 'skinSlot' cannot be negative.");
        }

        if (!string.IsNullOrWhiteSpace(manifest.Version) && !VersionRegex.IsMatch(manifest.Version))
        {
            return OperationResult.Failure($"Manifest 'version' format is invalid: '{manifest.Version}'. Use semantic format like '1.0.0'.");
        }

        // Validate Preview Path if specified
        if (!string.IsNullOrWhiteSpace(manifest.Preview))
        {
            if (!PathValidator.IsSafeRelativePath(manifest.Preview, out var err))
            {
                return OperationResult.Failure($"Invalid preview path: {err}");
            }
        }

        // Validate Screenshots
        foreach (var screenshot in manifest.Screenshots)
        {
            if (!PathValidator.IsSafeRelativePath(screenshot, out var err))
            {
                return OperationResult.Failure($"Invalid screenshot path '{screenshot}': {err}");
            }
        }

        // Validate Assets
        if (manifest.Assets == null || manifest.Assets.Count == 0)
        {
            return OperationResult.Failure("Manifest must contain at least one asset file.");
        }

        foreach (var asset in manifest.Assets)
        {
            if (!PathValidator.IsSafeRelativePath(asset.RelativePath, out var err))
            {
                return OperationResult.Failure($"Asset path '{asset.RelativePath}' is rejected: {err}");
            }
        }

        return OperationResult.Success();
    }
}
