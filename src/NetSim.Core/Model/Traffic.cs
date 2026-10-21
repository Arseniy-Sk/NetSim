namespace NetSim.Core.Model;

public enum TrafficPattern
{
    /// <summary>Источник посылает транзакции получателю, ответа нет.</summary>
    PeerToPeer,

    /// <summary>Клиент посылает запрос, сервер через Reply Delay отвечает пакетом Reply Size.</summary>
    ClientServer,
}

/// <summary>Профиль трафика (вкладка Profiles): HTTP, FTP, Database, Small office peer-to-peer...</summary>
public sealed class TrafficProfile : ObservableBase
{
    private string _nameRu = "";
    private string _nameEn = "";
    private TrafficPattern _pattern;
    private string? _requiredService;
    private string _protocol = "";

    public string Id { get; set; } = "";
    public string NameRu { get => _nameRu; set => Set(ref _nameRu, value); }
    public string NameEn { get => _nameEn; set => Set(ref _nameEn, value); }
    public TrafficPattern Pattern { get => _pattern; set => Set(ref _pattern, value); }

    /// <summary>Служба, которая должна быть включена на сервере-приёмнике (для клиент-серверных профилей).</summary>
    public string? RequiredService { get => _requiredService; set => Set(ref _requiredService, value); }

    /// <summary>Протокол прикладного уровня (Application Layer Protocol).</summary>
    public string Protocol { get => _protocol; set => Set(ref _protocol, value); }

    /// <summary>Размер транзакции (запроса), байт — "Transaction size".</summary>
    public Distribution TransactionSize { get; set; } = Distribution.Constant(1000);

    /// <summary>Пауза между транзакциями, с — "Time between transactions".</summary>
    public Distribution TimeBetween { get; set; } = Distribution.Exponential(1);

    public string Name(string lang) => lang == "en" || string.IsNullOrEmpty(NameRu) ? NameEn : NameRu;

    public TrafficProfile Clone() => new()
    {
        Id = Id, NameRu = NameRu, NameEn = NameEn, Pattern = Pattern, RequiredService = RequiredService,
        Protocol = Protocol, TransactionSize = TransactionSize.Clone(), TimeBetween = TimeBetween.Clone(),
    };
}

/// <summary>Поток данных между двумя устройствами (Set traffic, Global → Data Flow).</summary>
public sealed class TrafficFlow : ObservableBase
{
    private bool _showAnimation = true;
    private bool _isActive = true;

    public string Id { get; set; } = Ids.New();
    public string SourceId { get; set; } = "";
    public string TargetId { get; set; } = "";

    /// <summary>Копия профиля — её можно редактировать, не меняя библиотеку профилей.</summary>
    public TrafficProfile Profile { get; set; } = new();

    /// <summary>Показывать анимацию пакетов этого потока (галочка в Data Flow).</summary>
    public bool ShowAnimation { get => _showAnimation; set => Set(ref _showAnimation, value); }

    /// <summary>Поток участвует в моделировании.</summary>
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
}
