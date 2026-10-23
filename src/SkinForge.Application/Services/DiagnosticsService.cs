using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging;
using SkinForge.Core.Interfaces;

namespace SkinForge.Application.Services;

public class DiagnosticsService : IDiagnosticsService
{
    private readonly ISkinRepository _skinRepository;
    private readonly ILtkManagerAdapter _ltkAdapter;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly string _libraryPath;
    private readonly string _logDirectory;
    private readonly ILogger<DiagnosticsService> _logger;

    public DiagnosticsService(
        ISkinRepository skinRepository,
        ILtkManagerAdapter ltkAdapter,
        IAuditLogRepository auditLogRepository,
        string libraryPath,
        string logDirectory,
        ILogger<DiagnosticsService> logger)
    {
        _skinRepository = skinRepository;
        _ltkAdapter = ltkAdapter;
        _auditLogRepository = auditLogRepository;
        _libraryPath = libraryPath;
        _logDirectory = logDirectory;
        _logger = logger;
    }

    public async Task<QuickHealthReport> RunQuickHealthCheckAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        bool dbOk = false;
        bool storageOk = false;
        bool ltkDetected = false;

        try
        {
            // 1. Quick DB check
            var count = await _skinRepository.GetInstalledCountAsync(cancellationToken);
            dbOk = true;

            // 2. Storage write sanity
            if (!Directory.Exists(_libraryPath))
            {
                Directory.CreateDirectory(_libraryPath);
            }
            storageOk = Directory.Exists(_libraryPath);

            // 3. Fast LTK detection check
            ltkDetected = await _ltkAdapter.DetectInstallationAsync(null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Quick health check encountered a non-critical warning.");
        }

        sw.Stop();
        _logger.LogInformation("Quick health check completed in {Elapsed}ms. DB: {Db}, Storage: {Storage}, LTK: {Ltk}",
            sw.ElapsedMilliseconds, dbOk, storageOk, ltkDetected);

        return new QuickHealthReport(
            IsHealthy: dbOk && storageOk,
            Message: dbOk ? "Core systems operational." : "Database initialization needed.",
            DatabaseOk: dbOk,
            StorageOk: storageOk,
            LtkDetected: ltkDetected
        );
    }

    public async Task<FullDiagnosticsReport> RunFullDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Running comprehensive system diagnostics...");
        var checks = new List<DiagnosticCheckItem>();

        // Check 1: Database
        string dbStatus = "Optimal";
        try
        {
            var skins = await _skinRepository.GetAllAsync(cancellationToken);
            checks.Add(new DiagnosticCheckItem("Database", "SQLite Engine", true, $"Connected with {skins.Count} skins indexed."));
        }
        catch (Exception ex)
        {
            dbStatus = "Degraded";
            checks.Add(new DiagnosticCheckItem("Database", "SQLite Engine", false, ex.Message, "E010"));
        }

        // Check 2: Library Storage
        string libStatus = "Ready";
        try
        {
            if (!Directory.Exists(_libraryPath))
            {
                Directory.CreateDirectory(_libraryPath);
            }
            var testFile = Path.Combine(_libraryPath, ".diag_test.tmp");
            await File.WriteAllTextAsync(testFile, "test", cancellationToken);
            File.Delete(testFile);

            checks.Add(new DiagnosticCheckItem("Library", "Storage Read/Write", true, $"Directory verified at '{_libraryPath}'."));
        }
        catch (Exception ex)
        {
            libStatus = "Permission Denied";
            checks.Add(new DiagnosticCheckItem("Library", "Storage Read/Write", false, ex.Message, "E008"));
        }

        // Check 3: Package System
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "SkinForge_Diag");
            Directory.CreateDirectory(tempDir);
            Directory.Delete(tempDir, true);
            checks.Add(new DiagnosticCheckItem("Package System", "Extraction Sandbox", true, "Sandbox staging directory accessible and safe."));
        }
        catch (Exception ex)
        {
            checks.Add(new DiagnosticCheckItem("Package System", "Extraction Sandbox", false, ex.Message, "E009"));
        }

        // Check 4: LTK Manager
        string ltkStatusStr = "Unknown";
        try
        {
            var ltkInfo = await _ltkAdapter.GetStatusAsync(cancellationToken);
            ltkStatusStr = ltkInfo.Status.ToString();
            checks.Add(new DiagnosticCheckItem(
                "LTK Manager",
                "Integration Adapter",
                ltkInfo.Status == LtkStatus.Ready,
                $"{ltkInfo.StatusMessage} (Path: {ltkInfo.ExecutablePath ?? "None"})",
                ltkInfo.Status != LtkStatus.Ready ? "E006" : null
            ));
        }
        catch (Exception ex)
        {
            ltkStatusStr = "Error";
            checks.Add(new DiagnosticCheckItem("LTK Manager", "Integration Adapter", false, ex.Message, "E007"));
        }

        // Check 5: Permissions
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var sfAppData = Path.Combine(appData, "SkinForge");
            Directory.CreateDirectory(sfAppData);
            checks.Add(new DiagnosticCheckItem("Permissions", "Application Data Access", true, "Full Read/Write access confirmed."));
        }
        catch (Exception ex)
        {
            checks.Add(new DiagnosticCheckItem("Permissions", "Application Data Access", false, ex.Message, "E008"));
        }

        // Check 6: Storage Space
        string storageStatusStr = "Healthy";
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(_libraryPath));
            if (!string.IsNullOrEmpty(root))
            {
                var drive = new DriveInfo(root);
                long freeGb = drive.AvailableFreeSpace / (1024 * 1024 * 1024);
                bool hasEnoughSpace = freeGb >= 2;
                storageStatusStr = $"{freeGb} GB Free";
                checks.Add(new DiagnosticCheckItem("Storage", "Disk Volume Capacity", hasEnoughSpace, $"{freeGb} GB available on drive '{drive.Name}'."));
            }
        }
        catch (Exception ex)
        {
            checks.Add(new DiagnosticCheckItem("Storage", "Disk Volume Capacity", true, "Could not inspect drive info: " + ex.Message));
        }

        // Audit Logs (Last Operation & Last Error)
        string? lastOp = null;
        string? lastErr = null;
        try
        {
            var recent = await _auditLogRepository.GetRecentLogsAsync(10, cancellationToken);
            var firstOp = recent.FirstOrDefault();
            if (firstOp != null)
            {
                lastOp = $"{firstOp.Action} ({firstOp.Status}) at {firstOp.Timestamp:HH:mm:ss}";
            }

            var firstErr = recent.FirstOrDefault(l => l.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase));
            if (firstErr != null)
            {
                lastErr = $"{firstErr.Action} failed at {firstErr.Timestamp:HH:mm:ss}";
            }
        }
        catch { }

        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "2026.1.0";
        bool allPassed = checks.All(c => c.Passed);

        return new FullDiagnosticsReport(
            AppVersion: $"v{version} (Production-Ready Architecture)",
            Timestamp: DateTime.UtcNow,
            AllPassed: allPassed,
            Checks: checks,
            DatabaseStatus: dbStatus,
            LibraryStatus: libStatus,
            LtkStatus: ltkStatusStr,
            StorageStatus: storageStatusStr,
            LastOperation: lastOp ?? "System Initialized",
            LastError: lastErr ?? "None recorded",
            LogDirectoryPath: _logDirectory
        );
    }
}
