using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetSim.Core.Model;

namespace NetSim.Core.Catalog;

/// <summary>Элемент с названием на двух языках.</summary>
public abstract class Named
{
    public string Id { get; set; } = "";
    public string NameRu { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string DescriptionRu { get; set; } = "";
    public string DescriptionEn { get; set; } = "";

    public string Name(string lang) => lang == "en" || string.IsNullOrEmpty(NameRu) ? NameEn : NameRu;
    public string Description(string lang) => lang == "en" || string.IsNullOrEmpty(DescriptionRu) ? DescriptionEn : DescriptionRu;
}

/// <summary>Семейство технологий: от него зависит совместимость портов.</summary>
public enum TechFamily { Ethernet, TokenRing, Fddi, Wireless, Wan, Atm, Any }

/// <summary>Модель устройства в браузере оборудования.</summary>
public sealed class CatalogDevice : Named
{
    public DeviceKind Kind { get; set; }

    /// <summary>Ключ значка (pc, server, switch, router, cloud, ring, bus...).</summary>
    public string Icon { get; set; } = "pc";

    public string Vendor { get; set; } = "Generic";

    /// <summary>Поддерживаемые спецификации линий (id из MediaStandards). Пусто — любые.</summary>
    public List<string> Standards { get; set; } = new();

    /// <summary>Спецификация, предлагаемая по умолчанию в Link Assistant.</summary>
    public string DefaultStandard { get; set; } = "100base-tx";

    public int Ports { get; set; } = 1;

    /// <summary>Префикс имён интерфейсов (Fa0/, Gi0/, Eth, Wlan...).</summary>
    public string InterfacePrefix { get; set; } = "Fa0/";

    /// <summary>Внутренняя задержка обработки пакета, мс (коммутация, маршрутизация, облако).</summary>
    public double ProcessingDelayMs { get; set; }

    /// <summary>Значения по умолчанию для серверов.</summary>
    public List<string> PreinstalledSoftware { get; set; } = new();

    /// <summary>Фон вложенного уровня для контейнеров (country, city, building, room).</summary>
    public string? ChildBackground { get; set; }
}

public sealed class CatalogSoftware : Named
{
    /// <summary>Службы, которые можно включить галочками (Plugin Setup).</summary>
    public List<string> Services { get; set; } = new();

    /// <summary>Службы, включённые сразу после установки.</summary>
    public List<string> DefaultServices { get; set; } = new();
}

public sealed class CatalogService : Named { }

/// <summary>Тип физической среды (Media) и его цвет на схеме (View → Media Colors).</summary>
public sealed class MediaType : Named
{
    public string Color { get; set; } = "#2060C0";
    public bool Dashed { get; set; }
}

/// <summary>Спецификация линии (Protocol в Link Assistant).</summary>
public sealed class MediaStandard : Named
{
    public TechFamily Family { get; set; }
    public double BandwidthBps { get; set; }
    public List<string> Media { get; set; } = new();
    public double MaxLengthM { get; set; }

    /// <summary>Задержка распространения, нс/м (≈5 нс/м для меди и оптики).</summary>
    public double PropagationNsPerM { get; set; } = 5;

    /// <summary>Максимальный размер кадра (полезной нагрузки), байт.</summary>
    public int Mtu { get; set; } = 1500;
}

public sealed class CatalogCategory : Named
{
    public string Icon { get; set; } = "folder";
    public List<CatalogCategory> Children { get; set; } = new();

    /// <summary>Id устройств (или ПО, если IsSoftware) в этой группе.</summary>
    public List<string> Items { get; set; } = new();

    public bool IsSoftware { get; set; }
}

/// <summary>
/// База оборудования, ПО, сред и профилей трафика. Хранится в JSON,
/// чтобы новые устройства добавлялись без перекомпиляции.
/// </summary>
public sealed class DeviceCatalog
{
    public List<CatalogCategory> Categories { get; set; } = new();
    public List<CatalogDevice> Devices { get; set; } = new();
    public List<CatalogSoftware> Software { get; set; } = new();
    public List<CatalogService> Services { get; set; } = new();
    public List<MediaType> MediaTypes { get; set; } = new();
    public List<MediaStandard> Standards { get; set; } = new();
    public List<TrafficProfile> Profiles { get; set; } = new();

