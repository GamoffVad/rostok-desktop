using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Rostok.Core;

namespace Rostok.Controls;

public sealed record RadarAxis(string Label, double? Start, double? End, string? Full = null);
public sealed record TrendPoint(string Label, double? Value);
public sealed record DonutSegment(string Label, int Value, Brush Color);

// Основа графиков: рисование «вручную» (как SVG в веб-версии) — одна ось 0–3, тонкие штрихи, подписи чернилами,
// цвет — только у меток; подсказка при наведении и легенда под графиком.
public abstract class ChartBase : Grid
{
    private sealed class Surface(ChartBase owner) : FrameworkElement
    {
        protected override Size MeasureOverride(Size available)
        {
            var w = double.IsInfinity(available.Width) ? 520 : Math.Max(280, available.Width);
            return new Size(w, owner.HeightFor(w));
        }

        protected override void OnRender(DrawingContext dc)
        {
            owner._hits.Clear();
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
            owner.Draw(dc, ActualWidth);
        }
    }

    private readonly Surface _surface;
    private readonly Canvas _overlay = new() { IsHitTestVisible = false, ClipToBounds = false };
    private readonly Border _tip;
    private readonly TextBlock _tipText = new() { Foreground = Theme.Sheet, FontSize = 12, LineHeight = 17 };
    private readonly List<(Geometry Hit, Func<IEnumerable<Inline>> Tip)> _hits = [];

    protected ChartBase()
    {
        _surface = new Surface(this) { Cursor = Cursors.Arrow };
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Children.Add(_surface);
        _tip = new Border { Background = Theme.Ink, CornerRadius = new CornerRadius(2), Padding = new Thickness(9, 6, 9, 6), Child = _tipText, Visibility = Visibility.Collapsed };
        _overlay.Children.Add(_tip);
        Children.Add(_overlay);
        _surface.MouseMove += (_, e) => ShowTip(e.GetPosition(_surface));
        _surface.MouseLeftButtonDown += (_, e) => ShowTip(e.GetPosition(_surface));
        _surface.MouseLeave += (_, _) => _tip.Visibility = Visibility.Collapsed;
    }

    protected void SetLegend(UIElement? legend)
    {
        if (legend is null) return;
        SetRow(legend, 1);
        Children.Add(legend);
    }

    protected abstract double HeightFor(double width);
    protected abstract void Draw(DrawingContext dc, double width);

    protected void Hit(Geometry g, Func<IEnumerable<Inline>> tip) => _hits.Add((g, tip));

    private void ShowTip(Point p)
    {
        for (var i = _hits.Count - 1; i >= 0; i--)
        {
            if (!_hits[i].Hit.FillContains(p)) continue;
            _tipText.Inlines.Clear();
            _tipText.Inlines.AddRange(_hits[i].Tip());
            _tip.Visibility = Visibility.Visible;
            _tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var w = _surface.ActualWidth;
            var x = Math.Min(Math.Max(p.X, 70), w - 70);
            Canvas.SetLeft(_tip, x - _tip.DesiredSize.Width / 2);
            Canvas.SetTop(_tip, p.Y - _tip.DesiredSize.Height - 10);
            return;
        }
        _tip.Visibility = Visibility.Collapsed;
    }

    // ── рисование ──
    protected static readonly Pen GridPen = Theme.SolidPen(Theme.Line);
    protected static readonly Pen GridDash = Theme.DashPen(Theme.Dash);
    protected static readonly Pen PaperRing = Theme.SolidPen(Theme.Paper, 2);

    protected enum Anchor { Start, Middle, End }

