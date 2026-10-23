using Serilog;

namespace SkinForge.Application.Services;

public class GameDetectionService
{
    private static readonly string[] KnownDefaultPaths =
    [
        @"C:\Riot Games\League of Legends\Game",
        @"D:\Riot Games\League of Legends\Game",
        @"E:\Riot Games\League of Legends\Game",
        @"C:\Program Files\Riot Games\League of Legends\Game",
        @"C:\Program Files (x86)\Riot Games\League of Legends\Game"
    ];

    public string? DetectInstallationPath()
    {
        Log.Information("Scanning for League of Legends game installation...");

        // 1. Check known common paths
        foreach (var path in KnownDefaultPaths)
        {
            if (IsValidGameDirectory(path))
            {
                Log.Information("Located League of Legends at standard path: '{Path}'.", path);
                return path;
            }
        }

        // 2. Check parent folder variations e.g. "C:\Riot Games\League of Legends"
        foreach (var basePath in new[] { @"C:\Riot Games\League of Legends", @"D:\Riot Games\League of Legends" })
        {
            var subGame = Path.Combine(basePath, "Game");
            if (IsValidGameDirectory(subGame))
            {
                return subGame;
            }
        }

        Log.Warning("League of Legends installation could not be detected automatically.");
        return null;
    }

    public bool IsValidGameDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        // Must contain League of Legends.exe or LeagueClient.exe in parent
        var exe1 = Path.Combine(path, "League of Legends.exe");
        var clientExe = Path.Combine(Directory.GetParent(path)?.FullName ?? "", "LeagueClient.exe");

        return File.Exists(exe1) || File.Exists(clientExe);
    }
}
