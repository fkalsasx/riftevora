using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SkinForge.Core.Interfaces;
using SkinForge.Core.Models;

namespace SkinForge.Application.Services;

public class ModSessionService : IModSessionService, IDisposable
{
    private readonly ILtkManagerAdapter _ltkAdapter;
    private readonly ISkinRepository _skinRepository;
    private readonly IAuditLogRepository _auditRepository;
    private readonly ILogger<ModSessionService> _logger;

    private ModSession? _currentSession;
    private CancellationTokenSource? _monitorCts;
    private readonly object _lock = new();

    public ModSession? CurrentSession => _currentSession;
    public bool IsSessionActive => _currentSession != null &&
        _currentSession.Status != SessionStatus.Stopped &&
        _currentSession.Status != SessionStatus.Failed;

    public event Action<ModSession>? SessionStateChanged;

    public ModSessionService(
        ILtkManagerAdapter ltkAdapter,
        ISkinRepository skinRepository,
        IAuditLogRepository auditRepository,
        ILogger<ModSessionService> logger)
    {
        _ltkAdapter = ltkAdapter;
        _skinRepository = skinRepository;
        _auditRepository = auditRepository;
        _logger = logger;
    }

    public async Task<OperationResult<ModSession>> StartSessionAsync(string skinId, CancellationToken cancellationToken = default)
    {
        if (IsSessionActive)
        {
            return OperationResult<ModSession>.Failure(
                ApplicationError.UnexpectedError(new InvalidOperationException("A mod session is already active. Please stop the current session before starting a new one.")));
        }

        var skin = await _skinRepository.GetByIdAsync(skinId, cancellationToken);
        if (skin == null)
        {
            return OperationResult<ModSession>.Failure(ApplicationError.AssetMissing(skinId, "StartSession"));
        }

        // 1. Verify LTK Manager presence
        var ltkInfo = await _ltkAdapter.GetStatusAsync(cancellationToken);
        if (ltkInfo.Status == LtkStatus.NotDetected)
        {
            return OperationResult<ModSession>.Failure(ApplicationError.LtkManagerNotFound("LTK Manager is required to launch mod sessions."));
        }

        var session = new ModSession(
            SessionId: Guid.NewGuid().ToString("N")[..8],
            SkinId: skin.Id,
            SkinName: skin.Name,
            ChampionName: skin.Champion,
            EngineName: "LTK Manager (League Toolkit)",
            StartedAt: DateTime.UtcNow,
            Status: SessionStatus.WaitingForGame,
            GameProcessDetected: false,
            ElapsedSeconds: 0,
            ErrorMessage: null,
            ActiveModNames: [skin.Name]
        );

        lock (_lock)
        {
            _currentSession = session;
        }

        SessionStateChanged?.Invoke(session);
        _logger.LogInformation("[SESSION:{Id}] Mod session started for '{Skin}' ({Champion}). Waiting for League client...",
            session.SessionId, skin.Name, skin.Champion);

        // Start background session monitor
        _monitorCts?.Cancel();
        _monitorCts = new CancellationTokenSource();
        _ = Task.Run(() => MonitorSessionLifecycleAsync(session.SessionId, skin, _monitorCts.Token));

        return OperationResult<ModSession>.Success(session);
    }

    public async Task<OperationResult> StopSessionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentSession == null)
        {
            return OperationResult.Success();
        }

        _logger.LogInformation("[SESSION:{Id}] Stopping mod session...", _currentSession.SessionId);

        _monitorCts?.Cancel();

        UpdateStatus(SessionStatus.Stopping);

        var patcherResult = await _ltkAdapter.StopPatcherAsync(cancellationToken);
        await _ltkAdapter.RestoreAsync(cancellationToken);

        var duration = (long)(DateTime.UtcNow - _currentSession.StartedAt).TotalSeconds;

        await _auditRepository.InsertAsync(AuditLogEntry.Create(
            action: "SkinSession",
            entityId: _currentSession.SkinId,
            entityName: _currentSession.SkinName,
            status: "Stopped",
            durationMs: duration * 1000,
            correlationId: _currentSession.SessionId
        ), cancellationToken);

        UpdateStatus(SessionStatus.Stopped);
        _logger.LogInformation("Mod session stopped successfully.");

        return OperationResult.Success();
    }

    private async Task MonitorSessionLifecycleAsync(string sessionId, Skin skin, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        bool gameFound = false;
        bool patcherRan = false;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(1000, ct);

                if (_currentSession == null || _currentSession.SessionId != sessionId)
                    break;

                // 1. Process detection (League of Legends.exe or LeagueClient.exe)
                var processes = Process.GetProcessesByName("League of Legends");
                bool isRunning = processes.Length > 0;

                if (isRunning && !gameFound)
                {
                    gameFound = true;
                    _logger.LogInformation("[SESSION:{Id}] League of Legends game process detected!", sessionId);
                    UpdateStatus(SessionStatus.LeagueDetected, gameDetected: true);

                    await Task.Delay(800, ct);
                    UpdateStatus(SessionStatus.PreparingMod, gameDetected: true);

                    // 2. Prepare mod staging
                    var prepResult = await _ltkAdapter.PrepareModAsync(skin, ct);
                    if (!prepResult.IsSuccess)
                    {
                        UpdateStatus(SessionStatus.Failed, errorMessage: prepResult.ErrorMessage);
                        break;
                    }

                    // 3. Run LTK patcher
                    UpdateStatus(SessionStatus.Patching, gameDetected: true);
                    var runResult = await _ltkAdapter.RunPatcherAsync([skin.Id], ct);
                    if (!runResult.IsSuccess)
                    {
                        UpdateStatus(SessionStatus.Failed, errorMessage: runResult.ErrorMessage);
                        break;
                    }

                    patcherRan = true;
                    UpdateStatus(SessionStatus.Active, gameDetected: true);
                    _logger.LogInformation("[SESSION:{Id}] Mod applied and active in game overlay.", sessionId);
                }
                else if (!isRunning && gameFound)
                {
                    // Game exited
                    _logger.LogInformation("[SESSION:{Id}] League process exited. Cleaning up session...", sessionId);
                    await StopSessionAsync(CancellationToken.None);
                    break;
                }

                // Update elapsed time
                if (_currentSession != null)
                {
                    lock (_lock)
                    {
                        _currentSession = _currentSession with
                        {
                            ElapsedSeconds = (long)sw.Elapsed.TotalSeconds
                        };
                    }
                    SessionStateChanged?.Invoke(_currentSession);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in session monitor loop.");
            UpdateStatus(SessionStatus.Failed, errorMessage: ex.Message);
        }
    }

    private void UpdateStatus(SessionStatus newStatus, bool? gameDetected = null, string? errorMessage = null)
    {
        lock (_lock)
        {
            if (_currentSession == null) return;

            _currentSession = _currentSession with
            {
                Status = newStatus,
                GameProcessDetected = gameDetected ?? _currentSession.GameProcessDetected,
                ErrorMessage = errorMessage ?? _currentSession.ErrorMessage
            };

            SessionStateChanged?.Invoke(_currentSession);
        }
    }

    public void Dispose()
    {
        _monitorCts?.Cancel();
        _monitorCts?.Dispose();
    }
}
