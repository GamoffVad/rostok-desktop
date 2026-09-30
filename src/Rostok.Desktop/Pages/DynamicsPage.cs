using System.Windows;
using System.Windows.Controls;
using Rostok.Controls;
using Rostok.Core;
using Rostok.Desktop.Services;

namespace Rostok.Desktop.Pages;

// «Динамика»: сравнение двух срезов по группе — уровни, сдвиг среднего балла, итог коррекционной работы.
public sealed class DynamicsPage(Route route) : PageBase(route)
{
    private string _blockId = "speech";

    private sealed record RowData(Child Child, IReadOnlyDictionary<string, string> A, IReadOnlyDictionary<string, string> B, double? MA, double? MB, OutcomeResult? Res);

    protected override UIElement Build()
    {
        var group = S.SelectedGroup;
        var kids = D.ChildrenOf(group?.Id);
        var def = Dynamics.DefaultRange(D, kids);
        var start = D.Period(S.Ui.StartId) ?? def.Start;
        var end = D.Period(S.Ui.EndId) ?? def.End;
        var block = M.Blocks.First(b => b.Id == _blockId);
        var sections = block.Sections.Where(s => s.IsScored).ToList();
        if (start is null || end is null) return Ui.Empty("Нет срезов. Добавьте учебный год в разделе «Данные».").Margin(0, 24, 0, 0);

        var rows = kids.Select(k =>
        {
            var a = D.ScoresOf(k.Id, start.Id);
            var b = D.ScoresOf(k.Id, end.Id);
            var mA = Calc.BlockMean(sections, a);
            var mB = Calc.BlockMean(sections, b);
            return new RowData(k, a, b, mA, mB, Calc.Outcome(mA, mB));
        }).ToList();
        var measured = rows.Where(r => r.Res is not null).ToList();
        double? Avg(Func<RowData, double?> key) => measured.Count > 0 ? measured.Average(r => key(r)!.Value) : null;
        var improved = measured.Count(r => r.Res!.Id is "norm" or "major" or "minor");
        int[] Overall(Func<RowData, double?> key)
        {
            var dist = new int[4];
            foreach (var r in rows) if (Calc.LevelOf(key(r)) is { } l) dist[l]++;
            return dist;
        }
        var sectionMeans = sections.Select(s =>
        {
            double? Mean(string pid)
            {
                var v = kids.Select(k => Calc.Stats(s, D.ScoresOf(k.Id, pid)).Mean).Where(m => m is not null).Select(m => m!.Value).ToList();
                return v.Count > 0 ? v.Average() : null;
            }
            return new RadarAxis(s.Short, Mean(start.Id), Mean(end.Id), s.Title);
        }).ToList();
        var sLabel = start.Label;
        var eLabel = end.Label;
        var sameYear = start.Year == end.Year;
        var sShort = sameYear ? start.Point : start.Short;
        var eShort = sameYear ? end.Point : end.Short;
        var periodOptions = D.Periods.Select(p => new DropdownOption(p.Id, p.Label)).ToList();

        var export = Ui.Ghost("Выгрузить в Excel", () => Export(group!, block, sections, rows, start, end, kids), IconKind.Download);
        export.IsEnabled = kids.Count > 0;
        var page = Page(
            PageHeader("Динамика", "Сравнение двух срезов по группе: уровни по разделам, сдвиг среднего балла и итог коррекционной работы. Балл снижается — значит, ребёнок приближается к норме.", export),
            Filters(false, GroupFilter(group),
                new FilterCard("Начало", new Dropdown(periodOptions, start.Id, v => { S.SetUi(u => u.StartId = (string?)v); Refresh(); }, label: "Начало")),
                new FilterCard("Конец", new Dropdown(periodOptions, end.Id, v => { S.SetUi(u => u.EndId = (string?)v); Refresh(); }, label: "Конец"))),
            new TabBar(M.Blocks.Select(b => new TabSpec(b.Id, b.Title)), _blockId, k => { _blockId = k; Refresh(); }).Margin(0, 18, 0, 0));

        if (measured.Count == 0)
        {
            page.Children.Add(Ui.Empty("Нет детей, обследованных на обоих срезах. Выберите другие срезы или заполните протоколы.").Margin(0, 20, 0, 0));
            return page;
        }

        page.Children.Add(Ui.Card(Stats(
            Stat("Обследовано на обоих срезах", StatValue(measured.Count.ToString(), Ui.Num($"из {kids.Count} детей", Theme.Ink3, 12))),
            Stat("Средний балл группы", StatValue(Calc.Fmt(Avg(r => r.MB)), Delta.Of(Avg(r => r.MA), Avg(r => r.MB))), $"было {Calc.Fmt(Avg(r => r.MA))} · шкала 0–3, 0 — норма"),
            Stat("Положительная динамика", StatValue($"{Math.Round(improved * 100.0 / measured.Count)}%"), $"{improved} из {measured.Count}: улучшение или норма")), top: false, padTop: 22));

        page.Children.Add(Ui.Card(ChartGrid(
            ChartCell("Итог коррекционной работы", new Donut(M.Outcomes.Select(o => new DonutSegment(o.Label, measured.Count(r => r.Res!.Id == o.Id), Theme.OutcomeColor(o.Id))), measured.Count.ToString(), "детей")),
            ChartCell($"Дети по общему уровню: {sShort} и {eShort}", new LevelColumns(Overall(r => r.MA), Overall(r => r.MB), sLabel, eLabel)),
            ChartCell("Сдвиг среднего балла по разделам", new Dumbbell(sectionMeans, sLabel, eLabel)),
            ChartCell("Профиль группы", new Radar(sectionMeans, sLabel, eLabel), maxWidth: 460))));

        // ── распределение по уровням ──
        var distGrid = new UniformGrid3(sections.Count > 4 ? 3 : 2);
        foreach (var s in sections)
        {
            var cell = new StackPanel();
            cell.Children.Add(Ui.Text(s.Title, null, Theme.Ink, 13, FontWeights.SemiBold).Margin(0, 0, 0, 8));
            foreach (var p in new[] { start, end })
            {
                var (dist, n) = Calc.LevelDistribution(s, kids.Select(k => k.Id), id => D.ScoresOf(id, p.Id));
                var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                g.Children.Add(Ui.Caps(p == start ? sShort : eShort).With(t => t.VerticalAlignment = VerticalAlignment.Center));
                var bar = new DistBar(dist, n) { VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(bar, 1);
                g.Children.Add(bar);
                var nums = new TextBlock { FontFamily = Theme.MonoFont, FontSize = 11.5, Foreground = Theme.Ink3, Margin = new Thickness(0, 4, 0, 0) };
                for (var i = 0; i < 4; i++)
                {
                    nums.Inlines.Add(new System.Windows.Documents.Run($"{(i > 0 ? "   " : "")}{i}: "));
                    nums.Inlines.Add(Ui.Run(dist[i].ToString(), Theme.Ink, FontWeights.Bold, mono: true));
                }
                Grid.SetRow(nums, 1);
                Grid.SetColumn(nums, 1);
                g.Children.Add(nums);
                cell.Children.Add(g);
            }
            distGrid.Add(cell);
        }
        page.Children.Add(Ui.Card(Ui.VStack(0, Ui.BlockHead(Ui.H2("Распределение детей по уровням"), Legends.LevelLegend()), distGrid)));

        // ── сводная ──
        var cols = new List<Col> { Col.Star(1, min: 170) };
        foreach (var _ in sections) { cols.Add(Col.Star(0.5, Align.Center, sep: true, min: 52)); cols.Add(Col.Star(0.5, Align.Center, min: 52)); }
        cols.Add(Col.Auto(Align.Right, sep: true)); cols.Add(Col.Auto(Align.Right)); cols.Add(Col.Auto(Align.Right)); cols.Add(Col.Auto(Align.Left, sep: true));
        var t = new Tbl([.. cols]) { CellPadding = new Thickness(3), MinWidth = 860 };
        var head1 = new List<object?> { new Cell("Ребёнок", RowSpan: 2) };
        foreach (var s in sections) head1.Add(new Cell(Ui.Upper(s.Short), 2, Group: true));
        head1.Add(new Cell("Средний балл", 3, Group: true));
        head1.Add(new Cell("Итог", RowSpan: 2, Sep: true));
        t.Header([.. head1]);
        var sa = sameYear ? start.Point : "нач.";
        var ea = sameYear ? end.Point : "кон.";
        var head2 = new List<object?>();
        foreach (var _ in sections) { head2.Add(new Cell(sa, Sep: true, Align: Align.Center)); head2.Add(new Cell(ea, Align: Align.Center)); }
        head2.Add(new Cell(sa, Sep: true)); head2.Add(ea); head2.Add("Δ");
        t.Header([.. head2]);
        foreach (var r in rows)
        {
            var cells = new List<object?> { Ui.Link(r.Child.Name, () => Go($"/child/{r.Child.Id}")).Margin(7, 5, 7, 5) };
            foreach (var s in sections)
            {
                cells.Add(new Level(Calc.Stats(s, r.A).Level, stretch: true));
                cells.Add(new Level(Calc.Stats(s, r.B).Level, stretch: true));
            }
            cells.Add(Ui.Num(Calc.Fmt(r.MA)).Margin(7, 5, 7, 5));
            cells.Add(Ui.Num(Calc.Fmt(r.MB)).Margin(7, 5, 7, 5));
            cells.Add((object?)Delta.Of(r.MA, r.MB)?.Margin(7, 5, 7, 5) ?? "");
            cells.Add(r.Res is not null ? Ui.Text(r.Res.Label, null, Theme.Ink2, 13).Margin(7, 5, 7, 5) : Ui.Faint("нет данных").Margin(7, 5, 7, 5));
            t.Row([.. cells]);
        }
        page.Children.Add(Ui.Card(Ui.VStack(0, Ui.BlockHead(Ui.H2($"Сводная: {block.Title.ToLowerInvariant()}"), Ui.Faint($"{sLabel} → {eLabel}")), Tbl.Scroll(t))));
        return page;
    }

    private void Export(Group group, Block block, List<Section> sections, List<RowData> rows, Period start, Period end, List<Child> kids)
    {
        var path = Dialogs.SaveFile($"Динамика — {group.Name}.xlsx", "Книга Excel (*.xlsx)|*.xlsx");
        if (path is null) return;
        var head1 = new object?[] { "№", "Ребёнок" }.Concat(sections.SelectMany(s => new object?[] { s.Short, "" })).Concat(["Средний балл", "", "Итог"]).ToArray();
        var head2 = new object?[] { "", "" }.Concat(sections.SelectMany(_ => new object?[] { start.Point, end.Point })).Concat([start.Point, end.Point, ""]).ToArray();
        var body = rows.Select((r, i) => new object?[] { i + 1, r.Child.Name }
            .Concat(sections.SelectMany(s => new object?[] { Calc.Stats(s, r.A).Level, Calc.Stats(s, r.B).Level }))
            .Concat([r.MA is null ? null : Math.Round(r.MA.Value, 2), r.MB is null ? null : Math.Round(r.MB.Value, 2), r.Res?.Label ?? ""]).ToArray());
        var distRows = Enumerable.Range(0, 4).Select(lvl => new object?[] { "", $"Детей на уровне {lvl}" }
            .Concat(sections.SelectMany(s => new[] { start, end }.Select(p => (object?)Calc.LevelDistribution(s, kids.Select(k => k.Id), id => D.ScoresOf(id, p.Id)).Dist[lvl]))).ToArray());
        var all = new List<object?[]> { new object?[] { $"Динамика: {block.Title}. {group.Name}. {start.Label} → {end.Label}" }, head1, head2 };
        all.AddRange(body);
        all.Add([]);
        all.AddRange(distRows);
        try
        {
            Excel.ExportSheets(path, [new Sheet(block.Title, all, [4, 26, .. sections.SelectMany(_ => new[] { 7, 7 }), 8, 8, 26])]);
            Dialogs.OpenFolderOf(path);
        }
        catch (Exception e) { Dialogs.Error("Не удалось сохранить файл", e.Message); }
    }
}

// Сетка из 2–3 колонок с промежутками 22×32 (.dist-grid)
public sealed class UniformGrid3 : Grid
{
    private readonly int _cols;
    private int _count;

    public UniformGrid3(int cols)
    {
        _cols = cols;
        for (var i = 0; i < cols; i++)
        {
            if (i > 0) ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            ColumnDefinitions.Add(new ColumnDefinition());
        }
    }

    public void Add(UIElement el)
    {
        var row = _count / _cols;
        var col = _count % _cols;
        while (RowDefinitions.Count <= row) RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        SetRow(el, row);
        SetColumn(el, col * 2);
        if (el is FrameworkElement fe && row > 0) fe.Margin = new Thickness(0, 22, 0, 0);
        Children.Add(el);
        _count++;
    }
}
