using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Rostok.Controls;
using Rostok.Core;
using Rostok.Core.Storage;
using Rostok.Desktop.Components;
using Rostok.Desktop.Services;

namespace Rostok.Desktop.Pages;

// Карта ребёнка: профиль и динамика, программа коррекции, заключение.
public sealed class ChildPage(Route route) : PageBase(route)
{
    private Child? _form;
    private string _initial = "";
    private bool _saved;
    private string? _reportPeriod;
    private string _reportText = "";
    private string? _reportFor;
    private ProgramTab? _program;

    private string Tab => Route.Param("tab") ?? "profile";

    protected override UIElement Build()
    {
        var child = D.Child(Route.Part(1));
        if (child is null)
            return Ui.Empty("Ребёнок не найден: возможно, запись удалена.", Ui.Ghost("К списку детей", () => Go("/")));
        var group = D.Group(child.GroupId);
        var filled = D.FilledPeriods(child.Id);
        var subtitle = string.Join(" · ", new[] { group?.Name, Calc.AgeText(child.BirthDate), child.Tpmpk.Length > 0 ? $"ТПМПК: {child.Tpmpk}" : null }.Where(x => !string.IsNullOrEmpty(x)));

        var page = Page(
            PageHeader(child.Name, subtitle.Length > 0 ? subtitle : "Карта ребёнка",
                Ui.Primary("Обследовать", () => Go("/exam", ("child", child.Id))),
                Ui.Ghost("Отчёт для родителей", () => Go($"/parent/{child.Id}"), IconKind.Print)),
            new TabBar([new TabSpec("profile", "Профиль и динамика"), new TabSpec("program", "Программа коррекции"), new TabSpec("report", "Заключение")],
                Tab is "program" or "report" ? Tab : "profile",
                k => Go($"/child/{child.Id}", ("tab", k == "profile" ? null : k))));

        switch (Tab)
        {
            case "report": page.Children.Add(Report(child, group, filled)); break;
            case "program":
                _program ??= new ProgramTab(child, filled, Refresh);
                page.Children.Add(_program.Build());
                break;
            default: Profile(page, child, filled); break;
        }
        return page;
    }

