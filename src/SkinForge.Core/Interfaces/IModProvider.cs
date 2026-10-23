using SkinForge.Core.Models;

namespace SkinForge.Core.Interfaces;

public interface IModProvider
{
    string ProviderName { get; }
    Task<OperationResult> ValidateEnvironmentAsync(string gamePath, CancellationToken cancellationToken = default);
    Task<OperationResult> ApplyModsAsync(IReadOnlyList<Skin> activeMods, string gamePath, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task<OperationResult> DeactivateAllAsync(string gamePath, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task<OperationResult> RollbackAsync(string gamePath, CancellationToken cancellationToken = default);
}
