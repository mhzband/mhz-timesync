using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Mhz.TimeSync.Core;

namespace Mhz.TimeSync.App;

public sealed class MainForm : Form
{
    private readonly Color ink = Color.FromArgb(27, 44, 61), teal = Color.FromArgb(0, 112, 112);
    private readonly Label offset = new(), source = new(), grade = new(), stats = new(), wsjt = new(), ntpStatus = new(), privilege = new();
    private readonly FlowLayoutPanel mode = new() { Width = 410, Height = 34, WrapContents = false };
    private readonly RadioButton autoMode = new() { Text = "AUTO", AutoSize = true }, ntpMode = new() { Text = "NTP Thailand", AutoSize = true }, offlineMode = new() { Text = "Offline FT8", AutoSize = true };
    private readonly Button measure = new() { Text = "Measure NTP", AutoSize = true }, correct = new() { Text = "Sync Now / Correct Clock", AutoSize = true };
    private readonly CheckBox auto = new() { Text = "Auto Correct (off by default)", AutoSize = true };
    private readonly TextBox log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Color.FromArgb(245, 248, 250) };
    private readonly DataGridView grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.None };
    private readonly Consensus consensus = new();
    private readonly ContextMenuStrip stationMenu = new();
    private readonly ToolStripMenuItem syncStation = new("Sync with this Station");
    private StationReference? menuTarget;
    private long generation;
    private bool confirming;
    private readonly TextBox stationInput = new() { Width = 330, PlaceholderText = "HS9XKG, K1ABC, JA1ABC, W2XYZ, ..." };
    private readonly Button applyStations = new() { Text = "Use selected", AutoSize = true }, allStations = new() { Text = "Use all", AutoSize = true };
    private readonly Label stationSummary = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly NotifyIcon tray = new() { Text = "MHZ TimeSync — HS9XKG", Icon = BrandAssets.AppIcon };
    private readonly CancellationTokenSource shutdown = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private UdpClient? listener;
    private NtpResult? ntp;
    private Status? status;
    private string? client;
    private long lastPacket = -1000000, lastStatus = -1000000, lastProbe = -1000000, lastCorrection = -1000000;
    private long previousTick = Environment.TickCount64, blockedUntil;
    private DateTime previousUtc = DateTime.UtcNow;
    private bool probing, correcting, probeCompleted;
    private string bindError = "";
    private readonly Queue<string> lines = new();
    private readonly Font readyFont = new("Segoe UI", 31, FontStyle.Bold), waitingFont = new("Segoe UI", 21, FontStyle.Bold);
    private string SelectedMode => offlineMode.Checked ? "Offline FT8" : ntpMode.Checked ? "NTP Thailand" : "AUTO";
    public MainForm()
    {
        Text = "MHZ TimeSync — Offline FT8 Clock Sync"; Size = new(1040, 700); MinimumSize = new(820, 600);
        StartPosition = FormStartPosition.CenterScreen; BackColor = Color.FromArgb(239, 244, 247);
        Font = new("Segoe UI", 9.5f); ForeColor = ink; AutoScaleMode = AutoScaleMode.Dpi; AutoScroll = true;
        Icon = BrandAssets.AppIcon;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(16, 12, 16, 12), ColumnCount = 1, RowCount = 11 };
        foreach (float h in new[] { 52f, 27f, 40f, 58f, 32f, 66f, 44f, 32f, 64f }) root.RowStyles.Add(new(SizeType.Absolute, h));
        root.RowStyles.Add(new(SizeType.Percent, 65)); root.RowStyles.Add(new(SizeType.Percent, 35));
        Controls.Add(root);
        var header = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var brand = new PictureBox { Image = BrandAssets.Logo, Size = new(48, 48), SizeMode = PictureBoxSizeMode.Zoom, Margin = new(0, 0, 8, 0) };
        var hide = new Button { Text = "Hide to tray", AutoSize = true, Margin = new(24, 8, 0, 0) };
        var about = new Button { Text = "About / เกี่ยวกับ", AutoSize = true, Margin = new(8, 8, 0, 0) };
        header.Controls.Add(brand); header.Controls.Add(new Label { Text = "MHZ TimeSync", Font = new("Segoe UI", 23, FontStyle.Bold), AutoSize = true, ForeColor = teal, Margin = new(0, 4, 0, 0) }); header.Controls.Add(hide); header.Controls.Add(about); root.Controls.Add(header, 0, 0);
        var subtitle = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        subtitle.Controls.Add(new Label { Text = $"OFFLINE FT8 CLOCK SYNC   /   v{AboutForm.AppVersion}   /   Windows 10 & 11   |   Credit:", AutoSize = true });
        var credit = new LinkLabel { Text = "HS9XKG : https://mhz.band", AutoSize = true };
        credit.LinkClicked += (_, _) => { try { Process.Start(new ProcessStartInfo(AboutForm.WebsiteUrl) { UseShellExecute = true }); } catch (Exception ex) { Log($"Cannot open website: {ex.Message}"); } };
        subtitle.Controls.Add(credit); root.Controls.Add(subtitle, 0, 1);
        var trayMenu = new ContextMenuStrip(); trayMenu.Items.Add("Show MHZ TimeSync", null, (_, _) => RestoreWindow()); trayMenu.Items.Add("Exit", null, (_, _) => Close());
        tray.ContextMenuStrip = trayMenu; tray.DoubleClick += (_, _) => RestoreWindow(); hide.Click += (_, _) => HideToTray();
        about.Click += (_, _) => { using var dialog = new AboutForm(); dialog.ShowDialog(this); };
        mode.Controls.AddRange([autoMode, ntpMode, offlineMode]); autoMode.Checked = true;
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false }; controls.Controls.AddRange([mode, measure, correct]); root.Controls.Add(controls, 0, 2);
        offset.Font = readyFont; offset.ForeColor = teal; offset.Dock = DockStyle.Fill; root.Controls.Add(offset, 0, 3);
        var src = new FlowLayoutPanel { Dock = DockStyle.Fill }; source.AutoSize = true; grade.AutoSize = true; grade.Font = new(Font, FontStyle.Bold); src.Controls.AddRange([source, grade]); root.Controls.Add(src, 0, 4);
        stats.Dock = DockStyle.Fill; root.Controls.Add(stats, 0, 5);
        var state = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 }; wsjt.Dock = ntpStatus.Dock = DockStyle.Fill; state.Controls.Add(wsjt); state.Controls.Add(ntpStatus); root.Controls.Add(state, 0, 6);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false }; privilege.AutoSize = true; actions.Controls.AddRange([auto, privilege]); root.Controls.Add(actions, 0, 7);
        foreach (string c in new[] { "SNR", "DT (s)", "Mode", "Message", "Filter" }) grid.Columns.Add(c, c);
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.MultiSelect = false;
        stationMenu.Items.Add(syncStation); grid.ContextMenuStrip = stationMenu;
        grid.CellMouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            menuTarget = null; grid.ClearSelection();
            if (e.RowIndex < 0) return;
            grid.Rows[e.RowIndex].Selected = true;
            grid.CurrentCell = grid.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)];
            menuTarget = grid.Rows[e.RowIndex].Tag as StationReference;
        };
        stationMenu.Opening += (_, e) =>
        {
            var target = menuTarget ?? (grid.SelectedRows.Count == 1 ? grid.SelectedRows[0].Tag as StationReference : null);
            if (target is null) { e.Cancel = true; return; }
            // Capture the object, not the row index: new decodes insert at row zero.
            syncStation.Tag = target;
            syncStation.Enabled = CanSyncStation(target, out string reason);
            syncStation.ToolTipText = reason;
        };
        stationMenu.ShowItemToolTips = true;
        stationMenu.Closed += (_, _) => menuTarget = null;
        syncStation.Click += async (_, _) => { if (syncStation.Tag is StationReference target) await CorrectStation(target); };
        var selection = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        selection.RowStyles.Add(new(SizeType.Absolute, 34)); selection.RowStyles.Add(new(SizeType.Percent, 100));
        var stationControls = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        stationControls.Controls.AddRange([new Label { Text = "FT8 references:", AutoSize = true }, stationInput, applyStations, allStations]);
        selection.Controls.Add(stationControls, 0, 0); selection.Controls.Add(stationSummary, 0, 1); root.Controls.Add(selection, 0, 8);
        applyStations.Click += (_, _) => ApplyStationSelection();
        allStations.Click += (_, _) => { consensus.SelectStations(null); stationInput.Clear(); auto.Checked = false; ResetSamples(); Log("FT8 references: all stations. Fresh samples required."); RefreshView(); };
        grid.Columns[3].FillWeight = 260; grid.Columns[4].FillWeight = 240; root.Controls.Add(grid, 0, 9);
        var debug = new GroupBox { Text = "Log / debug (last 100 events)", Dock = DockStyle.Fill }; debug.Controls.Add(log); root.Controls.Add(debug, 0, 10);
        measure.Click += async (_, _) => await Probe();
        correct.Click += async (_, _) => await Correct(false);
        foreach (var option in new[] { autoMode, ntpMode, offlineMode })
            option.CheckedChanged += async (_, _) => { if (!option.Checked) return; auto.Checked = false; RefreshView(); if (SelectedMode != "Offline FT8") await Probe(); };
        auto.CheckedChanged += (_, _) =>
        {
            if (auto.Checked && !WindowsClock.IsAdmin) { auto.Checked = false; Log("Auto Correct requires launching this app as administrator. Manual correction uses a one-shot UAC helper."); }
            else Log(auto.Checked ? "Auto Correct enabled for this session; high confidence, RX-safe, 0.50–1.50s, 5-minute cooldown." : "Auto Correct disabled.");
        };
        Shown += async (_, _) => { FitToWorkingArea(); StartListener(); timer.Start(); RefreshView(); await Probe(); };
        timer.Tick += async (_, _) =>
        {
            DetectClockStep(); RefreshView();
            if (SelectedMode != "Offline FT8" && Environment.TickCount64 - lastProbe > 60000) await Probe();
            if (auto.Checked) await Correct(true);
        };
        FormClosing += (_, e) => { if (correcting || confirming) { e.Cancel = true; Log("Finish or cancel the clock correction before exiting."); } };
        FormClosed += (_, _) => { stationMenu.Dispose(); tray.Visible = false; tray.Dispose(); trayMenu.Dispose(); timer.Stop(); shutdown.Cancel(); listener?.Dispose(); timer.Dispose(); shutdown.Dispose(); };
        RefreshView();
    }
    private void FitToWorkingArea()
    {
        Rectangle work = Screen.FromControl(this).WorkingArea;
        int width = Math.Min(1040, Math.Max(760, work.Width - 24));
        int height = Math.Min(700, Math.Max(560, work.Height - 16));
        MinimumSize = new(Math.Min(820, width), Math.Min(600, height));
        Size = new(width, height);
        Location = new(work.Left + Math.Max(0, (work.Width - width) / 2), work.Top + Math.Max(0, (work.Height - height) / 2));
    }
    private void HideToTray() { tray.Visible = true; Hide(); }
    private void ResetSamples() { generation++; consensus.Clear(); grid.Rows.Clear(); }
    private bool CanSyncStation(StationReference target, out string reason)
    {
        if (!StationCorrection.TryGet(target, Environment.TickCount64, out _, out _, out reason)) return false;
        if (target.Generation != generation || target.Decode.Client != client) reason = "Receiver/band/clock context changed. Select a new decode.";
        else if (correcting || probing || Environment.TickCount64 < blockedUntil) reason = "Wait for the current measurement/correction to finish.";
        else if (status is null || status.Mode != "FT8" || Environment.TickCount64 - lastStatus >= 45000 || status.TxEnabled || status.Transmitting)
            reason = "Requires fresh FT8 RX status. Stop transmitting and turn off Enable Tx.";
        return reason.Length == 0;
    }
    private async Task CorrectStation(StationReference target)
    {
        if (confirming || correcting) return;
        DetectClockStep();
        if (!CanSyncStation(target, out string reason)) { Log(reason); return; }
        StationCorrection.TryGet(target, Environment.TickCount64, out string station, out double delta, out _);
        auto.Checked = false; confirming = true; RefreshView();
        try
        {
            string notice = $"Station: {station}\nMessage: {target.Decode.Message}\nDT: {target.Decode.Dt:+0.000;-0.000;0.000} s\nCorrection to add: {delta:+0.000;-0.000;0.000} s\n\nThis uses ONE selected decode, without multi-station consensus.\nEstimated Time Correction — not absolute UTC.\nThe station's clock and radio/audio delay may be wrong.\nAuto Correct is now OFF. Stop TX before continuing.\n\nApply this correction to the Windows clock?";
            if (MessageBox.Show(this, notice, "Sync with this Station — manual override", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            DetectClockStep();
            if (!CanSyncStation(target, out reason)) { Log($"Station sync cancelled: {reason}"); return; }
            await ApplyCorrection(delta, $"FT8 single station {station}; DT {target.Decode.Dt:+0.000;-0.000;0.000}s");
        }
        finally { confirming = false; RefreshView(); }
    }
    private void RestoreWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); tray.Visible = false; }
    private void ApplyStationSelection()
    {
        try
        {
            consensus.SelectStations(stationInput.Text); auto.Checked = false; ResetSamples();
            Log($"FT8 references: {consensus.Selection}. Fresh samples required; minimum 4 received stations."); RefreshView();
        }
        catch (ArgumentException ex) { Log(ex.Message); }
    }
    internal void CheckUiFeatures()
    {
        stationInput.Text = "hs9xkg, K1ABC, JA1ABC, W2XYZ"; applyStations.PerformClick();
        if (!consensus.Selection.Contains("HS9XKG")) throw new Exception("Selection UI failed");
        allStations.PerformClick(); if (consensus.Selection != "All stations") throw new Exception("Use all failed");
        HideToTray(); if (Visible || !tray.Visible) throw new Exception("Hide failed");
        RestoreWindow(); if (!Visible || tray.Visible) throw new Exception("Restore failed");
        CheckStationMenu();
        using (var about = new AboutForm())
        {
            if (!about.Text.Contains("เกี่ยวกับ") || !AboutForm.DiagnosticText.Contains($"v{AboutForm.AppVersion}")) throw new Exception("About/version UI failed");
            if (!Uri.TryCreate(AboutForm.WebsiteUrl, UriKind.Absolute, out _) || !Uri.TryCreate(AboutForm.FacebookUrl, UriKind.Absolute, out _)) throw new Exception("About contact links invalid");
        }
        Log("UI smoke passed: select/reset references and tray hide/restore. No clock changes.");
    }
    private void CheckStationMenu()
    {
        if (probing) throw new Exception("NTP still busy; retry UI smoke after network timeout.");
        long now = Environment.TickCount64;
        client = "UI-SMOKE"; status = new Status(client, 14074000, "FT8", false, false); lastStatus = lastPacket = now;
        var d = new Decode(client, true, 15000, -10, 1.2, 1500, "~", "CQ HS9XKG NK80", false, false);
        var target = new StationReference(d, now, generation);
        grid.Rows.Insert(0, d.Snr, d.Dt, d.Mode, d.Message, "UI TEST — no clock changes"); grid.Rows[0].Tag = target;
        menuTarget = target; stationMenu.Show(grid, new Point(140, 30));
        if (!syncStation.Enabled || !ReferenceEquals(syncStation.Tag, target)) throw new Exception("Station menu target/enable failed");
        grid.Rows.Insert(0, -12, -.3, "~", "CQ K1ABC FN42", "UI TEST");
        if (!ReferenceEquals(syncStation.Tag, target)) throw new Exception("New row changed captured station");
        stationMenu.Close(); status = status with { Transmitting = true };
        if (CanSyncStation(target, out _)) throw new Exception("TX interlock failed");
        status = status with { Transmitting = false }; ResetSamples();
        if (CanSyncStation(target, out _)) throw new Exception("Old context still allowed");
        client = null; status = null; lastPacket = lastStatus = -1000000;
        Log("Station menu smoke passed: enabled single decode, pinned target, TX/context interlocks. No clock changes.");
    }
    private void Log(string message)
    {
        if (IsDisposed) return;
        lines.Enqueue($"{DateTime.Now:HH:mm:ss}  {message}"); while (lines.Count > 100) lines.Dequeue();
        log.Text = string.Join(Environment.NewLine, lines); log.SelectionStart = log.TextLength; log.ScrollToCaret();
    }
    private void StartListener()
    {
        try
        {
            listener = new UdpClient(AddressFamily.InterNetwork); listener.Client.ExclusiveAddressUse = true;
            listener.Client.Bind(new IPEndPoint(IPAddress.Loopback, 2237));
            Log("Listening on 127.0.0.1:2237; no radio control commands are sent."); _ = Listen();
        }
        catch (SocketException ex) { bindError = "Port 2237 unavailable"; listener?.Dispose(); listener = null; Log($"{bindError}: {ex.Message}. Close another UDP consumer or use its forwarding feature; then restart MHZ TimeSync."); }
    }
    private async Task Listen()
    {
        while (!shutdown.IsCancellationRequested)
        {
            try
            {
                var incoming = await listener!.ReceiveAsync(shutdown.Token);
                if (correcting || Environment.TickCount64 < blockedUntil) continue;
                var p = WsjtProtocol.Parse(incoming.Buffer); long now = Environment.TickCount64;
                if (p.Type is not (0 or 1 or 2 or 3 or 6)) continue;
                if (client is null || now - lastPacket > 60000)
                { client = p.Id; ResetSamples(); status = null; lastStatus = -1000000; Log($"Selected WSJT-X instance: {client}"); }
                if (p.Id != client) continue; // Never mix two receivers.
                lastPacket = now;
                switch (p)
                {
                    case Status s:
                        if (status is not null && (status.DialHz != s.DialHz || status.Mode != s.Mode)) { ResetSamples(); Log("Frequency/mode changed: sample window cleared."); }
                        status = s; lastStatus = now; break;
                    case Decode d:
                        string verdict = consensus.Add(d, now);
                        grid.Rows.Insert(0, d.Snr, d.Dt.ToString("+0.000;-0.000;0.000"), d.Mode, d.Message, verdict);
                        grid.Rows[0].Tag = new StationReference(d, now, generation);
                        if (grid.Rows.Count > 50) grid.Rows.RemoveAt(grid.Rows.Count - 1); break;
                    default:
                        if (p.Type is 3 or 6) ResetSamples();
                        if (p.Type == 6) { client = null; status = null; lastPacket = -1000000; } break;
                }
                RefreshView();
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (FormatException ex) { Log($"Ignored UDP: {ex.Message}"); }
            catch (SocketException ex) { bindError = "UDP listener failed"; Log(ex.Message); break; }
        }
    }
    private async Task Probe()
    {
        if (probing || correcting || confirming || shutdown.IsCancellationRequested) return;
        probing = true; lastProbe = Environment.TickCount64; ntpStatus.Text = "NTP: measuring 3 samples with fallback…";
        try { ntp = await new NtpClient().QueryAsync(Log, shutdown.Token); Log($"NTP {ntp.Host}: correction {ntp.Correction:+0.000;-0.000;0.000}s, RTT {ntp.RoundTrip * 1000:F0}ms"); }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { ntp = null; Log(ex.Message); }
        finally { probing = false; probeCompleted = true; if (!IsDisposed) RefreshView(); }
    }
    private (double Delta, bool Ready, bool High, string Source) Choice()
    {
        long now = Environment.TickCount64;
        if (SelectedMode != "Offline FT8" && ntp is not null && now - ntp.MeasuredAt < 60000)
            return (ntp.Correction, true, ntp.RoundTrip <= .20, $"NTP · {ntp.Host}");
        if (SelectedMode == "NTP Thailand" || (SelectedMode == "AUTO" && (!probeCompleted || probing))) return (0, false, false, "NTP · waiting");
        var e = consensus.Get(now); return (e.Correction, e.Ready, e.Confidence == "HIGH", "FT8 · Estimated Time Correction (not absolute UTC)");
    }
    private bool ReceiveSafe()
    {
        long now = Environment.TickCount64;
        if (client is null || now - lastPacket > 60000) return SelectedMode != "Offline FT8" && Choice().Source.StartsWith("NTP");
        return status is not null && now - lastStatus < 45000 && !status.Transmitting && !status.TxEnabled;
    }
    private void RefreshView()
    {
        var e = consensus.Get(Environment.TickCount64); var c = Choice();
        offset.Text = c.Ready ? $"{c.Delta:+0.000;-0.000;0.000} s" : "—  awaiting reliable samples";
        offset.Font = c.Ready ? readyFont : waitingFont;
        source.Text = $"Correction to add to Windows clock  |  {c.Source}   "; grade.Text = c.Ready ? CorrectionPolicy.Grade(c.Delta) : "WAITING";
        stats.Text = $"Clock Offset (local − source): {(c.Ready ? (-c.Delta).ToString("+0.000;-0.000;0.000") + " s" : "—")}   |   Median DT: {(e.Count > 0 ? e.MedianDt.ToString("+0.000;-0.000;0.000") : "—")} s   |   Spread: {e.Spread:F3} s\r\nSamples: {e.Count}/30   Valid: {e.Valid}   Rejected (MAD): {e.Rejected}   Stations: {e.Stations}   Slots: {e.Slots}   FT8 confidence: {e.Confidence}\r\nFT8 is estimated relative alignment, including radio/audio delay. It cannot establish the UTC date or minute.";
        bool connected = Environment.TickCount64 - lastPacket < 60000;
        wsjt.Text = $"WSJT-X: {(bindError.Length > 0 ? bindError : connected ? $"{client} · connected" : "waiting on 127.0.0.1:2237")}   |   {(status is null ? "Frequency —" : $"{status.DialHz / 1e6:F6} MHz · {Band(status.DialHz)} · {status.Mode} · {(status.Transmitting ? "TRANSMITTING" : status.TxEnabled ? "TX ENABLED" : "RX")}")}";
        if (!probing) ntpStatus.Text = ntp is null ? "NTP: unavailable / not measured" : $"NTP: {ntp.Host} · RTT {ntp.RoundTrip * 1000:F0} ms · age {(Environment.TickCount64 - ntp.MeasuredAt) / 1000}s (expires at 60s)";
        privilege.Text = WindowsClock.IsAdmin ? "Administrator · Auto available" : "Standard user · manual correction requests UAC";
        correct.Enabled = c.Ready && ReceiveSafe() && !correcting && !confirming && !probing && Environment.TickCount64 >= blockedUntil;
        measure.Enabled = !probing && !correcting && !confirming; mode.Enabled = !correcting && !confirming;
        applyStations.Enabled = allStations.Enabled = stationInput.Enabled = auto.Enabled = !correcting && !confirming;
        stationSummary.Text = $"Active: {consensus.Selection} (consensus ≥4 stations / 20 samples)  |  Single station: right-click a decode.";
    }
    private async Task Correct(bool automatic)
    {
        if (correcting || confirming || probing || Environment.TickCount64 < blockedUntil) return;
        DetectClockStep(); var c = Choice();
        if (!c.Ready || !ReceiveSafe()) return;
        if (automatic && !CorrectionPolicy.AutoAllowed(c.Delta, c.Ready, c.High, ReceiveSafe(), Environment.TickCount64, lastCorrection)) return;
        if (!automatic)
        {
            var approved = c;
            string approvedReferences = consensus.Selection;
            string notice = $"Apply {c.Delta:+0.000;-0.000;0.000} seconds to the Windows clock?\nSource: {c.Source}\n{(c.Source.StartsWith("FT8") ? "References: " + approvedReferences + "\n" : "")}{CorrectionPolicy.Grade(c.Delta)}\n\nStop transmission and turn off Enable Tx first. Other applications use this system clock.";
            DialogResult answer;
            confirming = true; RefreshView();
            try { answer = MessageBox.Show(this, notice, "Confirm clock correction", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning); }
            finally { confirming = false; RefreshView(); }
            if (answer != DialogResult.OK) return;
            DetectClockStep(); c = Choice(); if (!c.Ready || !ReceiveSafe() || c.Source != approved.Source || (c.Source.StartsWith("FT8") && approvedReferences != consensus.Selection) || Math.Abs(c.Delta - approved.Delta) > .05) { Log("Correction cancelled: measurement/status expired or changed. Review the new value and retry."); return; }
        }
        await ApplyCorrection(c.Delta, c.Source);
    }
    private async Task ApplyCorrection(double delta, string selectedSource)
    {
        correcting = true; RefreshView();
        try
        {
            await WindowsClock.CorrectAsync(delta); lastCorrection = Environment.TickCount64;
            Log($"Clock corrected by {delta:+0.000;-0.000;0.000}s ({selectedSource}). Waiting for fresh measurements.");
            InvalidateMeasurements(); blockedUntil = Environment.TickCount64 + 15000;
        }
        catch (Exception ex) { auto.Checked = false; Log($"Clock unchanged or correction failed: {ex.Message}"); }
        finally { correcting = false; previousTick = Environment.TickCount64; previousUtc = DateTime.UtcNow; RefreshView(); }
    }
    private void InvalidateMeasurements()
    { ResetSamples(); ntp = null; probeCompleted = false; lastProbe = -1000000; }
    private void DetectClockStep()
    {
        long now = Environment.TickCount64; DateTime utc = DateTime.UtcNow;
        if (!correcting && Math.Abs((utc - previousUtc).TotalMilliseconds - (now - previousTick)) > 250)
        { InvalidateMeasurements(); Log("External clock step / resume detected; discarded prior measurements."); }
        previousTick = now; previousUtc = utc;
    }
    private static string Band(ulong hz) => hz switch
    { >= 1800000 and <= 2000000 => "160m", >= 3500000 and <= 4000000 => "80m", >= 7000000 and <= 7300000 => "40m", >= 10100000 and <= 10150000 => "30m", >= 14000000 and <= 14350000 => "20m", >= 18068000 and <= 18168000 => "17m", >= 21000000 and <= 21450000 => "15m", >= 24890000 and <= 24990000 => "12m", >= 28000000 and <= 29700000 => "10m", >= 50000000 and <= 54000000 => "6m", >= 144000000 and <= 148000000 => "2m", _ => "other band" };
}
