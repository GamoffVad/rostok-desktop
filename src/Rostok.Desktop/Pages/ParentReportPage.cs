using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rostok.Controls;
using Rostok.Core;
using Rostok.Desktop.Services;

namespace Rostok.Desktop.Pages;

// Отчёт для родителей: только данные одного ребёнка — печать / PDF, файл .html, Excel.
public sealed class ParentReportPage : PageBase
{
    private static readonly (string Key, string Label)[] Parts =
    [
        ("speech", "Речевое развитие"), ("neuro", "Нейродиагностика"), ("details", "Результаты по пробам"), ("program", "Программа коррекции"), ("recs", "Рекомендации"),
    ];

    private string? _periodId;
    private string _compareId = "";
    private readonly Dictionary<string, bool> _parts;

    public ParentReportPage(Route route) : base(route)
    {
        var only = route.Param("only") == "program";
        _parts = Parts.ToDictionary(p => p.Key, p => !only || p.Key is "program" or "recs");
        var child = D.Child(route.Part(1));
        if (child is null) return;
        var filled = D.FilledPeriods(child.Id);
        _periodId = route.Param("period") is { } pid && filled.Any(p => p.Id == pid) ? pid : filled.LastOrDefault()?.Id;
        var idx = filled.FindIndex(p => p.Id == _periodId);
        _compareId = idx > 0 ? filled[idx - 1].Id : "";
    }

    // Все данные отчёта для одного построения
    private sealed record Ctx(Child Child, Group? Group, List<Period> Filled, Period Period, Period? Compare,
        IReadOnlyDictionary<string, string> Cur, IReadOnlyDictionary<string, string>? Prev, ChildProgram Saved, ProgramPlan Program,
        IReadOnlyDictionary<string, string> Library, OutcomeResult? Res);

    private Ctx? Context()
    {
        var child = D.Child(Route.Part(1));
        if (child is null) return null;
        var filled = D.FilledPeriods(child.Id);
        var period = D.Period(_periodId);
        if (period is null) return null;
        var compare = D.Period(_compareId);
        var cur = D.ScoresOf(child.Id, period.Id);
        var prev = compare is null ? null : D.ScoresOf(child.Id, compare.Id);
        var saved = D.ProgramOf(child.Id, period.Id);
        return new Ctx(child, D.Group(child.GroupId), filled, period, compare, cur, prev, saved, CorrectionProgram.Build(cur, saved), D.LibraryOf(),
            prev is null ? null : Calc.Outcome(Calc.BlockMean(M.SpeechSections, prev), Calc.BlockMean(M.SpeechSections, cur)));
    }

