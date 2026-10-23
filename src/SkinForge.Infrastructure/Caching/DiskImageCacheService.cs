using Serilog;
using SkinForge.Core.Interfaces;
using SkinForge.Infrastructure.FileSystem;

namespace SkinForge.Infrastructure.Caching;

public class DiskImageCacheService : IImageCacheService
{
    private readonly string _cacheDirectory;

    public DiskImageCacheService(string cacheDirectory)
    {
        _cacheDirectory = cacheDirectory;
        Directory.CreateDirectory(_cacheDirectory);
    }

    public Task<string?> GetOrCacheThumbnailAsync(
        string? originalImagePath,
        int targetWidth = 320,
        int targetHeight = 180,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(originalImagePath) || !File.Exists(originalImagePath))
        {
            return Task.FromResult<string?>(null);
        }

        try
        {
            var hash = FileHashHelper.ComputeSha256(System.Text.Encoding.UTF8.GetBytes(originalImagePath));
            var cacheFileName = $"{hash}_{targetWidth}x{targetHeight}.thumb";
            var cachedPath = Path.Combine(_cacheDirectory, cacheFileName);

            if (File.Exists(cachedPath))
            {
                return Task.FromResult<string?>(cachedPath);
            }

            // Copy file to cache
            File.Copy(originalImagePath, cachedPath, overwrite: true);
            return Task.FromResult<string?>(cachedPath);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to cache thumbnail for '{Path}'. Returning original.", originalImagePath);
            return Task.FromResult<string?>(originalImagePath);
        }
    }

    public void ClearCache()
    {
        try
        {
            if (Directory.Exists(_cacheDirectory))
            {
                var files = Directory.GetFiles(_cacheDirectory, "*.thumb");
                foreach (var f in files)
                {
                    try { File.Delete(f); } catch { /* ignore */ }
                }
            }
            Log.Information("Image disk cache cleared.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to clear disk cache.");
        }
    }
}
