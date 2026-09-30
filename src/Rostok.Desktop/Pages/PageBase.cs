using System.Windows;
using System.Windows.Controls;
using Rostok.Controls;
using Rostok.Core;
using Rostok.Core.Storage;
using Rostok.Desktop.Services;

namespace Rostok.Desktop.Pages;

// Экран программы. Как компонент React: Build() собирает содержимое из текущих данных, Refresh() пересобирает его.
// Поля ввода текста экран не пересобирают — чтобы не терялся фокус и курсор.
public abstract class PageBase : UserControl
{
    protected PageBase(Route route)
    {
        Route = route;
        Focusable = true;
        FocusVisualStyle = null;
    }

    public Route Route { get; }
    protected static Store S => AppHost.Store!;
    protected static WorkspaceData D => S.Data;
    // Администратор смотрит чужое пространство: элементы правки скрыты, хранилище всё равно ничего не запишет.
    protected static bool ViewOnly => S.ReadOnly;

    public void Refresh()
    {
        Content = Build();
        AppHost.Main?.UpdateStatus();
    }

    protected abstract UIElement Build();

    // Переход на другой экран (hash-маршрут веб-версии).
    protected static void Go(string path, params (string, string?)[] query) => AppHost.Main?.Navigate(Route.Of(path, query));

    // Шапка страницы: заголовок и пояснение слева, действия справа внизу (.page-header)
    protected static FrameworkElement PageHeader(string title, string? subtitle, params UIElement?[] actions)
    {
        var grid = new Grid { Margin = new Thickness(0, 24, 0, 18) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        left.Children.Add(Ui.H1(title));
        if (!string.IsNullOrEmpty(subtitle)) left.Children.Add(Ui.Subtitle(subtitle));
        grid.Children.Add(left);
        var acts = actions.Where(a => a is not null).ToArray();
        if (acts.Length > 0)
        {
            var row = Ui.HStack(8, acts);
            row.VerticalAlignment = VerticalAlignment.Bottom;
            row.Margin = new Thickness(24, 0, 0, 0);
            Grid.SetColumn(row, 1);
            grid.Children.Add(row);
        }
        return grid;
    }

    protected FilterCard GroupFilter(Group? group) => new("Группа",
        new Dropdown(D.Groups.Select(g => new DropdownOption(g.Id, g.Name)), group?.Id, v => { S.SetUi(u => u.GroupId = (string?)v); Refresh(); }, label: "Группа"));

    protected FilterCard PeriodFilter(Period? period, string label = "Срез") => new(label,
        new Dropdown(D.Periods.Select(p => new DropdownOption(p.Id, p.Label)), period?.Id, v => { S.SetUi(u => u.PeriodId = (string?)v); Refresh(); }, label: label));

    protected static FilterBar Filters(bool fit, params UIElement[] cards)
    {
        var bar = new FilterBar { Fit = fit };
        foreach (var c in cards) bar.Children.Add(c);
        return bar;
    }

    protected static StackPanel Page(params UIElement?[] children)
    {
        var s = new StackPanel();
        foreach (var c in children) if (c is not null) s.Children.Add(c);
        return s;
    }

    // Две колонки: основная и боковая (.split--main / .split--300)
    protected static Grid Split(UIElement left, UIElement right, GridLength leftWidth, GridLength rightWidth, double gap = 26, double marginTop = 20)
    {
        var g = new Grid { Margin = new Thickness(0, marginTop, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = leftWidth });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gap) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = rightWidth });
        g.Children.Add(left);
        Grid.SetColumn(right, 2);
        g.Children.Add(right);
        return g;
    }

    // Сетка графиков в две колонки (.chart-grid)
    protected static Grid ChartGrid(params UIElement[] cells)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var row = 0;
        var col = 0;
        foreach (var c in cells)
        {
            var full = c is FrameworkElement { Tag: "full" };
            if (full && col != 0) { row++; col = 0; }
            while (g.RowDefinitions.Count <= row) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(c, row);
            Grid.SetColumn(c, full ? 0 : col * 2);
            if (full) Grid.SetColumnSpan(c, 3);
            if (c is FrameworkElement fe) fe.Margin = new Thickness(0, row > 0 ? 28 : 0, 0, 0);
            g.Children.Add(c);
            if (full || col == 1) { row++; col = 0; } else col = 1;
        }
        return g;
    }

    protected static StackPanel ChartCell(string title, UIElement chart, bool full = false, double? maxWidth = null)
    {
        var s = new StackPanel();
        if (full) s.Tag = "full";
        s.Children.Add(Ui.Text(title, null, Theme.Ink, 13, FontWeights.SemiBold, wrap: true).Margin(0, 0, 0, 8));
        if (maxWidth is not null && chart is FrameworkElement fe) { fe.MaxWidth = maxWidth.Value; fe.HorizontalAlignment = HorizontalAlignment.Center; }
        s.Children.Add(chart);
        return s;
    }

    // Показатели в ряд с пунктирными разделителями (.stats)
    protected static Grid Stats(params UIElement[] items)
    {
        var g = new Grid();
        for (var i = 0; i < items.Length; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var cell = new DashBorder { Sides = i == 0 ? Sides.None : Sides.Left, Padding = new Thickness(i == 0 ? 0 : 16, 0, 16, 0), Child = items[i] };
            Grid.SetColumn(cell, i);
            g.Children.Add(cell);
        }
        return g;
    }

    protected static StackPanel Stat(string caption, UIElement value, string? note = null)
    {
        var s = new StackPanel();
        s.Children.Add(Ui.Caps(caption));
        s.Children.Add(value);
        if (note is not null) s.Children.Add(Ui.Muted(note, 12).With(t => t.TextWrapping = TextWrapping.Wrap));
        return s;
    }

    protected static FrameworkElement StatValue(string text, params UIElement?[] extra)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = text, FontFamily = Theme.MonoFont, FontSize = 27, FontWeight = FontWeights.Bold, Foreground = Theme.Ink, VerticalAlignment = VerticalAlignment.Bottom });
        foreach (var e in extra)
        {
            if (e is not FrameworkElement fe) continue;
            fe.Margin = new Thickness(8, 0, 0, 7);
            fe.VerticalAlignment = VerticalAlignment.Bottom;
            row.Children.Add(fe);
        }
        return row;
    }
}