    // ── Профиль ─────────────────────────────────────────
    private void Profile(StackPanel page, Child child, List<Period> filled)
    {
        page.Children.Add(Ui.Card(ChildForm(child), top: false, padTop: 22));
        if (filled.Count == 0)
        {
            page.Children.Add(Ui.Empty("Обследований пока нет. Нажмите «Обследовать», чтобы заполнить первый срез."));
            return;
        }
        var first = filled[0];
        var last = filled[^1];
        var a = D.ScoresOf(child.Id, first.Id);
        var b = D.ScoresOf(child.Id, last.Id);
        List<RadarAxis> Axes(IEnumerable<Section> sections) => sections.Select(s => new RadarAxis(s.Short, Calc.Stats(s, a).Mean, first == last ? null : Calc.Stats(s, b).Mean, s.Title)).ToList();
        var res = first != last ? Calc.Outcome(Calc.BlockMean(M.SpeechSections, a), Calc.BlockMean(M.SpeechSections, b)) : null;
        var labelA = first.Label;
        var labelB = last != first ? last.Label : "—";

        var stats = M.Blocks.Select(bl =>
        {
            var mA = Calc.BlockMean(bl.Sections, a);
            var mB = Calc.BlockMean(bl.Sections, b);
            return (UIElement)Stat(bl.Title, StatValue(Calc.Fmt(mB), new Level(Calc.LevelOf(mB)), first != last ? Delta.Of(mA, mB) : null),
                first != last ? $"было {Calc.Fmt(mA)} · {labelA}" : labelA);
        }).ToList();
        stats.Add(Stat("Итог по речи", Ui.Text(res?.Label ?? "нужен второй срез", null, Theme.Ink, 17, FontWeights.Bold).Margin(0, 6, 0, 4), $"срезов с данными: {filled.Count}"));
        page.Children.Add(Ui.Card(Stats([.. stats])));

        page.Children.Add(Ui.Card(ChartGrid(
            ChartCell("Речевой профиль: чем ближе к центру, тем ближе к норме", new Radar(Axes(M.SpeechSections), labelA, labelB), maxWidth: 460),
            ChartCell("Средний балл по речи от среза к срезу", new TrendLine(D.Periods.Select(p => new TrendPoint(p.Short, Calc.BlockMean(M.SpeechSections, D.ScoresOf(child.Id, p.Id)))))),
            ChartCell("Нейродиагностика: сдвиг по сферам", new Dumbbell(Axes(M.NeuroScored), labelA, labelB), full: true))));

        var cols = new List<Col> { Col.Star(1, min: 260) };
        cols.AddRange(D.Periods.Select(_ => Col.Px(96, Align.Center)));
        var t = new Tbl([.. cols]) { CellPadding = new Thickness(3) };
        t.Header(["Раздел", .. D.Periods.Select(p => (object?)Ui.Text($"{p.Year}\n{p.Point}", null, Theme.Ink3, 11, FontWeights.SemiBold).With(x => x.TextAlignment = TextAlignment.Center))]);
        foreach (var s in M.AllSections.Where(s => s.IsScored))
        {
            t.Row([Ui.Text(s.Title, null, Theme.Ink2, 13).Margin(7, 5, 7, 5), .. D.Periods.Select(p =>
            {
                var st = Calc.Stats(s, D.ScoresOf(child.Id, p.Id));
                return (object?)new Level(st.Level, st.Mean is null ? null : $"средний балл {Calc.Fmt(st.Mean)}", stretch: true);
            })]);
        }
        t.Total(["Заполнено проб", .. D.Periods.Select(p => { var (f, tt) = Calc.TotalProgress(D.ScoresOf(child.Id, p.Id)); return (object?)Ui.Num($"{f}/{tt}", Theme.Ink, 13, FontWeights.Bold).Margin(0, 7, 0, 7); })]);
        page.Children.Add(Ui.Card(Ui.VStack(0, Ui.BlockHead(Ui.H2("Уровни по всем срезам")), Tbl.Scroll(t))));
    }

    private static string Snapshot(Child c) => JsonSerializer.Serialize(new { c.Name, c.BirthDate, c.Tpmpk, c.Note, c.Relatives }, Store.Json);

    private FrameworkElement ChildForm(Child child)
    {
        if (_form is null || _form.Id != child.Id)
        {
            _form = child.Clone();
            _initial = Snapshot(_form);
            _saved = false;
        }
        var form = _form;
        var save = Ui.Ghost("Сохранить сведения");
        var status = Ui.Status("Сохранено", true).With(t => t.VerticalAlignment = VerticalAlignment.Center);
        void Update()
        {
            var dirty = Snapshot(form) != _initial;
            if (dirty) _saved = false;
            save.IsEnabled = dirty && form.Name.Trim().Length > 0;
            status.Visibility = _saved && !dirty ? Visibility.Visible : Visibility.Collapsed;
        }
        save.Click += (_, _) =>
        {
            if (form.Name.Trim().Length == 0) return;
            var relatives = Relatives.Clean(form.Relatives);
            S.UpdateChild(child.Id, c =>
            {
                c.Name = form.Name.Trim(); c.BirthDate = form.BirthDate; c.Tpmpk = form.Tpmpk; c.Note = form.Note; c.Relatives = relatives;
            });
            _form = S.Data.Child(child.Id)!.Clone();
            _initial = Snapshot(_form);
            _saved = true;
            Refresh();
        };

        var birth = new DateField { Label = "Дата рождения", Min = Calc.BirthMin(), Max = Calc.TodayIso(), Value = form.BirthDate };
        birth.ValueChanged += v => { form.BirthDate = v; Update(); };
        var note = Ui.Input(form.Note, "Анамнез, особенности, договорённости с родителями", v => { form.Note = v; Update(); }, multiline: true, rows: 2);
        var fields = ChildrenPage.ChildFields(
            Ui.Field("Фамилия и имя", Ui.Input(form.Name, "", v => { form.Name = v; Update(); })),
            Ui.Field("Дата рождения", birth),
            Ui.Field("Заключение ТПМПК", Ui.Input(form.Tpmpk, "ТНР, ОНР III уровня…", v => { form.Tpmpk = v; Update(); })),
            Ui.Field("Заметки", note));
        var actions = Ui.Row(12, save, status);
        actions.Margin = new Thickness(0, 12, 0, 0);
        Update();
        return Ui.VStack(0, fields, Ui.H3("Родители и родственники").Margin(0, 20, 0, 8), new RelativesEditor(form.Relatives, Update), actions);
    }