    protected override UIElement Build()
    {
        var child = D.Child(Route.Part(1));
        if (child is null) return Ui.Empty("Ребёнок не найден.", Ui.Ghost("К списку детей", () => Go("/"))).Margin(0, 24, 0, 0);
        var back = Ui.Ghost("К карте ребёнка", () => Go($"/child/{child.Id}"), IconKind.ArrowBack);
        var ctx = Context();
        if (ctx is null) return Ui.Empty("У ребёнка нет обследований — отчёт пока не из чего собрать.", back).Margin(0, 24, 0, 0);
        if (AppHost.Main is { } w) w.Title = $"{child.Name} — результаты диагностики";

        var bar = new DashBorder { Sides = Sides.Bottom, Padding = new Thickness(0, 24, 0, 12), Margin = new Thickness(0, 0, 0, 14), Child = Ui.Row(16, back, Ui.Faint("Режим показа: на странице только данные этого ребёнка.").With(t => t.VerticalAlignment = VerticalAlignment.Center)) };
        var filters = Filters(false,
            new FilterCard("Срез", new Dropdown(ctx.Filled.Select(p => new DropdownOption(p.Id, p.Label)), ctx.Period.Id, v =>
            {
                _periodId = (string?)v;
                var i = ctx.Filled.FindIndex(p => p.Id == _periodId);
                _compareId = i > 0 ? ctx.Filled[i - 1].Id : "";
                Refresh();
            }, label: "Срез")),
            new FilterCard("Сравнить с", new Dropdown(new[] { new DropdownOption("", "без сравнения") }.Concat(ctx.Filled.Where(p => p.Id != ctx.Period.Id).Select(p => new DropdownOption(p.Id, p.Label))),
                _compareId, v => { _compareId = (string?)v ?? ""; Refresh(); }, label: "Сравнить с")));
        var checks = new Flow { Gap = 18, RowGap = 0, Margin = new Thickness(0, 8, 0, 8) };
        foreach (var (key, label) in Parts)
            checks.Children.Add(new Checkbox(label, _parts[key], on => { _parts[key] = on; Refresh(); }));
        var toolbar = Ui.Row(8,
            Ui.Primary("Печать / PDF", () => Printing.PrintBlocks($"{child.Name} — результаты диагностики", width => DocBlocks(Context()!, width, print: true)), IconKind.Print),
            Ui.Ghost("Файл для родителей (.html)", () => SaveHtml(Context()!), IconKind.Download),
            Ui.Ghost("Excel по ребёнку", () => SaveExcel(Context()!), IconKind.Download));
        toolbar.Margin = new Thickness(0, 4, 0, 22);

        var doc = new StackPanel();
        foreach (var b in DocBlocks(ctx, 880, print: false)) doc.Children.Add(b);
        var sheet = new DashBorder { Sides = Sides.All, Background = Theme.Sheet, Padding = new Thickness(40), MaxWidth = 960, HorizontalAlignment = HorizontalAlignment.Center, Child = doc };
        return Page(bar, filters, checks, toolbar, sheet);
    }

    private static DashBorder Section(UIElement child, bool top = true) => new DashBorder { Sides = top ? Sides.Top : Sides.None, Padding = new Thickness(0, 20, 0, 6), Child = child };