    protected FormattedText Ft(string text, double size = 11, Brush? brush = null, bool mono = false, bool bold = false) =>
        new(text, CultureInfo.GetCultureInfo("ru-RU"), FlowDirection.LeftToRight,
            new Typeface(mono ? Theme.MonoFont : Theme.UiFont, FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, brush ?? Theme.Ink3, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    // y — базовая линия (как у SVG text) или середина строки (middle = true, dominant-baseline: middle)
    protected void Text(DrawingContext dc, string text, double x, double y, Anchor anchor = Anchor.Start, bool middle = false,
        double size = 11, Brush? brush = null, bool mono = false, bool bold = false)
    {
        var ft = Ft(text, size, brush, mono, bold);
        var left = anchor switch { Anchor.Middle => x - ft.Width / 2, Anchor.End => x - ft.Width, _ => x };
        var top = middle ? y - ft.Height / 2 : y - ft.Baseline;
        dc.DrawText(ft, new Point(left, top));
    }

    protected void Axis(DrawingContext dc, string text, double x, double y, Anchor a = Anchor.Start) => Text(dc, text, x, y, a, size: 10, brush: Theme.Faint, mono: true);
    protected void Val(DrawingContext dc, string text, double x, double y, Anchor a = Anchor.Middle, Brush? brush = null, bool middle = false) =>
        Text(dc, text, x, y, a, middle, 11, brush ?? Theme.Ink, mono: true, bold: true);

    protected static void Dot(DrawingContext dc, Point c, Brush fill, double r = 5) => dc.DrawEllipse(fill, PaperRing, c, r, r);

    protected static Run B(string s) => new(s) { FontFamily = Theme.MonoFont, FontWeight = FontWeights.SemiBold };
    protected static Run T(string s) => new(s);
    protected static LineBreak Br() => new();

    protected static IEnumerable<(string, Brush)> Series(string a, string b) => [(a, Theme.SeriesStart), (b, Theme.SeriesEnd)];
}

// ── Радар: профиль по разделам, два среза. Чем ближе к центру, тем ближе к норме.
public class Radar : ChartBase
{
    private readonly List<RadarAxis> _axes;
    private readonly string _start, _end;
    private const double VW = 440, VH = 320, R = 100;

    public Radar(IEnumerable<RadarAxis> axes, string startLabel, string endLabel)
    {
        _axes = axes.ToList();
        _start = startLabel;
        _end = endLabel;
        SetLegend(Legends.Chart(Series(startLabel, endLabel)).With(f => f.HorizontalAlignment = HorizontalAlignment.Center));
    }

    protected override double HeightFor(double width) => Math.Min(width, 460) * VH / VW;

    protected override void Draw(DrawingContext dc, double width)
    {
        var w = Math.Min(width, 460);
        var s = w / VW;
        var ox = (width - w) / 2;
        var cx = VW / 2;
        var c = VH / 2;
        var n = _axes.Count;
        Point Pt(int i, double v)
        {
            var a = Math.PI * 2 * i / n - Math.PI / 2;
            var r = R * v / 3;
            return new Point(ox + (cx + r * Math.Cos(a)) * s, (c + r * Math.Sin(a)) * s);
        }
        StreamGeometry Poly(Func<int, double> v)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(Pt(0, v(0)), true, true);
                for (var i = 1; i < n; i++) ctx.LineTo(Pt(i, v(i)), true, false);
            }
            g.Freeze();
            return g;
        }
        foreach (var v in new[] { 1, 2, 3 }) dc.DrawGeometry(null, v == 3 ? GridPen : GridDash, Poly(_ => v));
        var center = new Point(ox + cx * s, c * s);
        for (var i = 0; i < n; i++)
        {
            dc.DrawLine(GridPen, center, Pt(i, 3));
            var l = Pt(i, 3.45);
            var anchor = Math.Abs(l.X - center.X) < 12 * s ? Anchor.Middle : l.X > center.X ? Anchor.Start : Anchor.End;
            Text(dc, _axes[i].Label, l.X, l.Y, anchor, middle: true);
        }
        foreach (var v in new[] { 1, 2, 3 }) Axis(dc, v.ToString(), center.X + 4 * s, (c - R * v / 3 + 3) * s);

        void Series(Func<RadarAxis, double?> key, Brush color, double opacity)
        {
            if (!_axes.Any(a => key(a) is not null)) return;
            var fill = color.Clone();
            fill.Opacity = opacity;
            var pen = new Pen(color, 2) { LineJoin = PenLineJoin.Round };
            dc.DrawGeometry(fill, pen, Poly(i => key(_axes[i]) ?? 0));
        }
        Series(a => a.Start, Theme.SeriesStart, 0.14);
        Series(a => a.End, Theme.SeriesEnd, 0.16);
        for (var i = 0; i < n; i++)
        {
            var ax = _axes[i];
            if (ax.Start is { } st) Dot(dc, Pt(i, st), Theme.SeriesStart, 4);
            if (ax.End is { } en) Dot(dc, Pt(i, en), Theme.SeriesEnd, 4);
            var hitC = Pt(i, 1.8);
            var idx = i;
            Hit(new EllipseGeometry(hitC, 34 * s, 34 * s), () => [T(ax.Full ?? ax.Label), Br(), T($"{_start}: "), B(Calc.Fmt(ax.Start)), T($" · {_end}: "), B(Calc.Fmt(ax.End))]);
            _ = idx;
        }
    }
}

