using SkinForge.Core.Enums;

namespace SkinForge.Core.Models;

public class AppSettings
{
    public string AppName { get; set; } = "SkinForge";
    public string GameDirectory { get; set; } = @"C:\Riot Games\League of Legends\Game";
    public string LibraryDirectory { get; set; } = string.Empty;
    public string CacheDirectory { get; set; } = string.Empty;
    public string AccentColorHex { get; set; } = "#C89B3C";
    public string Theme { get; set; } = "Carbon";
    public string LanguageCode { get; set; } = "tr";
    public bool EnableAnimations { get; set; } = true;
    public bool CompactMode { get; set; } = false;
    public ViewMode DefaultViewMode { get; set; } = ViewMode.Grid;
    public bool HasCompletedOnboarding { get; set; } = true;
    public bool CheckConflictsOnActivation { get; set; } = true;
    public bool AutoBackupBeforeDestructiveAction { get; set; } = true;
    public string PerformanceProfile { get; set; } = "Balanced"; // Ultra, Balanced, Eco, Potato
    public int TargetFps { get; set; } = 60;
    public bool PauseWhenIdle { get; set; } = true;
    public bool VanguardStrictMode { get; set; } = true;
    public bool AutoClearOverlayOnExit { get; set; } = true;
    public string StagingDirectory { get; set; } = string.Empty;

    public AppSettings()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var baseDir = Path.Combine(localAppData, "SkinForge");
        LibraryDirectory = Path.Combine(baseDir, "Library");
        CacheDirectory = Path.Combine(baseDir, "Cache");
        StagingDirectory = Path.Combine(baseDir, "Staged");
    }
}
