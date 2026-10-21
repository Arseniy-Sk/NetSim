using NetSim.App.Localization;
using NetSim.Calculators;
using NetSim.Core.Catalog;
using NetSim.Core.Model;
using NetSim.Simulation.Traffic;

namespace NetSim.App.Services;

/// <summary>Таблица для отчёта: заголовки и строки текста.</summary>
public sealed record ReportTable(string Title, string[] Headers, List<string[]> Rows);

/// <summary>Общие таблицы проекта — для окна сводной статистики и отчёта в Word.</summary>
public static class ReportTables
{
    private static string L(string ru, string en) => Loc.Pick(ru, en);

    public static ReportTable DeviceStatistics(TrafficSimulator sim)
    {
        var lang = Loc.I.Lang;
        string F(string key, double v) => StatKeys.Format(key, v, lang);
        var rows = sim.Graph.Devices.Values
            .Where(d => d.Kind.IsEndNode())
            .OrderBy(d => d.Kind != DeviceKind.Server).ThenBy(d => d.Name)
            .Select(d => new[]
            {
                d.Name, Loc.T("Kind_" + d.Kind),
                F(StatKeys.CurrentWorkload, sim.DeviceValue(d, StatKeys.CurrentWorkload)),
                F(StatKeys.AverageWorkload, sim.DeviceValue(d, StatKeys.AverageWorkload)),
                sim.DeviceValue(d, StatKeys.PacketsReceived).ToString("0"),
                sim.DeviceValue(d, StatKeys.PacketsSent).ToString("0"),
                sim.DeviceValue(d, StatKeys.RequestsServed).ToString("0"),
                sim.DeviceValue(d, StatKeys.Transactions).ToString("0"),
                F(StatKeys.AvgResponseTime, sim.DeviceValue(d, StatKeys.AvgResponseTime)),
                sim.DeviceValue(d, StatKeys.PacketsDropped).ToString("0"),
            }).ToList();
        return new ReportTable(Loc.T("Report_Devices"), new[]
        {
            Loc.T("Report_Device"), Loc.T("Report_Kind"), "Current workload", "Average workload", "Packets received", "Packets sent",
            Loc.T("Report_Requests"), Loc.T("Report_Transactions"), Loc.T("Report_Response"), Loc.T("Report_Dropped"),
        }, rows);
    }

    public static ReportTable ChannelStatistics(TrafficSimulator sim, Project project, DeviceCatalog catalog)
    {
        var lang = Loc.I.Lang;
        string F(string key, double v) => StatKeys.Format(key, v, lang);
        var rows = sim.Graph.Channels.Select(ch =>
        {
            string name = ch.IsShared
                ? ch.DisplayName
                : string.Join(", ", ch.Links.Select(l => $"{project.FindDevice(l.AId)?.Name} — {project.FindDevice(l.BId)?.Name}"));
            var std = ch.Links.Count > 0 ? catalog.Standard(ch.Links[0].StandardId) : null;
            string type = (ch.IsShared ? Loc.T("Report_Shared") : Loc.T("Report_PointToPoint")) + (std != null ? $", {std.NameEn}" : "");
            return new[]
            {
                name, type, StatKeys.FormatBits(ch.BandwidthBps),
                F(StatKeys.AverageUtilization, sim.ChannelValue(ch, StatKeys.AverageUtilization)),
                F(StatKeys.CurrentUtilization, sim.ChannelValue(ch, StatKeys.CurrentUtilization)),
                sim.ChannelValue(ch, StatKeys.PacketsCarried).ToString("0"),
                StatKeys.FormatBytes(sim.ChannelValue(ch, StatKeys.BytesCarried)),
                sim.ChannelValue(ch, StatKeys.PacketsDropped).ToString("0"),
                sim.ChannelValue(ch, StatKeys.MaxQueue).ToString("0"),
            };
        }).ToList();
        return new ReportTable(Loc.T("Report_Channels"), new[]
        {
            Loc.T("Report_Channel"), Loc.T("Report_Type"), Loc.T("Report_Bandwidth"), "Average utilization", "Current utilization",
            Loc.T("Report_Packets"), Loc.T("Report_Bytes"), Loc.T("Report_Dropped"), Loc.T("Report_MaxQueue"),
        }, rows);
    }

