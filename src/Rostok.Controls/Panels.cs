using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Rostok.Controls;

// Стопка с промежутком между элементами (display: grid / flex + gap).
public class Stack : Panel
{
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(nameof(Spacing), typeof(double), typeof(Stack),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(Stack),
        new FrameworkPropertyMetadata(Orientation.Vertical, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double Spacing { get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }
    public Orientation Orientation { get => (Orientation)GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }

    protected override Size MeasureOverride(Size available)
    {
        double main = 0, cross = 0;
        var visible = 0;
        var horizontal = Orientation == Orientation.Horizontal;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(horizontal ? new Size(double.PositiveInfinity, available.Height) : new Size(available.Width, double.PositiveInfinity));
            if (child.Visibility == Visibility.Collapsed) continue;
            var s = child.DesiredSize;
            main += horizontal ? s.Width : s.Height;
            cross = Math.Max(cross, horizontal ? s.Height : s.Width);
            visible++;
        }
        if (visible > 1) main += Spacing * (visible - 1);
        return horizontal ? new Size(main, cross) : new Size(cross, main);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double pos = 0;
        var horizontal = Orientation == Orientation.Horizontal;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            var s = child.DesiredSize;
            if (horizontal)
            {
                child.Arrange(new Rect(pos, 0, s.Width, final.Height));
                pos += s.Width + Spacing;
            }
            else
            {
                child.Arrange(new Rect(0, pos, final.Width, s.Height));
                pos += s.Height + Spacing;
            }
        }
        return final;
    }
}

// Строка с переносом и промежутками (flex-wrap + gap). Элементы по вертикали выравниваются по центру строки.
public class Flow : Panel
{
    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(nameof(Gap), typeof(double), typeof(Flow),
        new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty RowGapProperty = DependencyProperty.Register(nameof(RowGap), typeof(double), typeof(Flow),
        new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty AlignBottomProperty = DependencyProperty.Register(nameof(AlignBottom), typeof(bool), typeof(Flow),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsArrange));

    public double Gap { get => (double)GetValue(GapProperty); set => SetValue(GapProperty, value); }
    public double RowGap { get => (double)GetValue(RowGapProperty); set => SetValue(RowGapProperty, value); }
    public bool AlignBottom { get => (bool)GetValue(AlignBottomProperty); set => SetValue(AlignBottomProperty, value); }

    protected override Size MeasureOverride(Size available)
    {
        double x = 0, y = 0, rowH = 0, width = 0;
        var first = true;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(available.Width, double.PositiveInfinity));
            if (child.Visibility == Visibility.Collapsed) continue;
            var s = child.DesiredSize;
            if (!first && x + Gap + s.Width > available.Width)
            {
                y += rowH + RowGap;
                x = 0;
                rowH = 0;
                first = true;
            }
            x += (first ? 0 : Gap) + s.Width;
            rowH = Math.Max(rowH, s.Height);
            width = Math.Max(width, x);
            first = false;
        }
        return new Size(double.IsInfinity(available.Width) ? width : Math.Min(width, available.Width), y + rowH);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var rows = new System.Collections.Generic.List<(System.Collections.Generic.List<UIElement> Items, double H)>();
        var cur = new System.Collections.Generic.List<UIElement>();
        double x = 0, rowH = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            var s = child.DesiredSize;
            if (cur.Count > 0 && x + Gap + s.Width > final.Width)
            {
                rows.Add((cur, rowH));
                cur = [];
                x = 0;
                rowH = 0;
            }
            x += (cur.Count > 0 ? Gap : 0) + s.Width;
            rowH = Math.Max(rowH, s.Height);
            cur.Add(child);
        }
        if (cur.Count > 0) rows.Add((cur, rowH));
        double y = 0;
        foreach (var (items, h) in rows)
        {
            double cx = 0;
            foreach (var child in items)
            {
                var s = child.DesiredSize;
                var top = AlignBottom ? y + h - s.Height : y + (h - s.Height) / 2;
                child.Arrange(new Rect(cx, top, Math.Min(s.Width, final.Width), s.Height));
                cx += s.Width + Gap;
            }
            y += h + RowGap;
        }
        return final;
    }
}

// Полоса фильтров над контентом: пунктир сверху и снизу, между карточками — вертикальный пунктир.
// Fit = false — колонки поровну (как .filters), Fit = true — по содержимому (.filters--fit).
public class FilterBar : Panel
{
    public static readonly DependencyProperty FitProperty = DependencyProperty.Register(nameof(Fit), typeof(bool), typeof(FilterBar),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty BordersProperty = DependencyProperty.Register(nameof(Borders), typeof(Sides), typeof(FilterBar),
        new FrameworkPropertyMetadata(Sides.Top | Sides.Bottom, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool Fit { get => (bool)GetValue(FitProperty); set => SetValue(FitProperty, value); }
    public Sides Borders { get => (Sides)GetValue(BordersProperty); set => SetValue(BordersProperty, value); }

    private const double Pad = 20;
    private double[] _widths = [];

    public FilterBar()
    {
        Background = Brushes.Transparent;
        SnapsToDevicePixels = true;
    }

    protected override Size MeasureOverride(Size available)
    {
        var n = InternalChildren.Count;
        _widths = new double[n];
        if (n == 0) return new Size(0, 2);
        double height = 0, total = 0;
        if (Fit)
        {
            for (var i = 0; i < n; i++)
            {
                var child = InternalChildren[i];
                child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                _widths[i] = child.DesiredSize.Width + (i == 0 ? Pad : Pad * 2);
                total += _widths[i];
                height = Math.Max(height, child.DesiredSize.Height);
            }
        }
        else
        {
            var w = double.IsInfinity(available.Width) ? 300 * n : available.Width;
            // широкая карточка (FilterCard.Wide) получает не меньше 320 px, остальные — поровну
            var wideCount = 0;
            foreach (UIElement c in InternalChildren) if (c is FilterCard { Wide: true }) wideCount++;
            var each = w / n;
            for (var i = 0; i < n; i++) _widths[i] = each;
            if (wideCount > 0 && each < 320)
            {
                var rest = (w - 320 * wideCount) / Math.Max(1, n - wideCount);
                for (var i = 0; i < n; i++) _widths[i] = InternalChildren[i] is FilterCard { Wide: true } ? 320 : rest;
            }
            for (var i = 0; i < n; i++)
            {
                var inner = Math.Max(0, _widths[i] - (i == 0 ? Pad : Pad * 2));
                InternalChildren[i].Measure(new Size(inner, double.PositiveInfinity));
                height = Math.Max(height, InternalChildren[i].DesiredSize.Height);
            }
            total = w;
        }
        return new Size(total, height + 2);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double x = 0;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var left = i == 0 ? 0 : Pad;
            var inner = Math.Max(0, _widths[i] - left - Pad);
            InternalChildren[i].Arrange(new Rect(x + left, 1, inner, Math.Max(0, final.Height - 2)));
            x += _widths[i];
        }
        return final;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var pen = Theme.DashPen();
        var w = ActualWidth;
        var h = ActualHeight;
        if (Borders.HasFlag(Sides.Top)) dc.DrawLine(pen, new Point(0, 0.5), new Point(w, 0.5));
        if (Borders.HasFlag(Sides.Bottom)) dc.DrawLine(pen, new Point(0, h - 0.5), new Point(w, h - 0.5));
        double x = 0;
        for (var i = 0; i < _widths.Length - 1; i++)
        {
            x += _widths[i];
            dc.DrawLine(pen, new Point(Math.Round(x) + 0.5, 0), new Point(Math.Round(x) + 0.5, h));
        }
    }
}
