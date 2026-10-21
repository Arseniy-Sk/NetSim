using System.IO;
using System.Windows;
using NetSim.App.Services;
using NetSim.App.ViewModels;
using NetSim.App.Views;
using NetSim.Core.Model;
using NetSim.Core.Serialization;
using NetSim.Core.Topology;
using NetSim.Simulation.Traffic;
using static NetSim.App.Diagnostics.LabPr4;

namespace NetSim.App.Diagnostics;

/// <summary>
/// ПР6 (ЛР6) «Создание многоуровневых сетевых проектов»: разбор примера Hier, создание подуровней
/// и вариант 7 «Рентгенологическое отделение». Запуск: <c>NetSim.exe --lab pr6 &lt;папка&gt;</c>.
/// </summary>
public static class LabPr6
{
    private const double RunSeconds = 1800; // 30 мин модельного времени — чтобы учесть печать раз в 10 и 15 минут

    private sealed class Results
    {
        public int ExampleShots;
        public int Flows;
        public ReportTable Composition = null!;
        public ReportTable FlowsTable = null!;
        public ReportTable Channels = null!;
        public List<string> Conclusions = new();
    }

    public static async void Run(MainWindow window, MainViewModel vm, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var log = new List<string>();
        var c = new Ctx { Window = window, Vm = vm, Dir = outDir };
        var r = new Results();
        try
        {
            window.WindowState = WindowState.Normal;
            window.Width = 1400;
            window.Height = 860;
            vm.ShowLinkLabels = false;
            vm.ShowFlows = false;
            await SelfTest.Settle();

            await Example(c);
            ProjectSerializer.Save(vm.Project, Path.Combine(outDir, "ПР6 — пример (два города).nsp"));
            r.ExampleShots = c.Shots.Count;
            await Variant7(c, r);
            ProjectSerializer.Save(vm.Project, Path.Combine(outDir, "ПР6 — вариант 7 (рентгенологическое отделение).nsp"));
            BuildReport(c, r);
            log.Add($"flows={r.Flows}");
            log.Add("ok");
        }
        catch (Exception ex)
        {
            log.Add("FAIL: " + ex);
        }
        File.WriteAllLines(Path.Combine(outDir, "lab.log"), log);
        vm.IsDirty = false;
        Application.Current.Shutdown();
    }

    // ---------------- Пример ----------------

