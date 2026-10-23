using SkinForge.Core.Models;

namespace SkinForge.Core.Interfaces;

public interface ISkinRepository
{
    Task<List<Skin>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Skin?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<List<Skin>> GetByChampionAsync(string champion, CancellationToken cancellationToken = default);
    Task<List<Skin>> GetActiveAsync(CancellationToken cancellationToken = default);
    Task<List<Skin>> GetFavoritesAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> AddAsync(Skin skin, CancellationToken cancellationToken = default);
    Task<OperationResult> UpdateAsync(Skin skin, CancellationToken cancellationToken = default);
    Task<OperationResult> DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task<OperationResult> SetActiveStatusAsync(string id, bool isActive, CancellationToken cancellationToken = default);
    Task<OperationResult> SetFavoriteStatusAsync(string id, bool isFavorite, CancellationToken cancellationToken = default);
    Task<List<SkinCollection>> GetCollectionsAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> SaveCollectionAsync(SkinCollection collection, CancellationToken cancellationToken = default);
    Task<OperationResult> DeleteCollectionAsync(string id, CancellationToken cancellationToken = default);
    Task<AppSettings> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default);
    Task<int> GetInstalledCountAsync(CancellationToken cancellationToken = default);
    Task<int> GetActiveCountAsync(CancellationToken cancellationToken = default);
    Task<long> GetTotalStorageUsedBytesAsync(CancellationToken cancellationToken = default);
}
