using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Rostok.Controls;
using Rostok.Core;
using Rostok.Desktop.Services;

namespace Rostok.Desktop.Pages;

// «Протокол группы»: таблица как в Excel — дети по строкам, пробы по столбцам.
public sealed class ProtocolPage : PageBase
{
    private int _r, _c;

    public ProtocolPage(Route route) : base(route) => PreviewKeyDown += OnKey;

    private (Group? Group, Period? Period, Section Section, List<Child> Kids) State()
    {
        var group = S.SelectedGroup;
        var section = M.SectionById.GetValueOrDefault(S.Ui.SectionId ?? "") ?? M.AllSections[0];
        return (group, S.SelectedPeriod, section, D.ChildrenOf(group?.Id));
    }

    private void Rerender()
    {
        Refresh();
        Focus();
    }

    protected override UIElement Build()
    {
        var (group, period, section, kids) = State();
        var items = section.Items.ToList();
        var options = M.OptionsFor(section);
        _r = Math.Clamp(_r, 0, Math.Max(0, kids.Count - 1));
        _c = Math.Clamp(_c, 0, Math.Max(0, items.Count - 1));
        var export = Ui.Ghost("Выгрузить протоколы в Excel", () => Export(group!, period!, kids), IconKind.Download);
        export.IsEnabled = kids.Count > 0 && period is not null;

        var page = Page(
            PageHeader("Протокол группы", "Таблица как в Excel: дети по строкам, пробы по столбцам. Нажатие по ячейке перебирает отметки; с клавиатуры — цифры, стрелки, Backspace. Сумма, среднее и уровень считаются сами.", export),
            Filters(false, GroupFilter(group), PeriodFilter(period),
                new FilterCard("Раздел", new Dropdown(M.AllSections.Select(s => new DropdownOption(s.Id, $"{(s.BlockId == "neuro" ? "Нейро · " : "")}{s.Title}")), section.Id,
                    v => { S.SetUi(u => u.SectionId = (string?)v); _r = _c = 0; Rerender(); }, label: "Раздел"), wide: true)));

        if (kids.Count == 0 || period is null)
        {
            page.Children.Add(Ui.Empty("В группе нет детей: протокол появится после добавления списка."));
            return page;
        }

        page.Children.Add(Ui.Card(Tbl.Scroll(Matrix(section, items, options, kids, period)), top: false, padTop: 18));
        if (section.IsScored)
        {
            var (dist, n) = Calc.LevelDistribution(section, kids.Select(k => k.Id), id => D.ScoresOf(id, period.Id));
            var nums = Ui.HStack(12, Enumerable.Range(0, 4).Select(i => (UIElement)Ui.HStack(4, new Level(i), Ui.Num(dist[i].ToString(), Theme.Ink3, 11.5).With(t => t.VerticalAlignment = VerticalAlignment.Center))).ToArray());
            page.Children.Add(new StackPanel
            {
                MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0),
                Children = { Ui.Caps($"Дети по уровням раздела · {n} из {kids.Count}"), new DistBar(dist, n) { Margin = new Thickness(0, 8, 0, 6) }, nums },
            });
        }
        return page;
    }

    private Grid Matrix(Section section, List<Item> items, List<Option> options, List<Child> kids, Period period)
    {
        var scored = section.IsScored;
        var g = new Grid { SnapsToDevicePixels = true };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MaxWidth = 260 });
        foreach (var _ in items) g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (scored) for (var i = 0; i < 3; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var totalCols = g.ColumnDefinitions.Count;
        var firstInGroup = new HashSet<int>();
        var col = 1;
        foreach (var gr in section.Groups) { firstInGroup.Add(col); col += gr.Items.Count; }
        var sumCol = 1 + items.Count;

        var row = 0;
        void AddRow() => g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        FrameworkElement Cell(UIElement? content, int r, int c, Brush? bottom = null, double bottomW = 1, bool dashLeft = false, Brush? bg = null, int span = 1, double topW = 0, Brush? top = null, Thickness? pad = null, HorizontalAlignment h = HorizontalAlignment.Center, VerticalAlignment v = VerticalAlignment.Center)
        {
            if (content is FrameworkElement fe) { fe.HorizontalAlignment = h; fe.VerticalAlignment = v; }
            var b = new Border { BorderBrush = bottom ?? Theme.Line, BorderThickness = new Thickness(0, 0, 0, bottomW), Background = bg ?? Brushes.Transparent, Padding = pad ?? new Thickness(0), Child = content };
            FrameworkElement el = b;
            if (dashLeft || topW > 0)
            {
                var gg = new Grid();
                gg.Children.Add(b);
                if (dashLeft) gg.Children.Add(new DashBorder { Sides = Sides.Left, IsHitTestVisible = false });
                if (topW > 0) gg.Children.Add(new Border { BorderBrush = top ?? Theme.Accent, BorderThickness = new Thickness(0, topW, 0, 0), IsHitTestVisible = false });
                el = gg;
            }
            Grid.SetRow(el, r);
            Grid.SetColumn(el, c);
            if (span > 1) Grid.SetColumnSpan(el, span);
            g.Children.Add(el);
            return el;
        }
        TextBlock HeadText(string t) => new() { Text = Ui.Upper(t), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Theme.Ink3, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };

        // ── шапка: группы проб ──
        if (section.Groups.Count > 1)
        {
            AddRow();
            Cell(null, row, 0, Brushes.Transparent, 0);
            col = 1;
            foreach (var gr in section.Groups)
            {
                Cell(HeadText(gr.Title).With(t => t.MaxWidth = Math.Max(90, gr.Items.Count * 34)), row, col, Theme.Dash, 1, dashLeft: true, span: gr.Items.Count, pad: new Thickness(4, 6, 4, 6)).With(e => ((FrameworkElement)e).Tag = "grp");
                col += gr.Items.Count;
            }
            row++;
        }
        // ── шапка: пробы ──
        AddRow();
        Cell(HeadText("Ребёнок").With(t => t.TextAlignment = TextAlignment.Left), row, 0, Theme.Accent, 1, pad: new Thickness(8, 0, 8, 8), h: HorizontalAlignment.Left, v: VerticalAlignment.Bottom);
        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            UIElement head;
            if (section.Kind == Kinds.Sound)
                head = new TextBlock { Text = it.Short, FontFamily = Theme.MonoFont, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Theme.Ink, Margin = new Thickness(0, 8, 0, 8) };
            else
                head = new TextBlock
                {
                    Text = it.Short, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 150, LineHeight = 14,
                    Foreground = i == _c ? Theme.Accent : Theme.Ink3, FontWeight = i == _c ? FontWeights.Bold : FontWeights.Normal,
                    LayoutTransform = new RotateTransform(-90), Margin = new Thickness(2, 6, 2, 6), Height = double.NaN,
                };
            var hc = Cell(head, row, 1 + i, Theme.Accent, 1, dashLeft: firstInGroup.Contains(1 + i), v: VerticalAlignment.Bottom);
            if (section.Kind != Kinds.Sound) ((FrameworkElement)hc).MinHeight = 150;
            hc.ToolTip = it.Label;
        }
        if (scored)
        {
            var labels = new[] { "Σ", "ср.", "ур." };
            for (var i = 0; i < 3; i++) Cell(Ui.Num(labels[i], Theme.Ink3, 11.5).Margin(10, 0, 10, 8), row, sumCol + i, Theme.Accent, 1, dashLeft: i == 0, v: VerticalAlignment.Bottom);
        }
        Cell(null, row, totalCols - 1, Theme.Accent, 1);
        row++;

        // ── дети ──
        for (var r = 0; r < kids.Count; r++)
        {
            AddRow();
            var k = kids[r];
            var sc = D.ScoresOf(k.Id, period.Id);
            var st = Calc.Stats(section, sc);
            var activeRow = r == _r;
            var rowBg = activeRow ? Theme.Soft : null;
            var name = new TextBlock { Text = k.Name, FontSize = 13, FontWeight = activeRow ? FontWeights.Bold : FontWeights.SemiBold, Foreground = activeRow ? Theme.Accent : Theme.Ink, TextTrimming = TextTrimming.CharacterEllipsis };
            var nameCell = new Border { BorderBrush = activeRow ? Theme.Accent : Brushes.Transparent, BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(8, 10, 12, 10), Child = name };
            Cell(nameCell, row, 0, bg: rowBg, h: HorizontalAlignment.Stretch);
            for (var c = 0; c < items.Count; c++)
            {
                var it = items[c];
                var v = sc.GetValueOrDefault(it.Id);
                var s = Calc.ItemScore(section, v);
                var mark = v is null ? (activeRow ? "·" : "") : options.FirstOrDefault(o => o.Value == v)?.Mark ?? "·";
                var text = new TextBlock
                {
                    Text = mark, FontFamily = Theme.MonoFont, FontSize = 14, FontWeight = v is null ? FontWeights.Normal : FontWeights.SemiBold,
                    Foreground = v is null ? Theme.Faint : s is { } lv ? Theme.LevelInk[lv] : Theme.Ink2,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                };
                var active = activeRow && c == _c;
                var box = new Border
                {
                    Width = 34, Height = 34, CornerRadius = new CornerRadius(2), Background = s is { } l2 ? Theme.LevelBg[l2] : Brushes.Transparent,
                    BorderBrush = active ? Theme.Accent : Brushes.Transparent, BorderThickness = new Thickness(active ? 2 : 0), Child = text, Cursor = Cursors.Hand,
                    ToolTip = $"{k.Name}, {it.Label}: {(v is null ? "пусто" : options.FirstOrDefault(o => o.Value == v)?.Label)}",
                };
                var rr = r;
                var cc = c;
                box.MouseLeftButtonDown += (_, e) => { e.Handled = true; _r = rr; _c = cc; Cycle(kids[rr], it, period, options); };
                Cell(box, row, 1 + c, bg: rowBg, dashLeft: firstInGroup.Contains(1 + c));
            }
            if (scored)
            {
                Cell(Ui.Num(st.Counted > 0 ? st.Sum.ToString() : "—", Theme.Ink2).Margin(10, 0, 10, 0), row, sumCol, bg: rowBg, dashLeft: true);
                Cell(Ui.Num(Calc.Fmt(st.Mean), Theme.Ink2).Margin(10, 0, 10, 0), row, sumCol + 1, bg: rowBg);
                Cell(new Level(st.Level) { Margin = new Thickness(10, 0, 10, 0) }, row, sumCol + 2, bg: rowBg);
            }
            Cell(null, row, totalCols - 1, bg: rowBg);
            row++;
        }

        // ── итоги по отметкам ──
        for (var oi = 0; oi < options.Count; oi++)
        {
            AddRow();
            var o = options[oi];
            var topW = oi == 0 ? 2.0 : 0;
            var lbl = new TextBlock { FontSize = 12, Foreground = Theme.Ink3, FontWeight = FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 240 };
            lbl.Inlines.Add(Ui.Run(o.Mark, Theme.Ink, FontWeights.SemiBold, mono: true));
            lbl.Inlines.Add(new System.Windows.Documents.Run($" — {o.Label.Split(',')[0].Split(':')[0]}"));
            lbl.ToolTip = o.Label;
            Cell(lbl, row, 0, Brushes.Transparent, 0, topW: topW, pad: new Thickness(8), h: HorizontalAlignment.Left);
            var total = 0;
            for (var c = 0; c < items.Count; c++)
            {
                var cnt = kids.Count(k => D.ScoresOf(k.Id, period.Id).GetValueOrDefault(items[c].Id) == o.Value);
                total += cnt;
                Cell(Ui.Num(cnt > 0 ? cnt.ToString() : "", Theme.Ink3, 13, FontWeights.SemiBold), row, 1 + c, Brushes.Transparent, 0, topW: topW, pad: new Thickness(4, 8, 4, 8));
            }
            if (scored) Cell(Ui.Num(total.ToString(), Theme.Ink2, 13, FontWeights.SemiBold).Margin(10, 0, 10, 0), row, sumCol, Brushes.Transparent, 0, span: 3, topW: topW, h: HorizontalAlignment.Right);
            Cell(null, row, totalCols - 1, Brushes.Transparent, 0, topW: topW);
            row++;
        }
        return g;
    }

    private void Cycle(Child child, Item item, Period period, List<Option> options)
    {
        var cur = D.ScoresOf(child.Id, period.Id).GetValueOrDefault(item.Id);
        var i = options.FindIndex(o => o.Value == cur);
        var next = i == options.Count - 1 ? null : options[i + 1].Value;
        S.SetScore(child.Id, period.Id, item.Id, next);
        Rerender();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox || e.OriginalSource is Dropdown || Keyboard.FocusedElement is ComboBoxItem) return;
        var (_, period, section, kids) = State();
        if (period is null || kids.Count == 0) return;
        var items = section.Items.ToList();
        var options = M.OptionsFor(section);
        void Move(int dr, int dc)
        {
            e.Handled = true;
            _r = Math.Clamp(_r + dr, 0, kids.Count - 1);
            _c = Math.Clamp(_c + dc, 0, items.Count - 1);
            Rerender();
        }
        switch (e.Key)
        {
            case Key.Right: Move(0, 1); return;
            case Key.Left: Move(0, -1); return;
            case Key.Down: Move(1, 0); return;
            case Key.Up: Move(-1, 0); return;
            case Key.Enter or Key.Space: e.Handled = true; Cycle(kids[_r], items[_c], period, options); return;
            case Key.Back or Key.Delete: e.Handled = true; S.SetScore(kids[_r].Id, period.Id, items[_c].Id, null); Rerender(); return;
        }
        var digit = e.Key switch { >= Key.D0 and <= Key.D9 => e.Key - Key.D0, >= Key.NumPad0 and <= Key.NumPad9 => e.Key - Key.NumPad0, _ => -1 };
        if (digit < 0) return;
        var opt = section.Kind == Kinds.Scale ? (digit < options.Count ? options[digit] : null) : (digit >= 1 && digit <= options.Count ? options[digit - 1] : null);
        if (opt is null) return;
        e.Handled = true;
        S.SetScore(kids[_r].Id, period.Id, items[_c].Id, opt.Value);
        // как в Excel: после ввода — вправо, в конце строки — на следующего ребёнка
        if (_c < items.Count - 1) _c++;
        else if (_r < kids.Count - 1) { _r++; _c = 0; }
        Rerender();
    }

    private void Export(Group group, Period period, List<Child> kids)
    {
        var path = Dialogs.SaveFile($"Протоколы — {group.Name} — {period.Label}.xlsx", "Книга Excel (*.xlsx)|*.xlsx");
        if (path is null) return;
        var sheets = M.AllSections.Select(s =>
        {
            var its = s.Items.ToList();
            var opts = M.OptionsFor(s);
            var head = new object?[] { "№", "Ребёнок" }.Concat(its.Select(i => (object?)i.Label)).Concat(s.IsScored ? ["Сумма", "Среднее", "Уровень"] : Array.Empty<object?>()).ToArray();
            var rows = new List<object?[]> { new object?[] { $"{s.Title} · {group.Name} · {period.Label}" }, head };
            for (var i = 0; i < kids.Count; i++)
            {
                var sc = D.ScoresOf(kids[i].Id, period.Id);
                var st = Calc.Stats(s, sc);
                var cells = its.Select(it =>
                {
                    var v = sc.GetValueOrDefault(it.Id);
                    if (v is null) return (object?)null;
                    return s.Kind == Kinds.Scale ? int.Parse(v) : opts.FirstOrDefault(o => o.Value == v)?.Label;
                });
                var tail = s.IsScored ? new object?[] { st.Counted > 0 ? st.Sum : null, st.Mean is null ? null : Math.Round(st.Mean.Value, 2), st.Level } : [];
                rows.Add([i + 1, kids[i].Name, .. cells, .. tail]);
            }
            return new Sheet(s.Short, rows, [4, 26, .. its.Select(_ => 12), 8, 9, 9]);
        });
        try { Excel.ExportSheets(path, sheets); Dialogs.OpenFolderOf(path); }
        catch (Exception e) { Dialogs.Error("Не удалось сохранить файл", e.Message); }
    }
}
