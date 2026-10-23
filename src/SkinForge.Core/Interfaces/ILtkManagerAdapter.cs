using SkinForge.Core.Models;

namespace SkinForge.Core.Interfaces;

public enum LtkStatus
{
    NotDetected,
    Ready,
    NeedsAttention,
    IncompatibleEnvironment,
    PatcherRunning
}

public record LtkInfo(
    bool IsInstalled,
    string? Version,
    string? ExecutablePath,
    LtkStatus Status,
    string StatusMessage,
    int ActiveModCount,
    bool EnvironmentCompatible,
    string OfficialDownloadUrl = "https://github.com/LeagueToolkit/ltk-manager/releases"
);

public interface ILtkManagerAdapter
{
    /// <summary>
    /// Checks whether LTK Manager is installed on the host system.
    /// </summary>
    Task<bool> DetectInstallationAsync(string? customPath = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves current runtime status, version, and mod count.
    /// </summary>
    Task<LtkInfo> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the verified version of LTK Manager.
    /// </summary>
    Task<string?> GetVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets list of mod identifiers registered with LTK Manager.
    /// </summary>
    Task<IReadOnlyList<string>> GetInstalledModsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates environment (League process, permissions, file locks).
    /// </summary>
    Task<OperationResult> ValidateEnvironmentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports a .fantome or .modpkg archive into the LTK mod staging directory.
    /// </summary>
    Task<OperationResult> ImportModAsync(string modFilePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables a mod in the LTK profile.
    /// </summary>
    Task<OperationResult> EnableModAsync(string modId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disables a mod in the LTK profile.
    /// </summary>
    Task<OperationResult> DisableModAsync(string modId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prepares staging directory or packaging for LTK Manager ingestion.
    /// </summary>
    Task<OperationResult> PrepareModAsync(Skin skin, CancellationToken cancellationToken = default);

    /// <summary>
    /// Triggers mod activation via verified adapter mechanism.
    /// </summary>
    Task<OperationResult> ApplyModAsync(Skin skin, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disables mod in LTK Manager.
    /// </summary>
    Task<OperationResult> DisableModAsync(Skin skin, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the verified LTK patcher engine session.
    /// </summary>
    Task<OperationResult> RunPatcherAsync(IReadOnlyList<string> activeModIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the verified LTK patcher engine and restores unmodded state.
    /// </summary>
    Task<OperationResult> StopPatcherAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores clean state, unhooking any active mods.
    /// </summary>
    Task<OperationResult> RestoreAsync(CancellationToken cancellationToken = default);
}
