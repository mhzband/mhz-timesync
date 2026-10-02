namespace Mhz.TimeSync.Core;

// An explicit manual override; never used by the consensus/automatic policy.
public sealed record StationReference(Decode Decode, long ReceivedAt, long Generation);
public static class StationCorrection
{
    public static bool TryGet(StationReference sample, long now, out string station, out double correction, out string reason)
    {
        station = Callsigns.Sender(sample.Decode.Message) ?? "";
        correction = -sample.Decode.Dt;
        var d = sample.Decode;
        reason = "";
        if (now < sample.ReceivedAt || now - sample.ReceivedAt > 45000) reason = "Decode expired (maximum age 45 seconds). Wait for a new decode.";
        else if (!d.IsNew || d.OffAir || d.LowConfidence || d.Mode != "~") reason = "Requires a new, live, normal-confidence FT8 decode.";
        else if (!double.IsFinite(d.Dt) || Math.Abs(d.Dt) > 5 || d.Snr is < -30 or > 60) reason = "Decode outside supported quality/range limits (DT ±5 seconds).";
        else if (station.Length == 0) reason = "Sender callsign is ambiguous.";
        return reason.Length == 0;
    }
}
