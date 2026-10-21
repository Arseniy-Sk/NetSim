using NetSim.Core.Model;
using NetSim.Core.Topology;

namespace NetSim.Core.Samples;

/// <summary>Встроенные примеры проектов (аналоги Techno.net и Hier.net из NetCracker).</summary>
public static class SampleProjects
{
    public static readonly (string Id, string NameRu, string NameEn)[] All =
    {
        ("techno", "Techno — сеть предприятия (ПР2)", "Techno — enterprise network (Lab 1)"),
        ("hier", "Hier — многоуровневая сеть (ПР6)", "Hier — hierarchical network (Lab 6)"),
        ("ip-demo", "Две подсети и маршрутизатор (режим протоколов)", "Two subnets and a router (protocol mode)"),
        ("wifi-office", "Беспроводная сеть офиса по плану (ПР10)", "Office WLAN on a floor plan (Lab 10)"),
    };

    public static Project Create(string id) => id switch
    {
        "techno" => Techno(),
        "hier" => Hier(),
        "ip-demo" => IpDemo(),
        "wifi-office" => WifiOffice(),
        _ => throw new ArgumentException(id),
    };

    /// <summary>Корпоративная сеть: магистральный Cisco 7000 с FDDI, сегменты Ethernet и Token Ring, серверы, выход в глобальную сеть.</summary>
    public static Project Techno()
    {
        var p = new Project { Name = "Techno", Description = "Пример сети предприятия: магистраль FDDI, Cisco 7000, отделы Ethernet и Token Ring." };
        var b = new TopologyBuilder(p);

        var core = b.CreateDevice("router-cisco-7000", 520, 260, name: "Cisco 7000");
        var fddi = b.CreateDevice("seg-fddi", 520, 90, name: "FDDI backbone");
        b.Connect(core, fddi, "fddi", 30);

        var dataServer = b.CreateDevice("server-fddi", 330, 70, name: "Data server");
        b.InstallSoftware(dataServer, "sw-sql-server");
        b.Connect(dataServer, fddi, "fddi", 40);
        var webServer = b.CreateDevice("server-fddi", 710, 70, name: "Web server");
        b.InstallSoftware(webServer, "sw-http-server", "http", "ftp", "pop3");
        b.Connect(webServer, fddi, "fddi", 40);

        // Отдел продаж — Fast Ethernet
        var sw1 = b.CreateDevice("switch-eth", 250, 330, name: "Sales switch");
        b.Connect(core, sw1, "100base-fx", 300);
        var sales = new List<Device>();
        for (int i = 0; i < 4; i++)
        {
            var pc = b.CreateDevice("pc", 90 + i * 90, 470, name: $"Sales PC {i + 1}");
            b.Connect(sw1, pc, "100base-tx", 15);
            sales.Add(pc);
        }

        // Бухгалтерия — Token Ring
        var ring = b.CreateDevice("seg-token-ring", 760, 400, name: "Accounting ring");
        b.Connect(core, ring, "tr-16", 50);
        var acc = new List<Device>();
        for (int i = 0; i < 3; i++)
        {
            var ws = b.CreateDevice("ws-token-ring", 660 + i * 100, 540, name: $"Accounting WS {i + 1}");
            b.Connect(ws, ring, "tr-16", 20);
            acc.Add(ws);
        }

        // Выход в глобальную сеть
        var dsu = b.CreateDevice("dsu-csu", 860, 250, name: "DSU/CSU");
        b.Connect(core, dsu, "serial-v35", 3);
        var fr = b.CreateDevice("cloud-frame-relay", 1010, 250, name: "Frame Relay");
        b.Connect(dsu, fr, "frame-relay", 1000);

        foreach (var pc in sales)
        {
            b.AddFlow(pc, webServer, "http");
            b.AddFlow(pc, dataServer, "database");
        }
        foreach (var ws in acc) b.AddFlow(ws, dataServer, "database");
        b.AddFlow(sales[0], sales[1], "small-office-p2p");
        b.AddFlow(acc[0], fr, "interlan");

        dataServer.Server.ReplySize = Distribution.Normal(2048, 512);
        dataServer.ShownStatistics.Add("currentWorkload");
        webServer.ShownStatistics.Add("currentWorkload");
        ring.ShownStatistics.Add("averageUtilization");
        return p;
    }

    /// <summary>Иерархия: страна → города → здания/ЛВС. Открывается двойным щелчком по городу (Open/Expand).</summary>
    public static Project Hier()
    {
        var p = new Project { Name = "Hier", Description = "Многоуровневый проект: сеть в масштабе страны, города связаны каналами E1." };
        p.Root.Name = "USA";
        p.Root.Background = "country";
        var b = new TopologyBuilder(p);

        var cities = new[] { ("California", 180.0, 300.0), ("Texas", 520.0, 420.0), ("New York", 860.0, 200.0) };
        var cityDevices = new List<Device>();
        foreach (var (name, x, y) in cities)
        {
            var city = b.CreateDevice("cont-city", x, y, name: name);
            cityDevices.Add(city);
            var lvl = city.ChildLevel!;
            var router = b.CreateDevice("router", 300, 150, lvl, $"{name} router");
            b.Connect(router, city, "e1", 10, lvl); // межуровневая связь — к значку в левом углу
            var sw = b.CreateDevice("switch-eth", 300, 300, lvl, $"{name} switch");
            b.Connect(router, sw, "100base-tx", 10, lvl);
            var server = b.CreateDevice("server", 520, 300, lvl, $"{name} server");
            b.InstallSoftware(server, "sw-http-server");
            b.Connect(sw, server, "100base-tx", 10, lvl);
            for (int i = 0; i < 3; i++)
            {
                var pc = b.CreateDevice("pc", 160 + i * 140, 450, lvl, $"{name} PC {i + 1}");
                b.Connect(sw, pc, "100base-tx", 20, lvl);
            }
        }
        b.Connect(cityDevices[0], cityDevices[1], "e1", 1500_000);
        b.Connect(cityDevices[1], cityDevices[2], "e1", 2500_000);
        b.Connect(cityDevices[0], cityDevices[2], "e1", 4000_000);

        var caPc = p.AllDevices().First(d => d.Name == "California PC 1");
        var nyServer = p.AllDevices().First(d => d.Name == "New York server");
        b.AddFlow(caPc, nyServer, "http");
        return p;
    }

