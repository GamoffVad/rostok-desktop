using System.Text.Json.Nodes;

namespace Rostok.Core;

// Реестр словарей для «Администрирование → Словари».
// Правки хранятся в данных рабочего пространства только там, где отличаются от значений по умолчанию:
//   табличный словарь — dicts[id][код строки][поле] = текст
//   список            — dicts[id] = [значения]
// Apply накладывает правки на объекты методики на месте; пустая правка = значение по умолчанию.

public sealed record DictRow(string Key, string Lead, int? Level = null, string? Section = null);

public sealed class DictField
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required Func<string, string?> Get { get; init; }
    public required Action<string, string> Set { get; init; }
    public bool Multiline { get; init; }
    public int? Max { get; init; }
    public bool Narrow { get; init; }
}

public sealed class Dict
{
    public required string Id { get; init; }
    public required string Group { get; init; }
    public required string Title { get; init; }
    public bool IsList { get; init; }
    public string? Hint { get; init; }
    public bool FilterBySection { get; init; }
    public Func<List<DictRow>> Rows { get; init; } = () => [];
    public List<DictField> Fields { get; init; } = [];
    public List<string>? List { get; init; }
}

public static class Dicts
{
    public static readonly List<Dict> All;
    public static readonly Dictionary<string, Dict> ById;
    private static readonly Dictionary<string, object> Defaults = [];

    private static DictField F(string key, string label, Func<string, string?> get, Action<string, string> set, bool multiline = false, int? max = null, bool narrow = false) =>
        new() { Key = key, Label = label, Get = get, Set = set, Multiline = multiline, Max = max, Narrow = narrow };

    private static Option? ByValue(IEnumerable<Option> list, string row) => list.FirstOrDefault(o => o.Value == row);
    private static Section? Sec(string id) => M.SectionById.GetValueOrDefault(id);
    private static List<Option> Answers => [.. M.SideOptions, .. M.YesNoOptions];

