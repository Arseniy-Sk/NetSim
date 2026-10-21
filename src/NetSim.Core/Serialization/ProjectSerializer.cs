using System.Text.Json;
using NetSim.Core.Catalog;
using NetSim.Core.Model;

namespace NetSim.Core.Serialization;

/// <summary>Сохранение и загрузка проекта (*.nsp — JSON).</summary>
public static class ProjectSerializer
{
    public const string Extension = ".nsp";

    private static JsonSerializerOptions Options => DeviceCatalog.JsonOptions;

    public static string ToJson(Project project) => JsonSerializer.Serialize(project, Options);

    public static Project FromJson(string json)
    {
        var project = JsonSerializer.Deserialize<Project>(json, Options)
                      ?? throw new InvalidDataException("Пустой файл проекта");
        if (project.FormatVersion > Project.CurrentFormatVersion)
            throw new InvalidDataException(
                $"Файл создан более новой версией программы (формат {project.FormatVersion}).");
        return project;
    }

    public static void Save(Project project, string path) => File.WriteAllText(path, ToJson(project));

    public static Project Load(string path) => FromJson(File.ReadAllText(path));

    /// <summary>Глубокая копия через сериализацию (используется для «Сохранить как шаблон», undo и т.п.).</summary>
    public static Project Clone(Project project) => FromJson(ToJson(project));
}
