using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VoiceTyper.App.Branding;

internal static class AppIcon
{
    public static ImageSource? Load()
        => LoadFile("icon.ico");

    public static ImageSource? LoadBrandImage()
        => LoadFile("icon.png");

    private static ImageSource? LoadFile(string fileName)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "resources", fileName);
            if (!File.Exists(path)) return null;
            var image = BitmapFrame.Create(new Uri(path, UriKind.Absolute), BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnLoad);
            image.Freeze();
            return image;
        }
        catch { return null; }
    }
}