    // ── Заключение ──────────────────────────────────────
    private FrameworkElement Report(Child child, Group? group, List<Period> filled)
    {
        if (filled.Count == 0) return Ui.Empty("Заключение собирается из баллов обследования. Сначала заполните хотя бы один срез.");
        if (_reportPeriod is null || filled.All(p => p.Id != _reportPeriod)) _reportPeriod = filled[^1].Id;
        var period = D.Period(_reportPeriod)!;
        var idx = filled.FindIndex(p => p.Id == _reportPeriod);
        var prev = idx > 0 ? filled[idx - 1] : null;
        string Generate() => ReportBuilder.Build(child, group, period, D.ScoresOf(child.Id, period.Id), prev, prev is null ? null : D.ScoresOf(child.Id, prev.Id), D.NoteOf(child.Id, period.Id));
        if (_reportFor != $"{child.Id}:{period.Id}") { _reportText = Generate(); _reportFor = $"{child.Id}:{period.Id}"; }

        var text = new TextBox
        {
            Text = _reportText, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = 14, Padding = new Thickness(26, 24, 26, 24),
            BorderBrush = Theme.Dash, MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Top,
        };
        TextBlock.SetLineHeight(text, 23);
        text.TextChanged += (_, _) => _reportText = text.Text;
        var copyBtn = Ui.Ghost("Копировать текст", null, IconKind.Copy);
        copyBtn.Click += (_, _) =>
        {
            try { Clipboard.SetText(text.Text); copyBtn.Content = Ui.Content(IconKind.Check, "Скопировано"); }
            catch (Exception) { /* буфер занят другой программой */ }
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) => { timer.Stop(); copyBtn.Content = Ui.Content(IconKind.Copy, "Копировать текст"); };
            timer.Start();
        };
        var toolbar = Ui.Row(8,
            Ui.Primary("Печать", () => Printing.PrintText($"{child.Name} — заключение", text.Text), IconKind.Print),
            copyBtn,
            Ui.Ghost("Собрать заново из баллов", () => { _reportText = Generate(); Refresh(); }));
        toolbar.Margin = new Thickness(0, 0, 0, 14);

        return Page(
            Filters(true,
                new FilterCard("Срез", new Dropdown(filled.Select(p => new DropdownOption(p.Id, p.Label)), period.Id, v => { _reportPeriod = (string?)v; Refresh(); }, label: "Срез")),
                new FilterCard("Сравнение", FilterCard.Value(prev is not null ? $"с {prev.Label}" : "первый срез — без сравнения", mono: false))).With(f => f.Borders = Sides.Bottom),
            Ui.Card(Ui.VStack(0, toolbar,
                Ui.Faint("Это черновик: текст можно править прямо здесь. Правки не сохраняются — скопируйте или распечатайте готовый вариант.").With(t => { t.MaxWidth = 620; t.HorizontalAlignment = HorizontalAlignment.Left; t.Margin = new Thickness(0, 0, 0, 12); }),
                text), top: false));
    }
}
