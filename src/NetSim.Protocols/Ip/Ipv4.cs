using System.Globalization;

namespace NetSim.Protocols.Ip;

/// <summary>IPv4-адрес как 32-битное число. Простые операции, нужные для учебного стека.</summary>
public readonly record struct Ipv4(uint Value) : IComparable<Ipv4>
{
    public static readonly Ipv4 Any = new(0);
    public static readonly Ipv4 Broadcast = new(0xFFFFFFFF);

    public static bool TryParse(string? s, out Ipv4 ip)
    {
        ip = default;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var parts = s.Trim().Split('.');
        if (parts.Length != 4) return false;
        uint v = 0;
        foreach (var p in parts)
        {
            if (!byte.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var b)) return false;
            v = (v << 8) | b;
        }
        ip = new Ipv4(v);
        return true;
    }

    public static Ipv4 Parse(string s) => TryParse(s, out var ip) ? ip : throw new FormatException($"Неверный IP-адрес: {s}");

    public static bool IsValidMask(Ipv4 mask)
    {
        uint inv = ~mask.Value;
        return (inv & (inv + 1)) == 0;
    }

    public int PrefixLength => System.Numerics.BitOperations.PopCount(Value);

    public static Ipv4 FromPrefix(int prefix) => new(prefix == 0 ? 0 : 0xFFFFFFFF << (32 - prefix));

    public Ipv4 Network(Ipv4 mask) => new(Value & mask.Value);

    public bool SameSubnet(Ipv4 other, Ipv4 mask) => (Value & mask.Value) == (other.Value & mask.Value);

    public bool IsBroadcastFor(Ipv4 mask) => (Value | mask.Value) == 0xFFFFFFFF;

    public int CompareTo(Ipv4 other) => Value.CompareTo(other.Value);

    public override string ToString() => $"{Value >> 24}.{(Value >> 16) & 255}.{(Value >> 8) & 255}.{Value & 255}";
}

public static class Mac
{
    public const string Broadcast = "FFFF.FFFF.FFFF";
}
