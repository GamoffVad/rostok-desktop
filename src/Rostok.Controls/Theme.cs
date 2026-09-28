using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace Rostok.Controls;

// Токены дизайн-системы «Тёплый мел», схема «Ежевика» — для кода. Те же значения, что в Themes/Palette.xaml.
public static class Theme
{
    private static SolidColorBrush B(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    public static readonly SolidColorBrush Paper = B("#F8F4EB");
    public static readonly SolidColorBrush Sheet = B("#FFFDF8");
    public static readonly SolidColorBrush Ink = B("#2A2230");
    public static readonly SolidColorBrush Ink2 = B("#463E4D");
    public static readonly SolidColorBrush Ink3 = B("#655C6C");
    public static readonly SolidColorBrush Faint = B("#6B6272");
    public static readonly SolidColorBrush Dash = B("#CFC8BC");
    public static readonly SolidColorBrush Line = B("#E0DAD0");
    public static readonly SolidColorBrush Accent = B("#5A2B63");
    public static readonly SolidColorBrush AccentLight = B("#7A4A89");
    public static readonly SolidColorBrush AccentHover = B("#431F4A");
    public static readonly SolidColorBrush Soft = B("#F0EADF");
    public static readonly SolidColorBrush Soft2 = B("#E9E2D6");
    public static readonly SolidColorBrush FormBg = B("#F2EDE3");
    public static readonly SolidColorBrush Amber = B("#7A5312");
    public static readonly SolidColorBrush Danger = B("#A32D22");
    public static readonly SolidColorBrush Ok = B("#2C6B45");
    public static readonly SolidColorBrush Transparent = B("#00000000");

    // Уровни 0–3: цифра на смысловой подложке — от нормы к выраженному несоответствию.
    public static readonly SolidColorBrush[] LevelInk = [B("#2C6B45"), B("#746410"), B("#9A5413"), B("#A32D22")];
    public static readonly SolidColorBrush[] LevelBg = [B("#DDE9DF"), B("#EFE8CF"), B("#F3E0CC"), B("#F3DEDB")];

    // Графики: уровни 0–3 и два среза (проверено на различимость при дальтонизме).
    public static readonly SolidColorBrush[] ChartLevel = [B("#3A9466"), B("#D1AE1E"), B("#D4602A"), B("#8A2346")];
    public static readonly SolidColorBrush SeriesStart = B("#B8892F");
    public static readonly SolidColorBrush SeriesEnd = B("#6B3A78");

    // Итоги коррекционной работы: зелёный, ежевика, синий, жёлтый, красный.
    public static SolidColorBrush OutcomeColor(string id) => id switch
    {
        "norm" => B("#3A9466"),
        "major" => B("#6B3A78"),
        "minor" => B("#3F7FB5"),
        "none" => B("#D1AE1E"),
        _ => B("#B23A48"),
    };

    public static readonly FontFamily UiFont = new(new Uri("pack://application:,,,/"), "/Rostok.Controls;component/Fonts/#Golos Text");
    public static readonly FontFamily MonoFont = new(new Uri("pack://application:,,,/"), "/Rostok.Controls;component/Fonts/#IBM Plex Mono");

    public const double Radius = 2;

    private static bool _initialized;

    // Подключается один раз при запуске приложения: подсказки библиотеки вместо системных — с задержкой 300 мс, над элементом.
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        ToolTipService.InitialShowDelayProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(300));
        ToolTipService.BetweenShowDelayProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(100));
        ToolTipService.PlacementProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(PlacementMode.Top));
        ToolTipService.ShowDurationProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(20000));
        TextElement.FontFamilyProperty.OverrideMetadata(typeof(TextElement), new FrameworkPropertyMetadata(UiFont));
        TextBlock.FontFamilyProperty.OverrideMetadata(typeof(TextBlock), new FrameworkPropertyMetadata(UiFont));
    }

    public static Pen DashPen(Brush? brush = null, double thickness = 1)
    {
        var p = new Pen(brush ?? Dash, thickness) { DashStyle = new DashStyle([3, 3], 0), DashCap = PenLineCap.Flat };
        p.Freeze();
        return p;
    }

    public static Pen SolidPen(Brush brush, double thickness = 1)
    {
        var p = new Pen(brush, thickness);
        p.Freeze();
        return p;
    }
}
