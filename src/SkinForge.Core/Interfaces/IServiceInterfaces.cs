using SkinForge.Core.Models;

namespace SkinForge.Core.Interfaces;

public interface ISearchEngine
{
    IReadOnlyList<Skin> Search(IEnumerable<Skin> source, string query);
}

public interface IBackupService
{
    Task<OperationResult<string>> CreateBackupAsync(string targetFilePath, CancellationToken cancellationToken = default);
    Task<OperationResult> RestoreBackupAsync(string backupFilePath, CancellationToken cancellationToken = default);
}

public interface ILocalizationService
{
    string CurrentLanguage { get; }
    IReadOnlyList<string> SupportedLanguages { get; }
    string GetString(string key);
    void SetLanguage(string languageCode);
    event EventHandler? LanguageChanged;
}

public interface IImageCacheService
{
    Task<string?> GetOrCacheThumbnailAsync(string? originalImagePath, int targetWidth = 320, int targetHeight = 180, CancellationToken cancellationToken = default);
    void ClearCache();
}