    // Блоки документа по порядку: одинаковы для экрана и печати; печать режет страницы между ними.
    private List<FrameworkElement> DocBlocks(Ctx c, double width, bool print)
    {
        var list = new List<FrameworkElement>();
        var pLabel = c.Period.Label;
        var cLabel = c.Compare?.Label ?? "";

        // ── шапка ──
        var head = new StackPanel();
        var brand = Ui.HStack(8, new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Rostok.Controls;component/Assets/favicon.png")), Width = 28, Height = 28 },
            Ui.Muted("Росток · результаты диагностики", 12).With(t => t.VerticalAlignment = VerticalAlignment.Center));
        head.Children.Add(brand.Margin(0, 0, 0, 14));
        head.Children.Add(Ui.H1(c.Child.Name));
        var meta = new Flow { Gap = 28, RowGap = 8, Margin = new Thickness(0, 10, 0, 0) };
        void Meta(string dt, string dd) => meta.Children.Add(Ui.VStack(0, Ui.Caps(dt), Ui.Text(dd, null, Theme.Ink, 14, wrap: true)));
        if (c.Child.BirthDate.Length > 0) Meta("Дата рождения", $"{Calc.RuDate(c.Child.BirthDate)} ({Calc.AgeText(c.Child.BirthDate)})");
        if (c.Group is not null) Meta("Группа", c.Group.Name);
        Meta("Обследование", $"{c.Period.Year}, {M.PointName(c.Period.Point)}");
        if (c.Compare is not null) Meta("Сравнение с", $"{c.Compare.Year}, {M.PointName(c.Compare.Point)}");
        var legal = c.Child.Relatives.Where(r => r.Legal).ToList();
        if (legal.Count > 0) Meta("Законные представители", string.Join("; ", legal.Select(r => string.Join(" ", new[] { r.Role, r.Name }.Where(x => x.Length > 0)))));
        head.Children.Add(meta);
        list.Add(new Border { BorderBrush = Theme.Accent, BorderThickness = new Thickness(0, 0, 0, 2), Padding = new Thickness(0, 0, 0, 14), Margin = new Thickness(0, 0, 0, 8), Child = head });

        var firstSection = true;
        DashBorder Sec(UIElement child) { var s = Section(child, !firstSection); firstSection = false; return s; }

        // ── как читать ──
        if (_parts["speech"] || _parts["neuro"] || _parts["details"])
        {
            var s = new StackPanel();
            s.Children.Add(Ui.H2("Как читать результаты").Margin(0, 0, 0, 10));
            for (var i = 0; i < 4; i++)
                s.Children.Add(Ui.HStack(10, new Level(i), Ui.Text(M.ParentLevel[i], null, Theme.Ink2, 13.5).With(t => t.VerticalAlignment = VerticalAlignment.Center)).Margin(0, 0, 0, 6));
            if (c.Res is not null)
                s.Children.Add(Ui.Rich(Ui.Run("Итог по речевому развитию за период: "), Ui.Run(c.Res.Label, Theme.Ink, FontWeights.Bold),
                    Ui.Run($" (средний балл {Calc.Fmt(Calc.BlockMean(M.SpeechSections, c.Prev!))} → {Calc.Fmt(Calc.BlockMean(M.SpeechSections, c.Cur))}; меньше — ближе к норме).")).Margin(0, 14, 0, 0));
            list.Add(Sec(s));
        }

        // ── блоки ──
        foreach (var b in M.Blocks.Where(b => _parts[b.Id]))
        {
            var sections = b.Sections.Where(s => s.IsScored).ToList();
            var mA = c.Prev is null ? (double?)null : Calc.BlockMean(b.Sections, c.Prev);
            var mB = Calc.BlockMean(b.Sections, c.Cur);
            var score = Ui.HStack(8, Ui.Num(Calc.Fmt(mB), Theme.Ink, 20, FontWeights.Bold), new Level(Calc.LevelOf(mB)), c.Prev is not null ? Delta.Of(mA, mB) : null);
            var s = new StackPanel();
            s.Children.Add(Ui.BlockHead(Ui.H2(b.Title), score));
            var cols = new List<Col> { Col.Star(1.3) };
            if (c.Compare is not null) cols.Add(Col.Px(88, Align.Center));
            cols.Add(Col.Px(88, Align.Center));
            cols.Add(Col.Star(1.5));
            var t = new Tbl([.. cols]) { CellPadding = new Thickness(10, 7, 10, 7), HeadPadding = new Thickness(10, 7, 10, 7) };
            t.Header([.. new object?[] { "Раздел" }.Concat(c.Compare is not null ? [c.Compare.Short] : Array.Empty<object?>()).Append(c.Period.Short).Append("Что это значит")]);
            foreach (var sec in sections)
            {
                var now = Calc.Stats(sec, c.Cur);
                var cells = new List<object?> { Ui.Text(sec.Title, null, Theme.Ink, 13, FontWeights.SemiBold, wrap: true) };
                if (c.Compare is not null) cells.Add(new Level(Calc.Stats(sec, c.Prev!).Level));
                cells.Add(new Level(now.Level));
                cells.Add(now.Level is null ? Ui.Faint("не обследовалось") : Ui.Text(M.ParentLevel[now.Level.Value], null, Theme.Ink2, 13, wrap: true));
                t.Row([.. cells]);
            }
            s.Children.Add(t);
            list.Add(Sec(s));

            List<RadarAxis> Axes(IEnumerable<Section> ss) => ss.Select(x => new RadarAxis(x.Short, c.Prev is null ? null : Calc.Stats(x, c.Prev).Mean, Calc.Stats(x, c.Cur).Mean, x.Title)).ToList();
            FrameworkElement ChartBox(string title, UIElement chart) => new StackPanel { Margin = new Thickness(0, 18, 0, 0), Children = { Ui.Text(title, null, Theme.Ink, 13, FontWeights.SemiBold, wrap: true).Margin(0, 0, 0, 8), chart } };
            if (b.Id == "speech")
            {
                var radar = ChartBox("Речевой профиль: чем ближе к центру, тем ближе к норме", new Radar(Axes(M.SpeechSections), cLabel.Length > 0 ? cLabel : "—", pLabel) { MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Center });
                var trend = ChartBox("Средний балл по речи на каждом обследовании", new TrendLine(c.Filled.Select(p => new TrendPoint(p.Short, Calc.BlockMean(M.SpeechSections, D.ScoresOf(c.Child.Id, p.Id))))));
                if (print) { list.Add(radar); list.Add(trend); }
                else
                {
                    var g = ChartGrid(radar, trend);
                    g.MaxWidth = 760;
                    g.Margin = new Thickness(0, 18, 0, 0);
                    g.HorizontalAlignment = HorizontalAlignment.Left;
                    list.Add(g);
                }
            }
            else list.Add(ChartBox("Средний балл по сферам", new Dumbbell(Axes(M.NeuroScored), cLabel.Length > 0 ? cLabel : "—", pLabel)).With(x => x.MaxWidth = 760));
        }

        // ── результаты по пробам ──
        if (_parts["details"])
        {
            var firstProbe = true;
            foreach (var s in M.AllSections.Where(s => _parts[s.BlockId] || (!_parts["speech"] && !_parts["neuro"])))
            {
                var items = s.Items.Where(i => c.Cur.ContainsKey(i.Id) || (c.Prev?.ContainsKey(i.Id) ?? false)).ToList();
                if (items.Count == 0) continue;
                var box = new StackPanel();
                if (firstProbe) box.Children.Add(Ui.H2("Результаты по пробам"));
                box.Children.Add(Ui.H3(s.Title).Margin(0, firstProbe ? 10 : 16, 0, 6));
                var cols = new List<Col> { Col.Star() };
                if (c.Compare is not null) cols.Add(Col.Px(110, Align.Center));
                cols.Add(Col.Px(110, Align.Center));
                var t = new Tbl([.. cols]) { CellPadding = new Thickness(10, 7, 10, 7), HeadPadding = new Thickness(10, 7, 10, 7) };
                t.Header([.. new object?[] { "Проба" }.Concat(c.Compare is not null ? [c.Compare.Short] : Array.Empty<object?>()).Append(c.Period.Short)]);
                foreach (var i in items)
                {
                    var cells = new List<object?> { s.Kind == Kinds.Sound ? $"Звук [{i.Label}]" : i.Label };
                    if (c.Compare is not null) cells.Add(ValueCell(s, c.Prev!.GetValueOrDefault(i.Id)));
                    cells.Add(ValueCell(s, c.Cur.GetValueOrDefault(i.Id)));
                    t.Row([.. cells]);
                }
                box.Children.Add(t);
                list.Add(firstProbe ? Sec(box) : box);
                firstProbe = false;
            }
        }

        // ── программа ──
        if (_parts["program"])
        {
            var head2 = Ui.H2("Программа коррекции").Margin(0, 0, 0, 10);
            if (c.Program.Active == 0) list.Add(Sec(Ui.VStack(0, head2, Ui.Faint("По результатам обследования коррекционных упражнений не требуется."))));
            else
            {
                var first = true;
                foreach (var pb in c.Program.Blocks)
                {
                    foreach (var ps in pb.Sections)
                    {
                        var on = ps.Items.Where(i => !i.Off).ToList();
                        if (on.Count == 0) continue;
                        var s = new StackPanel();
                        if (first) s.Children.Add(head2);
                        s.Children.Add(Ui.Text(ps.Section.Title, null, Theme.Ink, 14.5, FontWeights.Bold).Margin(0, 8, 0, 4));
                        if (ps.Direction.Length > 0) s.Children.Add(Ui.Muted($"Направление работы: {ps.Direction}.").With(x => { x.TextWrapping = TextWrapping.Wrap; x.Margin = new Thickness(0, 0, 0, 8); }));
                        foreach (var it in on)
                        {
                            var lines = CorrectionProgram.ExerciseLines(c.Library.GetValueOrDefault(it.Item.Id));
                            var note = c.Saved.Notes.GetValueOrDefault(it.Item.Id);
                            var item = new StackPanel();
                            item.Children.Add(ps.Section.Kind == Kinds.Sound
                                ? Ui.Rich(Ui.Run("Звук "), Ui.Run($"[{it.Item.Label}]", weight: FontWeights.Bold, mono: true)).With(x => { x.FontSize = 13.5; x.Foreground = Theme.Ink; x.Margin = new Thickness(0, 0, 0, 4); })
                                : Ui.Text(it.Item.Label, null, Theme.Ink, 13.5, wrap: true).Margin(0, 0, 0, 4));
                            if (lines.Count > 0 || !string.IsNullOrEmpty(note))
                                item.Children.Add(ProgramTab.ExerciseList(lines.Concat(CorrectionProgram.ExerciseLines(note))));
                            s.Children.Add(new Border { BorderBrush = Theme.Accent, BorderThickness = new Thickness(print ? 1 : 2, 0, 0, 0), Padding = new Thickness(10, 8, 0, 10), Margin = new Thickness(0, 6, 0, 6), Child = item });
                        }
                        list.Add(first ? Sec(s) : s);
                        first = false;
                    }
                }
            }
        }

        // ── рекомендации ──
        if (_parts["recs"] && (c.Saved.Recs.Length > 0 || c.Saved.Home.Length > 0))
        {
            var s = new StackPanel();
            s.Children.Add(Ui.H2("Рекомендации").Margin(0, 0, 0, 10));
            if (c.Saved.Recs.Length > 0) { s.Children.Add(Ui.H3("Рекомендации специалиста")); s.Children.Add(Ui.Text(c.Saved.Recs, null, Theme.Ink2, 13.5, wrap: true).With(x => { x.LineHeight = 22; x.MaxWidth = 760; x.HorizontalAlignment = HorizontalAlignment.Left; })); }
            if (c.Saved.Home.Length > 0) { s.Children.Add(Ui.H3("Занятия дома")); s.Children.Add(Ui.Text(c.Saved.Home, null, Theme.Ink2, 13.5, wrap: true).With(x => { x.LineHeight = 22; x.MaxWidth = 760; x.HorizontalAlignment = HorizontalAlignment.Left; })); }
            list.Add(Sec(s));
        }

        var foot = new DockPanel();
        var sign = Ui.Muted("Специалист: ______________________");
        DockPanel.SetDock(sign, Dock.Right);
        foot.Children.Add(sign);
        foot.Children.Add(Ui.Muted($"Дата: {Calc.RuDate(DateTime.Today)}"));
        list.Add(new DashBorder { Sides = Sides.Top, Padding = new Thickness(0, 16, 0, 0), Margin = new Thickness(0, 18, 0, 0), Child = foot });
        return list;
    }

