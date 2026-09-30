using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Rostok.Controls;
using Rostok.Core;
using Rostok.Core.Storage;
using Rostok.Desktop.Services;

namespace Rostok.Desktop.Pages;

// Вкладка «Программа коррекции»: пробы с дефицитом, упражнения из библиотеки, свои дополнения и рекомендации.
public sealed class ProgramTab(Child child, List<Period> filled, Action refresh)
{
    private string? _periodId = filled.Count > 0 ? filled[^1].Id : null;
    private string? _editing;
    private static Store S => AppHost.Store!;
    private static WorkspaceData D => S.Data;
    private static bool ViewOnly => S.ReadOnly;

    public UIElement Build()
    {
        var period = D.Period(_periodId);
        if (period is null) return Ui.Empty("Программа собирается по результатам обследования. Сначала заполните хотя бы один срез.");
        var saved = D.ProgramOf(child.Id, period.Id);
        var program = CorrectionProgram.Build(D.ScoresOf(child.Id, period.Id), saved);
        var library = D.LibraryOf();
        void Set(Action<ChildProgram> patch) => S.SetProgram(child.Id, period.Id, patch);

        var page = new StackPanel();
        var bar = new FilterBar { Fit = true, Borders = Sides.Bottom };
        bar.Children.Add(new FilterCard("Срез обследования", new Dropdown(filled.Select(p => new DropdownOption(p.Id, p.Label)), period.Id, v => { _periodId = (string?)v; refresh(); }, label: "Срез")));
        bar.Children.Add(new FilterCard("Что считать дефицитом", new Dropdown(M.Thresholds.Select(t => new DropdownOption(t.Value, t.Label)), program.Threshold, v => { Set(p => p.Threshold = (int)v!); refresh(); }, label: "Порог") { MinWidth = 300, IsEnabled = !ViewOnly }, wide: true));
        bar.Children.Add(new FilterCard("В программе", FilterCard.Value($"{program.Active} из {program.Total} проб")));
        page.Children.Add(bar);

        var toolbar = Ui.Row(8,
            Ui.Primary("Печать программы", () => AppHost.Main?.Navigate(Route.Of($"/parent/{child.Id}", ("period", period.Id), ("only", "program"))), IconKind.Print),
            Ui.Ghost("Отчёт для родителей", () => AppHost.Main?.Navigate(Route.Of($"/parent/{child.Id}", ("period", period.Id)))));
        toolbar.Margin = new Thickness(0, 18, 0, 6);
        page.Children.Add(toolbar);
        page.Children.Add(Ui.Faint(ViewOnly
                ? "Программа строится по пробам с дефицитом: звуки — все, кроме нормы; остальные пробы — по выбранному порогу. Отметки и дополнения владельца — только просмотр."
                : "Программа строится по пробам с дефицитом: звуки — все, кроме нормы; остальные пробы — по выбранному порогу. Снимите отметку, чтобы исключить пробу; допишите свои упражнения к пробе или общие рекомендации внизу — всё сохраняется сразу.")
            .With(t => { t.MaxWidth = 680; t.HorizontalAlignment = HorizontalAlignment.Left; t.Margin = new Thickness(0, 6, 0, 6); }));

        if (program.Total == 0)
            page.Children.Add(Ui.Empty("На этом срезе дефицитов по выбранному порогу нет. Можно выбрать порог «уровень 1–3» или записать рекомендации ниже."));

        var saveNote = Ui.Debounce<(string Item, string Text)>(x => Set(p => p.Notes[x.Item] = x.Text));
        foreach (var pb in program.Blocks)
        {
            var block = new StackPanel();
            block.Children.Add(Ui.BlockHead(Ui.H2(pb.Block.Title)));
            var firstSection = true;
            foreach (var ps in pb.Sections)
            {
                var sec = new DashBorder { Sides = firstSection ? Sides.None : Sides.Top, Padding = new Thickness(0, 12, 0, 4) };
                firstSection = false;
                var s = new StackPanel();
                var headRight = ps.Level is not null ? Ui.HStack(6, Ui.Num($"ср. {Calc.Fmt(ps.Mean)}", Theme.Ink3, 12).With(t => t.VerticalAlignment = VerticalAlignment.Center), new Level(ps.Level)) : null;
                s.Children.Add(Ui.BlockHead(Ui.Text(ps.Section.Title, null, Theme.Ink, 14.5, FontWeights.Bold), headRight, 4));
                if (ps.Direction.Length > 0)
                    s.Children.Add(Ui.Muted($"Направление работы: {ps.Direction}.").With(t => { t.TextWrapping = TextWrapping.Wrap; t.MaxWidth = 700; t.HorizontalAlignment = HorizontalAlignment.Left; t.Margin = new Thickness(0, 0, 0, 8); }));
                foreach (var item in ps.Items)
                    s.Children.Add(ItemView(ps.Section, item, library, saved, Set, saveNote));
                sec.Child = s;
                block.Children.Add(sec);
            }
            page.Children.Add(Ui.Card(block));
        }

        var recs = Ui.Input(saved.Recs, "Направления работы, формы и частота занятий, консультации смежных специалистов…", null, multiline: true, rows: 5);
        var home = Ui.Input(saved.Home, "Что делать дома: игры, упражнения, как часто, на что обратить внимание…", null, multiline: true, rows: 5);
        recs.MinHeight = home.MinHeight = 130;
        var saveRecs = Ui.Debounce<string>(v => Set(p => p.Recs = v));
        var saveHome = Ui.Debounce<string>(v => Set(p => p.Home = v));
        if (ViewOnly) recs.IsReadOnly = home.IsReadOnly = true;
        else
        {
            recs.TextChanged += (_, _) => saveRecs(recs.Text);
            home.TextChanged += (_, _) => saveHome(home.Text);
        }
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(Ui.Field("Рекомендации специалиста", recs));
        var hf = Ui.Field("Рекомендации родителям (занятия дома)", home);
        Grid.SetColumn(hf, 2);
        grid.Children.Add(hf);
        page.Children.Add(Ui.Card(Ui.VStack(0, Ui.BlockHead(Ui.H2("Свои рекомендации")), grid)));
        return page;
    }

