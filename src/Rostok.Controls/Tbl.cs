using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Rostok.Controls;

public enum Align { Left, Center, Right }

public sealed record Col(GridLength Width, Align Align = Align.Left, bool Sep = false, double MinWidth = 0)
{
    public static Col Auto(Align a = Align.Left, bool sep = false) => new(GridLength.Auto, a, sep);
    public static Col Px(double w, Align a = Align.Left, bool sep = false) => new(new GridLength(w), a, sep);
    public static Col Star(double w = 1, Align a = Align.Left, bool sep = false, double min = 0) => new(new GridLength(w, GridUnitType.Star), a, sep, min);
}

// Ячейка с объединением колонок или строк; Group — заголовок группы колонок (по центру, пунктир слева).
public sealed record Cell(object? Content, int Span = 1, bool Group = false, Align? Align = null, int RowSpan = 1, bool Sep = false);

// Таблица библиотеки (.tbl): шапка капсом с акцентной линией снизу, строки через тонкий разделитель.
public class Tbl : Grid
{
    private readonly List<Col> _cols;
    private int _row;
    public Thickness HeadPadding { get; set; } = new(10, 8, 10, 8);
    public Thickness CellPadding { get; set; } = new(10, 11, 10, 11);
    public double FontSize { get; set; } = 13;

    public Tbl(params Col[] cols)
    {
        _cols = [.. cols];
        foreach (var c in cols) ColumnDefinitions.Add(new ColumnDefinition { Width = c.Width, MinWidth = c.MinWidth });
        SnapsToDevicePixels = true;
    }

    public Tbl Header(params object?[] cells) => AddRow(cells, head: true);

    public Tbl Row(params object?[] cells) => AddRow(cells, head: false);

    public Tbl Total(params object?[] cells) => AddRow(cells, head: false, total: true);

    // Строка с произвольной отрисовкой ячеек: подложка всей строки (текущая строка протокола и т. п.).
    public Tbl RowWith(Brush? background, params object?[] cells) => AddRow(cells, head: false, background: background);

    public int RowCount => _row;

    private Tbl AddRow(object?[] cells, bool head, bool total = false, Brush? background = null)
    {
        while (RowDefinitions.Count <= _row) RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var col = 0;
        foreach (var raw in cells)
        {
            // занятые объединением строк ячейки пропускаются
            while (col < _cols.Count && IsCovered(_row, col)) col++;
            if (col >= _cols.Count) break;
            var cell = raw as Cell ?? new Cell(raw);
            var spec = _cols[col];
            var align = cell.Align ?? (cell.Group ? Align.Center : spec.Align);
            var el = MakeCell(cell.Content, head, total, align, spec.Sep || cell.Group || cell.Sep, cell.Group, background);
            SetRow(el, _row);
            SetColumn(el, col);
            if (cell.Span > 1) SetColumnSpan(el, cell.Span);
            if (cell.RowSpan > 1)
            {
                while (RowDefinitions.Count < _row + cell.RowSpan) RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                SetRowSpan(el, cell.RowSpan);
                for (var r = 1; r < cell.RowSpan; r++) for (var s = 0; s < cell.Span; s++) _covered.Add((_row + r, col + s));
            }
            Children.Add(el);
            col += cell.Span;
        }
        _row++;
        return this;
    }

    private readonly HashSet<(int, int)> _covered = [];
    private bool IsCovered(int r, int c) => _covered.Contains((r, c));

    private FrameworkElement MakeCell(object? content, bool head, bool total, Align align, bool sep, bool group, Brush? background)
    {
        UIElement inner = content switch
        {
            null => new TextBlock(),
            UIElement u => u,
            _ => head
                ? new TextBlock { Text = Ui.Upper(content.ToString() ?? ""), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Theme.Ink3, TextWrapping = TextWrapping.Wrap, TextAlignment = ToText(align) }
                : new TextBlock { Text = content.ToString(), FontSize = FontSize, Foreground = Theme.Ink2, TextWrapping = TextWrapping.Wrap, FontWeight = total ? FontWeights.Bold : FontWeights.Normal, TextAlignment = ToText(align) },
        };
        if (inner is FrameworkElement fe)
        {
            fe.HorizontalAlignment = align switch { Align.Center => HorizontalAlignment.Center, Align.Right => HorizontalAlignment.Right, _ => fe.HorizontalAlignment == HorizontalAlignment.Stretch && inner is not TextBlock ? HorizontalAlignment.Stretch : HorizontalAlignment.Left };
            fe.VerticalAlignment = head ? VerticalAlignment.Bottom : VerticalAlignment.Center;
        }
        var border = new Border
        {
            Padding = head ? HeadPadding : CellPadding,
            BorderBrush = head ? Theme.Accent : total ? Theme.Accent : Theme.Line,
            BorderThickness = head ? new Thickness(0, 0, 0, 1) : total ? new Thickness(0, 2, 0, 0) : new Thickness(0, 0, 0, 1),
            Background = background ?? Brushes.Transparent,
            Child = inner,
        };
        if (!sep) return border;
        var g = new Grid();
        g.Children.Add(border);
        g.Children.Add(new DashBorder { Sides = Sides.Left, IsHitTestVisible = false });
        return g;
    }

    private static TextAlignment ToText(Align a) => a switch { Align.Center => TextAlignment.Center, Align.Right => TextAlignment.Right, _ => TextAlignment.Left };

    // Горизонтальная прокрутка широкой таблицы (.table-scroll)
    public static ScrollViewer Scroll(UIElement table) => new HScroll
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Content = table,
        Focusable = false,
        PanningMode = PanningMode.HorizontalOnly,
    };
}

// Горизонтальная прокрутка, которая не перехватывает колесо мыши: страница прокручивается дальше вниз.
public class HScroll : ScrollViewer
{
    protected override void OnMouseWheel(System.Windows.Input.MouseWheelEventArgs e)
    {
        if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0 && ScrollableWidth > 0)
        {
            ScrollToHorizontalOffset(HorizontalOffset - e.Delta);
            e.Handled = true;
        }
    }
}