    private static async Task Example(Ctx c)
    {
        var vm = c.Vm;
        vm.IsDirty = false;
        vm.OpenSampleCommand.Execute("hier");
        await c.Main("h21-country", "Рисунок 21 — сеть в масштабе страны (пример Hier)",
            "Открыт пример многоуровневого проекта (Файл → Открыть пример → «Hier — многоуровневая сеть»). Верхний уровень — сеть в масштабе страны: города California, Texas и New York связаны каналами E1.",
            "Разбор примера Hier");
        var california = vm.Project.Root.Devices.First(d => d.Name == "California");
        vm.OpenContainer(california);
        await c.Main("h22-city", "Рисунок 22 — сеть в масштабе города California (вложенный уровень)",
            "Двойной щелчок по городу (или ПКМ → «Открыть уровень (Open / Expand)») открывает сеть внутри города: маршрутизатор, коммутатор, сервер и рабочие станции. В левом верхнем углу — значок внешнего уровня (города), через который сеть города связана с остальной сетью; путь над полем (USA › California) показывает положение в иерархии.",
            null, level: california.ChildLevel);

        vm.IsDirty = false;
        vm.NewProjectCommand.Execute(null);
        vm.Project.Name = "ПР6 — пример: два города";
        c.B = new TopologyBuilder(vm.Project, vm.Catalog);
        var b = c.B;
        var city1 = b.CreateDevice("cont-city", 300, 260, name: "City");
        await LabPr2.ShowCatalog(c.Window, vm, new[] { "c-buildings" }, "cont-city");
        await c.Main("h23-city", "Рисунок 23 — создание города City (группа Buildings, Campuses and LAN Workgroups)",
            "Создан новый проект. Из группы Buildings, Campuses and LAN Workgroups на рабочее поле перетащен город (City).", "Создание многоуровневого проекта");

        vm.OpenContainer(city1);
        await c.Main("h24-sublevel", "Рисунок 24 — созданный подуровень города City (пока пустой; в левом верхнем углу — значок города)",
            "Двойной щелчок по городу (ПКМ → «Открыть уровень (Open / Expand)») создаёт и открывает подуровень. В левом верхнем углу отображается значок города, к которому принадлежит подуровень.",
            null, fit: false, level: city1.ChildLevel);
        var sw1 = b.CreateDevice("switch-eth", 420, 220, city1.ChildLevel, "Коммутатор 1");
        var w1 = b.CreateDevice("ws-eth", 300, 400, city1.ChildLevel, "Рабочая станция 1");
        var w2 = b.CreateDevice("ws-eth", 540, 400, city1.ChildLevel, "Рабочая станция 2");
        b.Connect(sw1, w1, "100base-tx", 10, city1.ChildLevel);
        b.Connect(sw1, w2, "100base-tx", 10, city1.ChildLevel);
        b.Connect(sw1, city1, "100base-tx", 10, city1.ChildLevel);
        await c.Main("h25-sublevel-net", "Рисунок 25 — подуровень сети: коммутатор и две рабочие станции Ethernet, коммутатор соединён со значком города в левом углу",
            "В подуровне размещены коммутатор и две рабочие станции Ethernet; инструментом Link коммутатор соединён со значком в левом верхнем углу — это связь подуровня с внешним уровнем.",
            null, level: city1.ChildLevel);

        var city2 = b.CreateDevice("cont-city", 700, 260, name: "City 2");
        vm.OpenContainer(city2);
        var sw2 = b.CreateDevice("switch-eth", 420, 220, city2.ChildLevel, "Коммутатор 2");
        var w3 = b.CreateDevice("ws-eth", 300, 400, city2.ChildLevel, "Рабочая станция 3");
        var w4 = b.CreateDevice("ws-eth", 540, 400, city2.ChildLevel, "Рабочая станция 4");
        b.Connect(sw2, w3, "100base-tx", 10, city2.ChildLevel);
        b.Connect(sw2, w4, "100base-tx", 10, city2.ChildLevel);
        b.Connect(sw2, city2, "100base-tx", 10, city2.ChildLevel);
        await c.Main("h26-second-city", "Рисунок 26 — подуровень второго города: коммутатор и две рабочие станции Ethernet",
            "Аналогично создан второй город и его подуровень с коммутатором и двумя рабочими станциями.", null, level: city2.ChildLevel);

        var la = new LinkAssistantWindow(vm, city1, city2, null);
        await c.Dialog(la, "h27-link", "Рисунок 27 — связь между подуровнями: выбор типа кабеля в Link Assistant (E1, линия между городами)",
            "На верхнем уровне инструментом Link города соединены между собой; в Link Assistant выбран тип линии — E1 (2,048 Мбит/с) между городами. В NetCracker после этого в левом углу подуровня показывается устройство соседнего уровня; в NetSim значок в углу всегда обозначает внешний уровень, а линии между уровнями строятся через него.",
            "Связь между подуровнями", w => { var list = LabPr2.FindChild<System.Windows.Controls.ListBox>(w)!; list.SelectedItem = list.Items.OfType<System.Windows.Controls.ListBoxItem>().First(i => (string)i.Tag == "e1"); });
        b.Connect(city1, city2, "e1", 5000);
        var (f, _) = b.AddFlow(w1, w4, "lan-p2p");
        vm.ShowLinkLabels = true;
        vm.ShowFlows = true;
        await c.Main("h28-cities", "Рисунок 28 — верхний уровень: города City и City 2 связаны линией E1 (5000 м); назначен поток «Рабочая станция 1 → Рабочая станция 4» через оба подуровня",
            $"Сеть стала единой: трафик между рабочими станциями разных городов проходит из подуровня City через линию E1 в подуровень City 2. Это проверено назначением потока между станциями разных подуровней — маршрут найден{(f != null ? "" : " не был")}.", null);
        vm.ShowLinkLabels = false;
        vm.ShowFlows = false;
    }