    /// <summary>Пример для режима протоколов: две подсети, коммутаторы и маршрутизатор с настроенными адресами.</summary>
    public static Project IpDemo()
    {
        var p = new Project { Name = "IP demo", Description = "Две подсети 192.168.1.0/24 и 192.168.2.0/24, маршрутизатор Cisco 2901. Попробуйте ping с PC 1 на 192.168.2.10." };
        var b = new TopologyBuilder(p);
        var r = b.CreateDevice("router-cisco-2901", 450, 120, name: "R1");
        var s1 = b.CreateDevice("switch-cisco-2960", 250, 280, name: "SW1");
        var s2 = b.CreateDevice("switch-cisco-2960", 650, 280, name: "SW2");
        var l1 = b.Connect(r, s1, "1000base-t", 5);
        var l2 = b.Connect(r, s2, "1000base-t", 5);
        Configure(r, l1.AInterface, "192.168.1.1");
        Configure(r, l2.AInterface, "192.168.2.1");
        foreach (var i in r.Network.Interfaces) i.IsUp = true;

        for (int k = 0; k < 2; k++)
        {
            var pc = b.CreateDevice("pc", 150 + k * 200, 430, name: $"PC {k + 1}");
            var link = b.Connect(s1, pc, "100base-tx", 10);
            Configure(pc, link.BInterface, $"192.168.1.{10 + k}");
            pc.Network.DefaultGateway = "192.168.1.1";
        }
        for (int k = 0; k < 2; k++)
        {
            var pc = b.CreateDevice("pc", 550 + k * 200, 430, name: $"PC {k + 3}");
            var link = b.Connect(s2, pc, "100base-tx", 10);
            Configure(pc, link.BInterface, $"192.168.2.{10 + k}");
            pc.Network.DefaultGateway = "192.168.2.1";
        }
        return p;
    }

    /// <summary>
    /// ПР10, план варианта 1: два ряда кабинетов 7 + 15 + 20 м глубиной 7 м, коридор 3 м.
    /// Точка доступа в среднем кабинете, сервер данных (С) и компьютер с интернетом (И).
    /// </summary>
    public static Project WifiOffice()
    {
        var p = new Project { Name = "Wi-Fi office", Description = "План помещения ПР10 (вариант 1): масштаб 20 пикс/м, стены по 6 дБ. Инструменты → Анализ покрытия Wi-Fi." };
        var plan = p.Root.Plan;
        plan.PixelsPerMeter = 20;
        plan.BuildCorridorPlan(new[] { 7.0, 15, 20 }, new[] { 7.0, 15, 20 }, 7, 3, 60, 60, "Каб. ");
        var b = new TopologyBuilder(p);
        double X(double m) => 60 + m * 20;
        double Y(double m) => 60 + m * 20;

        var ap = b.CreateDevice("ap-g", X(14.5), Y(8.5), name: "AP 1");
        var server = b.CreateDevice("server", X(8.5), Y(5.5), name: "Сервер данных");
        b.InstallSoftware(server, "sw-file-server");
        b.InstallSoftware(server, "sw-small-office-db");
        b.Connect(ap, server, "100base-tx", 12);
        var router = b.CreateDevice("router", X(40), Y(8.5), name: "Router");
        b.Connect(ap, router, "100base-tx", 30);
        var inet = b.CreateDevice("cloud-internet", X(46), Y(8.5), name: "Интернет");
        b.Connect(router, inet, "adsl", 1000);

        var spots = new (double X, double Y)[]
        {
            (1.5, 1.5), (5.5, 1.5), (9, 1.5), (13, 1.5), (20.5, 1.5), (25, 1.5), (31, 1.5), (35, 1.5), (40.5, 1.5),
            (1.5, 15.5), (5.5, 15.5), (9, 15.5), (20.5, 15.5), (25, 15.5), (31, 15.5), (40.5, 15.5),
        };
        int i = 1;
        foreach (var (sx, sy) in spots)
        {
            var pc = b.CreateDevice("pc-wireless", X(sx), Y(sy), name: $"WS {i++}");
            b.Connect(pc, ap, "802.11g", 10);
            b.AddFlow(pc, server, "file-cs");
        }
        return p;
    }

    private static void Configure(Device d, string iface, string ip, string mask = "255.255.255.0")
    {
        var i = d.Network.Interfaces.First(x => x.Name == iface);
        i.IpAddress = ip;
        i.SubnetMask = mask;
    }
}
