namespace Rostok.Core;

public sealed record ProgramItem(Item Item, string? Value, Option? Mark, int? Score, bool Off);

public sealed record ProgramSection(Section Section, List<ProgramItem> Items, int? Level, double? Mean, string Direction);

public sealed record ProgramBlock(Block Block, List<ProgramSection> Sections);

public sealed record ProgramPlan(int Threshold, List<ProgramBlock> Blocks, int Total, int Active);

// Программа коррекции: дефициты среза, сгруппированные по блокам и разделам.
public static class CorrectionProgram
{
    // Проба — дефицит, если балл не ниже порога; звук — если он не в норме; сканирование — если не сформировано.
    private static bool IsDeficit(Section section, string? value, int threshold)
    {
        if (value is null) return false;
        return section.Kind switch
        {
            Kinds.Sound => value != "norm",
            Kinds.Scale => Calc.ItemScore(section, value) >= threshold,
            Kinds.Choice => value != "formed",
            _ => false,
        };
    }

    // off — пробы, которые специалист исключил.
    public static ProgramPlan Build(IReadOnlyDictionary<string, string> scores, ChildProgram? program)
    {
        var threshold = program?.Threshold ?? 2;
        var off = (program?.Off ?? []).ToHashSet();
        var blocks = new List<ProgramBlock>();
        int total = 0, active = 0;
        foreach (var block in M.Blocks)
        {
            var sections = new List<ProgramSection>();
            foreach (var section in block.Sections)
            {
                var items = section.Items
                    .Where(i => IsDeficit(section, scores.GetValueOrDefault(i.Id), threshold))
                    .Select(i =>
                    {
                        var v = scores.GetValueOrDefault(i.Id);
                        return new ProgramItem(i, v, M.OptionOf(section, v), Calc.ItemScore(section, v), off.Contains(i.Id));
                    })
                    .ToList();
                if (items.Count == 0) continue;
                total += items.Count;
                active += items.Count(i => !i.Off);
                var st = section.IsScored ? Calc.Stats(section, scores) : null;
                sections.Add(new ProgramSection(section, items, st?.Level, st?.Mean, M.Recommendations.GetValueOrDefault(section.Id) ?? ""));
            }
            if (sections.Count > 0) blocks.Add(new ProgramBlock(block, sections));
        }
        return new ProgramPlan(threshold, blocks, total, active);
    }

    public static List<string> ExerciseLines(string? text) =>
        (text ?? "").Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
}
