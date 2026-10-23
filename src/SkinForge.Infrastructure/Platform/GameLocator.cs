using System.Diagnostics;
using System.Text.Json;

namespace SkinForge.Infrastructure.SystemInfo;

public sealed record GameInstallation(string RootDirectory, string? GameVersion, string? RiotClientPath, string Source);

/// <summary>Finds League of Legends the way Riot's own launcher does, then falls back to common folders.</summary>
public static class GameLocator
{
    public static GameInstallation? Detect(string? userChoice = null, string? manifestPath = null, IEnumerable<string>? extraRoots = null)
    {
        if (!string.IsNullOrWhiteSpace(userChoice) && Normalize(userChoice) is { } chosen)
            return Build(chosen, null, "Elle seçildi");

        manifestPath ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Riot Games", "RiotClientInstalls.json");
        try
        {
            if (File.Exists(manifestPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
                if (doc.RootElement.TryGetProperty("associated_client", out var clients))
                {
                    foreach (var client in clients.EnumerateObject())
                    {
                        if (Normalize(client.Name) is { } root) return Build(root, client.Value.GetString(), "Riot Client kaydı");
                    }
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException) { }

        var candidates = new List<string>(extraRoots ?? []);
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            candidates.Add(Path.Combine(drive.RootDirectory.FullName, "Riot Games", "League of Legends"));
            candidates.Add(Path.Combine(drive.RootDirectory.FullName, "Program Files", "Riot Games", "League of Legends"));
            candidates.Add(Path.Combine(drive.RootDirectory.FullName, "Program Files (x86)", "Riot Games", "League of Legends"));
        }
        foreach (var candidate in candidates)
            if (Normalize(candidate) is { } root) return Build(root, null, "Standart klasör");
        return null;
    }

    /// <summary>Accepts the install root, its Game folder or an exe inside it.</summary>
    public static string? Normalize(string path)
    {
        try
        {
            var full = Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full)) full = Path.GetDirectoryName(full)!;
            foreach (var root in new[] { full, Directory.GetParent(full)?.FullName })
            {
                if (root != null && (File.Exists(Path.Combine(root, "Game", "League of Legends.exe")) || File.Exists(Path.Combine(root, "LeagueClient.exe"))))
                    return root.TrimEnd(Path.DirectorySeparatorChar);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
        return null;
    }

    private static GameInstallation Build(string root, string? riotClient, string source)
    {
        string? version = null;
        try
        {
            var exe = Path.Combine(root, "Game", "League of Legends.exe");
            if (File.Exists(exe)) version = FileVersionInfo.GetVersionInfo(exe).FileVersion?.Replace(", ", ".");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return new GameInstallation(root, version, riotClient != null && File.Exists(riotClient) ? riotClient : null, source);
    }

    public static bool IsGameRunning() => AnyRunning("League of Legends");
    public static bool IsClientRunning() => AnyRunning("LeagueClient") || AnyRunning("RiotClientServices");

    private static bool AnyRunning(string name)
    {
        try
        {
            var processes = Process.GetProcessesByName(name);
            foreach (var process in processes) process.Dispose();
            return processes.Length > 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }
}