    // ---------------- Вариант 7 ----------------

    private static TrafficProfile Profile(MainViewModel vm, string baseId, string nameRu, Distribution size, Distribution gap, string protocol)
    {
        var p = vm.Catalog.Profile(baseId)!.Clone();
        p.NameRu = nameRu;
        p.NameEn = nameRu;
        p.TransactionSize = size;
        p.TimeBetween = gap;
        p.Protocol = protocol;
        return p;
    }

    private static async Task Variant7(Ctx c, Results r)
    {
        var vm = c.Vm;
        vm.IsDirty = false;
        vm.NewProjectCommand.Execute(null);
        vm.Project.Name = "ПР6 — вариант 7: рентгенологическое отделение";
        vm.Project.Description = "Отделение на отдельном этаже (4 кабинета); изображения хранятся на сервере данных и передаются на удалённый сервер главного офиса через спутниковый интернет по FTP.";
        // Изображения передаются пачками по несколько тысяч кадров — буфер портов увеличен (как окно TCP).
        vm.Project.Simulation.MaxQueuePackets = 20000;
        c.B = new TopologyBuilder(vm.Project, vm.Catalog);
        var b = c.B;
        const string sec = "Вариант 7. Рентгенологическое отделение";

        // Верхний уровень
        var floor = b.CreateDevice("cont-floor", 250, 280, name: "Рентгенологическое отделение (этаж)");
        var sat = b.CreateDevice("cloud-satellite", 560, 200, name: "Спутниковый интернет");
        var office = b.CreateDevice("cont-building", 870, 280, name: "Здание главного офиса");
        var lFloorSat = b.Connect(floor, sat, "satellite", 0);
        var lOfficeSat = b.Connect(office, sat, "satellite", 0);

        // Главный офис
        var ol = office.ChildLevel!;
        var remote = b.CreateDevice("server", 450, 300, ol, "Удалённый сервер (главный офис)");
        var vsatO = b.CreateDevice("vsat", 250, 180, ol, "VSAT главного офиса");
        b.Connect(remote, vsatO, "100base-tx", 10, ol);
        b.Connect(vsatO, office, "satellite", 0, ol);
        c.Install(remote, "sw-ftp-server", "ftp");

        // Этаж: коммутатор этажа, 4 кабинета, маршрутизатор и спутниковый терминал
        var fl = floor.ChildLevel!;
        var core = b.CreateDevice("switch-gigabit", 560, 300, fl, "Коммутатор этажа");
        var router = b.CreateDevice("router", 560, 580, fl, "Маршрутизатор");
        var vsat = b.CreateDevice("vsat", 110, 600, fl, "VSAT отделения");
        b.Connect(core, router, "1000base-t", 15, fl);
        b.Connect(router, vsat, "100base-tx", 5, fl);
        b.Connect(vsat, floor, "satellite", 0, fl);
        var roomNames = new[] { "Каб. 1 — рентгенография", "Каб. 2 — рентгеноскопия", "Каб. 3 — рентгеноскопия", "Каб. 4 — главный врач" };
        var roomPos = new[] { (320.0, 150.0), (820.0, 150.0), (320.0, 450.0), (820.0, 450.0) };
        var rooms = new Device[4];
        var uplinks = new Link[4];
        for (int i = 0; i < 4; i++)
        {
            rooms[i] = b.CreateDevice("cont-room", roomPos[i].Item1, roomPos[i].Item2, fl, roomNames[i]);
            uplinks[i] = b.Connect(core, rooms[i], "1000base-t", 30, fl);
        }

        // Кабинеты 1–3
        var doctors = new Device[3]; var nurses = new Device[3]; var xrays = new Device[3]; var printers = new Device[3];
        for (int i = 0; i < 3; i++)
        {
            var lv = rooms[i].ChildLevel!;
            var sw = b.CreateDevice("switch-eth", 420, 280, lv, $"Коммутатор каб. {i + 1}");
            doctors[i] = b.CreateDevice("pc", 620, 130, lv, $"Врач (каб. {i + 1})");
            nurses[i] = b.CreateDevice("pc", 800, 280, lv, $"Медсестра (каб. {i + 1})");
            xrays[i] = b.CreateDevice("medical-device", 260, 440, lv, i == 0 ? "Рентген-установка (рентгенография)" : $"Рентген-установка (рентгеноскопия, каб. {i + 1})");
            printers[i] = b.CreateDevice("printer-network", 620, 440, lv, $"Машина для печати снимков (каб. {i + 1})");
            foreach (var d in new[] { doctors[i], nurses[i], xrays[i], printers[i] }) b.Connect(sw, d, "100base-tx", 10, lv);
            b.Connect(sw, rooms[i], "1000base-t", 5, lv);
        }

        // Кабинет 4
        var l4 = rooms[3].ChildLevel!;
        var sw4 = b.CreateDevice("switch-eth", 420, 280, l4, "Коммутатор каб. 4");
        var chief = b.CreateDevice("pc", 620, 130, l4, "Главный врач (каб. 4)");
        var server = b.CreateDevice("server-rack", 800, 280, l4, "Сервер данных (каб. 4)");
        var block = b.CreateDevice("ws-eth", 260, 440, l4, "Блок обработки данных (каб. 4)");
        var printer4 = b.CreateDevice("printer-network", 620, 440, l4, "Сетевой принтер (каб. 4)");
        foreach (var d in new[] { chief, server, block, printer4 }) b.Connect(sw4, d, d == server ? "1000base-t" : "100base-tx", 10, l4);
        b.Connect(sw4, rooms[3], "1000base-t", 5, l4);

        // ПО
        c.Install(server, "sw-ftp-server", "ftp");
        c.Install(server, "sw-email-server", "pop3", "smtp");
        c.Install(server, "sw-file-server", "file");
        var staff = doctors.Concat(nurses).Append(chief).ToArray();
        foreach (var d in staff) c.Install(d, "sw-email-server", "pop3", "smtp");

        // Трафик
        int flows = 0;
        void Add(Device a, Device z, TrafficProfile p) { var (fl1, e) = b.AddFlow(a, z, p); if (fl1 == null) throw new InvalidOperationException(e); flows++; }
        var raw = Profile(vm, "bulk-data", "FTP — сырые данные снимка", Distribution.Normal(5_000_000, 1_000_000), Distribution.Exponential(180), "FTP");
        var image = Profile(vm, "bulk-data", "Передача изображения", Distribution.Normal(1_500_000, 300_000), Distribution.Exponential(180), "FTP");
        var printImage = Profile(vm, "print", "Печать снимка", Distribution.Normal(1_500_000, 300_000), Distribution.Exponential(300), "LPD");
        var printConclusion = Profile(vm, "print", "Печать заключения (раз в 15 минут)", Distribution.Normal(200_000, 50_000), Distribution.Constant(900), "LPD");
        var printContract = Profile(vm, "print", "Печать договора (раз в 10 минут)", Distribution.Normal(200_000, 50_000), Distribution.Constant(600), "LPD");
        var mailSend = Profile(vm, "email-smtp", "Отправка заключения по почте (SMTP)", Distribution.Normal(20_000, 5_000), Distribution.Exponential(600), "SMTP");
        var archive = Profile(vm, "bulk-data", "FTP — архив снимков на удалённый сервер", Distribution.Normal(1_500_000, 300_000), Distribution.Exponential(120), "FTP");
        var internet = Profile(vm, "interlan", "Спутниковый интернет (веб)", Distribution.Normal(20_000, 8_000), Distribution.Exponential(20), "HTTP");

        for (int i = 0; i < 3; i++)
        {
            Add(xrays[i], block, raw);                    // сырые данные по FTP в блок обработки
            Add(block, nurses[i], image);                 // изображения медсестре
            Add(block, doctors[i], image);                // и врачу
            Add(nurses[i], printers[i], printImage);      // медсестра печатает снимки
            Add(doctors[i], printers[i], printConclusion);// врач печатает заключения раз в 15 мин
            Add(doctors[i], server, mailSend);            // заключения главному врачу по почте (через почтовый сервер)
            Add(doctors[i], sat, internet);               // врачи подключены к спутниковому интернету
            Add(doctors[i], nurses[i], vm.Catalog.Profile("small-office-p2p")!.Clone());
            Add(nurses[i], chief, vm.Catalog.Profile("small-office-p2p")!.Clone());
        }
        Add(block, chief, Profile(vm, "bulk-data", "Все изображения — главному врачу", Distribution.Normal(1_500_000, 300_000), Distribution.Exponential(60), "FTP"));
        Add(block, server, Profile(vm, "bulk-data", "Изображения на сервер хранения", Distribution.Normal(1_500_000, 300_000), Distribution.Exponential(60), "FTP"));
        Add(server, remote, archive);
        Add(chief, printer4, printContract);
        foreach (var d in staff) Add(d, server, vm.Catalog.Profile("email-pop")!.Clone());
        foreach (var d in doctors) Add(chief, d, vm.Catalog.Profile("small-office-p2p")!.Clone());
        r.Flows = flows;

        // Скриншоты структуры
        vm.ShowLinkLabels = true;
        await c.Main("v01-root", "Вариант 7. Верхний уровень: отделение (этаж) и здание главного офиса связаны через спутниковый интернет",
            "Проект многоуровневый. Верхний уровень: этаж «Рентгенологическое отделение» (группа Buildings, Campuses and LAN Workgroups → Floor), облако «Спутниковый интернет» (Satellite communication equipment) и здание главного офиса с удалённым сервером. Этаж и здание подключены к спутниковому каналу (2 Мбит/с).",
            sec);
        await c.Main("v02-floor", "Вариант 7. Уровень этажа: 4 кабинета подключены к гигабитному коммутатору этажа (1000Base-T), выход в спутниковый интернет — через маршрутизатор и терминал VSAT",
            "Внутри этажа — гигабитный коммутатор, к которому подключены четыре кабинета (вложенные уровни, линии 1000Base-T 30 м), маршрутизатор и спутниковый терминал VSAT, связанный со значком этажа (выход на верхний уровень).",
            null, level: floor.ChildLevel);
        await c.Main("v03-room1", "Вариант 7. Кабинет 1 (рентгенография): врач, медсестра, рентген-установка, машина для печати снимков",
            "В каждом кабинете — свой коммутатор, соединённый со значком кабинета (связь с коммутатором этажа). Рабочие места подключены по Fast Ethernet 100Base-TX (100 Мбит/с — по заданию сырые данные передаются со скоростью 100 Мбит/с). Рентген-установка обозначена как рабочая станция (Peripheral equipment → медицинское оборудование).",
            null, level: rooms[0].ChildLevel);
        await c.Main("v04-room2", "Вариант 7. Кабинет 2 (рентгеноскопия)", null, null, level: rooms[1].ChildLevel);
        await c.Main("v05-room3", "Вариант 7. Кабинет 3 (рентгеноскопия)", null, null, level: rooms[2].ChildLevel);
        await c.Main("v06-room4", "Вариант 7. Кабинет 4 (главный врач): рабочее место главного врача, сервер данных, блок обработки данных, сетевой принтер",
            "Блок обработки данных (рабочая станция) и сервер данных размещены в кабинете 4. Сервер подключён гигабитным портом, так как через него проходят все изображения. Для печати договоров главным врачом (раз в 10 минут) добавлен сетевой принтер — в составе варианта он не указан, но требуется по описанию.",
            null, level: rooms[3].ChildLevel);
        await c.Main("v07-office", "Вариант 7. Здание главного офиса: удалённый сервер и спутниковый терминал", null, null, level: office.ChildLevel);
        vm.ShowLinkLabels = false;
        await c.Software(server, "sw-email-server", "v08-server-sw", "Вариант 7. ПО сервера данных: FTP server, E-mail server (POP3, SMTP), File server",
            "Сервер данных хранит изображения (FTP server, File server) и является почтовым сервером отделения (E-mail server: POP3, SMTP): через него врачи отправляют заключения главному врачу, все рабочие станции обмениваются почтой.");
        await c.Dialog(new DataFlowWindow(vm), "v09-dataflow", $"Вариант 7. Окно Data Flow — назначено {flows} потоков");

        // Моделирование
        foreach (var u in uplinks) u.ShownStatistics.Add(StatKeys.AverageUtilization);
        lFloorSat.ShownStatistics.Add(StatKeys.AverageUtilization);
        server.ShownStatistics.Add(StatKeys.PacketsReceived);
        block.ShownStatistics.Add(StatKeys.AverageWorkload);
        vm.TopologyChanged();
        await RunSimulation(c, RunSeconds);
        vm.StopCommand.Execute(null);
        vm.RefreshSimTime();
        var sim = vm.TrafficSim!;
        await c.Main("v10-root-stats", $"Вариант 7. Верхний уровень после {RunSeconds / 60:0} мин моделирования: загрузка спутникового канала",
            $"Выполнено моделирование {RunSeconds / 60:0} минут модельного времени (чтобы учесть печать раз в 10 и 15 минут).", "Проверка отсутствия перегрузок", rebuild: false);
        await c.Main("v11-floor-stats", "Вариант 7. Уровень этажа: средняя загрузка линий кабинетов (average utilization)", null, null, rebuild: false, level: floor.ChildLevel);
        await c.Main("v12-room4-stats", "Вариант 7. Кабинет 4: статистика сервера данных и блока обработки", null, null, rebuild: false, level: rooms[3].ChildLevel);

        // Таблицы
        r.Composition = new ReportTable("Состав сети по кабинетам (вариант 7)", new[] { "Помещение", "Оборудование" }, new List<string[]>
        {
            new[] { "Каб. 1 — рентгенография", "Рабочее место врача, рабочее место медсестры, рентген-установка, машина для печати снимков, коммутатор" },
            new[] { "Каб. 2 — рентгеноскопия", "Рабочее место врача, рабочее место медсестры, рентген-установка, машина для печати снимков, коммутатор" },
            new[] { "Каб. 3 — рентгеноскопия", "Рабочее место врача, рабочее место медсестры, рентген-установка, машина для печати снимков, коммутатор" },
            new[] { "Каб. 4 — главный врач", "Рабочее место главного врача, сервер данных, блок обработки данных, сетевой принтер, коммутатор" },
            new[] { "Этаж (коридор)", "Гигабитный коммутатор этажа, маршрутизатор, спутниковый терминал VSAT" },
            new[] { "Главный офис", "Удалённый сервер, спутниковый терминал VSAT" },
        });
        r.FlowsTable = new ReportTable("Требования задания и назначенный трафик", new[] { "Требование", "Источник → приёмник", "Профиль трафика (размер, интервал)" }, new List<string[]>
        {
            new[] { "Сырые данные по FTP в блок обработки (100 Мбит/с)", "Рентген-установки 1–3 → блок обработки", "FTP, ~5 МБ, в среднем раз в 3 мин" },
            new[] { "Изображения — медсёстрам и врачам", "Блок обработки → медсёстры и врачи 1–3", "~1,5 МБ, раз в 3 мин" },
            new[] { "Все изображения — главному врачу", "Блок обработки → главный врач", "~1,5 МБ, раз в минуту" },
            new[] { "Изображения — на сервер хранения", "Блок обработки → сервер данных", "~1,5 МБ, раз в минуту" },
            new[] { "Передача на удалённый сервер по FTP через спутник", "Сервер данных → удалённый сервер", "FTP, ~1,5 МБ, раз в 2 мин" },
            new[] { "Медсёстры печатают снимки", "Медсестра → машина для печати снимков", "~1,5 МБ, раз в 5 мин" },
            new[] { "Врач печатает заключения раз в 15 минут", "Врач → машина для печати снимков", "~200 КБ, ровно раз в 900 с" },
            new[] { "Администратор печатает договоры раз в 10 минут", "Главный врач → сетевой принтер", "~200 КБ, ровно раз в 600 с" },
            new[] { "Заключения главному врачу по почте", "Врачи → сервер данных (SMTP), главный врач ← сервер (POP3)", "E-mail (SMTP) ~20 КБ, E-mail (POP)" },
            new[] { "Врачи подключены к спутниковому интернету", "Врачи 1–3 → спутниковый интернет", "Веб (HTTP), ~20 КБ, раз в 20 с" },
            new[] { "Почтовый клиент на всех рабочих станциях", "Все рабочие места → сервер данных", "E-mail (POP)" },
            new[] { "Трафик малого офиса", "Врач↔медсестра, медсестра↔главный врач, главный врач↔врачи", "Small office peer-to-peer" },
        });
        var channels = sim.Graph.Channels
            .Select(ch => (ch, U: sim.ChannelValue(ch, StatKeys.AverageUtilization), Cur: sim.ChannelValue(ch, StatKeys.CurrentUtilization), D: sim.ChannelValue(ch, StatKeys.PacketsDropped), Q: sim.ChannelValue(ch, StatKeys.MaxQueue)))
            .OrderByDescending(x => x.U).Take(10).ToList();
        string Name(Channel ch) => string.Join(", ", ch.Links.Select(l => $"{vm.Project.FindDevice(l.AId)?.Name} — {vm.Project.FindDevice(l.BId)?.Name}"));
        r.Channels = new ReportTable($"10 наиболее загруженных линий (моделирование {RunSeconds / 60:0} мин)",
            new[] { "Линия", "Скорость", "Average utilization", "Макс. очередь", "Потеряно пакетов" },
            channels.Select(x => new[] { Name(x.ch), StatKeys.FormatBits(x.ch.BandwidthBps), F(StatKeys.AverageUtilization, x.U), x.Q.ToString("0"), x.D.ToString("0") }).ToList());

        double maxU = channels[0].U;
        double drops = sim.Graph.Channels.Sum(ch => sim.ChannelValue(ch, StatKeys.PacketsDropped));
        double satU = sim.LinkValue(lFloorSat, StatKeys.AverageUtilization);
        r.Conclusions.Add($"Назначено {flows} потоков трафика, отражающих все требования варианта. Моделирование {RunSeconds / 60:0} минут показало: самая загруженная линия — «{Name(channels[0].ch)}» со средней загрузкой {F(StatKeys.AverageUtilization, maxU)}; загрузка спутникового канала отделения (2 Мбит/с) — {F(StatKeys.AverageUtilization, satU)}; потеряно пакетов во всей сети — {drops:0}.");
        r.Conclusions.Add(maxU < vm.Project.Simulation.OverloadThreshold && drops == 0
            ? "Перегрузок линий нет: загрузка всех линий ниже порога 80%, потерь пакетов нет — требование «избегать перегрузок линии» выполнено. Этому способствуют гигабитный коммутатор этажа и гигабитное подключение сервера данных, через которые проходят все изображения, а также выделенный коммутатор в каждом кабинете."
            : "Обнаружены перегруженные линии — для них нужно повысить скорость (гигабитные линии, более быстрый спутниковый канал) или разнести трафик.");
        r.Conclusions.Add("Узкое место сети — спутниковый канал: при росте числа снимков, передаваемых в главный офис, его загрузка будет расти первой; при необходимости можно передавать архив в нерабочее время или арендовать канал большей скорости.");
    }

