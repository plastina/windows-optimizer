using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowsOptimizer.App;

public enum ThemeChoice { System, Light, Dark }

/// <summary>User preferences, kept in <c>%LocalAppData%\WindowsOptimizer\settings.json</c>.</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public ThemeChoice Theme { get; set; } = ThemeChoice.System;

    /// <summary>Whether to ask GitHub for the latest release when the app starts.</summary>
    public bool CheckForUpdates { get; set; } = true;

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsOptimizer", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preferences are a convenience; losing one must never get in the user's way.
        }
    }
}