    public static ReportTable Equipment(Project p, DeviceCatalog c)
    {
        var rows = new List<string[]>();
        foreach (var level in p.AllLevels())
        {
            var owner = p.OwnerOf(level);
            foreach (var d in level.Devices)
            {
                var sw = string.Join("; ", d.Software.Select(s =>
                {
                    var cs = c.SoftwareById(s.SoftwareId);
                    var svc = string.Join(", ", s.EnabledServices.Select(x => c.Service(x)?.NameEn ?? x));
                    return (cs?.Name(Loc.I.Lang) ?? s.SoftwareId) + (svc.Length > 0 ? $" ({svc})" : "");
                }));
                rows.Add(new[] { d.Name, c.Device(d.CatalogId)?.Name(Loc.I.Lang) ?? d.CatalogId, Loc.T("Kind_" + d.Kind), owner?.Name ?? "—", sw });
            }
        }
        return new ReportTable(L("Состав оборудования", "Equipment"),
            new[] { Loc.T("Report_Device"), L("Модель", "Model"), Loc.T("Report_Kind"), L("Уровень", "Level"), L("Программное обеспечение", "Software") }, rows);
    }

    public static ReportTable Links(Project p, DeviceCatalog c)
    {
        var rows = p.AllLinks().Select(l => new[]
        {
            p.FindDevice(l.AId)?.Name ?? "?", p.FindDevice(l.BId)?.Name ?? "?",
            c.Standard(l.StandardId)?.Name(Loc.I.Lang) ?? l.StandardId,
            c.Media(l.MediaId)?.Name(Loc.I.Lang) ?? l.MediaId,
            StatKeys.FormatBits(l.BandwidthBps), l.LengthM.ToString("0.#"),
        }).ToList();
        return new ReportTable(L("Линии связи", "Links"),
            new[] { L("Устройство 1", "Device 1"), L("Устройство 2", "Device 2"), L("Спецификация", "Specification"), L("Среда", "Media"), L("Скорость", "Bandwidth"), L("Длина, м", "Length, m") }, rows);
    }

    public static ReportTable Flows(Project p)
    {
        var rows = p.Flows.Select(f => new[]
        {
            p.FindDevice(f.SourceId)?.Name ?? "?", p.FindDevice(f.TargetId)?.Name ?? "?", f.Profile.Name(Loc.I.Lang),
            f.Profile.TransactionSize.Summary, f.Profile.TimeBetween.Summary,
        }).ToList();
        return new ReportTable(Loc.F("Flow_Count", p.Flows.Count),
            new[] { Loc.T("Flow_Source"), Loc.T("Flow_Target"), Loc.T("Flow_Profile"), "Transaction size", "Time between" }, rows);
    }

    public static ReportTable Servers(Project p)
    {
        var rows = p.AllDevices().Where(d => d.Kind is DeviceKind.Server or DeviceKind.Mainframe && d.Software.Count > 0)
            .Select(d => new[] { d.Name, d.Server.ReplySize.Summary, d.Server.ReplyDelay.Summary }).ToList();
        return new ReportTable(L("Параметры серверов", "Server settings"), new[] { Loc.T("Report_Device"), "Reply Size", "Reply Delay" }, rows);
    }

    public static ReportTable Addresses(Project p)
    {
        var rows = new List<string[]>();
        foreach (var d in p.AllDevices())
            foreach (var i in d.Network.Interfaces.Where(i => !string.IsNullOrEmpty(i.IpAddress)))
                rows.Add(new[] { d.Name, i.Name, i.IpAddress, i.SubnetMask, d.Network.DefaultGateway });
        return new ReportTable(L("IP-адресация", "IP addressing"),
            new[] { Loc.T("Report_Device"), Loc.T("Config_Interface"), "IP", Loc.T("Config_Mask"), Loc.T("Config_Gateway") }, rows);
    }

    public static ReportTable Coverage(CoverageResult res)
    {
        string m = Loc.I.IsRu ? "м" : "m";
        var rows = res.Rows.OrderBy(r => r.AccessPoint.Name).ThenByDescending(r => r.DistanceM).Select(r => new[]
        {
            r.AccessPoint.Name, r.Station.Name, $"{r.DistanceM:0.0} {m}", r.Walls.ToString(), $"{r.RangeM:0.0} {m}",
            r.Ok ? Loc.T("Plan_StatusOk") : Loc.T("Plan_StatusFail"),
        }).ToList();
        return new ReportTable(Loc.T("Plan_CoverageTitle"),
            new[] { Loc.T("Plan_ColAp"), Loc.T("Plan_ColStation"), Loc.T("Plan_ColDistance"), Loc.T("Plan_ColWalls"), Loc.T("Plan_ColRange"), Loc.T("Plan_ColStatus") }, rows);
    }
}