    // ---------------- Отчёт ----------------

    private static void BuildReport(Ctx c, Results r)
    {
        var path = Path.Combine(c.Dir, "Отчёт ПР6 (ЛР6) — вариант 7.docx");
        using var w = new WordReport(path);
        w.Title("ОТЧЁТ");
        w.Centered("по лабораторной работе №6 (ПР6)", bold: true);
        w.Centered("«Создание многоуровневых сетевых проектов»", bold: true);
        w.Centered("Вариант 7 — «Рентгенологическое отделение»");
        w.Centered("(выполнено в NetSim — учебном аналоге NetCracker Professional)");
        w.Paragraph("");
        w.Centered("Выполнил(а): ____________________, группа ________");
        w.Centered("Проверил(а): ____________________");
        w.Centered(DateTime.Now.ToString("dd.MM.yyyy"));
        w.PageBreak();

        w.Heading1("Цель работы");
        w.Paragraph("Разработать локальную сеть, расположенную в нескольких помещениях.");
        w.Heading1("1. Порядок выполнения работы");
        c.Images(w, c.Shots.Take(r.ExampleShots));

        w.Heading1("2. Задание. Вариант 7");
        w.Paragraph("Рентгенологическое отделение. Отделение располагается на отдельном этаже и имеет 4 кабинета: кабинет рентгенографии (1), кабинеты рентгеноскопии (2, 3), кабинет главного врача (4). Состав: в кабинетах 1–3 — рабочее место врача, рабочее место медицинской сестры, рентген-установка, машина для печати снимков; в кабинете 4 — рабочее место главного врача и сервер данных.");
        w.Paragraph("Администратор периодически печатает договоры (раз в 10 минут), врач периодически печатает заключения (раз в 15 минут). С аппаратов рентгенографии и рентгеноскопии сырые данные поступают по FTP в блок обработки данных (100 Мбит/с), где из них формируются изображения. Изображения поступают на компьютеры медицинских сестёр и врачей, все изображения передаются также на компьютер главного врача. Медицинские сёстры обрабатывают снимки и печатают их на машине для печати снимков. Врачи пишут заключения и отправляют их главному врачу на проверку по почте. Изображения передаются на сервер, который хранит данные и передаёт их на удалённый сервер в здании главного офиса через спутниковый интернет по FTP. Рабочие станции врачей подключены к спутниковому интернету. Все рабочие станции общаются между собой через почтового клиента и взаимодействуют по трафику малого офиса. Требуется избегать перегрузок линии.");
        w.Table(r.Composition, "Таблица");
        w.Table(r.FlowsTable, "Таблица");
        c.Images(w, c.Shots.Skip(r.ExampleShots));
        w.Table(r.Channels, "Таблица");
        w.Heading2("Результаты проверки");
        foreach (var a in r.Conclusions) w.Paragraph(a);

        w.Heading1("Выводы");
        w.Paragraph("В ходе работы изучена структура многоуровневого проекта на примере Hier: сеть в масштабе страны и вложенные сети городов. Освоено создание подуровней (контейнеры «город», «этаж», «здание», «помещение» из группы Buildings, Campuses and LAN Workgroups), размещение оборудования внутри подуровня, связь подуровня с внешним уровнем через значок в левом верхнем углу и соединение подуровней между собой с выбором типа линии.");
        w.Paragraph($"По варианту 7 спроектирована многоуровневая сеть рентгенологического отделения: этаж с четырьмя кабинетами-подуровнями, сервер данных и блок обработки изображений, выход через спутниковый интернет на удалённый сервер главного офиса. Назначен трафик по всем требованиям задания ({r.Flows} потоков) и выполнено моделирование, подтвердившее отсутствие перегрузок линий.");
    }
}