    private UIElement ItemView(Section section, ProgramItem it, IReadOnlyDictionary<string, string> library, ChildProgram saved,
        Action<Action<ChildProgram>> set, Action<(string, string)> saveNote)
    {
        var label = section.Kind == Kinds.Sound
            ? Ui.Rich(Ui.Run("Звук "), Ui.Run($"[{it.Item.Label}]", weight: FontWeights.Bold, mono: true)).With(t => { t.FontSize = 13.5; t.Foreground = Theme.Ink; })
            : Ui.Text(it.Item.Label, null, Theme.Ink, 13.5, wrap: true);
        var badge = new Level(it.Score, it.Mark?.Label, it.Mark?.Mark ?? "·");
        var content = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(badge, Dock.Right);
        badge.Margin = new Thickness(10, 0, 0, 0);
        content.Children.Add(badge);
        content.Children.Add(label);
        var check = new Checkbox { IsChecked = !it.Off, Content = content, HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 38, IsHitTestVisible = !ViewOnly, Focusable = !ViewOnly };
        check.Click += (_, _) =>
        {
            set(p => { if (!p.Off.Remove(it.Item.Id)) p.Off.Add(it.Item.Id); });
            refresh();
        };

        var s = new StackPanel();
        s.Children.Add(check);
        if (!it.Off)
        {
            var body = new StackPanel { Margin = new Thickness(28, 0, 0, 0) };
            var lines = CorrectionProgram.ExerciseLines(library.GetValueOrDefault(it.Item.Id));
            if (lines.Count > 0) body.Children.Add(ExerciseList(lines));
            else
            {
                UIElement? add = ViewOnly ? null : Ui.TextAction("Добавить в библиотеку", () => { S.SetUi(u => u.LibrarySection = section.Id); AppHost.Main?.Navigate(Route.Of("/library")); });
                if (add is Control addLink) addLink.Foreground = Theme.Accent;
                body.Children.Add(Ui.Row(6, Ui.Faint("Упражнения для этой пробы ещё не заданы.").With(t => t.VerticalAlignment = VerticalAlignment.Center), add));
            }
            var note = saved.Notes.GetValueOrDefault(it.Item.Id) ?? "";
            if (note.Length > 0 || _editing == it.Item.Id)
            {
                var box = Ui.Input(note, "Своё упражнение для этого ребёнка, дозировка, материал — каждое с новой строки", null, multiline: true, rows: 2);
                box.Margin = new Thickness(0, 4, 0, 0);
                if (ViewOnly) box.IsReadOnly = true;
                else box.TextChanged += (_, _) => saveNote((it.Item.Id, box.Text));
                box.LostKeyboardFocus += (_, _) => _editing = null;
                if (_editing == it.Item.Id) box.Loaded += (_, _) => box.Focus();
                body.Children.Add(box);
            }
            else if (!ViewOnly) body.Children.Add(Ui.TextAction("+ дополнить для этого ребёнка", () => { _editing = it.Item.Id; refresh(); }));
            s.Children.Add(body);
        }
        return new Border
        {
            BorderBrush = it.Off ? Brushes.Transparent : Theme.Accent, BorderThickness = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(10, 2, 0, 8), Margin = new Thickness(0, 6, 0, 6), Opacity = it.Off ? 0.7 : 1, Child = s,
        };
    }

    // Нумерованный список упражнений (.exercise-list)
    public static StackPanel ExerciseList(IEnumerable<string> lines)
    {
        var list = new StackPanel { Margin = new Thickness(0, 2, 0, 8), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        var n = 1;
        foreach (var l in lines)
        {
            var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(Ui.Text($"{n++}.", null, Theme.Ink2, 13));
            var t = Ui.Text(l, null, Theme.Ink2, 13, wrap: true);
            t.LineHeight = 20;
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            list.Children.Add(g);
        }
        return list;
    }
}