    private Dictionary<string, CatalogDevice>? _devices;
    private Dictionary<string, CatalogSoftware>? _software;
    private Dictionary<string, MediaStandard>? _standards;
    private Dictionary<string, MediaType>? _media;
    private Dictionary<string, CatalogService>? _services;

    public CatalogDevice? Device(string id) => (_devices ??= Devices.ToDictionary(d => d.Id)).GetValueOrDefault(id);
    public CatalogSoftware? SoftwareById(string id) => (_software ??= Software.ToDictionary(d => d.Id)).GetValueOrDefault(id);
    public MediaStandard? Standard(string id) => (_standards ??= Standards.ToDictionary(d => d.Id)).GetValueOrDefault(id);
    public MediaType? Media(string id) => (_media ??= MediaTypes.ToDictionary(d => d.Id)).GetValueOrDefault(id);
    public CatalogService? Service(string id) => (_services ??= Services.ToDictionary(d => d.Id)).GetValueOrDefault(id);
    public TrafficProfile? Profile(string id) => Profiles.FirstOrDefault(p => p.Id == id);

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static DeviceCatalog FromJson(string json) =>
        JsonSerializer.Deserialize<DeviceCatalog>(json, JsonOptions) ?? new DeviceCatalog();

    private static DeviceCatalog? _default;

    /// <summary>Встроенный каталог (Catalog/catalog.json). Рядом с программой можно положить
    /// catalog.json — тогда будет загружен он (так преподаватель может расширять базу).</summary>
    public static DeviceCatalog Default => _default ??= LoadDefault();

    private static DeviceCatalog LoadDefault()
    {
        var external = Path.Combine(AppContext.BaseDirectory, "catalog.json");
        if (File.Exists(external)) return FromJson(File.ReadAllText(external));

        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("NetSim.Core.Catalog.catalog.json")
                           ?? throw new InvalidOperationException("Встроенный каталог не найден");
        using var reader = new StreamReader(stream);
        return FromJson(reader.ReadToEnd());
    }

    /// <summary>
    /// Поддерживает ли модель спецификацию. В списке Standards можно указывать id спецификаций
    /// или целые семейства с префиксом «@» (@ethernet, @tokenring, @fddi, @wireless, @wan, @atm).
    /// Пустой список — устройство подключается к чему угодно.
    /// </summary>
    public static bool Supports(CatalogDevice? device, MediaStandard standard)
    {
        if (device == null || device.Standards.Count == 0) return true;
        foreach (var s in device.Standards)
        {
            if (s.StartsWith('@'))
            {
                if (string.Equals(s[1..], standard.Family.ToString(), StringComparison.OrdinalIgnoreCase)) return true;
            }
            else if (s == standard.Id) return true;
        }
        return false;
    }

    /// <summary>Спецификации, доступные для соединения двух моделей (пересечение их списков).</summary>
    public IReadOnlyList<MediaStandard> CompatibleStandards(CatalogDevice? a, CatalogDevice? b) =>
        Standards.Where(s => Supports(a, s) && Supports(b, s)).ToList();

    /// <summary>Спецификация, которую Link Assistant предложит по умолчанию.</summary>
    public MediaStandard? SuggestStandard(CatalogDevice? a, CatalogDevice? b)
    {
        var compatible = CompatibleStandards(a, b);
        if (compatible.Count == 0) return null;
        // Сначала — устройство с ограниченным набором (сегмент, хаб), затем источник, затем приёмник.
        var ordered = new[] { a, b }.Where(d => d != null).OrderByDescending(d => d!.Standards.Count > 0 ? 1 : 0);
        foreach (var d in ordered)
        {
            var s = compatible.FirstOrDefault(x => x.Id == d!.DefaultStandard);
            if (s != null) return s;
        }
        return compatible[0];
    }
}
