namespace Mhz.TimeSync.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--apply") return WindowsClock.RunHelper(args);
        ApplicationConfiguration.Initialize();
        if (args.Length == 2 && args[0] == "--about-screenshot")
        {
            using var about = new AboutForm();
            about.Shown += async (_, _) =>
            {
                await Task.Delay(750);
                using var bmp = new Bitmap(about.Width, about.Height);
                about.DrawToBitmap(bmp, about.ClientRectangle with { Width = about.Width, Height = about.Height });
                bmp.Save(args[1]); about.Close();
            };
            Application.Run(about); return 0;
        }
        using var form = new MainForm();
        if (args.Length == 2 && args[0] is "--screenshot" or "--ui-smoke")
        {
            form.Shown += async (_, _) =>
            {
                await Task.Delay(5000);
                if (args[0] == "--ui-smoke") form.CheckUiFeatures();
                using var bmp = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bmp, form.ClientRectangle with { Width = form.Width, Height = form.Height });
                bmp.Save(args[1]); form.Close();
            };
        }
        Application.Run(form); return 0;
    }
}
