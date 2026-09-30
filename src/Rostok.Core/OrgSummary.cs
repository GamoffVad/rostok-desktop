namespace Rostok.Core;

public static class Dynamics
{
    // Срезы по умолчанию: последний учебный год, где заполнены и начало, и конец; иначе первый и последний срез с данными.
    public static (Period? Start, Period? End) DefaultRange(WorkspaceData d, IReadOnlyCollection<Child> kids)
    {
        var withData = d.Periods.Where(p => kids.Any(k => d.HasScores(k.Id, p.Id))).ToList();
        foreach (var year in withData.Select(p => p.Year).Distinct().Reverse())
        {
            var pair = withData.Where(p => p.Year == year).ToList();
            if (pair.Count == 2) return (pair[0], pair[1]);
        }
        var start = withData.FirstOrDefault() ?? d.Periods.FirstOrDefault();
        var end = withData.Count > 1 ? withData[^1] : d.Periods.ElementAtOrDefault(Math.Min(1, d.Periods.Count - 1));
        return (start, end);
    }
}

// Строка сводки по одному рабочему пространству для экрана «Организация» старшего специалиста.
public sealed record OrgSummaryRow(
    int Groups, int Children, int PeriodsWithData,
    Period? LastPeriod, double? LastFilled,
    Period? Start, Period? End, int Measured, double? MeanStart, double? MeanEnd, int Improved)
{
    public double? ImprovedShare => Measured > 0 ? (double)Improved / Measured : null;
}

public static class OrgSummary
{
    public static OrgSummaryRow Build(WorkspaceData d)
    {
        var kids = d.Children;
        var withData = d.Periods.Where(p => kids.Any(k => d.HasScores(k.Id, p.Id))).ToList();
        var last = withData.LastOrDefault();

        // заполненность последнего среза: отмеченные пробы у детей, обследованных на нём, от всех проб методики
        double? lastFilled = null;
        if (last is not null)
        {
            var examined = kids.Where(k => d.HasScores(k.Id, last.Id)).ToList();
            var totals = examined.Select(k => Calc.TotalProgress(d.ScoresOf(k.Id, last.Id))).ToList();
            var all = totals.Sum(t => t.Total);
            lastFilled = all > 0 ? (double)totals.Sum(t => t.Filled) / all : null;
        }

        // динамика речи — по тем же правилам, что экран «Динамика»: дети, обследованные на обоих срезах
        var (start, end) = Dynamics.DefaultRange(d, kids);
        var measured = new List<(double A, double B, OutcomeResult R)>();
        if (start is not null && end is not null && start != end)
        {
            foreach (var k in kids)
            {
                var a = Calc.SpeechMean(d.ScoresOf(k.Id, start.Id));
                var b = Calc.SpeechMean(d.ScoresOf(k.Id, end.Id));
                if (Calc.Outcome(a, b) is { } r) measured.Add((a!.Value, b!.Value, r));
            }
        }
        return new OrgSummaryRow(
            d.Groups.Count, kids.Count, withData.Count, last, lastFilled,
            start, end, measured.Count,
            measured.Count > 0 ? measured.Average(m => m.A) : null,
            measured.Count > 0 ? measured.Average(m => m.B) : null,
            measured.Count(m => m.R.Id is "norm" or "major" or "minor"));
    }
}
