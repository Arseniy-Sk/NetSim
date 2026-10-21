using System.IO;
using System.Text.Json;

namespace NetSim.App.Services;

/// <summary>Пользовательские настройки (%AppData%\NetSim\settings.json): язык, последние файлы.</summary>
public sealed class AppSettings
{
    public string Language { get; set; } = "ru";
    public List<string> RecentFiles { get; set; } = new();
    public bool ShowFlows { get; set; } = true;
    public bool ShowLinkLabels { get; set; }
    public Dictionary<string, string> MediaColors { get; set; } = new();

    private static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetSim");
    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            // повреждённый файл настроек — начинаем с настроек по умолчанию
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // настройки не критичны
        }
    }

    public void AddRecent(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > 10) RecentFiles.RemoveRange(10, RecentFiles.Count - 10);
        Save();
    }
}
