using SkinForge.Core.Models;

namespace SkinForge.Core.Interfaces;

public interface IConflictDetector
{
    IReadOnlyList<ModConflict> DetectConflicts(IReadOnlyList<Skin> activeSkins);
    bool HasConflict(Skin skinA, Skin skinB, out string? conflictingAssetPath);
}

public interface ISkinImporter
{
    Task<OperationResult<Skin>> ImportFromPackageAsync(
        string packageFilePath,
        string libraryDirectory,
        IProgress<(int percentage, string message)>? progress = null,
        CancellationToken cancellationToken = default);
}
