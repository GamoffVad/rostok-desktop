using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Rostok.Controls;

// Снимок элемента в PNG — для проверки дизайна и документации (режимы --shots).
public static class Snapshot
{
    public static void Save(FrameworkElement el, string file)
    {
        var rtb = new RenderTargetBitmap((int)Math.Ceiling(el.ActualWidth), (int)Math.Ceiling(el.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        rtb.Render(el);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(file);
        enc.Save(fs);
    }
}
