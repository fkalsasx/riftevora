using SkinForge.Core.Models;

namespace SkinForge.Core.Interfaces;

public interface IDiagnosticsEngine
{
    Task<ComprehensiveDiagnostics> RunFullDiagnosticsAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> ExecuteFixAsync(FixActionType action, CancellationToken cancellationToken = default);
}
