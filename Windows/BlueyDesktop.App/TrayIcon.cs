using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using H.NotifyIcon;

namespace BlueyDesktop.App;

/// <summary>
/// Bluey's icon in the notification area. Left click or "Show/Hide" toggles the companion window;
/// "Quit" ends the app. Dispose it on exit so the icon does not linger until the mouse passes over it.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const int IconSize = 64;  // px; Windows scales it down for the tray
    private static readonly Color BerryLight = Color.FromRgb(0xA9, 0xBC, 0xFF);
    private static readonly Color BerryMid = Color.FromRgb(0x6C, 0x86, 0xF5);
    private static readonly Color BerryDeep = Color.FromRgb(0x2B, 0x2F, 0x8F);
    private static readonly Color Ink = Color.FromRgb(0x17, 0x15, 0x1F);

    private readonly Window _window;
    private readonly TaskbarIcon _icon;
    private readonly System.Drawing.Icon _iconImage;

    public TrayIcon(Window window, bool hasOpenAiKey)
    {
        _window = window;

        var menu = new ContextMenu();
        menu.Items.Add(MenuItemFor("Show/Hide", (_, _) => ToggleWindow()));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItemFor("Quit", (_, _) => Application.Current.Shutdown()));

        // Icon, not IconSource: IconSource throws (later, on the dispatcher) for a RenderTargetBitmap.
        _iconImage = DrawIcon();
        _icon = new TaskbarIcon
        {
            ToolTipText = hasOpenAiKey ? "Bluey" : "Bluey (no OpenAI key configured)",
            Icon = _iconImage,
            ContextMenu = menu,
        };
        _icon.TrayLeftMouseUp += (_, _) => ToggleWindow();

        // The icon is not in a visual tree, so it has to be created by hand. Efficiency mode off:
        // it would throttle the whole process, and Bluey animates and listens for hotkeys.
        _icon.ForceCreate(enablesEfficiencyMode: false);
    }

    public void Dispose()
    {
        _icon.Dispose();
        _iconImage.Dispose();
    }

    private void ToggleWindow()
    {
        if (_window.IsVisible)
        {
            _window.Hide();
        }
        else
        {
            _window.Show();
        }
    }

    private static MenuItem MenuItemFor(string header, RoutedEventHandler onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += onClick;
        return item;
    }

    /// <summary>A small blueberry with two eyes, drawn in code so the app needs no icon file.</summary>
    private static System.Drawing.Icon DrawIcon()
    {
        var size = (double)IconSize;
        var body = new RadialGradientBrush(BerryLight, BerryDeep) { GradientOrigin = new Point(0.35, 0.3) };
        body.GradientStops.Insert(1, new GradientStop(BerryMid, 0.45));

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawEllipse(body, null, new Point(size / 2, size / 2), size * 0.46, size * 0.46);
            foreach (var x in new[] { 0.36, 0.64 })
            {
                var eye = new Point(size * x, size * 0.46);
                dc.DrawEllipse(Brushes.White, null, eye, size * 0.12, size * 0.14);
                dc.DrawEllipse(new SolidColorBrush(Ink), null, new Point(eye.X, eye.Y + size * 0.02), size * 0.06, size * 0.07);
            }
        }

        var bitmap = new RenderTargetBitmap(IconSize, IconSize, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var png = new MemoryStream();
        encoder.Save(png);
        return new System.Drawing.Icon(new MemoryStream(WrapPngAsIco(png.ToArray())));
    }

    /// <summary>An .ico file holding one PNG image: a 6-byte header, one 16-byte directory entry, then the PNG.</summary>
    private static byte[] WrapPngAsIco(byte[] png)
    {
        const int HeaderSize = 6;
        const int EntrySize = 16;
        using var ico = new MemoryStream();
        using var writer = new BinaryWriter(ico);
        writer.Write((ushort)0);          // reserved
        writer.Write((ushort)1);          // type: icon
        writer.Write((ushort)1);          // one image
        writer.Write((byte)IconSize);     // width (0 would mean 256)
        writer.Write((byte)IconSize);     // height
        writer.Write((byte)0);            // palette size: none
        writer.Write((byte)0);            // reserved
        writer.Write((ushort)1);          // colour planes
        writer.Write((ushort)32);         // bits per pixel
        writer.Write(png.Length);         // image size
        writer.Write(HeaderSize + EntrySize);  // image offset
        writer.Write(png);
        writer.Flush();
        return ico.ToArray();
    }
}