// ── «Гантели»: сдвиг среднего балла от начала к концу по каждому разделу.
public class Dumbbell : ChartBase
{
    private readonly List<RadarAxis> _rows;
    private readonly string _start, _end;
    private const double Right = 44, RowH = 30, Top = 22;

    public Dumbbell(IEnumerable<RadarAxis> rows, string startLabel, string endLabel)
    {
        _rows = rows.ToList();
        _start = startLabel;
        _end = endLabel;
        SetLegend(Legends.Chart(Series(startLabel, endLabel)));
    }

    protected override double HeightFor(double width) => Top + _rows.Count * RowH + 8;

    protected override void Draw(DrawingContext dc, double w)
    {
        var left = w < 420 ? 112 : 132;
        var h = HeightFor(w);
        double X(double v) => left + (w - left - Right) * v / 3;
        for (var v = 0; v <= 3; v++)
        {
            dc.DrawLine(v == 0 ? GridPen : GridDash, new Point(X(v), Top - 6), new Point(X(v), h - 6));
            Axis(dc, v.ToString(), X(v), 10, Anchor.Middle);
        }
        var link = Theme.SolidPen(new SolidColorBrush(Color.FromArgb(115, 0x65, 0x5C, 0x6C)), 2);
        for (var i = 0; i < _rows.Count; i++)
        {
            var r = _rows[i];
            var y = Top + i * RowH + RowH / 2;
            Text(dc, r.Label, left - 12, y, Anchor.End, middle: true);
            var both = r.Start is not null && r.End is not null;
            if (both) dc.DrawLine(link, new Point(X(r.Start!.Value), y), new Point(X(r.End!.Value), y));
            if (r.Start is { } s) Dot(dc, new Point(X(s), y), Theme.SeriesStart);
            if (r.End is { } e) Dot(dc, new Point(X(e), y), Theme.SeriesEnd);
            if (both)
            {
                var better = r.End < r.Start;
                var txt = r.End == r.Start ? "0,0" : $"{(better ? "−" : "+")}{Calc.Fmt(Math.Abs(r.End!.Value - r.Start!.Value), 1)}";
                Val(dc, txt, w - Right + 8, y, Anchor.Start, better ? Theme.Ok : r.End > r.Start ? Theme.Danger : Theme.Ink3, middle: true);
            }
            Hit(new RectangleGeometry(new Rect(0, y - RowH / 2, w, RowH)), () => [T(r.Full ?? r.Label), Br(), T($"{_start}: "), B(Calc.Fmt(r.Start)), T($" → {_end}: "), B(Calc.Fmt(r.End))]);
        }
    }
}

// ── Кольцо: доли итогов коррекционной работы.
public class Donut : ChartBase
{
    private readonly List<DonutSegment> _segments;
    private readonly string _centerValue, _centerLabel;

