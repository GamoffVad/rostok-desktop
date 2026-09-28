using System.Text;

namespace Rostok.Core;

// Черновик заключения: собирается из баллов, специалист правит текст перед печатью.
public static class ReportBuilder
{
    private static readonly Dictionary<string, Dictionary<string, string>> SideWord = new()
    {
        ["nl_eye"] = new() { ["right"] = "правый", ["left"] = "левый" },
        ["nl_hand"] = new() { ["right"] = "правая", ["left"] = "левая" },
        ["nl_foot"] = new() { ["right"] = "правая", ["left"] = "левая" },
    };

    public static string Build(Child child, Group? group, Period period, IReadOnlyDictionary<string, string> scores,
        Period? prevPeriod, IReadOnlyDictionary<string, string>? prevScores, string? note)
    {
        var soundLabel = M.SoundStates.ToDictionary(s => s.Value, s => s.Label);
        var lines = new List<string> { "ЗАКЛЮЧЕНИЕ ПО РЕЗУЛЬТАТАМ ОБСЛЕДОВАНИЯ", "" };
        var birth = child.BirthDate.Length > 0 ? $", дата рождения {Calc.RuDate(child.BirthDate)} ({Calc.AgeText(child.BirthDate)})" : "";
        lines.Add($"Ребёнок: {child.Name}{birth}");
        lines.Add($"Группа: {group?.Name ?? "—"}");
        lines.Add($"Срез: {period.Year} учебный год, {M.PointName(period.Point)}");
        if (child.Tpmpk.Length > 0) lines.Add($"Заключение ТПМПК: {child.Tpmpk}");

        foreach (var block in M.Blocks)
        {
            var parts = new List<string>();
            var recs = new List<string>();
            foreach (var section in block.Sections)
            {
                var st = Calc.Stats(section, scores);
                if (st.Filled == 0) continue;
                var items = section.Items.ToList();
                if (section.IsScored)
                {
                    var text = new StringBuilder($"{section.Title}: {M.LevelPhrase[st.Level!.Value]} (средний балл {Calc.Fmt(st.Mean)}, уровень {st.Level}).");
                    if (section.Kind == Kinds.Sound)
                    {
                        var byState = new List<(string State, List<string> Sounds)>();
                        foreach (var i in items)
                        {
                            var v = scores.GetValueOrDefault(i.Id);
                            if (v is null || v == "norm") continue;
                            var entry = byState.FirstOrDefault(x => x.State == v);
                            if (entry.Sounds is null) { entry = (v, []); byState.Add(entry); }
                            entry.Sounds.Add($"[{i.Label}]");
                        }
                        var list = byState.Select(x => $"{soundLabel.GetValueOrDefault(x.State)} — {string.Join(", ", x.Sounds)}").ToList();
                        if (list.Count > 0) text.Append($" Звуки: {string.Join("; ", list)}.");
                    }
                    else
                    {
                        var hard = items.Where(i => Calc.ItemScore(section, scores.GetValueOrDefault(i.Id)) >= 2).Select(i => Calc.Lower(i.Label)).ToList();
                        var forming = items.Where(i => Calc.ItemScore(section, scores.GetValueOrDefault(i.Id)) == 1).Select(i => Calc.Lower(i.Label)).ToList();
                        if (hard.Count > 0) text.Append($" Требуют коррекции: {string.Join("; ", hard)}.");
                        if (forming.Count > 0) text.Append($" Формируются: {string.Join("; ", forming)}.");
                    }
                    if (prevScores is not null)
                    {
                        var prev = Calc.Stats(section, prevScores);
                        if (prev.Mean is not null)
                        {
                            var d = prev.Mean.Value - st.Mean!.Value;
                            if (d >= 0.3) text.Append($" Динамика положительная (было {Calc.Fmt(prev.Mean)}).");
                            else if (d <= -0.3) text.Append($" Динамика отрицательная (было {Calc.Fmt(prev.Mean)}).");
                            else text.Append(" Без выраженной динамики.");
                        }
                    }
                    parts.Add($"— {text}");
                    if (st.Level >= 2 && M.Recommendations.TryGetValue(section.Id, out var rec) && rec.Length > 0) recs.Add(rec);
                }
                else if (section.Kind == Kinds.Side)
                {
                    var sides = items.Where(i => scores.ContainsKey(i.Id))
                        .Select(i => $"{Calc.Lower(i.Short)} — {SideWord.GetValueOrDefault(i.Id)?.GetValueOrDefault(scores[i.Id]) ?? M.OptionOf(section, scores[i.Id])?.Label}");
                    parts.Add($"— {section.Title}: {string.Join(", ", sides)}.");
                }
                else if (section.Kind == Kinds.YesNo)
                {
                    var yes = items.Where(i => scores.GetValueOrDefault(i.Id) == "yes").Select(i => Calc.Lower(i.Label)).ToList();
                    parts.Add($"— {section.Title}: {(yes.Count > 0 ? string.Join("; ", yes) : "не выявлены")}.");
                }
                else
                {
                    var opt = M.ScanOptions.FirstOrDefault(o => o.Value == scores.GetValueOrDefault(items[0].Id));
                    if (opt is not null) parts.Add($"— {section.Title}: {opt.Label}.");
                }
            }
            if (parts.Count == 0) continue;
            lines.Add("");
            lines.Add(block.Title.ToUpper(System.Globalization.CultureInfo.GetCultureInfo("ru-RU")));
            lines.AddRange(parts);
            if (recs.Count > 0)
            {
                lines.Add("");
                lines.Add("Рекомендации:");
                for (var i = 0; i < recs.Count; i++) lines.Add($"{i + 1}. {Calc.Upper(recs[i])}.");
            }
        }

        if (prevScores is not null && prevPeriod is not null)
        {
            var res = Calc.Outcome(Calc.SpeechMean(prevScores), Calc.SpeechMean(scores));
            if (res is not null)
            {
                lines.Add("");
                lines.Add($"ИТОГ РЕЧЕВОГО РАЗВИТИЯ ({prevPeriod.Year} {prevPeriod.Point} → {period.Year} {period.Point}): {res.Label}; средний балл {Calc.Fmt(Calc.SpeechMean(prevScores))} → {Calc.Fmt(Calc.SpeechMean(scores))}.");
            }
        }
        if (!string.IsNullOrEmpty(note))
        {
            lines.Add("");
            lines.Add($"Наблюдения специалиста: {note}");
        }
        lines.Add("");
        lines.Add($"Дата: {Calc.RuDate(DateTime.Today)}          Специалист: ______________________");
        return string.Join("\n", lines);
    }
}
