using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkinForge.Infrastructure.SystemInfo;

namespace SkinForge.Infrastructure.ModProviders;

/// <summary>Updates the official flat leaguePath preference while the manager is closed.</summary>
public sealed class LtkGameConfiguration(string? settingsPath = null, Func<bool>? managerRunning = null)
{
    public string SettingsPath { get; } = settingsPath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dev.leaguetoolkit.manager", "settings.json");

    public async Task ConfigureAsync(string gamePath, CancellationToken ct = default)
    {
        var root = GameLocator.Normalize(gamePath);
        if (root == null || !File.Exists(Path.Combine(root, "Game", "League of Legends.exe")))
            throw new LtkSetupException("settings_game_invalid");
        var exists = File.Exists(SettingsPath);
        var original = exists ? await File.ReadAllTextAsync(SettingsPath, ct) : null;
        JsonObject settings;
        try
        {
            settings = original == null ? new JsonObject
            {
                ["firstRunComplete"] = false, ["theme"] = "system", ["accentColor"] = new JsonObject()
            } : JsonNode.Parse(original)?.AsObject() ?? throw new JsonException();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        { throw new LtkSetupException("setup_config_invalid", ex); }
        if (settings["leaguePath"] is JsonValue value && value.TryGetValue<string>(out var current)
            && string.Equals(GameLocator.Normalize(current), root, StringComparison.OrdinalIgnoreCase)) return;
        if ((managerRunning ?? IsManagerRunning)()) throw new LtkSetupException("setup_close_ltk");
        settings["leaguePath"] = root;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(SettingsPath))!);
        var temp = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), ct);
            ct.ThrowIfCancellationRequested();
            // Check again immediately before committing. Never overwrite changes made by a running manager.
            if ((managerRunning ?? IsManagerRunning)()) throw new LtkSetupException("setup_close_ltk");
            if (exists)
            {
                if (await File.ReadAllTextAsync(SettingsPath, ct) != original) throw new LtkSetupException("setup_config_changed");
                File.Replace(temp, SettingsPath, SettingsPath + ".riftevora-" + Guid.NewGuid().ToString("N") + ".bak");
            }
            else File.Move(temp, SettingsPath);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static bool IsManagerRunning()
    {
        var processes = Process.GetProcessesByName("ltk-manager");
        foreach (var process in processes) process.Dispose();
        return processes.Length != 0;
    }
}
