using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rostok.Controls;
using Rostok.Core;

namespace Rostok.Desktop.Services;

// Файл .html для родителей: самостоятельная страница в стиле «Тёплого мела»; графики вставлены картинками.
public static class ParentHtml
{
    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    private static readonly string[] LevelInk = ["#2C6B45", "#746410", "#9A5413", "#A32D22"];
    private static readonly string[] LevelBg = ["#DDE9DF", "#EFE8CF", "#F3E0CC", "#F3DEDB"];

    private static string Lvl(int? v, string? text = null, string? tip = null) => v is { } l and >= 0 and <= 3
        ? $"<span class=\"lvl\" style=\"color:{LevelInk[l]};background:{LevelBg[l]}\" title=\"{E(tip ?? M.LevelNames[l])}\">{E(text ?? l.ToString())}</span>"
        : "<span class=\"lvl none\" title=\"нет данных\">·</span>";

    private static string Delta(double? from, double? to)
    {
        if (from is null || to is null) return "";
        var d = to.Value - from.Value;
        if (Math.Abs(d) < 0.005) return "<span class=\"delta\" style=\"color:#655C6C\">0,00</span>";
        return $"<span class=\"delta\" style=\"color:{(d < 0 ? "#2C6B45" : "#A32D22")}\">{Calc.FmtDelta(d)}</span>";
    }

