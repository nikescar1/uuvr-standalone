using System.Text.Json;

namespace Uuvr.Loader.Core;

// Remembers manually added games between runs. Stored under %AppData%\UUVR.
public class LoaderSettings
{
    public List<string> ManualGamePaths { get; set; } = new();

    private static string SettingsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UUVR");

    private static string SettingsPath => Path.Combine(SettingsDir, "loader-settings.json");

    public static LoaderSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                return JsonSerializer.Deserialize<LoaderSettings>(File.ReadAllText(SettingsPath)) ?? new LoaderSettings();
            }
        }
        catch (Exception)
        {
        }

        return new LoaderSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
        }
    }
}
