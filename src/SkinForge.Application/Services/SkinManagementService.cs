using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SkinForge.Application.DTOs;
using SkinForge.Core.Interfaces;
using SkinForge.Core.Models;

namespace SkinForge.Application.Services;

public class SkinManagementService
{
    private readonly ISkinRepository _repository;
    private readonly IConflictDetector _conflictDetector;
    private readonly IModProvider _modProvider;
    private readonly ISearchEngine _searchEngine;
    private readonly ILtkManagerAdapter _ltkAdapter;
    private readonly ITransactionCoordinator _txCoordinator;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<SkinManagementService> _logger;

    public SkinManagementService(
        ISkinRepository repository,
        IConflictDetector conflictDetector,
        IModProvider modProvider,
        ISearchEngine searchEngine,
        ILtkManagerAdapter ltkAdapter,
        ITransactionCoordinator txCoordinator,
        IAuditLogRepository auditLogRepository,
        ILogger<SkinManagementService> logger)
    {
        _repository = repository;
        _conflictDetector = conflictDetector;
        _modProvider = modProvider;
        _searchEngine = searchEngine;
        _ltkAdapter = ltkAdapter;
        _txCoordinator = txCoordinator;
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task<DashboardStatsDto> GetDashboardStatsAsync(CancellationToken cancellationToken = default)
    {
        var installed = await _repository.GetInstalledCountAsync(cancellationToken);
        var active = await _repository.GetActiveCountAsync(cancellationToken);
        var bytes = await _repository.GetTotalStorageUsedBytesAsync(cancellationToken);

        var allSkins = await _repository.GetAllAsync(cancellationToken);
        var championCount = allSkins.Select(s => s.Champion).Distinct(StringComparer.OrdinalIgnoreCase).Count();

        double mb = bytes / (1024.0 * 1024.0);
        var formatted = mb > 1024 ? $"{mb / 1024.0:F2} GB" : $"{mb:F1} MB";

        return new DashboardStatsDto(installed, active, championCount, formatted);
    }

    public async Task<List<Skin>> FilterSkinsAsync(SkinFilterCriteriaDto criteria, CancellationToken cancellationToken = default)
    {
        var all = await _repository.GetAllAsync(cancellationToken);

        IEnumerable<Skin> query = all;

        if (!string.IsNullOrWhiteSpace(criteria.Champion) && !criteria.Champion.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(s => string.Equals(s.Champion, criteria.Champion, StringComparison.OrdinalIgnoreCase));
        }

        if (criteria.Category.HasValue)
        {
            query = query.Where(s => s.Category == criteria.Category.Value);
        }

        if (!string.IsNullOrWhiteSpace(criteria.Author))
        {
            query = query.Where(s => string.Equals(s.Author, criteria.Author, StringComparison.OrdinalIgnoreCase));
        }

        if (criteria.OnlyInstalled)
        {
            query = query.Where(s => s.IsInstalled);
        }

        if (criteria.OnlyFavorites)
        {
            query = query.Where(s => s.IsFavorite);
        }

        if (criteria.OnlyActive)
        {
            query = query.Where(s => s.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(criteria.SearchQuery))
        {
            query = _searchEngine.Search(query, criteria.SearchQuery);
        }

        // Sorting
        query = criteria.SortBy switch
        {
            "Champion" => criteria.SortAscending ? query.OrderBy(s => s.Champion) : query.OrderByDescending(s => s.Champion),
            "RecentlyAdded" => criteria.SortAscending ? query.OrderBy(s => s.CreatedAt) : query.OrderByDescending(s => s.CreatedAt),
            "RecentlyUsed" => criteria.SortAscending ? query.OrderBy(s => s.LastUsedAt ?? DateTime.MinValue) : query.OrderByDescending(s => s.LastUsedAt ?? DateTime.MinValue),
            "FileSize" => criteria.SortAscending ? query.OrderBy(s => s.FileSizeBytes) : query.OrderByDescending(s => s.FileSizeBytes),
            _ => criteria.SortAscending ? query.OrderBy(s => s.Name) : query.OrderByDescending(s => s.Name)
        };

        return query.ToList();
    }

    /// <summary>
    /// Executes controlled 6-phase atomic activation:
    /// VALIDATE -> PREPARE -> BACKUP -> EXECUTE -> VERIFY -> COMMIT
    /// with rollback on failure.
    /// </summary>
    public async Task<OperationResult> ActivateSkinAsync(string skinId, CancellationToken cancellationToken = default)
    {
        var targetSkin = await _repository.GetByIdAsync(skinId, cancellationToken);
        if (targetSkin == null)
        {
            return OperationResult.Failure(ApplicationError.AssetMissing(skinId, "ActivateSkin"));
        }

        var tx = await _txCoordinator.BeginTransactionAsync("Activate", skinId, targetSkin.Name);

        try
        {
            // PHASE 1: VALIDATE
            await _txCoordinator.UpdatePhaseAsync(tx.TransactionId, TransactionPhase.Validated);
            var settings = await _repository.GetSettingsAsync(cancellationToken);

            // Check conflicts against already active skins
            if (settings.CheckConflictsOnActivation)
            {
                var activeSkins = await _repository.GetActiveAsync(cancellationToken);
                foreach (var active in activeSkins)
                {
                    if (active.Id == targetSkin.Id) continue;
                    if (_conflictDetector.HasConflict(targetSkin, active, out var conflictingPath))
                    {
                        var err = ApplicationError.ConflictDetected(
                            $"Cannot activate '{targetSkin.Name}' because it conflicts with active skin '{active.Name}'.",
                            conflictingPath,
                            "ActivateSkin"
                        );
                        await _txCoordinator.RollbackTransactionAsync(tx.TransactionId, err.Message);
                        return OperationResult.Failure(err);
                    }
                }
            }

            // PHASE 2: PREPARE
            await _txCoordinator.UpdatePhaseAsync(tx.TransactionId, TransactionPhase.Prepared);
            var ltkPrep = await _ltkAdapter.PrepareModAsync(targetSkin, cancellationToken);
            if (!ltkPrep.IsSuccess && ltkPrep.Error != null)
            {
                await _txCoordinator.RollbackTransactionAsync(tx.TransactionId, ltkPrep.ErrorMessage ?? "LTK Prepare failed");
                return ltkPrep;
            }

            // PHASE 3: BACKUP
            await _txCoordinator.UpdatePhaseAsync(tx.TransactionId, TransactionPhase.BackedUp);

            // PHASE 4: EXECUTE
            await _txCoordinator.UpdatePhaseAsync(tx.TransactionId, TransactionPhase.Executed);
            var applyResult = await _ltkAdapter.ApplyModAsync(targetSkin, cancellationToken);
            if (!applyResult.IsSuccess)
            {
                await _txCoordinator.RollbackTransactionAsync(tx.TransactionId, applyResult.ErrorMessage ?? "LTK Apply failed");
                return applyResult;
            }

            // PHASE 5: VERIFY
            await _txCoordinator.UpdatePhaseAsync(tx.TransactionId, TransactionPhase.Verified);

            // PHASE 6: COMMIT
            await _repository.SetActiveStatusAsync(skinId, true, cancellationToken);
            await _txCoordinator.CommitTransactionAsync(tx.TransactionId);

            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during activation transaction for '{SkinName}'", targetSkin.Name);
            await _txCoordinator.RollbackTransactionAsync(tx.TransactionId, ex.Message);
            return OperationResult.Failure(ApplicationError.UnexpectedError(ex, "ActivateSkin"));
        }
    }

    public async Task<OperationResult> DeactivateSkinAsync(string skinId, CancellationToken cancellationToken = default)
    {
        var skin = await _repository.GetByIdAsync(skinId, cancellationToken);
        if (skin == null) return OperationResult.Failure("Skin not found.");

        var sw = Stopwatch.StartNew();
        var ltkResult = await _ltkAdapter.DisableModAsync(skin, cancellationToken);
        if (!ltkResult.IsSuccess && ltkResult.Error != null)
        {
            return ltkResult;
        }

        var dbResult = await _repository.SetActiveStatusAsync(skinId, false, cancellationToken);
        sw.Stop();

        await _auditLogRepository.InsertAsync(AuditLogEntry.Create(
            action: "Deactivate",
            entityId: skin.Id,
            entityName: skin.Name,
            status: dbResult.IsSuccess ? "Success" : "Failed",
            durationMs: sw.ElapsedMilliseconds
        ), cancellationToken);

        return dbResult;
    }

    public async Task<OperationResult> ToggleFavoriteAsync(string skinId, CancellationToken cancellationToken = default)
    {
        var skin = await _repository.GetByIdAsync(skinId, cancellationToken);
        if (skin == null) return OperationResult.Failure("Skin not found.");

        return await _repository.SetFavoriteStatusAsync(skinId, !skin.IsFavorite, cancellationToken);
    }

    public async Task<OperationResult> ApplyActiveModsToGameAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var settings = await _repository.GetSettingsAsync(cancellationToken);
        var activeMods = await _repository.GetActiveAsync(cancellationToken);

        return await _modProvider.ApplyModsAsync(activeMods, settings.GameDirectory, progress, cancellationToken);
    }

    public async Task<OperationResult> DeactivateAllFromGameAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var settings = await _repository.GetSettingsAsync(cancellationToken);
        var result = await _modProvider.DeactivateAllAsync(settings.GameDirectory, progress, cancellationToken);
        if (result.IsSuccess)
        {
            var activeMods = await _repository.GetActiveAsync(cancellationToken);
            foreach (var mod in activeMods)
            {
                await _repository.SetActiveStatusAsync(mod.Id, false, cancellationToken);
            }
        }
        return result;
    }

    public async Task<IReadOnlyList<AuditLogEntry>> GetAuditHistoryAsync(int count = 100, CancellationToken cancellationToken = default)
    {
        return await _auditLogRepository.GetRecentLogsAsync(count, cancellationToken);
    }
}
