using SkinForge.Core.Interfaces;
using SkinForge.Core.Models;

namespace SkinForge.Infrastructure.ModProviders;

public class MockLtkManagerAdapter : ILtkManagerAdapter
{
    public LtkStatus CurrentStatus { get; set; } = LtkStatus.Ready;
    public string MockVersion { get; set; } = "2.4.1";
    public string MockExecutablePath { get; set; } = @"C:\Program Files\LTK Manager\LTK Manager.exe";
    public List<string> InstalledMods { get; set; } = new() { "mod-cyber-yasuo", "mod-ahri-spirit" };
    public bool SimulateEnvironmentFailure { get; set; } = false;
    public bool SimulateApplyFailure { get; set; } = false;
    public bool IsPatcherRunning { get; private set; } = false;

    public Task<bool> DetectInstallationAsync(string? customPath = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CurrentStatus != LtkStatus.NotDetected);
    }

    public Task<LtkInfo> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentStatus == LtkStatus.NotDetected)
        {
            return Task.FromResult(new LtkInfo(
                IsInstalled: false,
                Version: null,
                ExecutablePath: null,
                Status: LtkStatus.NotDetected,
                StatusMessage: "LTK Manager not detected. Provide custom executable path in Settings.",
                ActiveModCount: 0,
                EnvironmentCompatible: false
            ));
        }

        bool compatible = !SimulateEnvironmentFailure && CurrentStatus == LtkStatus.Ready;
        return Task.FromResult(new LtkInfo(
            IsInstalled: true,
            Version: MockVersion,
            ExecutablePath: MockExecutablePath,
            Status: IsPatcherRunning ? LtkStatus.PatcherRunning : CurrentStatus,
            StatusMessage: compatible ? $"Mock LTK Manager v{MockVersion} is ready." : "Environment issues detected.",
            ActiveModCount: InstalledMods.Count,
            EnvironmentCompatible: compatible
        ));
    }

    public Task<string?> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(CurrentStatus != LtkStatus.NotDetected ? MockVersion : null);
    }

    public Task<IReadOnlyList<string>> GetInstalledModsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<string>>(InstalledMods.AsReadOnly());
    }

    public Task<OperationResult> ValidateEnvironmentAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentStatus == LtkStatus.NotDetected)
        {
            return Task.FromResult(OperationResult.Failure(
                ApplicationError.LtkManagerNotFound(MockExecutablePath, "ValidateEnvironment")));
        }

        if (SimulateEnvironmentFailure)
        {
            return Task.FromResult(OperationResult.Failure(
                ApplicationError.LtkManagerUnavailable("Simulated mock environment lock.", "ValidateEnvironment")));
        }

        return Task.FromResult(OperationResult.Success());
    }

    public Task<OperationResult> ImportModAsync(string modFilePath, CancellationToken cancellationToken = default)
    {
        var id = Path.GetFileNameWithoutExtension(modFilePath);
        if (!InstalledMods.Contains(id)) InstalledMods.Add(id);
        return Task.FromResult(OperationResult.Success());
    }

    public Task<OperationResult> EnableModAsync(string modId, CancellationToken cancellationToken = default)
    {
        if (!InstalledMods.Contains(modId)) InstalledMods.Add(modId);
        return Task.FromResult(OperationResult.Success());
    }

    public Task<OperationResult> DisableModAsync(string modId, CancellationToken cancellationToken = default)
    {
        InstalledMods.Remove(modId);
        return Task.FromResult(OperationResult.Success());
    }

    public Task<OperationResult> PrepareModAsync(Skin skin, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult.Success());
    }

    public Task<OperationResult> ApplyModAsync(Skin skin, CancellationToken cancellationToken = default)
    {
        if (SimulateApplyFailure)
        {
            return Task.FromResult(OperationResult.Failure(
                ApplicationError.ConflictDetected($"Mock simulation: Conflict applying mod '{skin.Name}'", "Simulated collision")));
        }

        if (!InstalledMods.Contains(skin.Id))
        {
            InstalledMods.Add(skin.Id);
        }

        return Task.FromResult(OperationResult.Success());
    }

    public Task<OperationResult> DisableModAsync(Skin skin, CancellationToken cancellationToken = default)
    {
        InstalledMods.Remove(skin.Id);
        return Task.FromResult(OperationResult.Success());
    }

    public Task<OperationResult> RunPatcherAsync(IReadOnlyList<string> activeModIds, CancellationToken cancellationToken = default)
    {
        IsPatcherRunning = true;
        return Task.FromResult(OperationResult.Success());
    }

    public Task<OperationResult> StopPatcherAsync(CancellationToken cancellationToken = default)
    {
        IsPatcherRunning = false;
        return Task.FromResult(OperationResult.Success());
    }

    public Task<OperationResult> RestoreAsync(CancellationToken cancellationToken = default)
    {
        InstalledMods.Clear();
        IsPatcherRunning = false;
        return Task.FromResult(OperationResult.Success());
    }
}