    public Donut(IEnumerable<DonutSegment> segments, string centerValue, string centerLabel)
    {
        _segments = segments.ToList();
        _centerValue = centerValue;
        _centerLabel = centerLabel;
        SetLegend(Legends.Chart(_segments.Select(sg => ($"{sg.Label} — {sg.Value}", (Brush)sg.Color))));
    }

    protected override double HeightFor(double width) => Math.Min(width, 220);

    protected override void Draw(DrawingContext dc, double width)
    {
        var size = Math.Min(width, 220);
        var s = size / 180;
        var cx = width / 2;
        var cy = 90 * s;
        var r = 62 * s;
        var sw = 16 * s;
        dc.DrawEllipse(null, Theme.SolidPen(Theme.Line, sw), new Point(cx, cy), r, r);
        var total = _segments.Sum(x => x.Value);
        if (total > 0)
        {
            var circ = 2 * Math.PI * 62;
            double offset = 0;
            foreach (var sg in _segments.Where(x => x.Value > 0))
            {
                var len = circ * sg.Value / total;
                var gap = total == sg.Value ? 0 : 2;
                var drawLen = Math.Max(len - gap, 0.5);
                var a0 = offset / circ * 2 * Math.PI - Math.PI / 2;
                var a1 = (offset + drawLen) / circ * 2 * Math.PI - Math.PI / 2;
                Geometry arc;
                if (drawLen >= circ - 0.01) arc = new EllipseGeometry(new Point(cx, cy), r, r);
                else
                {
                    var fig = new PathFigure { StartPoint = new Point(cx + r * Math.Cos(a0), cy + r * Math.Sin(a0)), IsClosed = false };
                    fig.Segments.Add(new ArcSegment(new Point(cx + r * Math.Cos(a1), cy + r * Math.Sin(a1)), new Size(r, r), 0, a1 - a0 > Math.PI, SweepDirection.Clockwise, true));
                    arc = new PathGeometry([fig]);
                }
                var pen = new Pen(sg.Color, sw) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
                dc.DrawGeometry(null, pen, arc);
                var seg = sg;
                Hit(arc.GetWidenedPathGeometry(pen), () => [T($"{seg.Label}: "), B(seg.Value.ToString()), T($" из {total} · "), B($"{Math.Round(seg.Value * 100.0 / total)}%")]);
                offset += len;
            }
        }
        var big = Ft(_centerValue, 26 * s, Theme.Ink, mono: true, bold: true);
        dc.DrawText(big, new Point(cx - big.Width / 2, 88 * s - big.Baseline));
        Text(dc, _centerLabel, cx, 106 * s, Anchor.Middle, size: 11 * Math.Max(1, s * 0.9));
    }
}

// ── Линия: средний балл по срезам (один ряд — без легенды).
public class TrendLine : ChartBase
{
    private readonly List<TrendPoint> _points;
    private readonly string _label;
    private const double H = 190, PL = 34, PR = 34, PT = 22, PB = 34;

    public TrendLine(IEnumerable<TrendPoint> points, string label = "Средний балл")
    {
        _points = points.ToList();
        _label = label;
    }

    protected override double HeightFor(double width) => H;

    protected override void Draw(DrawingContext dc, double w)
    {
        double X(int i) => _points.Count == 1 ? (w + PL - PR) / 2 : PL + (w - PL - PR) * i / (_points.Count - 1);
        double Y(double v) => PT + (H - PT - PB) * (3 - v) / 3;
        for (var v = 0; v <= 3; v++)
        {
            dc.DrawLine(v == 0 ? GridPen : GridDash, new Point(PL, Y(v)), new Point(w - PR, Y(v)));
            Axis(dc, v.ToString(), PL - 8, Y(v) + 3, Anchor.End);
        }
        var valid = _points.Select((p, i) => (p, i)).Where(x => x.p.Value is not null).ToList();
        if (valid.Count > 1)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(X(valid[0].i), Y(valid[0].p.Value!.Value)), false, false);
                foreach (var (p, i) in valid.Skip(1)) ctx.LineTo(new Point(X(i), Y(p.Value!.Value)), true, true);
            }
            dc.DrawGeometry(null, new Pen(Theme.SeriesEnd, 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, g);
        }
        for (var i = 0; i < _points.Count; i++)
        {
            var p = _points[i];
            Text(dc, p.Label, X(i), H - 12, Anchor.Middle);
            if (p.Value is { } v)
            {
                Dot(dc, new Point(X(i), Y(v)), Theme.SeriesEnd);
                Val(dc, Calc.Fmt(v), X(i), Y(v) - 11);
            }
            Hit(new RectangleGeometry(new Rect(X(i) - 30, 0, 60, H)), () => [T(p.Label), Br(), T($"{_label}: "), B(Calc.Fmt(p.Value))]);
        }
    }
}