    static Dicts()
    {
        All =
        [
            new Dict
            {
                Id = "levels", Group = "Оценка", Title = "Уровни 0–3",
                Hint = "Название уровня — в легендах и графиках; описание — в протоколах и справке; формулировки — в заключении и отчёте для родителей.",
                Rows = () => new[] { "0", "1", "2", "3" }.Select(k => new DictRow(k, "", int.Parse(k))).ToList(),
                Fields =
                [
                    F("name", "Название", r => M.LevelNames[int.Parse(r)], (r, v) => M.LevelNames[int.Parse(r)] = v),
                    F("scale", "Описание балла", r => M.Scale[int.Parse(r)].Label, (r, v) => M.Scale[int.Parse(r)].Label = v, multiline: true),
                    F("phrase", "В заключении", r => M.LevelPhrase[int.Parse(r)], (r, v) => M.LevelPhrase[int.Parse(r)] = v, multiline: true),
                    F("parent", "Для родителей", r => M.ParentLevel[int.Parse(r)], (r, v) => M.ParentLevel[int.Parse(r)] = v, multiline: true),
                ],
            },
            new Dict
            {
                Id = "sound", Group = "Оценка", Title = "Этапы звукопроизношения",
                Hint = "Знак — буква в протоколе и на кнопке отметки (1–2 символа). Балл этапа менять нельзя: от него зависит расчёт.",
                Rows = () => M.SoundStates.Select(s => new DictRow(s.Value, $"балл {s.Score}", s.Score)).ToList(),
                Fields =
                [
                    F("mark", "Знак", r => ByValue(M.SoundStates, r)?.Mark, (r, v) => { if (ByValue(M.SoundStates, r) is { } o) o.Mark = v; }, max: 2, narrow: true),
                    F("label", "Название этапа", r => ByValue(M.SoundStates, r)?.Label, (r, v) => { if (ByValue(M.SoundStates, r) is { } o) o.Label = v; }),
                ],
            },
            new Dict
            {
                Id = "answers", Group = "Оценка", Title = "Варианты ответов",
                Hint = "Латеральность (глаз, рука, нога), ошибки и наблюдаемые симптомы.",
                Rows = () => [new("right", "латеральность"), new("left", "латеральность"), new("no", "есть / нет"), new("yes", "есть / нет")],
                Fields =
                [
                    F("mark", "Знак", r => ByValue(Answers, r)?.Mark, (r, v) => { if (ByValue(Answers, r) is { } o) o.Mark = v; }, max: 2, narrow: true),
                    F("label", "Название", r => ByValue(Answers, r)?.Label, (r, v) => { if (ByValue(Answers, r) is { } o) o.Label = v; }),
                ],
            },
            new Dict
            {
                Id = "scan", Group = "Оценка", Title = "Стратегии сканирования",
                Hint = "Варианты для пробы «Стратегия сканирования зрительного поля». Первый вариант считается нормой.",
                Rows = () => M.ScanOptions.Select(o => new DictRow(o.Value, o.Mark)).ToList(),
                Fields = [F("label", "Название", r => ByValue(M.ScanOptions, r)?.Label, (r, v) => { if (ByValue(M.ScanOptions, r) is { } o) o.Label = v; })],
            },
            new Dict
            {
                Id = "blocks", Group = "Методика", Title = "Блоки методики",
                Rows = () => M.Blocks.Select(b => new DictRow(b.Id, b.Id == "speech" ? "речь" : "нейро")).ToList(),
                Fields = [F("title", "Название блока", r => M.Blocks.FirstOrDefault(b => b.Id == r)?.Title, (r, v) => { if (M.Blocks.FirstOrDefault(b => b.Id == r) is { } b) b.Title = v; })],
            },
            new Dict
            {
                Id = "sections", Group = "Методика", Title = "Разделы",
                Hint = "Полное название — в заголовках и отчётах; краткое — на вкладках, в графиках и списках.",
                Rows = () => M.AllSections.Select(s => new DictRow(s.Id, s.BlockId == "speech" ? "речь" : "нейро")).ToList(),
                Fields =
                [
                    F("title", "Полное название", r => Sec(r)?.Title, (r, v) => { if (Sec(r) is { } s) s.Title = v; }),
                    F("short", "Краткое", r => Sec(r)?.Short, (r, v) => { if (Sec(r) is { } s) s.Short = v; }, narrow: true),
                ],
            },
            new Dict
            {
                Id = "groups", Group = "Методика", Title = "Группы проб",
                Hint = "Подзаголовки внутри раздела: «Свистящие», «Словарь признаков»…",
                FilterBySection = true,
                Rows = () => M.AllSections.SelectMany(s => s.Groups.Select((g, i) => new DictRow($"{s.Id}#{i}", s.Short, null, s.Id))).ToList(),
                Fields = [F("title", "Название группы", r => GroupOf(r)?.Title, (r, v) => { if (GroupOf(r) is { } g) g.Title = v; })],
            },
            new Dict
            {
                Id = "items", Group = "Методика", Title = "Пробы",
                Hint = "Полное название — в обследовании, программе и отчётах; краткое — в заголовках столбцов протокола и графиках. Код пробы не меняется, поэтому баллы и упражнения сохраняются.",
                FilterBySection = true,
                Rows = () => M.AllSections.SelectMany(s => s.Items.Select(it => new DictRow(it.Id, it.Id, null, s.Id))).ToList(),
                Fields =
                [
                    F("label", "Полное название", r => M.ItemById.GetValueOrDefault(r)?.Label, (r, v) => { if (M.ItemById.GetValueOrDefault(r) is { } i) i.Label = v; }, multiline: true),
                    F("short", "Краткое", r => M.ItemById.GetValueOrDefault(r)?.Short, (r, v) => { if (M.ItemById.GetValueOrDefault(r) is { } i) i.Short = v; }, narrow: true),
                ],
            },
            new Dict
            {
                Id = "directions", Group = "Методика", Title = "Направления работы",
                Hint = "Текст «Направление работы» в программе коррекции и рекомендации в заключении для разделов с уровнем 2–3.",
                Rows = () => M.AllSections.Where(s => s.IsScored || s.Kind == Kinds.Choice).Select(s => new DictRow(s.Id, s.Short)).ToList(),
                Fields = [F("text", "Направление работы", r => M.Recommendations.GetValueOrDefault(r), (r, v) => M.Recommendations[r] = v, multiline: true)],
            },
            new Dict
            {
                Id = "outcomes", Group = "Результаты", Title = "Итоги коррекционной работы",
                Hint = "Подписи итогов на экране «Динамика», в карте ребёнка, заключении и выгрузках. Правила расчёта не меняются.",
                Rows = () => M.Outcomes.Select(o => new DictRow(o.Id, o.Id switch
                {
                    "norm" => "норма", "major" => "Δ ≥ 1,0", "minor" => "Δ ≥ 0,3", "none" => "|Δ| < 0,3", _ => "Δ ≤ −0,3",
                })).ToList(),
                Fields = [F("label", "Название итога", r => M.Outcomes.FirstOrDefault(o => o.Id == r)?.Label, (r, v) => { if (M.Outcomes.FirstOrDefault(o => o.Id == r) is { } o) o.Label = v; })],
            },
            new Dict
            {
                Id = "thresholds", Group = "Результаты", Title = "Пороги дефицита",
                Hint = "Варианты «Что считать дефицитом» в программе коррекции.",
                Rows = () => M.Thresholds.Select(t => new DictRow(t.Value.ToString(), $"балл ≥ {t.Value}")).ToList(),
                Fields = [F("label", "Название", r => M.Thresholds.FirstOrDefault(t => t.Value.ToString() == r)?.Label, (r, v) => { if (M.Thresholds.FirstOrDefault(t => t.Value.ToString() == r) is { } t) t.Label = v; })],
            },
            new Dict
            {
                Id = "points", Group = "Результаты", Title = "Срезы",
                Hint = "Полное название точки среза — в заключении и отчёте для родителей. Код (НГ, КГ) хранится в данных и не меняется.",
                Rows = () => M.PointNames.Keys.Select(k => new DictRow(k, k)).ToList(),
                Fields = [F("label", "Полное название", r => M.PointNames.GetValueOrDefault(r), (r, v) => M.PointNames[r] = v)],
            },
            new Dict { Id = "roles", Group = "Контакты", Title = "Кем приходится", IsList = true, List = M.Roles, Hint = "Подсказки в поле «Кем приходится» у родителей и родственников. В карточке по-прежнему можно вписать своё." },
            new Dict { Id = "phoneKinds", Group = "Контакты", Title = "Виды телефонов", IsList = true, List = M.PhoneKinds, Hint = "Подсказки для вида телефона; новый телефон получает виды по очереди." },
            new Dict { Id = "addressKinds", Group = "Контакты", Title = "Виды адресов", IsList = true, List = M.AddressKinds, Hint = "Подсказки для вида адреса." },
        ];
        ById = All.ToDictionary(d => d.Id);

        // Значения по умолчанию снимаются один раз — до того, как на объекты легли правки.
        foreach (var d in All)
        {
            if (d.IsList) { Defaults[d.Id] = d.List!.ToList(); continue; }
            var table = new Dictionary<string, Dictionary<string, string>>();
            foreach (var row in d.Rows())
                table[row.Key] = d.Fields.ToDictionary(f => f.Key, f => f.Get(row.Key) ?? "");
            Defaults[d.Id] = table;
        }
    }

