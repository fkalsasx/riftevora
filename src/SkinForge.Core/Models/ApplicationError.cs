namespace SkinForge.Core.Models;

public enum ErrorSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Critical = 3
}

public class ApplicationError
{
    public string Code { get; init; } = "E014";
    public string Title { get; init; } = "Unexpected Error";
    public string Message { get; init; } = string.Empty;
    public ErrorSeverity Severity { get; init; } = ErrorSeverity.Error;
    public string? TechnicalDetails { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string? Operation { get; init; }
    public bool Recoverable { get; init; } = true;
    public string? SuggestedAction { get; init; }
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N")[..8];

    public ApplicationError() { }

    public ApplicationError(
        string code,
        string title,
        string message,
        ErrorSeverity severity = ErrorSeverity.Error,
        string? technicalDetails = null,
        string? operation = null,
        bool recoverable = true,
        string? suggestedAction = null)
    {
        Code = code;
        Title = title;
        Message = message;
        Severity = severity;
        TechnicalDetails = technicalDetails;
        Operation = operation;
        Recoverable = recoverable;
        SuggestedAction = suggestedAction;
    }

    // Standardized Factory Methods for E001 - E014
    public static ApplicationError PackageInvalid(string details, string? operation = null) =>
        new("E001", "Package Invalid", "The provided skin package format is invalid or corrupted.", ErrorSeverity.Error, details, operation, true, "Verify the .zip or .fantome archive integrity.");

    public static ApplicationError ManifestMissing(string details, string? operation = null) =>
        new("E002", "Manifest Missing", "The skin archive is missing 'manifest.json' or metadata.", ErrorSeverity.Error, details, operation, true, "Ensure the package contains a valid manifest.json file.");

    public static ApplicationError AssetMissing(string assetPath, string? operation = null) =>
        new("E003", "Asset Missing", $"A required asset was not found in the package: '{assetPath}'.", ErrorSeverity.Error, assetPath, operation, true, "Check package contents for missing files.");

    public static ApplicationError DuplicatePackage(string skinId, string? operation = null) =>
        new("E004", "Duplicate Package", $"A skin package with ID '{skinId}' is already installed in the library.", ErrorSeverity.Warning, skinId, operation, true, "Remove existing skin before reinstalling.");

    public static ApplicationError UnsupportedVersion(string version, string? operation = null) =>
        new("E005", "Unsupported Version", $"The skin package version '{version}' is incompatible with the current client.", ErrorSeverity.Warning, version, operation, true, "Check for a newer release from the mod author.");

    public static ApplicationError LtkManagerNotFound(string path, string? operation = null) =>
        new("E006", "LTK Manager Not Found", "LTK Manager was not detected at the specified system location.", ErrorSeverity.Warning, path, operation, true, "Specify the installation path in Settings > Integration.");

    public static ApplicationError LtkManagerUnavailable(string reason, string? operation = null) =>
        new("E007", "LTK Manager Unavailable", "LTK Manager service or CLI is currently unavailable.", ErrorSeverity.Error, reason, operation, true, "Ensure LTK Manager is not blocked by Windows permissions.");

    public static ApplicationError PermissionError(string path, string details, string? operation = null) =>
        new("E008", "Permission Denied", $"Insufficient file system permissions to access '{path}'.", ErrorSeverity.Error, details, operation, true, "Run SkinForge as Administrator or adjust folder security.");

    public static ApplicationError FileAccessError(string path, string details, string? operation = null) =>
        new("E009", "File Access Error", $"Could not access or lock file: '{path}'.", ErrorSeverity.Error, details, operation, true, "Ensure the file is not currently locked by another process.");

    public static ApplicationError DatabaseError(string details, string? operation = null) =>
        new("E010", "Database Error", "A local database storage operation failed.", ErrorSeverity.Critical, details, operation, false, "Run database integrity check in Settings > Diagnostics.");

    public static ApplicationError ConflictDetected(string message, string details, string? operation = null) =>
        new("E011", "Conflict Detected", message, ErrorSeverity.Warning, details, operation, true, "Open Conflict Manager to resolve overlapping assets.");

    public static ApplicationError BackupFailed(string details, string? operation = null) =>
        new("E012", "Backup Failed", "Could not complete metadata and database backup.", ErrorSeverity.Error, details, operation, true, "Ensure sufficient disk space is available.");

    public static ApplicationError RestoreFailed(string details, string? operation = null) =>
        new("E013", "Restore Failed", "Could not restore database from selected archive.", ErrorSeverity.Critical, details, operation, false, "Verify that the backup zip is not corrupted.");

    public static ApplicationError UnexpectedError(Exception ex, string? operation = null) =>
        new("E014", "Unexpected Error", "An unexpected condition occurred, but application state was preserved.", ErrorSeverity.Error, ex.ToString(), operation, true, "Check diagnostic logs for details.");
}
