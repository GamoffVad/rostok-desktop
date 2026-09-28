using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Rostok.Controls;
using Rostok.Core;
using Rostok.Desktop.Services;

namespace Rostok.Desktop.Pages;

// «Обследование»: один ребёнок, пробы раздела; балл — нажатием или цифрой с клавиатуры.
public sealed class ExamPage : PageBase
{
    private int _cursor;
    private string? _cursorKey;
    private readonly Action<(string Child, string Period, string Text)> _saveNote;

    public ExamPage(Route route) : base(route)
    {
        _saveNote = Ui.Debounce<(string Child, string Period, string Text)>(x => S.SetNote(x.Child, x.Period, x.Text));
        PreviewKeyDown += OnKey;
    }

    private (List<Child> Kids, Child? Child, Period? Period, Section Section) State()
    {
        var group = S.SelectedGroup;
        var kids = D.ChildrenOf(group?.Id);
        var child = kids.FirstOrDefault(c => c.Id == Route.Param("child")) ?? kids.FirstOrDefault();
        var section = M.SectionById.GetValueOrDefault(S.Ui.SectionId ?? "") ?? M.AllSections[0];
        return (kids, child, S.SelectedPeriod, section);
    }

    private void SetSection(string id)
    {
        S.SetUi(u => u.SectionId = id);
        Refresh();
    }

    private void Rerender()
    {
        Refresh();
        if (!Ui.IsTyping(Keyboard.FocusedElement)) Focus();
    }

    protected override UIElement Build()
    {
        var (kids, child, period, section) = State();
        if (child is null)
            return Ui.Empty("В группе нет детей. Добавьте их на экране «Дети».", Ui.Ghost("Перейти к списку", () => Go("/"))).Margin(0, 24, 0, 0);
        if (period is null) return Ui.Empty("Нет срезов. Добавьте учебный год в «Администрирование → Данные».");
        var block = M.Blocks.First(b => b.Id == section.BlockId);
        var key = $"{section.Id}:{child.Id}";
        if (_cursorKey != key) { _cursor = 0; _cursorKey = key; }

        var scores = D.ScoresOf(child.Id, period.Id);
        var pi = D.Periods.FindIndex(p => p.Id == period.Id);
        var prevPeriod = pi > 0 ? D.Periods[pi - 1] : null;
        var prevScores = prevPeriod is null ? null : D.ScoresOf(child.Id, prevPeriod.Id);
        var items = section.Items.ToList();
        var options = M.OptionsFor(section);
        var stats = Calc.Stats(section, scores);
        var idx = kids.IndexOf(child);

        var prevBtn = Ui.Ghost("Предыдущий", () => Go("/exam", ("child", kids[idx - 1].Id)), IconKind.ArrowBack);
        prevBtn.IsEnabled = idx > 0;
        var nextBtn = Ui.Button("GhostButton", Ui.HStack(8, Ui.Text("Следующий").With(t => t.VerticalAlignment = VerticalAlignment.Center), new Icon(IconKind.Arrow) { VerticalAlignment = VerticalAlignment.Center }),
            () => Go("/exam", ("child", kids[idx + 1].Id)));
        nextBtn.IsEnabled = idx < kids.Count - 1;

        var page = Page(
            PageHeader("Обследование", "Отмечайте балл нажатием или с клавиатуры: цифра ставит отметку и переходит к следующей пробе, стрелки ↑ ↓ — перемещение, Backspace — очистить. Всё сохраняется сразу.", prevBtn, nextBtn),
            Filters(false,
                GroupFilter(S.SelectedGroup),
                new FilterCard($"Ребёнок · {idx + 1} из {kids.Count}", new Dropdown(kids.Select(c => new DropdownOption(c.Id, c.Name)), child.Id, v => Go("/exam", ("child", (string?)v)), label: "Ребёнок"), wide: true),
                PeriodFilter(period)),
            new TabBar(M.Blocks.Select(b => new TabSpec(b.Id, b.Title)), block.Id, k => SetSection(M.Blocks.First(b => b.Id == k).Sections[0].Id)).Margin(0, 18, 0, 0));

        // ── основная колонка ──
        var main = new StackPanel();
        main.Children.Add(new TabBar(block.Sections.Select(s => { var st = Calc.Stats(s, scores); return new TabSpec(s.Id, s.Short, $"{st.Filled}/{st.Total}", st.Filled == st.Total); }), section.Id, SetSection, type: true));

        var title = new WrapPanel();
        title.Children.Add(Ui.H2(section.Title));
        if (section.IsScored && stats.Mean is not null)
            title.Children.Add(Ui.HStack(6, Ui.Num($"ср. {Calc.Fmt(stats.Mean)}", Theme.Ink3, 15, FontWeights.Medium).With(t => t.VerticalAlignment = VerticalAlignment.Center), new Level(stats.Level)).Margin(10, 0, 0, 0));
        string? normValue = section.Kind switch { Kinds.Scale => "0", Kinds.Sound => "norm", Kinds.YesNo => "no", _ => null };
        var prevHasData = prevScores is not null && items.Any(i => prevScores.ContainsKey(i.Id));
        var tools = Ui.HStack(12,
            normValue is null ? null : Ui.TextAction(section.Kind == Kinds.YesNo ? "везде «нет»" : "всё в норме", () => FillAll(child, period, items, normValue)),
            prevHasData ? Ui.TextAction($"как на срезе {prevPeriod!.Label}", () =>
            {
                S.SetMany(child.Id, period.Id, items.Where(i => prevScores!.ContainsKey(i.Id)).ToDictionary(i => i.Id, i => (string?)prevScores![i.Id]));
                Rerender();
            }) : null,
            stats.Filled > 0 ? Ui.TextAction("очистить раздел", () => FillAll(child, period, items, null), danger: true) : null);
        main.Children.Add(Ui.BlockHead(title, tools).Margin(0, 18, 0, 4));
        if (section.Hint is not null) main.Children.Add(Ui.Faint(section.Hint).Margin(0, 0, 0, 4));

        var n = 0;
        foreach (var gr in section.Groups)
        {
            var g = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
            g.Children.Add(new Border { BorderBrush = Theme.Accent, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 6), Child = Ui.Caps(gr.Title) });
            foreach (var item in gr.Items)
                g.Children.Add(ProbeRow(child, period, section, item, options, scores, prevScores, n++));
            main.Children.Add(g);
        }
        if (section.IsScored)
        {
            var legend = new Flow { Gap = 16, RowGap = 4, Margin = new Thickness(0, 12, 0, 0) };
            foreach (var o in options)
                legend.Children.Add(Ui.Rich(Ui.Run(o.Mark, Theme.Ink, FontWeights.SemiBold, mono: true), Ui.Run(" " + o.Label, Theme.Ink3)).With(t => t.FontSize = 12));
            main.Children.Add(legend);
        }