    private static ProbeGroup? GroupOf(string key)
    {
        var parts = key.Split('#');
        return parts.Length == 2 && Sec(parts[0]) is { } s && int.TryParse(parts[1], out var i) && i < s.Groups.Count ? s.Groups[i] : null;
    }

    public static string DefaultValue(string dictId, string row, string field) =>
        Defaults.GetValueOrDefault(dictId) is Dictionary<string, Dictionary<string, string>> t && t.TryGetValue(row, out var f) && f.TryGetValue(field, out var v) ? v : "";

    public static List<string> DefaultList(string dictId) =>
        Defaults.GetValueOrDefault(dictId) is List<string> l ? [.. l] : [];

    public static void Apply(JsonObject? overrides)
    {
        overrides ??= [];
        foreach (var d in All)
        {
            var ov = overrides[d.Id];
            if (d.IsList)
            {
                var defaults = DefaultList(d.Id);
                var next = ov is JsonArray arr ? arr.Select(x => x?.GetValue<string>()?.Trim() ?? "").Where(x => x.Length > 0).ToList() : defaults;
                d.List!.Clear();
                d.List.AddRange(next.Count > 0 ? next : defaults);
                continue;
            }
            foreach (var row in d.Rows())
            {
                foreach (var f in d.Fields)
                {
                    var v = (ov as JsonObject)?[row.Key]?[f.Key] is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : null;
                    f.Set(row.Key, !string.IsNullOrWhiteSpace(v) ? v.Trim() : DefaultValue(d.Id, row.Key, f.Key));
                }
            }
        }
    }

    // Сколько значений словаря изменено — для счётчика в списке словарей.
    public static int ChangedCount(string dictId, JsonObject? overrides)
    {
        var ov = overrides?[dictId];
        if (ov is null) return 0;
        if (ov is JsonArray) return 1;
        if (ov is not JsonObject obj) return 0;
        return obj.Sum(row => row.Value is JsonObject fields
            ? fields.Count(f => f.Value is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s))
            : 0);
    }
}
