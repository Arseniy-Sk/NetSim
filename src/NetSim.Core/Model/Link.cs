using System.Collections.ObjectModel;

namespace NetSim.Core.Model;

/// <summary>
/// Линия связи между двумя устройствами (Link Assistant).
/// Если линия лежит во вложенном уровне и один из концов — контейнер-владелец этого уровня,
/// то это межуровневая связь: на схеме она рисуется к значку в левом верхнем углу (ПР6).
/// </summary>
public sealed class Link : ObservableBase
{
    private string _standardId = "100base-tx";
    private string _mediaId = "utp5";
    private double _bandwidthBps = 100e6;
    private double _lengthM = 10;
    private string _aInterface = "";
    private string _bInterface = "";

    public string Id { get; set; } = Ids.New();
    public string AId { get; set; } = "";
    public string BId { get; set; } = "";

    /// <summary>Спецификация (протокол) — 10Base-T, 100Base-T4, Token Ring 16, 802.11g...</summary>
    public string StandardId { get => _standardId; set => Set(ref _standardId, value); }

    /// <summary>Тип среды: витая пара, коаксиал, оптоволокно, радио...</summary>
    public string MediaId { get => _mediaId; set => Set(ref _mediaId, value); }

    public double BandwidthBps { get => _bandwidthBps; set => Set(ref _bandwidthBps, value); }
    public double LengthM { get => _lengthM; set => Set(ref _lengthM, value); }

    /// <summary>Имена интерфейсов на концах (для режима протоколов).</summary>
    public string AInterface { get => _aInterface; set => Set(ref _aInterface, value); }
    public string BInterface { get => _bInterface; set => Set(ref _bInterface, value); }

    public ObservableCollection<string> ShownStatistics { get; set; } = new();

    public string Other(string deviceId) => deviceId == AId ? BId : AId;
    public bool Touches(string deviceId) => AId == deviceId || BId == deviceId;
}

/// <summary>Уровень (лист) схемы. Корневой уровень у проекта, вложенные — у контейнеров.</summary>
public sealed class Level : ObservableBase
{
    private string _name = "";
    public string Id { get; set; } = Ids.New();
    public string Name { get => _name; set => Set(ref _name, value); }

    /// <summary>Вид фона: обычный лист, карта страны, план города/здания.</summary>
    public string Background { get; set; } = "plain";

    public ObservableCollection<Device> Devices { get; set; } = new();
    public ObservableCollection<Link> Links { get; set; } = new();

    /// <summary>План помещения (стены, кабинеты, масштаб). Пустой план не рисуется.</summary>
    public FloorPlan Plan { get; set; } = new();
}