        // ── сводка ──
        var progress = Calc.TotalProgress(scores);
        var aside = new StackPanel();
        aside.Children.Add(Ui.BlockHead(Ui.H2($"Сводка: {child.Name}"), Ui.Num($"{progress.Filled}/{progress.Total}", progress.Filled == progress.Total ? Theme.Ok : Theme.Ink3, 12)));
        foreach (var s in block.Sections)
        {
            var st = Calc.Stats(s, scores);
            var row = new DockPanel();
            var count = Ui.Num($"{st.Filled}/{st.Total}", st.Filled == st.Total ? Theme.Ok : Theme.Ink3, 11).With(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(10, 0, 0, 0); });
            DockPanel.SetDock(count, Dock.Right);
            row.Children.Add(count);
            if (s.IsScored)
            {
                var lvl = new Level(st.Level) { Margin = new Thickness(10, 0, 0, 0) };
                DockPanel.SetDock(lvl, Dock.Right);
                row.Children.Add(lvl);
                var mean = Ui.Num(Calc.Fmt(st.Mean), Theme.Ink3, 12).With(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(10, 0, 0, 0); });
                DockPanel.SetDock(mean, Dock.Right);
                row.Children.Add(mean);
            }
            else if (s.Kind == Kinds.YesNo && st.Filled > 0)
            {
                var yes = Ui.Num($"есть: {st.Yes}", Theme.Ink3, 11).With(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(10, 0, 0, 0); });
                DockPanel.SetDock(yes, Dock.Right);
                row.Children.Add(yes);
            }
            row.Children.Add(new TextBlock { Text = s.Short, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Style = Ui.Style("TreeRow"), Content = row };
            Ui.SetIsActive(b, s.Id == section.Id);
            var sid = s.Id;
            b.Click += (_, _) => SetSection(sid);
            aside.Children.Add(b);
        }
        var notes = Ui.Input(D.NoteOf(child.Id, period.Id), "Поведение на обследовании, контакт, утомляемость…", null, multiline: true, rows: 4);
        notes.MinHeight = 100;
        notes.TextChanged += (_, _) => _saveNote((child.Id, period.Id, notes.Text));
        aside.Children.Add(Ui.Field("Наблюдения на срезе", notes).Margin(0, 16, 0, 0));
        aside.Children.Add(Ui.TextAction("открыть заключение", () => Go($"/child/{child.Id}", ("tab", "report"))));

        page.Children.Add(Split(main, Sticky.Attach(new Border { Padding = new Thickness(0, 20, 0, 0), Child = aside }), new GridLength(1, GridUnitType.Star), new GridLength(320), marginTop: 0));
        return page;
    }

    private FrameworkElement ProbeRow(Child child, Period period, Section section, Item item, List<Option> options,
        IReadOnlyDictionary<string, string> scores, IReadOnlyDictionary<string, string>? prevScores, int index)
    {
        var v = scores.GetValueOrDefault(item.Id);
        var prev = prevScores?.GetValueOrDefault(item.Id);
        var prevOpt = prev is null ? null : options.FirstOrDefault(o => o.Value == prev);
        var wide = section.Kind == Kinds.Choice;
        var pair = section.Kind is Kinds.Side or Kinds.YesNo;

        var label = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Theme.Ink, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center };
        if (section.Kind == Kinds.Sound) label.Inlines.Add(Ui.Run(item.Label, weight: FontWeights.Bold, mono: true, size: 16));
        else label.Inlines.Add(new System.Windows.Documents.Run(item.Label));
        if (prevOpt is not null) label.Inlines.Add(Ui.Run($" · было: {(section.Kind == Kinds.Scale ? prevOpt.Mark : prevOpt.Label)}", Theme.Ink3, size: 12));

        Panel marks = wide ? new Flow { Gap = 6, RowGap = 6, HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 560 } : new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var o in options)
        {
            var b = new Button
            {
                Style = Ui.Style(wide || pair ? "MarkWide" : "Mark"),
                Content = section.IsScored ? o.Mark : o.Label,
                ToolTip = o.Label,
                Margin = new Thickness(marks is StackPanel && marks.Children.Count > 0 ? 6 : 0, 0, 0, 0),
            };
            if (pair) { b.MinWidth = 110; b.HorizontalContentAlignment = HorizontalAlignment.Center; }
            Ui.SetIsActive(b, v == o.Value);
            if (section.Kind == Kinds.Scale || section.Kind == Kinds.Sound) Ui.SetLevel(b, o.Score);
            var value = o.Value;
            b.Click += (_, _) =>
            {
                _cursor = index;
                S.SetScore(child.Id, period.Id, item.Id, v == value ? null : value);
                Rerender();
            };
            marks.Children.Add(b);
        }

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = wide ? new GridLength(1.3, GridUnitType.Star) : GridLength.Auto });
        row.Children.Add(label);
        Grid.SetColumn(marks, 2);
        marks.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(marks);

        var border = new Border
        {
            BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 0, 1), Background = index == _cursor ? Theme.Soft : Brushes.Transparent,
            Child = new Border { BorderBrush = v is not null ? Theme.Accent : Brushes.Transparent, BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(10, 10, 0, 10), Child = row },
        };
        border.MouseLeftButtonDown += (_, _) => { if (_cursor != index) { _cursor = index; Rerender(); } };
        return border;
    }

    private void FillAll(Child child, Period period, List<Item> items, string? value)
    {
        S.SetMany(child.Id, period.Id, items.ToDictionary(i => i.Id, _ => value));
        Rerender();
    }

    // Клавиатура: цифра ставит отметку и переводит к следующей пробе.
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (Ui.IsTyping(e.OriginalSource) || e.OriginalSource is Dropdown || Keyboard.FocusedElement is ComboBoxItem) return;
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0) return;
        var (_, child, period, section) = State();
        if (child is null || period is null) return;
        var items = section.Items.ToList();
        var options = M.OptionsFor(section);
        switch (e.Key)
        {
            case Key.Down: e.Handled = true; _cursor = Math.Min(items.Count - 1, _cursor + 1); Rerender(); return;
            case Key.Up: e.Handled = true; _cursor = Math.Max(0, _cursor - 1); Rerender(); return;
            case Key.Back or Key.Delete: e.Handled = true; S.SetScore(child.Id, period.Id, items[_cursor].Id, null); Rerender(); return;
        }
        var digit = e.Key switch
        {
            >= Key.D0 and <= Key.D9 => e.Key - Key.D0,
            >= Key.NumPad0 and <= Key.NumPad9 => e.Key - Key.NumPad0,
            _ => -1,
        };
        if (digit < 0) return;
        var opt = section.Kind == Kinds.Scale ? (digit < options.Count ? options[digit] : null) : (digit >= 1 && digit <= options.Count ? options[digit - 1] : null);
        if (opt is null) return;
        e.Handled = true;
        S.SetScore(child.Id, period.Id, items[_cursor].Id, opt.Value);
        _cursor = Math.Min(items.Count - 1, _cursor + 1);
        Rerender();
    }
}
