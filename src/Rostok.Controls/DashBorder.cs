using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Rostok.Controls;

[Flags]
public enum Sides
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
    All = Left | Top | Right | Bottom,
}

// Граница «Тёплого мела»: пунктир 1 px цвета --dash на выбранных сторонах (или сплошная линия).
// Заменяет CSS-правила border-top: 1px dashed … — у стандартной Border пунктира нет.
public class DashBorder : Decorator
{
    public static readonly DependencyProperty SidesProperty = DependencyProperty.Register(nameof(Sides), typeof(Sides), typeof(DashBorder),
        new FrameworkPropertyMetadata(Sides.All, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(DashBorder),
        new FrameworkPropertyMetadata(Theme.Dash, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty DashedProperty = DependencyProperty.Register(nameof(Dashed), typeof(bool), typeof(DashBorder),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(DashBorder),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(nameof(Padding), typeof(Thickness), typeof(DashBorder),
        new FrameworkPropertyMetadata(new Thickness(0), FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(nameof(Background), typeof(Brush), typeof(DashBorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Sides Sides { get => (Sides)GetValue(SidesProperty); set => SetValue(SidesProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public bool Dashed { get => (bool)GetValue(DashedProperty); set => SetValue(DashedProperty, value); }
    public double StrokeThickness { get => (double)GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }
    public Thickness Padding { get => (Thickness)GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }
    public Brush? Background { get => (Brush?)GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }

    public DashBorder()
    {
        SnapsToDevicePixels = true;
    }

    private Thickness Inner()
    {
        var t = StrokeThickness;
        var s = Sides;
        return new Thickness(
            Padding.Left + (s.HasFlag(Sides.Left) ? t : 0),
            Padding.Top + (s.HasFlag(Sides.Top) ? t : 0),
            Padding.Right + (s.HasFlag(Sides.Right) ? t : 0),
            Padding.Bottom + (s.HasFlag(Sides.Bottom) ? t : 0));
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var i = Inner();
        var h = i.Left + i.Right;
        var v = i.Top + i.Bottom;
        if (Child is null) return new Size(h, v);
        Child.Measure(new Size(Math.Max(0, constraint.Width - h), Math.Max(0, constraint.Height - v)));
        return new Size(Child.DesiredSize.Width + h, Child.DesiredSize.Height + v);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var i = Inner();
        Child?.Arrange(new Rect(i.Left, i.Top, Math.Max(0, arrangeSize.Width - i.Left - i.Right), Math.Max(0, arrangeSize.Height - i.Top - i.Bottom)));
        return arrangeSize;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (Background is not null) dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));
        var t = StrokeThickness;
        if (t <= 0 || Sides == Sides.None) return;
        var pen = Dashed ? Theme.DashPen(Stroke, t) : Theme.SolidPen(Stroke, t);
        var o = t / 2;
        var guidelines = new GuidelineSet();
        guidelines.GuidelinesX.Add(0); guidelines.GuidelinesX.Add(w);
        guidelines.GuidelinesY.Add(0); guidelines.GuidelinesY.Add(h);
        dc.PushGuidelineSet(guidelines);
        if (Sides.HasFlag(Sides.Top)) dc.DrawLine(pen, new Point(0, o), new Point(w, o));
        if (Sides.HasFlag(Sides.Bottom)) dc.DrawLine(pen, new Point(0, h - o), new Point(w, h - o));
        if (Sides.HasFlag(Sides.Left)) dc.DrawLine(pen, new Point(o, 0), new Point(o, h));
        if (Sides.HasFlag(Sides.Right)) dc.DrawLine(pen, new Point(w - o, 0), new Point(w - o, h));
        dc.Pop();
    }
}
