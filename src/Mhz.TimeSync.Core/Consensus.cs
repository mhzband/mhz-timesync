namespace Mhz.TimeSync.Core;

public record Sample(Decode Decode, string Station, long Received);
public record Estimate(int Count, int Valid, int Rejected, int Stations, int Slots,
    double MedianDt, double Spread, string Confidence, bool Ready)
{
    public double Correction => -MedianDt;
}

public sealed class Consensus
{
    private readonly List<Sample> samples = [];
    private HashSet<string>? selected;
    public string Selection => selected is null ? "All stations" : string.Join(", ", selected.Order());
    public void SelectStations(string? text)
    {
        HashSet<string>? next = null;
        if (text is not null)
        {
            next = text.ToUpperInvariant().Split([',', ';', ' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
            if (next.Count == 0 || next.Count > 50 || next.Any(s => !Callsigns.Valid(s)))
                throw new ArgumentException("Enter 1–50 valid callsigns, separated by commas/spaces. At least 4 received stations are required to correct.");
        }
        selected = next;
        Clear(); // Never reuse votes from the previous reference set.
    }
    public void Clear() => samples.Clear();
    public string Add(Decode d, long now)
    {
        Prune(now);
        if (!d.IsNew || d.OffAir || d.LowConfidence || d.Mode != "~" || !double.IsFinite(d.Dt) || Math.Abs(d.Dt) > 5 || d.Snr is < -30 or > 60)
            return "Filtered: replay / off-air / quality / non-FT8 / range";
        string? station = Callsigns.Sender(d.Message);
        if (station is null) return "Filtered: sender callsign is ambiguous";
        if (selected is not null && !selected.Contains(station)) return "Filtered: station not selected";
        if (samples.Any(s => s.Station == station && s.Decode.TimeMs == d.TimeMs)) return "Filtered: duplicate station/slot";
        // Limit any one station to six votes, while retaining its latest observations.
        if (samples.Count(s => s.Station == station) >= 6) samples.RemoveAt(samples.FindIndex(s => s.Station == station));
        samples.Add(new(d, station, now));
        if (samples.Count > 30) samples.RemoveAt(0);
        return "Candidate (see MAD totals)";
    }
    private void Prune(long now) => samples.RemoveAll(s => now - s.Received > 180000 || now < s.Received);
    public Estimate Get(long now)
    {
        Prune(now);
        if (samples.Count == 0) return new(0, 0, 0, 0, 0, 0, 0, "WAITING", false);
        // Equal station weighting prevents a repetitive station from dominating the center.
        double center = Median(samples.GroupBy(s => s.Station).Select(g => Median(g.Select(s => s.Decode.Dt))));
        double mad = Median(samples.Select(s => Math.Abs(s.Decode.Dt - center)));
        double gate = Math.Max(0.10, 3 * 1.4826 * mad);
        var good = samples.Where(s => Math.Abs(s.Decode.Dt - center) <= gate).ToArray();
        var stationMedians = good.GroupBy(s => s.Station).Select(g => Median(g.Select(s => s.Decode.Dt))).ToArray();
        double median = stationMedians.Length == 0 ? center : Median(stationMedians);
        double spread = good.Length == 0 ? 99 : good.Max(s => s.Decode.Dt) - good.Min(s => s.Decode.Dt);
        int slots = good.Select(s => s.Decode.TimeMs / 15000).Distinct().Count();
        bool fresh = good.Length > 0 && now - good.Max(s => s.Received) <= 45000;
        bool ready = samples.Count >= 20 && good.Length >= 20 && stationMedians.Length >= 4 && slots >= 3 && spread <= 0.35 && fresh;
        string confidence = ready ? (stationMedians.Length >= 5 && spread <= 0.15 ? "HIGH" : "MEDIUM") : "LOW / NOT READY";
        return new(samples.Count, good.Length, samples.Count - good.Length, stationMedians.Length, slots, median, spread, confidence, ready);
    }
    public static double Median(IEnumerable<double> input)
    {
        var v = input.Order().ToArray(); if (v.Length == 0) throw new ArgumentException("Empty median");
        return v.Length % 2 == 1 ? v[v.Length / 2] : (v[v.Length / 2 - 1] + v[v.Length / 2]) / 2;
    }
}

public static class CorrectionPolicy
{
    public static string Grade(double seconds) => Math.Abs(seconds) switch
    { < .20 => "GOOD", < .50 => "OPTIONAL", <= 1.50 => "SYNC RECOMMENDED", _ => "WARNING — large offset" };
    public static bool AutoAllowed(double correction, bool ready, bool high, bool receiveSafe, long now, long lastCorrection)
        => ready && high && receiveSafe && Math.Abs(correction) is >= .50 and <= 1.50 && now - lastCorrection >= 300000;
}
