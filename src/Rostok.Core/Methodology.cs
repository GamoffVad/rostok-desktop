using System.Reflection;
using System.Text.Json;

namespace Rostok.Core;

// Методика: перенесена из файла «Динамика речевого развития» (протоколы + «Лист2»).
// Единая шкала для всех проб: 0 — возрастная норма … 3 — выраженное несоответствие норме.
// Тексты — изменяемые объекты: правки из «Администрирование → Словари» накладываются на них на месте (Dicts).

public sealed class Option
{
    public string Value { get; init; } = "";
    public string Mark { get; set; } = "";
    public string Label { get; set; } = "";
    public string Short { get; set; } = "";
    public int Score { get; init; }
}

public sealed class Item
{
    public string Id { get; init; } = "";
    public string Label { get; set; } = "";
    public string Short { get; set; } = "";
}

public sealed class ProbeGroup
{
    public string Title { get; set; } = "";
    public List<Item> Items { get; init; } = [];
}

public static class Kinds
{
    public const string Scale = "scale";   // 0–3
    public const string Sound = "sound";   // этап работы над звуком
    public const string Side = "side";     // правый / левый
    public const string YesNo = "yesno";   // есть / нет
    public const string Choice = "choice"; // стратегия сканирования
}

public sealed class Section
{
    public string Id { get; init; } = "";
    public string Title { get; set; } = "";
    public string Short { get; set; } = "";
    public string Kind { get; init; } = Kinds.Scale;
    public string? Hint { get; init; }
    public string BlockId { get; internal set; } = "";
    public List<ProbeGroup> Groups { get; init; } = [];

    public IEnumerable<Item> Items => Groups.SelectMany(g => g.Items);
    // Разделы, которые сводятся к уровню 0–3 (участвуют в динамике).
    public bool IsScored => Kind is Kinds.Scale or Kinds.Sound;
}

public sealed class Block
{
    public string Id { get; init; } = "";
    public string Title { get; set; } = "";
    public List<Section> Sections { get; init; } = [];
}

public sealed class Outcome
{
    public string Id { get; init; } = "";
    public string Label { get; set; } = "";
}

public sealed class Threshold
{
    public int Value { get; init; }
    public string Label { get; set; } = "";
}

public static class M
{
    public static readonly List<Option> Scale;
    public static readonly List<Option> SoundStates;
    public static readonly List<Option> SideOptions;
    public static readonly List<Option> YesNoOptions;
    public static readonly List<Option> ScanOptions;
    public static readonly List<Block> Blocks;
    public static readonly List<Section> AllSections;
    public static readonly Dictionary<string, Section> SectionById;
    public static readonly Dictionary<string, Item> ItemById;
    public static readonly Dictionary<string, Section> SectionOfItem;

    // Рекомендации для заключения и «Направление работы» в программе — по разделам с уровнем 2–3.
    public static readonly Dictionary<string, string> Recommendations;
    // Названия уровней 0–3 — в легендах и графиках.
    public static readonly string[] LevelNames;
    // Формулировки уровня в черновике заключения специалиста.
    public static readonly string[] LevelPhrase;
    // Формулировки уровня в отчёте для родителей — простым языком.
    public static readonly string[] ParentLevel;
    // Итог коррекционной работы: id фиксированы (от них зависят расчёт и цвета), подписи — словарь.
    public static readonly List<Outcome> Outcomes;
    // Порог дефицита в программе коррекции: значение — минимальный балл пробы.
    public static readonly List<Threshold> Thresholds;
    // Точки среза: код (НГ, КГ) хранится в данных, словарь — только полное название.
    public static readonly Dictionary<string, string> PointNames;
    // Подсказки в карточке родителя; можно вписать и своё значение.
    public static readonly List<string> Roles;
    public static readonly List<string> PhoneKinds;
    public static readonly List<string> AddressKinds;
    // Образцы упражнений по пробам: показываются, пока специалист не задал свою библиотеку.
    public static readonly IReadOnlyDictionary<string, string> ExampleLibrary;

    public static List<Section> SpeechSections => Blocks[0].Sections;
    public static List<Section> NeuroScored => Blocks[1].Sections.Where(s => s.IsScored).ToList();

