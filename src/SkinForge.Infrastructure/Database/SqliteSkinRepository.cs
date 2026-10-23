using System.Text.Json;
using Microsoft.Data.Sqlite;
using Serilog;
using SkinForge.Core.Enums;
using SkinForge.Core.Interfaces;
using SkinForge.Core.Models;

namespace SkinForge.Infrastructure.Database;

public class SqliteSkinRepository : ISkinRepository
{
    private readonly string _connectionString;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public SqliteSkinRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    private SqliteConnection CreateConnection() => new(_connectionString);

    public async Task<List<Skin>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<Skin>();
        using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Skins ORDER BY LoadOrderPriority ASC, CreatedAt DESC;";

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(MapSkinFromReader(reader));
        }

        return list;
    }

    public async Task<Skin?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Skins WHERE Id = @Id LIMIT 1;";
        cmd.Parameters.AddWithValue("@Id", id);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return MapSkinFromReader(reader);
        }

        return null;
    }

    public async Task<List<Skin>> GetByChampionAsync(string champion, CancellationToken cancellationToken = default)
    {
        var list = new List<Skin>();
        using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Skins WHERE LOWER(Champion) = LOWER(@Champ) ORDER BY Name ASC;";
        cmd.Parameters.AddWithValue("@Champ", champion);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(MapSkinFromReader(reader));
        }

        return list;
    }

    public async Task<List<Skin>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<Skin>();
        using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Skins WHERE IsActive = 1 ORDER BY LoadOrderPriority ASC;";

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(MapSkinFromReader(reader));
        }

        return list;
    }

    public async Task<List<Skin>> GetFavoritesAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<Skin>();
        using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Skins WHERE IsFavorite = 1 ORDER BY Name ASC;";

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(MapSkinFromReader(reader));
        }

        return list;
    }

    public async Task<OperationResult> AddAsync(Skin skin, CancellationToken cancellationToken = default)
    {
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Skins (
                    Id, Name, Champion, SkinSlot, Author, Version, Description, GameVersion,
                    Category, Status, IsInstalled, IsActive, IsFavorite, FileSizeBytes,
                    PreviewImagePath, ScreenshotsJson, TagsJson, AssetsJson, DependenciesJson,
                    CreatedAt, LastUsedAt, LoadOrderPriority
                ) VALUES (
                    @Id, @Name, @Champion, @SkinSlot, @Author, @Version, @Description, @GameVersion,
                    @Category, @Status, @IsInstalled, @IsActive, @IsFavorite, @FileSizeBytes,
                    @PreviewImagePath, @ScreenshotsJson, @TagsJson, @AssetsJson, @DependenciesJson,
                    @CreatedAt, @LastUsedAt, @LoadOrderPriority
                );
            ";

            BindSkinParameters(cmd, skin);
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            Log.Information("Added skin {SkinName} ({SkinId}) to database.", skin.Name, skin.Id);
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to add skin {SkinId} to database.", skin.Id);
            return OperationResult.Failure(ex.Message);
        }
    }

    public async Task<OperationResult> UpdateAsync(Skin skin, CancellationToken cancellationToken = default)
    {
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE Skins SET
                    Name = @Name,
                    Champion = @Champion,
                    SkinSlot = @SkinSlot,
                    Author = @Author,
                    Version = @Version,
                    Description = @Description,
                    GameVersion = @GameVersion,
                    Category = @Category,
                    Status = @Status,
                    IsInstalled = @IsInstalled,
                    IsActive = @IsActive,
                    IsFavorite = @IsFavorite,
                    FileSizeBytes = @FileSizeBytes,
                    PreviewImagePath = @PreviewImagePath,
                    ScreenshotsJson = @ScreenshotsJson,
                    TagsJson = @TagsJson,
                    AssetsJson = @AssetsJson,
                    DependenciesJson = @DependenciesJson,
                    CreatedAt = @CreatedAt,
                    LastUsedAt = @LastUsedAt,
                    LoadOrderPriority = @LoadOrderPriority
                WHERE Id = @Id;
            ";

            BindSkinParameters(cmd, skin);
            var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);

            return rows > 0 ? OperationResult.Success() : OperationResult.Failure("Skin not found.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to update skin {SkinId}.", skin.Id);
            return OperationResult.Failure(ex.Message);
        }
    }

    public async Task<OperationResult> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Skins WHERE Id = @Id;";
            cmd.Parameters.AddWithValue("@Id", id);

            var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
            Log.Information("Deleted skin {SkinId} from database.", id);
            return rows > 0 ? OperationResult.Success() : OperationResult.Failure("Skin not found.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete skin {SkinId}.", id);
            return OperationResult.Failure(ex.Message);
        }
    }

    public async Task<OperationResult> SetActiveStatusAsync(string id, bool isActive, CancellationToken cancellationToken = default)
    {
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE Skins SET 
                    IsActive = @IsActive, 
                    Status = @Status,
                    LastUsedAt = CASE WHEN @IsActive = 1 THEN @Now ELSE LastUsedAt END
                WHERE Id = @Id;
            ";
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@IsActive", isActive ? 1 : 0);
            cmd.Parameters.AddWithValue("@Status", (int)(isActive ? ModStatus.Active : ModStatus.Disabled));
            cmd.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("O"));

            var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
            return rows > 0 ? OperationResult.Success() : OperationResult.Failure("Skin not found.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to set active status for skin {SkinId}.", id);
            return OperationResult.Failure(ex.Message);
        }
    }

    public async Task<OperationResult> SetFavoriteStatusAsync(string id, bool isFavorite, CancellationToken cancellationToken = default)
    {
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Skins SET IsFavorite = @IsFavorite WHERE Id = @Id;";
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@IsFavorite", isFavorite ? 1 : 0);

            var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
            return rows > 0 ? OperationResult.Success() : OperationResult.Failure("Skin not found.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to set favorite status for skin {SkinId}.", id);
            return OperationResult.Failure(ex.Message);
        }
    }

    public async Task<List<SkinCollection>> GetCollectionsAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<SkinCollection>();
        using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM SkinCollections ORDER BY Name ASC;";

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new SkinCollection
            {
                Id = reader.GetString(reader.GetOrdinal("Id")),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                Description = reader.GetString(reader.GetOrdinal("Description")),
                SkinIds = JsonSerializer.Deserialize<List<string>>(reader.GetString(reader.GetOrdinal("SkinIdsJson")), JsonOptions) ?? [],
                CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
                Notes = reader.IsDBNull(reader.GetOrdinal("Notes")) ? null : reader.GetString(reader.GetOrdinal("Notes"))
            });
        }

        return list;
    }

    public async Task<OperationResult> SaveCollectionAsync(SkinCollection collection, CancellationToken cancellationToken = default)
    {
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO SkinCollections (Id, Name, Description, SkinIdsJson, CreatedAt, Notes)
                VALUES (@Id, @Name, @Description, @SkinIdsJson, @CreatedAt, @Notes)
                ON CONFLICT(Id) DO UPDATE SET
                    Name = excluded.Name,
                    Description = excluded.Description,
                    SkinIdsJson = excluded.SkinIdsJson,
                    Notes = excluded.Notes;
            ";

            cmd.Parameters.AddWithValue("@Id", collection.Id);
            cmd.Parameters.AddWithValue("@Name", collection.Name);
            cmd.Parameters.AddWithValue("@Description", collection.Description);
            cmd.Parameters.AddWithValue("@SkinIdsJson", JsonSerializer.Serialize(collection.SkinIds, JsonOptions));
            cmd.Parameters.AddWithValue("@CreatedAt", collection.CreatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("@Notes", (object?)collection.Notes ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save collection {CollectionId}.", collection.Id);
            return OperationResult.Failure(ex.Message);
        }
    }

    public async Task<OperationResult> DeleteCollectionAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM SkinCollections WHERE Id = @Id;";
            cmd.Parameters.AddWithValue("@Id", id);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete collection {CollectionId}.", id);
            return OperationResult.Failure(ex.Message);
        }
    }

    public async Task<AppSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Value FROM Settings WHERE Key = 'AppSettings' LIMIT 1;";

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        if (result != null && result != DBNull.Value)
        {
            var json = (string)result;
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }

        return new AppSettings();
    }

    public async Task<OperationResult> SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Settings (Key, Value) VALUES ('AppSettings', @Value)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            ";
            cmd.Parameters.AddWithValue("@Value", JsonSerializer.Serialize(settings, JsonOptions));

            await cmd.ExecuteNonQueryAsync(cancellationToken);
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save settings.");
            return OperationResult.Failure(ex.Message);
        }
    }

    public async Task<int> GetInstalledCountAsync(CancellationToken cancellationToken = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Skins WHERE IsInstalled = 1;";
        var res = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(res);
    }

    public async Task<int> GetActiveCountAsync(CancellationToken cancellationToken = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Skins WHERE IsActive = 1;";
        var res = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(res);
    }

    public async Task<long> GetTotalStorageUsedBytesAsync(CancellationToken cancellationToken = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(SUM(FileSizeBytes), 0) FROM Skins;";
        var res = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(res);
    }

    private static void BindSkinParameters(SqliteCommand cmd, Skin skin)
    {
        cmd.Parameters.AddWithValue("@Id", skin.Id);
        cmd.Parameters.AddWithValue("@Name", skin.Name);
        cmd.Parameters.AddWithValue("@Champion", skin.Champion);
        cmd.Parameters.AddWithValue("@SkinSlot", skin.SkinSlot);
        cmd.Parameters.AddWithValue("@Author", skin.Author);
        cmd.Parameters.AddWithValue("@Version", skin.Version);
        cmd.Parameters.AddWithValue("@Description", skin.Description);
        cmd.Parameters.AddWithValue("@GameVersion", skin.GameVersion);
        cmd.Parameters.AddWithValue("@Category", (int)skin.Category);
        cmd.Parameters.AddWithValue("@Status", (int)skin.Status);
        cmd.Parameters.AddWithValue("@IsInstalled", skin.IsInstalled ? 1 : 0);
        cmd.Parameters.AddWithValue("@IsActive", skin.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("@IsFavorite", skin.IsFavorite ? 1 : 0);
        cmd.Parameters.AddWithValue("@FileSizeBytes", skin.FileSizeBytes);
        cmd.Parameters.AddWithValue("@PreviewImagePath", (object?)skin.PreviewImagePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ScreenshotsJson", JsonSerializer.Serialize(skin.Screenshots, JsonOptions));
        cmd.Parameters.AddWithValue("@TagsJson", JsonSerializer.Serialize(skin.Tags, JsonOptions));
        cmd.Parameters.AddWithValue("@AssetsJson", JsonSerializer.Serialize(skin.Assets, JsonOptions));
        cmd.Parameters.AddWithValue("@DependenciesJson", JsonSerializer.Serialize(skin.Dependencies, JsonOptions));
        cmd.Parameters.AddWithValue("@CreatedAt", skin.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@LastUsedAt", skin.LastUsedAt.HasValue ? skin.LastUsedAt.Value.ToString("O") : DBNull.Value);
        cmd.Parameters.AddWithValue("@LoadOrderPriority", skin.LoadOrderPriority);
    }

    private static Skin MapSkinFromReader(SqliteDataReader reader)
    {
        var id = reader.GetString(reader.GetOrdinal("Id"));
        var name = reader.GetString(reader.GetOrdinal("Name"));
        var champ = reader.GetString(reader.GetOrdinal("Champion"));

        var skin = new Skin(id, name, champ)
        {
            SkinSlot = reader.GetInt32(reader.GetOrdinal("SkinSlot")),
            Author = reader.GetString(reader.GetOrdinal("Author")),
            Version = reader.GetString(reader.GetOrdinal("Version")),
            Description = reader.GetString(reader.GetOrdinal("Description")),
            GameVersion = reader.GetString(reader.GetOrdinal("GameVersion")),
            Category = (ModCategory)reader.GetInt32(reader.GetOrdinal("Category")),
            Status = (ModStatus)reader.GetInt32(reader.GetOrdinal("Status")),
            IsInstalled = reader.GetInt32(reader.GetOrdinal("IsInstalled")) == 1,
            IsActive = reader.GetInt32(reader.GetOrdinal("IsActive")) == 1,
            IsFavorite = reader.GetInt32(reader.GetOrdinal("IsFavorite")) == 1,
            FileSizeBytes = reader.GetInt64(reader.GetOrdinal("FileSizeBytes")),
            PreviewImagePath = reader.IsDBNull(reader.GetOrdinal("PreviewImagePath")) ? null : reader.GetString(reader.GetOrdinal("PreviewImagePath")),
            Screenshots = JsonSerializer.Deserialize<List<string>>(reader.GetString(reader.GetOrdinal("ScreenshotsJson")), JsonOptions) ?? [],
            Tags = JsonSerializer.Deserialize<List<string>>(reader.GetString(reader.GetOrdinal("TagsJson")), JsonOptions) ?? [],
            Assets = JsonSerializer.Deserialize<List<SkinAsset>>(reader.GetString(reader.GetOrdinal("AssetsJson")), JsonOptions) ?? [],
            Dependencies = JsonSerializer.Deserialize<List<string>>(reader.GetString(reader.GetOrdinal("DependenciesJson")), JsonOptions) ?? [],
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
            LastUsedAt = reader.IsDBNull(reader.GetOrdinal("LastUsedAt")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("LastUsedAt"))),
            LoadOrderPriority = reader.GetInt32(reader.GetOrdinal("LoadOrderPriority"))
        };

        return skin;
    }
}
