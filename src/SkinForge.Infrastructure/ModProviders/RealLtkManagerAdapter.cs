using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SkinForge.Core.Interfaces;
using SkinForge.Core.Models;

namespace SkinForge.Infrastructure.ModProviders;

/// <summary>Uses LTK's supported file-open integration. Never invents patcher state.</summary>
public class RealLtkManagerAdapter : ILtkManagerAdapter
{
    private readonly ILogger<RealLtkManagerAdapter> _logger;
    private string? _detectedPath;
    private readonly IReadOnlyList<string> _candidates;
    private readonly bool _inspectProcesses;
    private static readonly string[] Candidates =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LTK Manager", "ltk-manager.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LTK Manager", "ltk-manager.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "LTK Manager", "ltk-manager.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "LTK Manager", "ltk-manager.exe")
    ];

    public RealLtkManagerAdapter(ILogger<RealLtkManagerAdapter> logger, IReadOnlyList<string>? candidateLocations = null)
    {
        _logger = logger;
        _candidates = candidateLocations ?? Candidates;
        _inspectProcesses = candidateLocations == null;
    }

    public Task<bool> DetectInstallationAsync(string? customPath = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (customPath != null)
        {
            _detectedPath = File.Exists(customPath) && Path.GetFileName(customPath).Equals("ltk-manager.exe", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFullPath(customPath) : null;
            return Task.FromResult(_detectedPath != null);
        }
        if (_detectedPath != null && File.Exists(_detectedPath)) return Task.FromResult(true);
        _detectedPath = _candidates.FirstOrDefault(File.Exists);
        if (_detectedPath == null && _inspectProcesses)
        {
            foreach (var process in Process.GetProcessesByName("ltk-manager"))
            {
                using (process)
                {
                    try { _detectedPath = process.MainModule?.FileName; }
                    catch (Exception ex) { _logger.LogDebug(ex, "Cannot inspect LTK process"); }
                }
                if (_detectedPath != null) break;
            }
        }
        return Task.FromResult(_detectedPath != null);
    }

    public async Task<LtkInfo> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var installed = await DetectInstallationAsync(cancellationToken: cancellationToken);
        return new LtkInfo(installed, await GetVersionAsync(cancellationToken), _detectedPath,
            installed ? LtkStatus.Ready : LtkStatus.NotDetected,
            installed ? "LTK Manager bulundu. Modu LTK'de etkinleştirip Start düğmesine basın."
                : "LTK Manager bulunamadı. Kurun veya uygulama dosyasını seçin.",
            0, installed);
    }

    public Task<string?> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        try { return Task.FromResult(_detectedPath == null ? null : FileVersionInfo.GetVersionInfo(_detectedPath).ProductVersion); }
        catch { return Task.FromResult<string?>(null); }
    }

    public async Task<OperationResult> OpenManagerAsync(string? packagePath = null, CancellationToken cancellationToken = default)
    {
        if (!await DetectInstallationAsync(cancellationToken: cancellationToken))
            return OperationResult.Failure(ApplicationError.LtkManagerNotFound("ltk-manager.exe", "OpenManager"));
        if (packagePath != null && (!File.Exists(packagePath) || !IsPackage(packagePath)))
            return OperationResult.Failure(ApplicationError.PackageInvalid("Var olan bir .fantome veya .modpkg dosyası seçin."));
        try
        {
            var start = new ProcessStartInfo(_detectedPath!) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(_detectedPath)! };
            if (packagePath != null) start.ArgumentList.Add(Path.GetFullPath(packagePath));
            using var process = Process.Start(start);
            if (process == null) return OperationResult.Failure("LTK Manager başlatılamadı.");
            _logger.LogInformation("Opened LTK Manager with package {Package}. Activation is controlled by LTK.", packagePath);
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not open LTK Manager");
            return OperationResult.Failure(ApplicationError.LtkManagerUnavailable(ex.Message, "OpenManager"));
        }
    }

    private static bool IsPackage(string path) => Path.GetExtension(path).ToLowerInvariant() is ".fantome" or ".modpkg";
    private static Task<OperationResult> ManagedByLtk() => Task.FromResult(OperationResult.Failure(
        ApplicationError.LtkManagerUnavailable("Bu işlem LTK Manager penceresinde tamamlanır. LTK henüz harici mod yönetimi API'si sunmuyor.", "LtkHandoff")));
    public Task<OperationResult> ImportModAsync(string modFilePath, CancellationToken cancellationToken = default) => OpenManagerAsync(modFilePath, cancellationToken);
    public Task<IReadOnlyList<string>> GetInstalledModsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
    public async Task<OperationResult> ValidateEnvironmentAsync(CancellationToken cancellationToken = default) =>
        await DetectInstallationAsync(cancellationToken: cancellationToken) ? OperationResult.Success() : OperationResult.Failure(ApplicationError.LtkManagerNotFound("ltk-manager.exe", "ValidateEnvironment"));
    public Task<OperationResult> EnableModAsync(string modId, CancellationToken cancellationToken = default) => ManagedByLtk();
    public Task<OperationResult> DisableModAsync(string modId, CancellationToken cancellationToken = default) => ManagedByLtk();
    public Task<OperationResult> PrepareModAsync(Skin skin, CancellationToken cancellationToken = default) => ManagedByLtk();
    public Task<OperationResult> ApplyModAsync(Skin skin, CancellationToken cancellationToken = default) =>
        _detectedPath == null ? Task.FromResult(OperationResult.Failure(ApplicationError.LtkManagerNotFound("ltk-manager.exe", "ApplyMod"))) : ManagedByLtk();
    public Task<OperationResult> DisableModAsync(Skin skin, CancellationToken cancellationToken = default) => ManagedByLtk();
    public Task<OperationResult> RunPatcherAsync(IReadOnlyList<string> activeModIds, CancellationToken cancellationToken = default) => ManagedByLtk();
    public Task<OperationResult> StopPatcherAsync(CancellationToken cancellationToken = default) => ManagedByLtk();
    public Task<OperationResult> RestoreAsync(CancellationToken cancellationToken = default) => ManagedByLtk();
}

