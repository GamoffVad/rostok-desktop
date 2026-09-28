using System.Globalization;

namespace Rostok.Core;

public sealed record SectionStats(int Total, int Filled, int Sum, int Counted, double? Mean, int? Level, int Yes);

public sealed record OutcomeResult(string Id, string Label, double Delta);

public sealed record Age(int Years, int Months);

public static class Calc
{
    // Балл одной пробы по единой шкале 0–3 (null — не заполнено или не балльная проба).
    public static int? ItemScore(Section section, string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (section.Kind == Kinds.Scale) return int.TryParse(value, out var n) ? n : null;
        if (section.Kind == Kinds.Sound) return M.SoundStates.FirstOrDefault(s => s.Value == value)?.Score;
        return null;
    }

    // Пороги уровня. В Excel: ЕСЛИ(ср<=0.9;0;ЕСЛИ(ср<=1.4;1;ЕСЛИ(ср<=2.4;2;3))) при шкале «3 — норма».
    // Здесь шкала зеркальная («0 — норма»), поэтому пороги отражены: 3 − x.
    public static int? LevelOf(double? mean)
    {
        if (mean is null || double.IsNaN(mean.Value)) return null;
        if (mean < 0.6) return 0;
        if (mean < 1.6) return 1;
        if (mean < 2.1) return 2;
        return 3;
    }

    public static SectionStats Stats(Section section, IReadOnlyDictionary<string, string> scores)
    {
        int total = 0, filled = 0, sum = 0, counted = 0, yes = 0;
        foreach (var item in section.Items)
        {
            total++;
            if (!scores.TryGetValue(item.Id, out var v) || string.IsNullOrEmpty(v)) continue;
            filled++;
            var s = ItemScore(section, v);
            if (s is not null) { sum += s.Value; counted++; }
            if (v == "yes") yes++;
        }
        double? mean = counted > 0 ? (double)sum / counted : null;
        return new SectionStats(total, filled, sum, counted, mean, LevelOf(mean), yes);
    }

    public static double? BlockMean(IEnumerable<Section> sections, IReadOnlyDictionary<string, string> scores)
    {
        var means = sections.Where(s => s.IsScored).Select(s => Stats(s, scores).Mean).Where(m => m is not null).Select(m => m!.Value).ToList();
        return means.Count > 0 ? means.Sum() / means.Count : null;
    }

    public static double? SpeechMean(IReadOnlyDictionary<string, string> scores) => BlockMean(M.SpeechSections, scores);

    public static (int Filled, int Total) TotalProgress(IReadOnlyDictionary<string, string> scores)
    {
        int filled = 0, total = 0;
        foreach (var s in M.AllSections)
        {
            var st = Stats(s, scores);
            filled += st.Filled;
            total += st.Total;
        }
        return (filled, total);
    }

    // Итог коррекционной работы: сравнение среднего на начало и конец.
    public static OutcomeResult? Outcome(double? startMean, double? endMean)
    {
        if (startMean is null || endMean is null) return null;
        var delta = startMean.Value - endMean.Value;
        var id = "worse";
        if (LevelOf(endMean) == 0) id = "norm";
        else if (delta >= 1) id = "major";
        else if (delta >= 0.3) id = "minor";
        else if (delta > -0.3) id = "none";
        // подпись итога — из словаря, чтобы правка в «Администрирование → Словари» была видна везде
        return new OutcomeResult(id, M.Outcomes.First(o => o.Id == id).Label, delta);
    }

    // Распределение детей по уровням 0–3 для раздела и среза (аналог СЧЁТЕСЛИ в сводной).
    public static (int[] Dist, int N) LevelDistribution(Section section, IEnumerable<string> childIds, Func<string, IReadOnlyDictionary<string, string>> scoresOf)
    {
        var dist = new int[4];
        var n = 0;
        foreach (var id in childIds)
        {
            var lvl = Stats(section, scoresOf(id)).Level;
            if (lvl is not null) { dist[lvl.Value]++; n++; }
        }
        return (dist, n);
    }

    public static string Fmt(double? x, int digits = 2) =>
        x is null ? "—" : x.Value.ToString("F" + digits, CultureInfo.InvariantCulture).Replace('.', ',');

    public static string FmtDelta(double? x)
    {
        if (x is null) return "";
        var sign = x > 0 ? "+" : x < 0 ? "−" : "";
        return sign + Math.Abs(x.Value).ToString("F2", CultureInfo.InvariantCulture).Replace('.', ',');
    }

    public static DateTime? ParseIso(string? iso) =>
        DateTime.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    public static string Iso(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string RuDate(DateTime d) => d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    public static string RuDate(string? iso) => ParseIso(iso) is { } d ? RuDate(d) : "";

    public static Age? AgeAt(string? birthDate, DateTime? at = null)
    {
        var b = ParseIso(birthDate);
        if (b is null) return null;
        var now = at ?? DateTime.Today;
        var months = (now.Year - b.Value.Year) * 12 + now.Month - b.Value.Month;
        if (now.Day < b.Value.Day) months--;
        if (months < 0) return null;
        return new Age(months / 12, months % 12);
    }

    public static string AgeText(string? birthDate)
    {
        var a = AgeAt(birthDate);
        if (a is null) return "";
        var y = a.Years;
        var word = y % 10 == 1 && y % 100 != 11 ? "год" : y % 10 >= 2 && y % 10 <= 4 && (y % 100 < 12 || y % 100 > 14) ? "года" : "лет";
        return a.Months > 0 ? $"{y} {word} {a.Months} мес." : $"{y} {word}";
    }

    public static string TodayIso() => Iso(DateTime.Today);

    // Самая ранняя допустимая дата рождения: 25 лет назад (дошкольники и младшие школьники с запасом).
    public static string BirthMin() => Iso(new DateTime(DateTime.Today.Year - 25, 1, 1));

    public static string CurrentAcademicYear(DateTime? now = null)
    {
        var d = now ?? DateTime.Today;
        var y = d.Month >= 8 ? d.Year : d.Year - 1;
        return $"{y}–{y + 1}";
    }

    public static List<Period> YearPeriods(string year)
    {
        var key = System.Text.RegularExpressions.Regex.Replace(year, @"\D+", "-");
        return
        [
            new Period { Id = $"{key}:ng", Year = year, Point = "НГ" },
            new Period { Id = $"{key}:kg", Year = year, Point = "КГ" },
        ];
    }

    public static string Lower(string s) => string.IsNullOrEmpty(s) ? s : char.ToLower(s[0], CultureInfo.GetCultureInfo("ru-RU")) + s[1..];
    public static string Upper(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0], CultureInfo.GetCultureInfo("ru-RU")) + s[1..];
}
