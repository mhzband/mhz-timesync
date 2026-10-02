using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Mhz.TimeSync.Core;

public record NtpResult(string Host, double Correction, double RoundTrip, long MeasuredAt);
public sealed class NtpClient
{
    public static readonly string[] Hosts = ["time1.nimt.or.th", "time2.nimt.or.th", "time3.nimt.or.th", "time.navy.mi.th", "clock.nectec.or.th"];
    private static readonly DateTime Epoch = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public async Task<NtpResult> QueryAsync(Action<string> log, CancellationToken cancellation)
    {
        foreach (string host in Hosts)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                var readings = new List<NtpResult>();
                for (int i = 0; i < 3; i++)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                    timeout.CancelAfter(2500);
                    readings.Add(await QueryOne(host, timeout.Token));
                    if (i < 2) await Task.Delay(250, cancellation);
                }
                if (readings.Max(x => x.Correction) - readings.Min(x => x.Correction) > .20)
                    throw new IOException("Unstable NTP samples");
                // Lowest RTT minimizes network-delay uncertainty.
                return readings.MinBy(x => x.RoundTrip)!;
            }
            catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or ArgumentException)
            { cancellation.ThrowIfCancellationRequested(); log($"{host}: {ex.Message}"); }
        }
        throw new IOException("All Thailand NTP servers unavailable; check DNS / outbound UDP 123.");
    }
    private static async Task<NtpResult> QueryOne(string host, CancellationToken token)
    {
        var ips = await Dns.GetHostAddressesAsync(host, token);
        var ip = ips.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork) ?? ips.First();
        using var udp = new UdpClient(ip.AddressFamily); udp.Connect(ip, 123);
        byte[] request = new byte[48]; request[0] = 0x23; // VN4 client
        DateTime t1 = DateTime.UtcNow; long start = Environment.TickCount64;
        WriteTimestamp(request.AsSpan(40), t1);
        await udp.SendAsync(request, token);
        var reply = await udp.ReceiveAsync(token);
        DateTime t4 = DateTime.UtcNow; long end = Environment.TickCount64;
        if (Math.Abs((t4 - t1).TotalMilliseconds - (end - start)) > 100) throw new IOException("System clock changed during NTP query");
        return ParseReply(reply.Buffer, request, t1, t4, host, end);
    }
    public static NtpResult ParseReply(byte[] p, byte[] request, DateTime t1, DateTime t4, string host, long stamp)
    {
        if (p.Length < 48 || request.Length < 48) throw new IOException("Short NTP packet");
        if ((p[0] & 7) != 4 || ((p[0] >> 3) & 7) is < 3 or > 4 || (p[0] >> 6) == 3 || p[1] is 0 or > 15)
            throw new IOException("Unsynchronized / invalid NTP response or Kiss-o'-Death");
        if (!p.AsSpan(24, 8).SequenceEqual(request.AsSpan(40, 8))) throw new IOException("NTP originate mismatch");
        if (BinaryPrimitives.ReadUInt64BigEndian(p.AsSpan(32)) == 0 || BinaryPrimitives.ReadUInt64BigEndian(p.AsSpan(40)) == 0)
            throw new IOException("Missing NTP timestamps");
        double dispersion = BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(8)) / 65536.0;
        double rootDelay = BinaryPrimitives.ReadInt32BigEndian(p.AsSpan(4)) / 65536.0;
        if (dispersion > 1 || Math.Abs(rootDelay) > 2) throw new IOException("NTP root distance too large");
        DateTime t2 = ReadTimestamp(p.AsSpan(32), t1), t3 = ReadTimestamp(p.AsSpan(40), t1);
        double rtt = (t4 - t1).TotalSeconds - (t3 - t2).TotalSeconds;
        double correction = ((t2 - t1).TotalSeconds + (t3 - t4).TotalSeconds) / 2;
        if (t3 < t2 || rtt < -.002 || rtt > 1 || !double.IsFinite(correction) || Math.Abs(correction) > 300)
            throw new IOException("NTP timing outside MVP limits (RTT 1s, correction 300s)");
        return new(host, correction, Math.Max(0, rtt), stamp);
    }
    public static void WriteTimestamp(Span<byte> dest, DateTime utc)
    {
        double seconds = (utc - Epoch).TotalSeconds;
        BinaryPrimitives.WriteUInt32BigEndian(dest, unchecked((uint)(ulong)Math.Floor(seconds)));
        BinaryPrimitives.WriteUInt32BigEndian(dest[4..], (uint)((seconds - Math.Floor(seconds)) * 4294967296.0));
    }
    private static DateTime ReadTimestamp(ReadOnlySpan<byte> data, DateTime near)
    {
        double seconds = BinaryPrimitives.ReadUInt32BigEndian(data) + BinaryPrimitives.ReadUInt32BigEndian(data[4..]) / 4294967296.0;
        double era = Math.Round(((near - Epoch).TotalSeconds - seconds) / 4294967296.0);
        return Epoch.AddSeconds(seconds + era * 4294967296.0);
    }
}
