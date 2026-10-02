using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Mhz.TimeSync.Core;

int passed = 0;
void Test(string name, Action test) { test(); passed++; Console.WriteLine($"PASS {name}"); }
void Check(bool ok) { if (!ok) throw new Exception("Assertion failed"); }
void Near(double a, double b) => Check(Math.Abs(a - b) < .0001);
Decode D(int station, int slot, double dt = .70) => new("WSJT-X", true, (uint)(slot * 15000), -12, dt, 1000, "~", $"CQ K{station}ABC FN42", false, false);
byte[] Packet(Decode d)
{
    using var stream = new MemoryStream();
    void U32(uint n) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, n); stream.Write(b); }
    void Str(string s) { byte[] b = Encoding.UTF8.GetBytes(s); U32((uint)b.Length); stream.Write(b); }
    U32(0xadbccbda); U32(3); U32(2); Str(d.Client); stream.WriteByte(d.IsNew ? (byte)1 : (byte)0);
    U32(d.TimeMs); U32(unchecked((uint)d.Snr)); Span<byte> dt = stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(dt, BitConverter.DoubleToInt64Bits(d.Dt)); stream.Write(dt);
    U32(d.AudioHz); Str(d.Mode); Str(d.Message); stream.WriteByte(d.LowConfidence ? (byte)1 : (byte)0); stream.WriteByte(d.OffAir ? (byte)1 : (byte)0); return stream.ToArray();
}
if (args.Contains("--simulate") || args.Contains("--simulate-fast"))
{
    Console.WriteLine("Synthetic WSJT-X data to localhost:2237. Keep Auto Correct OFF. Do not correct your real clock from simulated data.");
    using var udp = new UdpClient();
    for (int slot = 0; slot < 5; slot++)
    {
        for (int station = 1; station <= 6; station++)
        { byte[] p = Packet(D(station, slot, station == 6 ? 2.5 : .7 + station * .01)); await udp.SendAsync(p, p.Length, "127.0.0.1", 2237); }
        await Task.Delay(args.Contains("--simulate-fast") ? 100 : 15000);
    }
    return;
}
if (args.Contains("--ntp"))
{
    try { Console.WriteLine(await new NtpClient().QueryAsync(Console.WriteLine, CancellationToken.None)); }
    catch (IOException ex) { Console.WriteLine(ex.Message); Environment.ExitCode = 1; }
    return;
}
Test("Big-endian Decode and double DT", () => { var d = (Decode)WsjtProtocol.Parse(Packet(D(1, 2, -.73))); Near(d.Dt, -.73); Check(d.Snr == -12 && d.Message == "CQ K1ABC FN42" && d.TimeMs == 30000); });
Test("Status frequency, mode and TX interlock fields", () =>
{
    using var s = new MemoryStream();
    void U(uint n) { byte[] b = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, n); s.Write(b); }
    void Str(string v) { var b = Encoding.UTF8.GetBytes(v); U((uint)b.Length); s.Write(b); }
    U(0xadbccbda); U(2); U(1); Str("WSJT-X"); U(0); U(14074000); Str("FT8"); Str("K1ABC"); Str("-10"); Str("FT8"); s.Write([1, 1, 0]);
    var status = (Status)WsjtProtocol.Parse(s.ToArray()); Check(status.DialHz == 14074000 && status.Mode == "FT8" && status.TxEnabled && status.Transmitting);
});
Test("All truncated decode prefixes rejected or marked off-air", () =>
{
    byte[] packet = Packet(D(1, 0));
    for (int i = 0; i < packet.Length; i++)
    { try { var d = (Decode)WsjtProtocol.Parse(packet[..i]); Check(d.OffAir); } catch (FormatException) { } }
});
Test("Invalid schema / oversized strings / NaN rejected", () =>
{
    foreach (int variant in new[] { 0, 1, 2 })
    { byte[] p = Packet(D(1, 0, variant == 2 ? double.NaN : .3)); if (variant == 0) p[7] = 9; if (variant == 1) BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(12), 50000); bool bad = false; try { WsjtProtocol.Parse(p); } catch (FormatException) { bad = true; } Check(bad); }
});
Test("Random malformed packets cannot escape FormatException", () =>
{
    var random = new Random(73);
    for (int i = 0; i < 1000; i++) { byte[] bytes = new byte[random.Next(0, 300)]; random.NextBytes(bytes); try { WsjtProtocol.Parse(bytes); } catch (FormatException) { } }
});
Test("Sender extraction does not count addressed station", () =>
{
    Check(Callsigns.Sender("K1ABC W2XYZ -10") == "W2XYZ"); Check(Callsigns.Sender("CQ DX JA1ABC PM95") == "JA1ABC");
    Check(Callsigns.Sender("CQ <...> FN42") is null); Check(Callsigns.Sender("HELLO WORLD") is null);
});
Test("Single station cannot authorize correction", () => { var c = new Consensus(); for (int i = 0; i < 30; i++) c.Add(D(1, i), 1000); var e = c.Get(1000); Check(!e.Ready && e.Count == 6 && e.Stations == 1); });
Test("MAD rejects minority outlier and corrects with negative DT", () =>
{
    var c = new Consensus(); for (int slot = 0; slot < 5; slot++) for (int station = 1; station <= 6; station++) c.Add(D(station, slot, station == 6 ? 2.5 : .7), 1000);
    var e = c.Get(1000); Check(e.Ready && e.Count == 30 && e.Valid == 25 && e.Rejected == 5 && e.Stations == 5 && e.Confidence == "HIGH"); Near(e.Correction, -.7);
});
Test("Replay, duplicates, off-air, low-confidence, non-FT8 blocked", () =>
{
    var c = new Consensus(); var d = D(1, 1); c.Add(d, 100); c.Add(d, 101);
    foreach (var bad in new[] { d with { IsNew = false }, d with { OffAir = true }, d with { LowConfidence = true }, d with { Mode = "+" }, d with { Dt = double.NaN } }) c.Add(bad, 100);
    Check(c.Get(100).Count == 1);
});
Test("Freshness expiration and clearing", () =>
{
    var c = new Consensus(); for (int i = 0; i < 5; i++) for (int j = 1; j <= 5; j++) c.Add(D(j, i), 1000);
    Check(c.Get(1000).Ready); Check(!c.Get(47000).Ready); Check(c.Get(182000).Count == 0); c.Add(D(1, 1), 183000); c.Clear(); Check(c.Get(183000).Count == 0);
});
Test("Disagreeing stations and single slot blocked", () =>
{
    var c = new Consensus(); for (int i = 0; i < 5; i++) for (int j = 1; j <= 5; j++) c.Add(D(j, i, j * .3), 1000); Check(!c.Get(1000).Ready);
    c.Clear(); for (int j = 1; j <= 25; j++) c.Add(D(j, 1), 1000); Check(!c.Get(1000).Ready);
});
Test("Threshold boundaries and automatic interlocks", () =>
{
    Check(CorrectionPolicy.Grade(.199) == "GOOD" && CorrectionPolicy.Grade(.2) == "OPTIONAL" && CorrectionPolicy.Grade(.5) == "SYNC RECOMMENDED" && CorrectionPolicy.Grade(1.5) == "SYNC RECOMMENDED" && CorrectionPolicy.Grade(1.501).StartsWith("WARNING"));
    Check(CorrectionPolicy.AutoAllowed(.7, true, true, true, 400000, 0));
    Check(!CorrectionPolicy.AutoAllowed(.7, true, true, false, 400000, 0)); Check(!CorrectionPolicy.AutoAllowed(1.6, true, true, true, 400000, 0)); Check(!CorrectionPolicy.AutoAllowed(.7, true, true, true, 10000, 0)); Check(!CorrectionPolicy.AutoAllowed(.7, true, false, true, 400000, 0));
});
Test("NTP four timestamps, sign and rollover after 2036", () =>
{
    foreach (int year in new[] { 2026, 2040 })
    {
        DateTime t1 = new(year, 1, 1, 0, 0, 0, DateTimeKind.Utc); byte[] req = new byte[48], p = new byte[48]; p[0] = 0x24; p[1] = 2;
        NtpClient.WriteTimestamp(req.AsSpan(40), t1); req.AsSpan(40, 8).CopyTo(p.AsSpan(24)); NtpClient.WriteTimestamp(p.AsSpan(32), t1.AddSeconds(.55)); NtpClient.WriteTimestamp(p.AsSpan(40), t1.AddSeconds(.56));
        var n = NtpClient.ParseReply(p, req, t1, t1.AddSeconds(.11), "test", 1); Near(n.Correction, .5); Near(n.RoundTrip, .1);
        foreach (int bad in new[] { 0, 1, 24, 40 })
        { byte[] broken = (byte[])p.Clone(); if (bad == 0) broken[0] = 0xe4; else if (bad == 1) broken[1] = 0; else if (bad == 24) broken[24] ^= 1; else broken.AsSpan(40, 8).Clear(); bool rejected = false; try { NtpClient.ParseReply(broken, req, t1, t1.AddSeconds(.11), "test", 1); } catch (IOException) { rejected = true; } Check(rejected); }
    }
});
Test("Selected stations exclude others, normalize case and require multiple senders", () =>
{
    var c = new Consensus(); c.SelectStations("k1abc, K2ABC K3ABC;K4ABC K5ABC k1abc");
    for (int slot = 0; slot < 5; slot++) for (int station = 1; station <= 6; station++) c.Add(D(station, slot, station == 6 ? -2 : .7), 1000);
    var e = c.Get(1000); Check(e.Count == 25 && e.Stations == 5 && e.Ready); Near(e.Correction, -.7);
    Check(c.Add(D(6, 8), 1000) == "Filtered: station not selected");
    c.SelectStations("K1ABC"); Check(c.Get(1000).Count == 0);
    for (int slot = 0; slot < 30; slot++) c.Add(D(1, slot), 1000);
    Check(!c.Get(1000).Ready);
});
Test("Station selection validation is atomic and reset clears samples", () =>
{
    var c = new Consensus(); c.SelectStations("K1ABC"); c.Add(D(1, 1), 1000);
    foreach (string bad in new[] { "", "  , ;", "K1ABC NOT-A-CALL", "<HS9XKG>" })
    { bool rejected = false; try { c.SelectStations(bad); } catch (ArgumentException) { rejected = true; } Check(rejected && c.Selection == "K1ABC" && c.Get(1000).Count == 1); }
    c.SelectStations(null); Check(c.Selection == "All stations" && c.Get(1000).Count == 0);
    c.Add(D(6, 1), 1000); Check(c.Get(1000).Count == 1);
    c.SelectStations("K1ABC/P"); Check(c.Add(D(1, 2), 1000).Contains("not selected"));
});
Test("Manual single station needs only one live decode and preserves sign", () =>
{
    var sample = new StationReference(D(1, 1, 1.2), 1000, 0);
    Check(StationCorrection.TryGet(sample, 2000, out var call, out var delta, out _) && call == "K1ABC"); Near(delta, -1.2);
    Check(StationCorrection.TryGet(sample with { Decode = D(1, 1, -.7) }, 2000, out _, out delta, out _)); Near(delta, .7);
});
Test("Manual station rejects stale, replay, WAV, poor/unknown/invalid decodes", () =>
{
    var d = D(1, 1); var sample = new StationReference(d, 1000, 0);
    Check(!StationCorrection.TryGet(sample, 46001, out _, out _, out _));
    Check(!StationCorrection.TryGet(sample, 999, out _, out _, out _));
    foreach (var bad in new[] { d with { IsNew = false }, d with { OffAir = true }, d with { LowConfidence = true }, d with { Mode = "+" }, d with { Dt = double.NaN }, d with { Dt = 5.1 }, d with { Message = "CQ <...> FN42" }, d with { Snr = -50 } })
        Check(!StationCorrection.TryGet(sample with { Decode = bad }, 2000, out _, out _, out _));
});
Console.WriteLine($"{passed} tests passed. No system clock was changed.");
