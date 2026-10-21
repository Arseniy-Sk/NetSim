using NetSim.Core.Model;
using NetSim.Protocols.Ip;
using NetSim.Protocols.Nodes;

namespace NetSim.Protocols.Cli;

/// <summary>
/// Командная оболочка устройства. Для маршрутизаторов и коммутаторов — упрощённый Cisco IOS,
/// для компьютеров — «Командная строка» (ipconfig, ping, arp -a).
/// Новые команды добавляются в соответствующие обработчики режимов.
/// </summary>
public abstract class CommandShell
{
    protected Device Device { get; }
    protected ProtocolSimulator Sim { get; }

    protected CommandShell(Device device, ProtocolSimulator sim)
    {
        Device = device;
        Sim = sim;
    }

    public abstract string Prompt { get; }

    /// <summary>Приветствие при открытии консоли.</summary>
    public abstract IEnumerable<string> Banner();

    public IReadOnlyList<string> Execute(string line)
    {
        var output = new List<string>();
        var tokens = Tokenize(line);
        if (tokens.Count == 0) return output;
        try
        {
            Run(tokens, line.Trim(), output);
        }
        catch (Exception ex)
        {
            output.Add("% " + ex.Message);
        }
        return output;
    }

    protected abstract void Run(List<string> t, string raw, List<string> output);

    public static CommandShell For(Device device, ProtocolSimulator sim) =>
        device.Kind is DeviceKind.Router or DeviceKind.Switch or DeviceKind.Firewall or DeviceKind.Gateway
            or DeviceKind.AccessServer or DeviceKind.Bridge or DeviceKind.AccessPoint
            ? new CiscoShell(device, sim)
            : new PcShell(device, sim);

    protected static List<string> Tokenize(string line) =>
        line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>Сокращение команды: «conf» совпадает с «configure», минимум minLen символов.</summary>
    protected static bool Is(IReadOnlyList<string> t, int index, string keyword, int minLen = 1) =>
        index < t.Count && t[index].Length >= Math.Min(minLen, keyword.Length)
        && keyword.StartsWith(t[index], StringComparison.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> LongNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Fa"] = "FastEthernet", ["Gi"] = "GigabitEthernet", ["S"] = "Serial", ["Eth"] = "Ethernet",
        ["Wlan"] = "Wireless", ["Tr"] = "TokenRing", ["Te"] = "TenGigabitEthernet",
    };