    private static UIElement ValueCell(Section s, string? value)
    {
        var o = M.OptionOf(s, value);
        if (o is null) return Ui.Faint("—");
        if (s.IsScored) return new Level(Calc.ItemScore(s, value), o.Label, o.Mark) { HorizontalAlignment = HorizontalAlignment.Center };
        return Ui.Text(o.Label, null, Theme.Ink2, 13, wrap: true).With(t => t.TextAlignment = TextAlignment.Center);
    }

    // ── файл .html для родителей ────────────────────────
    private void SaveHtml(Ctx c)
    {
        var path = Dialogs.SaveFile($"{c.Child.Name} — результаты диагностики, {c.Period.Label}.html", "Страница HTML (*.html)|*.html");
        if (path is null) return;
        try
        {
            File.WriteAllText(path, ParentHtml.Build(c.Child, c.Group, c.Filled, c.Period, c.Compare, c.Cur, c.Prev, c.Saved, c.Program, c.Library, c.Res, _parts, D), new UTF8Encoding(false));
            Dialogs.OpenFolderOf(path);
        }
        catch (Exception e) { Dialogs.Error("Не удалось сохранить файл", e.Message); }
    }

    // ── Excel по ребёнку ────────────────────────────────
    private void SaveExcel(Ctx c)
    {
        var path = Dialogs.SaveFile($"{c.Child.Name} — диагностика.xlsx", "Книга Excel (*.xlsx)|*.xlsx");
        if (path is null) return;
        var summary = new List<object?[]> { new object?[] { "Раздел" }.Concat(c.Filled.Select(p => (object?)p.Label)).ToArray() };
        foreach (var b in M.Blocks)
        {
            summary.Add([b.Title.ToUpperInvariant()]);
            foreach (var s in b.Sections.Where(s => s.IsScored))
                summary.Add(new object?[] { s.Title }.Concat(c.Filled.Select(p => { var st = Calc.Stats(s, D.ScoresOf(c.Child.Id, p.Id)); return (object?)(st.Level is null ? null : $"{st.Level} (ср. {Calc.Fmt(st.Mean)})"); })).ToArray());
        }
        var probes = new List<object?[]> { new object?[] { "Раздел", "Проба" }.Concat(c.Filled.Select(p => (object?)p.Label)).ToArray() };
        foreach (var s in M.AllSections)
            foreach (var item in s.Items)
                probes.Add(new object?[] { s.Title, item.Label }.Concat(c.Filled.Select(p =>
                {
                    var v = D.ScoresOf(c.Child.Id, p.Id).GetValueOrDefault(item.Id);
                    if (v is null) return null;
                    return s.Kind == Kinds.Scale ? (object?)int.Parse(v) : M.OptionOf(s, v)?.Label;
                })).ToArray());
        var prog = new List<object?[]> { new object?[] { "Раздел", "Проба", "Отметка", "Упражнения", "Дополнение специалиста" } };
        foreach (var pb in c.Program.Blocks)
            foreach (var ps in pb.Sections)
                foreach (var it in ps.Items.Where(i => !i.Off))
                    prog.Add([ps.Section.Title, it.Item.Label, it.Mark?.Label ?? "", string.Join("\n", CorrectionProgram.ExerciseLines(c.Library.GetValueOrDefault(it.Item.Id))), c.Saved.Notes.GetValueOrDefault(it.Item.Id) ?? ""]);
        prog.Add([]);
        prog.Add(["Рекомендации специалиста", c.Saved.Recs]);
        prog.Add(["Рекомендации родителям", c.Saved.Home]);
        var head = new object?[] { $"{c.Child.Name}{(c.Child.BirthDate.Length > 0 ? $", {Calc.AgeText(c.Child.BirthDate)}" : "")} · {c.Group?.Name ?? ""}" };
        var contacts = new List<object?[]> { new object?[] { "Кем приходится", "ФИО", "Телефоны", "Эл. почта", "Адреса", "Законный представитель", "Примечание" } };
        contacts.AddRange(c.Child.Relatives.Select(r => new object?[] { r.Role, r.Name, Relatives.PhonesText(r), Relatives.EmailsText(r), Relatives.AddressesText(r), r.Legal ? "да" : "нет", r.Note }));
        var y = c.Period.Year;
        try
        {
            Excel.ExportSheets(path,
            [
                new Sheet("Уровни", [head, [], .. summary], [44, .. c.Filled.Select(_ => 18)]),
                new Sheet("Пробы", [head, [], .. probes], [30, 52, .. c.Filled.Select(_ => 18)]),
                new Sheet("Родители", [head, [], .. contacts], [18, 34, 36, 28, 50, 12, 40]),
                new Sheet($"Программа {c.Period.Point} {(y.Length >= 4 ? y.Substring(2, 2) : y)}-{(y.Length >= 2 ? y[^2..] : y)}", [head, [], .. prog], [30, 44, 22, 80, 40]),
            ]);
            Dialogs.OpenFolderOf(path);
        }
        catch (Exception e) { Dialogs.Error("Не удалось сохранить файл", e.Message); }
    }
}
