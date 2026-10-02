using System.Reflection;

namespace Mhz.TimeSync.App;

internal static class BrandAssets
{
    private const string IconResource = "Mhz.TimeSync.App.Assets.MHZ-TimeSync.ico";
    private const string LogoResource = "Mhz.TimeSync.App.Assets.MHZ-TimeSync-Logo.png";

    public static Icon AppIcon { get; } = LoadIcon();
    public static Image Logo { get; } = LoadImage();

    private static Icon LoadIcon()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(IconResource)
            ?? throw new InvalidOperationException($"Missing resource: {IconResource}");
        return new Icon(stream);
    }

    private static Image LoadImage()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(LogoResource)
            ?? throw new InvalidOperationException($"Missing resource: {LogoResource}");
        using Image source = Image.FromStream(stream);
        return new Bitmap(source);
    }
}