    /// <summary>Найти интерфейс по сокращённому имени: fa0/1, f0/1, FastEthernet0/1, gi0/0...</summary>
    protected NetInterface? FindInterface(string text)
    {
        var (ua, ur) = SplitName(text.Replace(" ", ""));
        foreach (var i in Device.Network.Interfaces)
        {
            var (ia, ir) = SplitName(i.Name);
            if (!string.Equals(ir, ur, StringComparison.OrdinalIgnoreCase)) continue;
            if (ia.StartsWith(ua, StringComparison.OrdinalIgnoreCase)) return i;
            if (ua.Length > 0 && LongNames.TryGetValue(ia, out var full) && full.StartsWith(ua, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return null;
    }

    private static (string Alpha, string Tail) SplitName(string s)
    {
        int k = 0;
        while (k < s.Length && char.IsLetter(s[k])) k++;
        return (s[..k], s[k..]);
    }

    protected static string LongName(string iface)
    {
        var (a, r) = SplitName(iface);
        return LongNames.TryGetValue(a, out var full) ? full + r : iface;
    }

    protected void Ping(IReadOnlyList<string> t, int argIndex, List<string> output, int count)
    {
        if (argIndex >= t.Count) { output.Add("% Incomplete command."); return; }
        output.AddRange(Sim.PingBlocking(Device, t[argIndex], count));
    }

    protected IEnumerable<string> ArpTable()
    {
        if (Sim.Node(Device) is not IpNode node) yield break;
        foreach (var (ip, e) in node.ArpCache.OrderBy(x => x.Key))
            yield return $"  {ip,-18}{e.Mac,-20}dynamic";
    }
}

/// <summary>Командная строка компьютера (как Command Prompt в Packet Tracer).</summary>
public sealed class PcShell : CommandShell
{
    public PcShell(Device device, ProtocolSimulator sim) : base(device, sim) { }

    public override string Prompt => "C:\\>";

    public override IEnumerable<string> Banner()
    {
        yield return "NetSim Command Line 1.0";
        yield return "Команды / commands: ipconfig [/all | /renew | /release], ipconfig <ip> <mask> [gateway], ping <ip|имя> [-n N], tracert <ip|имя>, nslookup <имя>, arp -a, help";
    }

    protected override void Run(List<string> t, string raw, List<string> output)
    {
        var cmd = t[0].ToLowerInvariant();
        switch (cmd)
        {
            case "ipconfig" when t.Count > 1 && t[1].Equals("/renew", StringComparison.OrdinalIgnoreCase):
                Device.Network.UseDhcp = true;
                output.AddRange(Sim.DhcpRenewBlocking(Device));
                return;
            case "ipconfig" when t.Count > 1 && t[1].Equals("/release", StringComparison.OrdinalIgnoreCase):
                if (Sim.Node(Device) is IpNode releasing) releasing.ReleaseDhcp();
                output.Add("IP address released. / Адрес освобождён.");
                return;
            case "tracert":
                if (t.Count < 2) { output.Add("Usage: tracert <ip|name>"); return; }
                output.AddRange(Sim.TraceBlocking(Device, t[1], cisco: false));
                return;
            case "nslookup":
                if (t.Count < 2) { output.Add("Usage: nslookup <name>"); return; }
                output.Add($"Server:  {(string.IsNullOrEmpty(Device.Network.DnsServer) ? "(none)" : Device.Network.DnsServer)}");
                var resolved = Sim.ResolveBlocking(Device, t[1]);
                output.Add(resolved is { } addr ? $"Name:    {t[1]}" : $"*** Can't find {t[1]}: Non-existent domain");
                if (resolved is { } a2) output.Add($"Address: {a2}");
                return;
            case "ipconfig":
                if (t.Count >= 3 && Ipv4.TryParse(t[1], out var ip) && Ipv4.TryParse(t[2], out var mask))
                {
                    var iface = Device.Network.Interfaces.FirstOrDefault();
                    if (iface == null) { output.Add("Нет сетевых интерфейсов — подключите устройство кабелем."); return; }
                    iface.IpAddress = ip.ToString();
                    iface.SubnetMask = mask.ToString();
                    if (t.Count >= 4 && Ipv4.TryParse(t[3], out var gw)) Device.Network.DefaultGateway = gw.ToString();
                    output.Add($"{iface.Name}: IP {ip} / {mask}, gateway {Device.Network.DefaultGateway}");
                    return;
                }
                foreach (var i in Device.Network.Interfaces)
                {
                    output.Add("");
                    output.Add($"Ethernet adapter {i.Name}:");
                    if (t.Count > 1 && t[1].Equals("/all", StringComparison.OrdinalIgnoreCase))
                        output.Add($"   Physical Address. . . . . . . . . : {i.MacAddress}");
                    output.Add($"   IPv4 Address. . . . . . . . . . . : {(string.IsNullOrEmpty(i.IpAddress) ? "0.0.0.0" : i.IpAddress)}");
                    output.Add($"   Subnet Mask . . . . . . . . . . . : {(string.IsNullOrEmpty(i.SubnetMask) ? "0.0.0.0" : i.SubnetMask)}");
                    output.Add($"   Default Gateway . . . . . . . . . : {(string.IsNullOrEmpty(Device.Network.DefaultGateway) ? "0.0.0.0" : Device.Network.DefaultGateway)}");
                    if (t.Count > 1 && t[1].Equals("/all", StringComparison.OrdinalIgnoreCase))
                    {
                        output.Add($"   DHCP Enabled. . . . . . . . . . . : {(Device.Network.UseDhcp ? "Yes" : "No")}");
                        output.Add($"   DNS Servers . . . . . . . . . . . : {(string.IsNullOrEmpty(Device.Network.DnsServer) ? "0.0.0.0" : Device.Network.DnsServer)}");
                    }
                }
                if (Device.Network.Interfaces.Count == 0) output.Add("Нет сетевых интерфейсов.");
                return;
            case "ping":
                int n = 4;
                int idx = t.FindIndex(x => x == "-n");
                if (idx > 0 && idx + 1 < t.Count && int.TryParse(t[idx + 1], out var parsed)) { n = Math.Clamp(parsed, 1, 100); t.RemoveRange(idx, 2); }
                Ping(t, 1, output, n);
                return;
            case "arp":
                output.Add("  Internet Address  Physical Address    Type");
                output.AddRange(ArpTable());
                return;
            case "help":
            case "?":
                output.AddRange(Banner().Skip(1));
                return;
            default:
                output.Add($"Invalid Command: {t[0]}");
                return;
        }
    }
}
