using System;
using System.IO;
using System.Text.Json;
using CMPSimXS2.WPF.Models;

namespace CMPSimXS2.WPF.Helpers;

/// <summary>
/// Manages simulator settings persistence
/// Auto-saves on change, auto-loads on startup
/// </summary>
public static class SettingsManager
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CMPSimulator"
    );

    private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Load settings from file. Returns default settings if file doesn't exist.
    /// </summary>
    public static SimulatorSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                // Return default settings if file doesn't exist
                return new SimulatorSettings();
            }

            string json = File.ReadAllText(SettingsFilePath);
            var settings = JsonSerializer.Deserialize<SimulatorSettings>(json, JsonOptions);

            return settings ?? new SimulatorSettings();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading settings: {ex.Message}");
            return new SimulatorSettings();
        }
    }

    /// <summary>
    /// Save settings to file. Creates directory if it doesn't exist.
    /// </summary>
    public static void SaveSettings(SimulatorSettings settings)
    {
        try
        {
            // Create directory if it doesn't exist
            if (!Directory.Exists(SettingsDirectory))
            {
                Directory.CreateDirectory(SettingsDirectory);
            }

            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsFilePath, json);

            System.Diagnostics.Debug.WriteLine($"Settings saved to: {SettingsFilePath}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving settings: {ex.Message}");
        }
    }

    /// <summary>
    /// Get the settings file path
    /// </summary>
    public static string GetSettingsFilePath() => SettingsFilePath;
}
