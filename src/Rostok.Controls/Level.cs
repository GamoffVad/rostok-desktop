using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Rostok.Core;

namespace Rostok.Controls;

// Уровень 0–3: моноширинная цифра на смысловой подложке; нет данных — серая точка.
public class Level : Border
{
    public Level(int? value, string? tip = null, string? text = null, bool stretch = false)
    {
        CornerRadius = new CornerRadius(Theme.Radius);
        MinWidth = stretch ? 30 : 24;
        Height = stretch ? 28 : 24;
        Padding = new Thickness(4, 0, 4, 0);
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalAlignment = stretch ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        var t = new TextBlock
        {
            Text = text ?? (value?.ToString() ?? "·"),
            FontFamily = Theme.MonoFont, FontSize = 13, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        if (value is { } v and >= 0 and <= 3)
        {
            Background = Theme.LevelBg[v];
            t.Foreground = Theme.LevelInk[v];
            ToolTip = tip ?? M.LevelNames[v];
        }
        else
        {
            Background = Brushes.Transparent;
            t.Foreground = Theme.Faint;
            ToolTip = tip ?? "нет данных";
        }
        Child = t;
    }
}

// Дельта среднего балла: балл снизился — улучшение (зелёная), вырос — ухудшение (красная).
public static class Delta
{
    public static TextBlock? Of(double? from, double? to, double size = 12)
    {
        if (from is null || to is null) return null;
        var d = to.Value - from.Value;
        var t = new TextBlock { FontFamily = Theme.MonoFont, FontSize = size, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        if (Math.Abs(d) < 0.005)
        {
            t.Text = "0,00";
            t.Foreground = Theme.Ink3;
        }
        else
        {
            t.Text = Calc.FmtDelta(d);
            t.Foreground = d < 0 ? Theme.Ok : Theme.Danger;
        }
        return t;
    }
}

// Полоса распределения детей по уровням 0–3.
public class DistBar : FrameworkElement
{
    private readonly int[] _dist;

    public DistBar(int[] dist, int n)
    {
        _dist = dist;
        Height = 10;
        ToolTip = $"уровни 0–3: {string.Join(", ", dist)} из {n}";
    }

    protected override void OnRender(DrawingContext dc)
    {
        var total = _dist.Sum();
        if (total == 0) return;
        var parts = _dist.Count(v => v > 0);
        var free = ActualWidth - 2 * Math.Max(0, parts - 1);
        double x = 0;
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), 2, 2));
        for (var i = 0; i < 4; i++)
        {
            if (_dist[i] == 0) continue;
            var w = free * _dist[i] / total;
            dc.DrawRectangle(Theme.ChartLevel[i], null, new Rect(x, 0, w, ActualHeight));
            x += w + 2;
        }
        dc.Pop();
    }
}

public static class Legends
{
    // Легенда уровней 0–3 (.legend)
    public static Flow LevelLegend()
    {
        var f = new Flow { Gap = 16, RowGap = 4 };
        for (var i = 0; i < 4; i++)
        {
            var item = Ui.HStack(6, new Level(i), Ui.Muted(M.LevelNames[i], 12).With(t => t.VerticalAlignment = VerticalAlignment.Center));
            f.Children.Add(item);
        }
        return f;
    }

    // Легенда графика: цветной квадрат и подпись (.chart-legend)
    public static Flow Chart(IEnumerable<(string Label, Brush Color)> items)
    {
        var f = new Flow { Gap = 16, RowGap = 4, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var (label, color) in items)
        {
            var sw = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(2), Background = color, VerticalAlignment = VerticalAlignment.Center };
            f.Children.Add(Ui.HStack(6, sw, Ui.Muted(label, 12).With(t => t.VerticalAlignment = VerticalAlignment.Center)));
        }
        return f;
    }
}