    // График — в PNG с двойной чёткостью.
    private static string Png(FrameworkElement chart, double width)
    {
        var host = new System.Windows.Controls.Border { Background = Theme.Sheet, Child = chart, Width = width };
        host.Measure(new Size(width, double.PositiveInfinity));
        host.Arrange(new Rect(0, 0, width, host.DesiredSize.Height));
        host.UpdateLayout();
        var scale = 2.0;
        var rtb = new RenderTargetBitmap((int)(width * scale), (int)(host.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(host);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return $"<img class=\"chart\" alt=\"\" style=\"max-width:{(int)width}px\" src=\"data:image/png;base64,{Convert.ToBase64String(ms.ToArray())}\">";
    }

    private static string Logo()
    {
        var info = Application.GetResourceStream(new Uri("pack://application:,,,/Rostok.Controls;component/Assets/favicon.png"));
        using var ms = new MemoryStream();
        info.Stream.CopyTo(ms);
        return $"data:image/png;base64,{Convert.ToBase64String(ms.ToArray())}";
    }

    private const string Css = """

            body{margin:0;background:#F8F4EB;color:#463E4D;font:14px/1.55 'Golos Text',system-ui,sans-serif;font-variant-numeric:tabular-nums}
            .doc{background:#FFFDF8;border:1px dashed #CFC8BC;border-radius:2px;padding:clamp(16px,4vw,40px);max-width:960px;margin:24px auto}
            .head{border-bottom:2px solid #5A2B63;padding-bottom:14px;margin-bottom:8px}
            .brand{display:flex;align-items:center;gap:8px;font-size:12px;color:#655C6C;margin-bottom:14px}
            h1{margin:0;font-size:30px;font-weight:700;color:#2A2230;letter-spacing:-.02em}
            h2{margin:0 0 10px;font-size:16px;color:#2A2230}
            h3{margin:16px 0 6px;font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:.08em;color:#655C6C}
            .meta{display:flex;flex-wrap:wrap;gap:8px 28px;margin:10px 0 0}.meta dt{font-size:11px;font-weight:600;text-transform:uppercase;letter-spacing:.06em;color:#655C6C}.meta dd{margin:0;color:#2A2230}
            section{padding:20px 0 6px;border-top:1px dashed #CFC8BC}.head+section{border-top:0}
            .legend{list-style:none;padding:0;margin:0;display:grid;gap:6px}.legend li{display:flex;align-items:center;gap:10px}
            .lvl{display:inline-flex;align-items:center;justify-content:center;min-width:24px;height:24px;padding:0 4px;font:600 13px 'IBM Plex Mono',monospace;border-radius:2px}.lvl.none{color:#6B6272}
            .delta{font:600 12px 'IBM Plex Mono',monospace}.mono{font-family:'IBM Plex Mono',monospace}
            .bh{display:flex;justify-content:space-between;align-items:baseline;flex-wrap:wrap;gap:6px 16px;margin-bottom:12px}.bh h2{margin:0}
            .score{display:inline-flex;align-items:baseline;gap:8px;font:700 20px 'IBM Plex Mono',monospace;color:#2A2230}
            table{width:100%;border-collapse:collapse}th{font-size:11px;font-weight:600;text-transform:uppercase;letter-spacing:.05em;color:#655C6C;text-align:left;padding:7px 10px;border-bottom:1px solid #5A2B63}
            td{font-size:13px;padding:7px 10px;border-bottom:1px solid #E0DAD0}td.name{color:#2A2230;font-weight:600}.c{text-align:center}
            .charts{display:grid;grid-template-columns:repeat(auto-fit,minmax(300px,1fr));gap:28px 32px;margin-top:18px;max-width:760px}.charts .wide{grid-column:1/-1}
            figure{margin:0}figcaption{font-size:13px;font-weight:600;color:#2A2230;margin-bottom:8px}img.chart{width:100%;height:auto;display:block}
            .ps{padding:12px 0 4px}.dir{margin:4px 0 8px;font-size:13px;color:#655C6C}.pi{padding:8px 0 10px 10px;border-left:2px solid #5A2B63;margin:6px 0}.pi p{margin:0 0 4px;color:#2A2230}
            ol{margin:2px 0 8px;padding-left:20px;font-size:13px}.faint{color:#6B6272;font-size:13px}.text{white-space:pre-wrap;font-size:13.5px;line-height:1.6}
            footer{display:flex;justify-content:space-between;flex-wrap:wrap;gap:10px;border-top:1px dashed #CFC8BC;padding-top:16px;margin-top:18px;font-size:13px;color:#655C6C}
            @media print{body{background:#fff}.doc{border:0;margin:0;padding:0;max-width:none}tr,.pi{break-inside:avoid}}
        """;

    public static string Build(Child child, Group? group, List<Period> filled, Period period, Period? compare,
        IReadOnlyDictionary<string, string> cur, IReadOnlyDictionary<string, string>? prev, ChildProgram saved, ProgramPlan program,
        IReadOnlyDictionary<string, string> library, OutcomeResult? res, IReadOnlyDictionary<string, bool> parts, WorkspaceData data)
    {
        var h = new StringBuilder();
        var cLabel = compare?.Label ?? "—";
        h.Append($"<header class=\"head\"><div class=\"brand\"><img src=\"{Logo()}\" width=\"28\" height=\"28\" alt=\"\"> Росток · результаты диагностики</div><h1>{E(child.Name)}</h1><dl class=\"meta\">");
        void Meta(string dt, string dd) => h.Append($"<div><dt>{E(dt)}</dt><dd>{E(dd)}</dd></div>");
        if (child.BirthDate.Length > 0) Meta("Дата рождения", $"{Calc.RuDate(child.BirthDate)} ({Calc.AgeText(child.BirthDate)})");
        if (group is not null) Meta("Группа", group.Name);
        Meta("Обследование", $"{period.Year}, {M.PointName(period.Point)}");
        if (compare is not null) Meta("Сравнение с", $"{compare.Year}, {M.PointName(compare.Point)}");
        var legal = child.Relatives.Where(r => r.Legal).ToList();
        if (legal.Count > 0) Meta("Законные представители", string.Join("; ", legal.Select(r => string.Join(" ", new[] { r.Role, r.Name }.Where(x => x.Length > 0)))));
        h.Append("</dl></header>");

        if (parts["speech"] || parts["neuro"] || parts["details"])
        {
            h.Append("<section><h2>Как читать результаты</h2><ul class=\"legend\">");
            for (var i = 0; i < 4; i++) h.Append($"<li>{Lvl(i)} {E(M.ParentLevel[i])}</li>");
            h.Append("</ul>");
            if (res is not null) h.Append($"<p>Итог по речевому развитию за период: <b>{E(res.Label)}</b> (средний балл {Calc.Fmt(Calc.BlockMean(M.SpeechSections, prev!))} → {Calc.Fmt(Calc.BlockMean(M.SpeechSections, cur))}; меньше — ближе к норме).</p>");
            h.Append("</section>");
        }

        foreach (var b in M.Blocks.Where(b => parts[b.Id]))
        {
            var sections = b.Sections.Where(s => s.IsScored).ToList();
            var mA = prev is null ? (double?)null : Calc.BlockMean(b.Sections, prev);
            var mB = Calc.BlockMean(b.Sections, cur);
            h.Append($"<section><div class=\"bh\"><h2>{E(b.Title)}</h2><span class=\"score\">{Calc.Fmt(mB)} {Lvl(Calc.LevelOf(mB))} {(prev is not null ? Delta(mA, mB) : "")}</span></div>");
            h.Append("<table><thead><tr><th>Раздел</th>");
            if (compare is not null) h.Append($"<th class=\"c\">{E(compare.Short)}</th>");
            h.Append($"<th class=\"c\">{E(period.Short)}</th><th>Что это значит</th></tr></thead><tbody>");
            foreach (var s in sections)
            {
                var now = Calc.Stats(s, cur);
                h.Append($"<tr><td class=\"name\">{E(s.Title)}</td>");
                if (compare is not null) h.Append($"<td class=\"c\">{Lvl(Calc.Stats(s, prev!).Level)}</td>");
                h.Append($"<td class=\"c\">{Lvl(now.Level)}</td><td>{(now.Level is null ? "<span class=\"faint\">не обследовалось</span>" : E(M.ParentLevel[now.Level.Value]))}</td></tr>");
            }
            h.Append("</tbody></table>");
            List<RadarAxis> Axes(IEnumerable<Section> ss) => ss.Select(x => new RadarAxis(x.Short, prev is null ? null : Calc.Stats(x, prev).Mean, Calc.Stats(x, cur).Mean, x.Title)).ToList();
            h.Append("<div class=\"charts\">");
            if (b.Id == "speech")
            {
                h.Append($"<figure><figcaption>Речевой профиль: чем ближе к центру, тем ближе к норме</figcaption>{Png(new Radar(Axes(M.SpeechSections), cLabel, period.Label), 420)}</figure>");
                h.Append($"<figure><figcaption>Средний балл по речи на каждом обследовании</figcaption>{Png(new TrendLine(filled.Select(p => new TrendPoint(p.Short, Calc.BlockMean(M.SpeechSections, data.ScoresOf(child.Id, p.Id))))), 420)}</figure>");
            }
            else h.Append($"<figure class=\"wide\"><figcaption>Средний балл по сферам</figcaption>{Png(new Dumbbell(Axes(M.NeuroScored), cLabel, period.Label), 720)}</figure>");
            h.Append("</div></section>");
        }

        if (parts["details"])
        {
            h.Append("<section><h2>Результаты по пробам</h2>");
            foreach (var s in M.AllSections.Where(s => parts[s.BlockId] || (!parts["speech"] && !parts["neuro"])))
            {
                var items = s.Items.Where(i => cur.ContainsKey(i.Id) || (prev?.ContainsKey(i.Id) ?? false)).ToList();
                if (items.Count == 0) continue;
                h.Append($"<h3>{E(s.Title)}</h3><table><thead><tr><th>Проба</th>");
                if (compare is not null) h.Append($"<th class=\"c\">{E(compare.Short)}</th>");
                h.Append($"<th class=\"c\">{E(period.Short)}</th></tr></thead><tbody>");
                string Cell(string? v)
                {
                    var o = M.OptionOf(s, v);
                    if (o is null) return "<span class=\"faint\">—</span>";
                    return s.IsScored ? Lvl(Calc.ItemScore(s, v), o.Mark, o.Label) : E(o.Label);
                }
                foreach (var i in items)
                {
                    h.Append($"<tr><td>{E(s.Kind == Kinds.Sound ? $"Звук [{i.Label}]" : i.Label)}</td>");
                    if (compare is not null) h.Append($"<td class=\"c\">{Cell(prev!.GetValueOrDefault(i.Id))}</td>");
                    h.Append($"<td class=\"c\">{Cell(cur.GetValueOrDefault(i.Id))}</td></tr>");
                }
                h.Append("</tbody></table>");
            }
            h.Append("</section>");
        }

        if (parts["program"])
        {
            h.Append("<section><h2>Программа коррекции</h2>");
            if (program.Active == 0) h.Append("<p class=\"faint\">По результатам обследования коррекционных упражнений не требуется.</p>");
            foreach (var pb in program.Blocks)
                foreach (var ps in pb.Sections)
                {
                    var on = ps.Items.Where(i => !i.Off).ToList();
                    if (on.Count == 0) continue;
                    h.Append($"<div class=\"ps\"><b>{E(ps.Section.Title)}</b>");
                    if (ps.Direction.Length > 0) h.Append($"<p class=\"dir\">Направление работы: {E(ps.Direction)}.</p>");
                    foreach (var it in on)
                    {
                        var lines = CorrectionProgram.ExerciseLines(library.GetValueOrDefault(it.Item.Id)).Concat(CorrectionProgram.ExerciseLines(saved.Notes.GetValueOrDefault(it.Item.Id))).ToList();
                        h.Append($"<div class=\"pi\"><p>{(ps.Section.Kind == Kinds.Sound ? $"Звук <b class=\"mono\">[{E(it.Item.Label)}]</b>" : E(it.Item.Label))}</p>");
                        if (lines.Count > 0) h.Append("<ol>" + string.Concat(lines.Select(l => $"<li>{E(l)}</li>")) + "</ol>");
                        h.Append("</div>");
                    }
                    h.Append("</div>");
                }
            h.Append("</section>");
        }

        if (parts["recs"] && (saved.Recs.Length > 0 || saved.Home.Length > 0))
        {
            h.Append("<section><h2>Рекомендации</h2>");
            if (saved.Recs.Length > 0) h.Append($"<h3>Рекомендации специалиста</h3><p class=\"text\">{E(saved.Recs)}</p>");
            if (saved.Home.Length > 0) h.Append($"<h3>Занятия дома</h3><p class=\"text\">{E(saved.Home)}</p>");
            h.Append("</section>");
        }
        h.Append($"<footer><span>Дата: {Calc.RuDate(DateTime.Today)}</span><span>Специалист: ______________________</span></footer>");

        return "<!doctype html><html lang=\"ru\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">"
            + $"<title>{E(child.Name)} — результаты диагностики</title>"
            + "<link href=\"https://fonts.googleapis.com/css2?family=Golos+Text:wght@400;500;600;700;800&family=IBM+Plex+Mono:wght@400;500;600&display=swap\" rel=\"stylesheet\">"
            + $"<style>{Css}</style></head><body><article class=\"doc\">{h}</article></body></html>";;
    }
}
