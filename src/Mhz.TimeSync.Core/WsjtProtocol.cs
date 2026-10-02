using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace Mhz.TimeSync.Core;

public record WsjtPacket(uint Type, string Id);
public sealed record Decode(string Client, bool IsNew, uint TimeMs, int Snr, double Dt,
    uint AudioHz, string Mode, string Message, bool LowConfidence, bool OffAir) : WsjtPacket(2, Client);
public sealed record Status(string Client, ulong DialHz, string Mode, bool TxEnabled, bool Transmitting) : WsjtPacket(1, Client);

public static class WsjtProtocol
{
    public static WsjtPacket Parse(byte[] bytes)
    {
        var r = new Reader(bytes);
        if (r.U32() != 0xadbccbda) throw new FormatException("Not a WSJT-X packet");
        uint schema = r.U32();
        if (schema is < 2 or > 3) throw new FormatException("Supported WSJT-X schemas: 2 and 3");
        uint type = r.U32(); string id = r.Text();
        if (type == 2)
        {
            bool fresh = r.Bool(); uint time = r.U32(); int snr = unchecked((int)r.U32());
            double dt = BitConverter.Int64BitsToDouble(unchecked((long)r.U64()));
            uint hz = r.U32(); string mode = r.Text(); string message = r.Text();
            bool low = r.Bool();
            // Off-air was appended to the protocol. Without it, do not trust data for correction.
            bool off = r.Remaining == 0 || r.Bool();
            if (time >= 86400000 || !double.IsFinite(dt)) throw new FormatException("Invalid decode values");
            return new Decode(id, fresh, time, snr, dt, hz, mode, message, low, off);
        }
        if (type == 1)
        {
            ulong hz = r.U64(); string mode = r.Text();
            r.Text(); r.Text(); r.Text(); bool enabled = r.Bool(); bool tx = r.Bool(); r.Bool();
            return new Status(id, hz, mode, enabled, tx);
        }
        return new WsjtPacket(type, id);
    }

    private sealed class Reader(byte[] data)
    {
        private int pos;
        public int Remaining => data.Length - pos;
        private ReadOnlySpan<byte> Take(int n)
        {
            if (n < 0 || n > Remaining) throw new FormatException("Truncated UDP packet");
            var span = data.AsSpan(pos, n); pos += n; return span;
        }
        public uint U32() => BinaryPrimitives.ReadUInt32BigEndian(Take(4));
        public ulong U64() => BinaryPrimitives.ReadUInt64BigEndian(Take(8));
        public bool Bool() { byte b = Take(1)[0]; if (b > 1) throw new FormatException("Invalid boolean"); return b == 1; }
        public string Text()
        {
            uint n = U32(); if (n == uint.MaxValue) return "";
            if (n > 4096) throw new FormatException("Oversized string");
            return Encoding.UTF8.GetString(Take((int)n));
        }
    }
}

public static partial class Callsigns
{
    [GeneratedRegex(@"^(?=.{3,16}$)(?=.*[A-Z])(?=.*[0-9])[A-Z0-9]+(?:/[A-Z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
    [GeneratedRegex(@"^[A-R]{2}[0-9]{2}(?:[A-X]{2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex Grid();
    public static string? Sender(string message)
    {
        var t = message.ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // Standard CQ [modifier] sender grid, or recipient sender report/grid.
        // Never treat recipient as sender. Hashed, contest, free-text forms fail closed.
        string? candidate = null;
        if (t.Length >= 3 && t[0] == "CQ" && Grid().IsMatch(t[^1])) candidate = t[^2];
        else if (t.Length == 3 && (Valid(t[0]) || t[0].StartsWith('<'))) candidate = t[1];
        return candidate is not null && Valid(candidate) ? candidate : null;
    }
    public static bool Valid(string s) => Pattern().IsMatch(s) && !Grid().IsMatch(s) && s is not "RR73";
}