    static M()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Rostok.Core.Resources.methodology.json")
            ?? throw new InvalidOperationException("Не найдена методика (methodology.json).");
        using var doc = JsonDocument.Parse(stream);
        var r = doc.RootElement;
        string S(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
        string V(JsonElement e) => e.GetProperty("value").ValueKind == JsonValueKind.Number ? e.GetProperty("value").GetInt32().ToString() : e.GetProperty("value").GetString()!;
        List<Option> Opts(string name) => r.GetProperty(name).EnumerateArray().Select(e => new Option
        {
            Value = V(e), Mark = S(e, "mark"), Label = S(e, "label"), Short = S(e, "short"),
            Score = e.TryGetProperty("score", out var sc) ? sc.GetInt32() : int.TryParse(V(e), out var n) ? n : 0,
        }).ToList();

        Scale = Opts("scale");
        foreach (var o in Scale) { o.Mark = o.Value; }
        SoundStates = Opts("soundStates");
        SideOptions = Opts("sideOptions");
        YesNoOptions = Opts("yesnoOptions");
        ScanOptions = Opts("scanOptions");

        Blocks = r.GetProperty("blocks").EnumerateArray().Select(b => new Block
        {
            Id = S(b, "id"),
            Title = S(b, "title"),
            Sections = b.GetProperty("sections").EnumerateArray().Select(s => new Section
            {
                Id = S(s, "id"), Title = S(s, "title"), Short = S(s, "short"), Kind = S(s, "kind"),
                Hint = s.TryGetProperty("hint", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString() : null,
                Groups = s.GetProperty("groups").EnumerateArray().Select(g => new ProbeGroup
                {
                    Title = S(g, "title"),
                    Items = g.GetProperty("items").EnumerateArray().Select(i => new Item { Id = S(i, "id"), Label = S(i, "label"), Short = S(i, "short") }).ToList(),
                }).ToList(),
            }).ToList(),
        }).ToList();
        foreach (var b in Blocks) foreach (var s in b.Sections) s.BlockId = b.Id;
        AllSections = Blocks.SelectMany(b => b.Sections).ToList();
        SectionById = AllSections.ToDictionary(s => s.Id);
        ItemById = new();
        SectionOfItem = new();
        foreach (var s in AllSections)
            foreach (var i in s.Items) { ItemById[i.Id] = i; SectionOfItem[i.Id] = s; }

        Recommendations = r.GetProperty("recommendations").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
        LevelNames = r.GetProperty("levelNames").EnumerateArray().Select(e => e.GetString()!).ToArray();
        LevelPhrase = r.GetProperty("levelPhrase").EnumerateArray().Select(e => e.GetString()!).ToArray();
        ParentLevel = r.GetProperty("parentLevel").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Outcomes = r.GetProperty("outcomes").EnumerateArray().Select(e => new Outcome { Id = S(e, "id"), Label = S(e, "label") }).ToList();
        Thresholds = r.GetProperty("thresholds").EnumerateArray().Select(e => new Threshold { Value = e.GetProperty("value").GetInt32(), Label = S(e, "label") }).ToList();
        PointNames = r.GetProperty("pointNames").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
        Roles = r.GetProperty("roles").EnumerateArray().Select(e => e.GetString()!).ToList();
        PhoneKinds = r.GetProperty("phoneKinds").EnumerateArray().Select(e => e.GetString()!).ToList();
        AddressKinds = r.GetProperty("addressKinds").EnumerateArray().Select(e => e.GetString()!).ToList();
        ExampleLibrary = r.GetProperty("exampleLibrary").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
    }

    public static List<Option> OptionsFor(Section section) => section.Kind switch
    {
        Kinds.Scale => Scale,
        Kinds.Sound => SoundStates,
        Kinds.Side => SideOptions,
        Kinds.YesNo => YesNoOptions,
        _ => ScanOptions,
    };

    public static Option? OptionOf(Section section, string? value) =>
        value is null ? null : OptionsFor(section).FirstOrDefault(o => o.Value == value);

    public static string PointName(string point) => PointNames.TryGetValue(point, out var n) ? n : point;
}
