using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Mhz.TimeSync.App;

internal static class WindowsClock
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime { public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds; }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSystemTime(ref SystemTime time);
    public static bool IsAdmin
    {
        get { using var id = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); }
    }
    private static void Apply(double correction)
    {
        DateTime utc = DateTime.UtcNow.AddSeconds(correction);
        var st = new SystemTime { Year = (ushort)utc.Year, Month = (ushort)utc.Month, Day = (ushort)utc.Day,
            DayOfWeek = (ushort)utc.DayOfWeek, Hour = (ushort)utc.Hour, Minute = (ushort)utc.Minute,
            Second = (ushort)utc.Second, Milliseconds = (ushort)utc.Millisecond };
        if (!SetSystemTime(ref st)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public static async Task CorrectAsync(double correction)
    {
        if (!double.IsFinite(correction) || Math.Abs(correction) > 300) throw new InvalidOperationException("Correction outside safety limit");
        if (IsAdmin) { Apply(correction); return; }
        string exe = Environment.ProcessPath ?? throw new IOException("Executable path unavailable");
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Run MHZ.TimeSync.exe directly to use UAC elevation.");
        long tick = Environment.TickCount64; long utc = DateTime.UtcNow.Ticks;
        var info = new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" };
        foreach (string a in new[] { "--apply", correction.ToString("R", CultureInfo.InvariantCulture), tick.ToString(CultureInfo.InvariantCulture), utc.ToString(CultureInfo.InvariantCulture) }) info.ArgumentList.Add(a);
        using var child = Process.Start(info) ?? throw new IOException("Could not start clock helper");
        await child.WaitForExitAsync();
        if (child.ExitCode != 0) throw new IOException($"Clock helper refused/failed ({child.ExitCode}). Re-measure and try again. Check Windows 'Change the system time' policy.");
    }
    public static int RunHelper(string[] args)
    {
        try
        {
            if (args.Length != 4 || !IsAdmin) return 2;
            double delta = double.Parse(args[1], CultureInfo.InvariantCulture);
            long elapsed = Environment.TickCount64 - long.Parse(args[2], CultureInfo.InvariantCulture);
            long ticks = long.Parse(args[3], CultureInfo.InvariantCulture);
            // UAC delays and another time service stepping the clock invalidate the request.
            if (!double.IsFinite(delta) || Math.Abs(delta) > 300 || elapsed is < 0 or > 30000 ||
                Math.Abs((DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalMilliseconds - elapsed) > 200) return 3;
            Apply(delta); return 0;
        }
        catch { return 4; }
    }
}
