using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Rostok.Controls;

public enum IconKind
{
    Plus, Trash, Pencil, Download, Upload, Database, Help, Print, Copy, Check, Arrow, ArrowBack, Table, Calendar,
    ChevronLeft, ChevronRight, Gear, Logout, Folder, Lock, User, Refresh, Plug, Close, Minus, Key, Building, Eye,
}

// Иконки библиотеки: контур 24×24, штрих 1,7, скруглённые концы — те же пути, что в веб-версии (ui/Icons.jsx).
public class Icon : FrameworkElement
{
    private static readonly Dictionary<IconKind, string> Paths = new()
    {
        [IconKind.Plus] = "M12 5v14M5 12h14",
        [IconKind.Trash] = "M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3",
        [IconKind.Pencil] = "M4 20h4L19 9l-4-4L4 16v4zM13.5 6.5l4 4",
        [IconKind.Download] = "M12 4v11M7 11l5 5 5-5M5 20h14",
        [IconKind.Upload] = "M12 16V5M7 9l5-5 5 5M5 20h14",
        // эллипс cx12 cy6 rx7 ry3 + два «цилиндра»
        [IconKind.Database] = "M5 6 A7 3 0 1 0 19 6 A7 3 0 1 0 5 6 Z M5 6v6c0 1.7 3.1 3 7 3s7-1.3 7-3V6M5 12v6c0 1.7 3.1 3 7 3s7-1.3 7-3v-6",
        [IconKind.Help] = "M3 12 A9 9 0 1 0 21 12 A9 9 0 1 0 3 12 Z M9.5 9.5 A2.5 2.5 0 1 1 13 11.8 C12.3 12.2 12 12.8 12 13.5 M12 17h.01",
        [IconKind.Print] = "M7 9V4h10v5M7 17H5a1 1 0 0 1-1-1v-6a1 1 0 0 1 1-1h14a1 1 0 0 1 1 1v6a1 1 0 0 1-1 1h-2M7 14h10v6H7z",
        [IconKind.Copy] = "M9 8h10a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1H9a1 1 0 0 1-1-1V9a1 1 0 0 1 1-1z M16 8V5a1 1 0 0 0-1-1H5a1 1 0 0 0-1 1v10a1 1 0 0 0 1 1h3",
        [IconKind.Check] = "M5 12.5l4.5 4.5L19 7.5",
        [IconKind.Arrow] = "M5 12h14M13 6l6 6-6 6",
        [IconKind.ArrowBack] = "M19 12H5M11 6l-6 6 6 6",
        [IconKind.Table] = "M5 5h14a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1z M4 10h16M4 14.5h16M10 5v14",
        [IconKind.Calendar] = "M5 5h14a1 1 0 0 1 1 1v13a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1z M4 10h16M9 3v4M15 3v4",
        [IconKind.ChevronLeft] = "M15 5l-7 7 7 7",
        [IconKind.ChevronRight] = "M9 5l7 7-7 7",
        // шестерёнка администрирования: восемь зубцов и ось
        [IconKind.Gear] = "M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z M9 12 A3 3 0 1 0 15 12 A3 3 0 1 0 9 12 Z",
        [IconKind.Logout] = "M10 5H6a1 1 0 0 0-1 1v12a1 1 0 0 0 1 1h4M14 8l4 4-4 4M18 12H9",
        [IconKind.Folder] = "M4 7a1 1 0 0 1 1-1h4l2 2h8a1 1 0 0 1 1 1v9a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1z",
        [IconKind.Lock] = "M7 11h10a1 1 0 0 1 1 1v7a1 1 0 0 1-1 1H7a1 1 0 0 1-1-1v-7a1 1 0 0 1 1-1z M8.5 11V8a3.5 3.5 0 0 1 7 0v3",
        [IconKind.User] = "M8 8 A4 4 0 1 0 16 8 A4 4 0 1 0 8 8 Z M5 20c.8-3.5 3.6-5 7-5s6.2 1.5 7 5",
        [IconKind.Refresh] = "M19 8a8 8 0 0 0-14 1M5 4v5h5M5 16a8 8 0 0 0 14-1M19 20v-5h-5",
        [IconKind.Plug] = "M9 3v5M15 3v5M7 8h10v3a5 5 0 0 1-10 0zM12 16v5",
        // «Организация»: пространства всех сотрудников
        [IconKind.Building] = "M4 20V9l8-5 8 5v11M4 20h16M9 20v-5h6v5M8 11h.01M12 11h.01M16 11h.01",
        [IconKind.Eye] = "M2 12c2.5-4.5 6-7 10-7s7.5 2.5 10 7c-2.5 4.5-6 7-10 7s-7.5-2.5-10-7z M9 12 A3 3 0 1 0 15 12 A3 3 0 1 0 9 12 Z",
        [IconKind.Close] = "M6 6l12 12M18 6L6 18",
        [IconKind.Minus] = "M5 12h14",
        [IconKind.Key] = "M4 15.5 A3.5 3.5 0 1 0 11 15.5 A3.5 3.5 0 1 0 4 15.5 Z M10 13l8-8M15 8l2.5 2.5M17.5 5.5l2 2",
    };

    private static readonly Dictionary<IconKind, Geometry> Cache = [];

    public static Geometry GeometryOf(IconKind kind)
    {
        if (!Cache.TryGetValue(kind, out var g))
        {
            g = Geometry.Parse(Paths[kind]);
            g.Freeze();
            Cache[kind] = g;
        }
        return g;
    }

    public static IEnumerable<IconKind> AllKinds => Paths.Keys;

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(IconKind), typeof(Icon),
        new FrameworkPropertyMetadata(IconKind.Plus, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(nameof(Size), typeof(double), typeof(Icon),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeWidthProperty = DependencyProperty.Register(nameof(StrokeWidth), typeof(double), typeof(Icon),
        new FrameworkPropertyMetadata(1.7, FrameworkPropertyMetadataOptions.AffectsRender));
    // цвет — от текста родителя, как currentColor в SVG
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(Icon),
        new FrameworkPropertyMetadata(Theme.Ink2, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public IconKind Kind { get => (IconKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public double StrokeWidth { get => (double)GetValue(StrokeWidthProperty); set => SetValue(StrokeWidthProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    public Icon() { }

    public Icon(IconKind kind, double size = 16)
    {
        Kind = kind;
        Size = size;
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    protected override void OnRender(DrawingContext dc)
    {
        var scale = Size / 24.0;
        var pen = new Pen(Foreground, StrokeWidth) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.DrawGeometry(null, pen, GeometryOf(Kind));
        dc.Pop();
    }
}
