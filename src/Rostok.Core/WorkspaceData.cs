using System.Globalization;
using System.Text.Json.Nodes;

namespace Rostok.Core;

// Все данные одного рабочего пространства в памяти: группы, дети, срезы, баллы, программы, библиотека, словари.
public sealed class WorkspaceData
{
    public static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();
    private static readonly StringComparer RuCompare = StringComparer.Create(new CultureInfo("ru-RU"), false);

    public List<Group> Groups { get; set; } = [];
    public List<Child> Children { get; set; } = [];
    public List<Period> Periods { get; set; } = [];
    // баллы: ребёнок → срез → проба → значение («0»…«3», «norm», «right»…)
    public Dictionary<string, Dictionary<string, Dictionary<string, string>>> Scores { get; set; } = [];
    public Dictionary<string, Dictionary<string, string>> Notes { get; set; } = [];
    public Dictionary<string, Dictionary<string, ChildProgram>> Programs { get; set; } = [];
    // null → показываются образцы упражнений; пустой словарь — специалист очистил библиотеку.
    public Dictionary<string, string>? Library { get; set; }
    // правки словарей: только отличия от значений по умолчанию
    public JsonObject Dicts { get; set; } = [];

    public IReadOnlyDictionary<string, string> ScoresOf(string childId, string? periodId) =>
        periodId is not null && Scores.TryGetValue(childId, out var byPeriod) && byPeriod.TryGetValue(periodId, out var s) ? s : Empty;

    public bool HasScores(string childId, string periodId) => ScoresOf(childId, periodId).Count > 0;

    public List<Child> ChildrenOf(string? groupId) =>
        Children.Where(c => c.GroupId == groupId).OrderBy(c => c.Name, RuCompare).ToList();

    public IReadOnlyDictionary<string, string> LibraryOf() => Library ?? M.ExampleLibrary;

    public ChildProgram ProgramOf(string childId, string periodId) =>
        Programs.TryGetValue(childId, out var p) && p.TryGetValue(periodId, out var prog) ? prog : new ChildProgram();

    public string NoteOf(string childId, string periodId) =>
        Notes.TryGetValue(childId, out var n) && n.TryGetValue(periodId, out var t) ? t : "";

    public Child? Child(string? id) => Children.FirstOrDefault(c => c.Id == id);
    public Group? Group(string? id) => Groups.FirstOrDefault(g => g.Id == id);
    public Period? Period(string? id) => Periods.FirstOrDefault(p => p.Id == id);

    // Срезы, где у ребёнка есть баллы, — в порядке учебных лет.
    public List<Period> FilledPeriods(string childId) => Periods.Where(p => HasScores(childId, p.Id)).ToList();

    public List<string> Years => Periods.Select(p => p.Year).Distinct().ToList();
}