// ── Столбцы по уровням: сколько детей на каждом уровне в начале и в конце (аналог диаграмм Excel).
public class LevelColumns : ChartBase
{
    private static readonly string[] LevelShort = ["норма", "формир.", "коррекция", "выраж."];
    private readonly int[] _start, _end;
    private readonly string _sl, _el;
    private const double H = 200, PL = 28, PR = 8, PT = 18, PB = 40;

    public LevelColumns(int[] start, int[] end, string startLabel, string endLabel)
    {
        _start = start;
        _end = end;
        _sl = startLabel;
        _el = endLabel;
        SetLegend(Legends.Chart(Series(startLabel, endLabel)));
    }

    protected override double HeightFor(double width) => H;

    protected override void Draw(DrawingContext dc, double w)
    {
        var max = Math.Max(1, _start.Concat(_end).Max());
        var band = (w - PL - PR) / 4;
        var bw = Math.Min(34, band / 2 - 8);
        double Hh(double v) => (H - PT - PB) * v / max;
        var baseY = H - PB;
        foreach (var v in new[] { 0, (int)Math.Ceiling(max / 2.0), max }.Distinct())
        {
            dc.DrawLine(v == 0 ? GridPen : GridDash, new Point(PL, baseY - Hh(v)), new Point(w - PR, baseY - Hh(v)));
            Axis(dc, v.ToString(), PL - 6, baseY - Hh(v) + 3, Anchor.End);
        }
        for (var lvl = 0; lvl < 4; lvl++)
        {
            var cx = PL + band * lvl + band / 2;
            foreach (var (v, color, dx) in new[] { (_start[lvl], Theme.SeriesStart, -bw - 1), (_end[lvl], Theme.SeriesEnd, 1.0) })
            {
                var hh = Hh(v);
                if (hh > 0)
                {
                    dc.DrawGeometry(color, null, BarPath(cx + dx, baseY, bw, hh));
                    Val(dc, v.ToString(), cx + dx + bw / 2, baseY - hh - 5);
                }
            }
            Val(dc, lvl.ToString(), cx, H - 22);
            Text(dc, w < 440 ? LevelShort[lvl] : M.LevelNames[lvl], cx, H - 8, Anchor.Middle, size: 10);
            var l = lvl;
            Hit(new RectangleGeometry(new Rect(cx - band / 2, 0, band, H)), () => [T($"Уровень {l} — {M.LevelNames[l]}"), Br(), T($"{_sl}: "), B(_start[l].ToString()), T($" · {_el}: "), B(_end[l].ToString())]);
        }
    }

    // Столбец со скруглением 3px только у верхнего края: основание стоит на оси.
    private static Geometry BarPath(double x, double baseY, double w, double height)
    {
        var r = Math.Min(3, height);
        var top = baseY - height;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x, baseY), true, true);
            c.LineTo(new Point(x, top + r), false, false);
            c.QuadraticBezierTo(new Point(x, top), new Point(x + r, top), false, false);
            c.LineTo(new Point(x + w - r, top), false, false);
            c.QuadraticBezierTo(new Point(x + w, top), new Point(x + w, top + r), false, false);
            c.LineTo(new Point(x + w, baseY), false, false);
        }
        g.Freeze();
        return g;
    }
}
