using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Mhz.TimeSync.App;

internal sealed class AboutForm : Form
{
    internal const string WebsiteUrl = "https://mhz.band";
    internal const string FacebookUrl = "https://www.facebook.com/mhzbandradio";
    internal static string AppVersion =>
        typeof(AboutForm).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(AboutForm).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    internal static string DiagnosticText =>
        $"MHZ TimeSync v{AppVersion}\r\n" +
        $"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})\r\n" +
        $"Process: {RuntimeInformation.ProcessArchitecture}\r\n" +
        $"Runtime: {RuntimeInformation.FrameworkDescription}\r\n" +
        "WSJT-X UDP: 127.0.0.1:2237";

    public AboutForm()
    {
        Text = "About / เกี่ยวกับโปรแกรม";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new(780, 620);
        Font = new("Segoe UI", 10);
        BackColor = Color.White;
        Icon = BrandAssets.AppIcon;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(24), ColumnCount = 1, RowCount = 8 };
        root.RowStyles.Add(new(SizeType.Absolute, 58));
        root.RowStyles.Add(new(SizeType.Absolute, 42));
        root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.Absolute, 46));
        root.RowStyles.Add(new(SizeType.Absolute, 46));
        root.RowStyles.Add(new(SizeType.Absolute, 78));
        root.RowStyles.Add(new(SizeType.Absolute, 58));
        root.RowStyles.Add(new(SizeType.Absolute, 48));
        Controls.Add(root);

        var brand = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        brand.Controls.Add(new PictureBox
        {
            Image = BrandAssets.Logo,
            Size = new(52, 52),
            SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new(0, 0, 10, 0)
        });
        brand.Controls.Add(new Label
        {
            Text = "MHZ TimeSync",
            Font = new("Segoe UI", 25, FontStyle.Bold),
            ForeColor = Color.FromArgb(0, 112, 112),
            AutoSize = true,
            Margin = new(0, 5, 0, 0)
        });
        root.Controls.Add(brand, 0, 0);
        root.Controls.Add(new Label
        {
            Text = $"Offline FT8 Clock Sync   •   Version {AppVersion}",
            Font = new("Segoe UI", 12, FontStyle.Bold),
            AutoSize = true
        }, 0, 1);

        var languages = new TabControl { Dock = DockStyle.Fill };
        var thai = new TabPage("ภาษาไทย") { Padding = new(12), BackColor = Color.White };
        thai.Controls.Add(ReadOnlyText(
            "MHZ TimeSync เป็นเครื่องมือสำหรับนักวิทยุสมัครเล่น ใช้ตรวจวัดและปรับเวลา Windows จาก NTP ประเทศไทย หรือประเมินค่าชดเชยเวลาจาก DT ของสัญญาณ FT8 ที่ WSJT-X รับได้ รองรับ consensus หลายสถานีและการเลือก Sync with this Station แบบ manual\r\n\r\n" +
            "ค่า FT8 เป็น Estimated Time Correction ไม่ใช่ absolute UTC และรวมความคลาดเคลื่อนของนาฬิกาสถานีส่ง propagation รวมถึง radio/audio latency ผู้ใช้ควรตรวจสอบแหล่งอ้างอิงก่อนปรับเวลา"));
        var english = new TabPage("English") { Padding = new(12), BackColor = Color.White };
        english.Controls.Add(ReadOnlyText(
            "MHZ TimeSync is a utility for amateur-radio operators. It measures and corrects the Windows clock using Thailand NTP servers, or estimates a correction from FT8 DT values received through WSJT-X. It supports multi-station consensus and an explicit manual Sync with this Station action.\r\n\r\n" +
            "FT8 provides an Estimated Time Correction, not absolute UTC. It includes the transmitting station's clock error, propagation, and radio/audio latency. Verify the selected reference before changing the system clock."));
        languages.TabPages.AddRange([thai, english]);
        root.Controls.Add(languages, 0, 2);

        var author = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        author.Controls.Add(new Label { Text = "Developed by / ผู้พัฒนา: HS9XKG   •   ", AutoSize = true, Margin = new(0, 8, 0, 0) });
        author.Controls.Add(Link("Website: mhz.band", WebsiteUrl));
        root.Controls.Add(author, 0, 3);

        var contact = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        contact.Controls.Add(new Label { Text = "Contact / ติดต่อ: ", AutoSize = true, Margin = new(0, 8, 0, 0) });
        contact.Controls.Add(Link("Facebook — MHZ Band Radio", FacebookUrl));
        root.Controls.Add(contact, 0, 4);

        root.Controls.Add(new Label
        {
            Text = "Privacy / ความเป็นส่วนตัว: ไม่มี telemetry; log และ decode อยู่ในหน่วยความจำและไม่บันทึกลงดิสก์\r\nNo telemetry; logs and decoded messages remain in memory and are not written to disk.",
            Dock = DockStyle.Fill
        }, 0, 5);

        root.Controls.Add(new Label
        {
            Text = $"© 2026 HS9XKG. All rights reserved.\r\nBuilt with .NET • {RuntimeInformation.ProcessArchitecture} • Windows 10/11",
            Dock = DockStyle.Fill,
            ForeColor = Color.DimGray
        }, 0, 6);

        var buttons = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Right, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        var close = new Button { Text = "Close / ปิด", AutoSize = true, DialogResult = DialogResult.OK };
        var copy = new Button { Text = "Copy system info / คัดลอกข้อมูลระบบ", AutoSize = true };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(DiagnosticText); copy.Text = "Copied / คัดลอกแล้ว"; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Clipboard", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        buttons.Controls.Add(copy); buttons.Controls.Add(close); root.Controls.Add(buttons, 0, 7);
        AcceptButton = close; CancelButton = close;
    }

    private LinkLabel Link(string text, string url)
    {
        var link = new LinkLabel { Text = text, AutoSize = true, Margin = new(0, 8, 0, 0), Tag = url };
        link.LinkClicked += (_, _) => OpenUrl(url);
        return link;
    }

    private static RichTextBox ReadOnlyText(string text) => new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        BorderStyle = BorderStyle.None,
        BackColor = Color.White,
        TabStop = false,
        WordWrap = true,
        ScrollBars = RichTextBoxScrollBars.Vertical,
        Text = text
    };

    private void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Open link", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
}
