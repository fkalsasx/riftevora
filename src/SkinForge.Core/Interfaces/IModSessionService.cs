using SkinForge.Core.Models;

namespace SkinForge.Core.Interfaces;

public interface IModSessionService
{
    ModSession? CurrentSession { get; }
    bool IsSessionActive { get; }

    event Action<ModSession>? SessionStateChanged;

    Task<OperationResult<ModSession>> StartSessionAsync(string skinId, CancellationToken cancellationToken = default);
    Task<OperationResult> StopSessionAsync(CancellationToken cancellationToken = default);
}
