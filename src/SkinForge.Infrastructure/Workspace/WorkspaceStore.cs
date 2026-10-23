using System.Text.Json;
using System.Text.Json.Serialization;

namespace SkinForge.Infrastructure.Workspace;

public sealed class RecentEntry
{
    public string Ref { get; set; } = "";
    public DateTime ViewedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class CollectionData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<string> Items { get; set; } = [];
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public enum HistoryResult { Ok, Warning, Error }

public sealed class HistoryEntry
{
    public DateTime TimeUtc { get; set; } = DateTime.UtcNow;
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public HistoryResult Result { get; set; }
    public string Detail { get; set; } = "";
}

/// <summary>Everything the user decided. Missing or unknown fields never break an older file.</summary>
public sealed class WorkspaceData
{
    public HashSet<string> Favorites { get; set; } = [];
    public string? LtkPath { get; set; }
    public string? GamePath { get; set; }
    public bool ReduceMotion { get; set; }
    public string Language { get; set; } = "tr";
    public string Accent { get; set; } = "gold";
    public bool CompactSidebar { get; set; }
    public bool OfflineMode { get; set; }
    public bool SetupCompleted { get; set; }
    public DateTime? RiskAcceptedUtc { get; set; }
    public string StartPage { get; set; } = "home";
    public List<RecentEntry> Recents { get; set; } = [];
    public List<CollectionData> Collections { get; set; } = [];
    public List<HistoryEntry> History { get; set; } = [];
}

/// <summary>Atomic, debounced JSON persistence. A crash can lose at most the last few hundred milliseconds.</summary>
public sealed class WorkspaceStore : IDisposable
{
    public const int MaxHistory = 300;
    public const int MaxRecents = 24;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never };
    private readonly string _file;
    private readonly object _gate = new();
    private CancellationTokenSource? _pending;
    private Task _writing = Task.CompletedTask;
    public WorkspaceData Data { get; private set; } = new();
    public string FilePath => _file;
    public string? LoadWarning { get; private set; }

    public WorkspaceStore(string file) { _file = file; }

    public void Load()
    {
        try
        {
            if (!File.Exists(_file)) return;
            Data = JsonSerializer.Deserialize<WorkspaceData>(File.ReadAllText(_file), Options) ?? new();
            Data.Favorites = new HashSet<string>(Data.Favorites ?? [], StringComparer.Ordinal);
            Data.Recents ??= []; Data.Collections ??= []; Data.History ??= [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Keep the unreadable file for inspection instead of silently overwriting the user's data.
            LoadWarning = ex.Message;
            try { File.Copy(_file, _file + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"), true); } catch (IOException) { }
            Data = new();
        }
    }

    public void Replace(WorkspaceData data) { lock (_gate) Data = data; SaveSoon(); }

    public void SaveSoon()
    {
        CancellationTokenSource cts;
        lock (_gate) { _pending?.Cancel(); cts = _pending = new CancellationTokenSource(); }
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(350, cts.Token); } catch (OperationCanceledException) { return; }
            Flush();
        });
    }

    public void Flush()
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
                var temp = _file + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(Data, Options));
                File.Move(temp, _file, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SaveFailure = ex.Message;
            }
        }
    }

    public string? SaveFailure { get; private set; }

    // ----- Favorites -----
    public bool IsFavorite(string reference) { lock (_gate) return Data.Favorites.Contains(reference); }
    public bool ToggleFavorite(string reference)
    {
        bool now;
        lock (_gate) { now = Data.Favorites.Add(reference); if (!now) Data.Favorites.Remove(reference); }
        SaveSoon();
        return now;
    }

    // ----- Recents -----
    public void AddRecent(string reference)
    {
        lock (_gate)
        {
            Data.Recents.RemoveAll(r => r.Ref == reference);
            Data.Recents.Insert(0, new RecentEntry { Ref = reference });
            if (Data.Recents.Count > MaxRecents) Data.Recents.RemoveRange(MaxRecents, Data.Recents.Count - MaxRecents);
        }
        SaveSoon();
    }

    // ----- History -----
    public HistoryEntry Log(string action, string target, HistoryResult result = HistoryResult.Ok, string detail = "")
    {
        var entry = new HistoryEntry { Action = action, Target = target, Result = result, Detail = detail };
        lock (_gate)
        {
            Data.History.Insert(0, entry);
            if (Data.History.Count > MaxHistory) Data.History.RemoveRange(MaxHistory, Data.History.Count - MaxHistory);
        }
        SaveSoon();
        return entry;
    }

    public void ClearHistory() { lock (_gate) Data.History.Clear(); SaveSoon(); }

    // ----- Collections -----
    public CollectionData CreateCollection(string name)
    {
        var collection = new CollectionData { Name = name.Trim() };
        lock (_gate) Data.Collections.Add(collection);
        SaveSoon();
        return collection;
    }

    public void RenameCollection(string id, string name)
    {
        lock (_gate) { var c = Data.Collections.FirstOrDefault(c => c.Id == id); if (c != null) c.Name = name.Trim(); }
        SaveSoon();
    }

    public void DeleteCollection(string id) { lock (_gate) Data.Collections.RemoveAll(c => c.Id == id); SaveSoon(); }

    public bool ToggleInCollection(string id, string reference)
    {
        bool added = false;
        lock (_gate)
        {
            var c = Data.Collections.FirstOrDefault(c => c.Id == id);
            if (c == null) return false;
            if (c.Items.Contains(reference)) c.Items.Remove(reference); else { c.Items.Add(reference); added = true; }
        }
        SaveSoon();
        return added;
    }

    public void Dispose() { Flush(); }
}
