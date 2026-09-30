using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Rostok.Core.Storage;

// Выбор группы, среза, раздела и словаря запоминается между экранами и сеансами — в рабочем пространстве.
public sealed class UiState
{
    public string? GroupId { get; set; }
    public string? PeriodId { get; set; }
    public string? SectionId { get; set; }
    public string? StartId { get; set; }
    public string? EndId { get; set; }
    public string? LibrarySection { get; set; }
    public string? DictId { get; set; }
    public string? DictSection { get; set; }
}

// Хранилище открытого рабочего пространства: данные в памяти + построчная запись в базу при каждом изменении.
// Действия повторяют действия веб-версии (lib/store.js).
public sealed class Store
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public Database Db { get; }
    public string WorkspaceId { get; }
    public string WorkspaceName { get; set; }
    public WorkspaceData Data { get; private set; } = new();
    public UiState Ui { get; private set; } = new();
    // Текст последней ошибки записи: база недоступна, файл занят, нет прав на папку…
    public string? SaveError { get; private set; }

    // Только чтение: руководитель смотрит пространство сотрудника — ни одно действие ничего не меняет ни в памяти, ни в базе.
    public bool ReadOnly { get; }

    public event Action? Changed;
    public event Action? UiChanged;
    // Попытка изменить данные в режиме только чтения.
    public event Action? Blocked;

    private readonly bool _applyDicts;

    private Store(Database db, string workspaceId, string name, bool readOnly, bool applyDicts)
    {
        Db = db;
        WorkspaceId = workspaceId;
        WorkspaceName = name;
        ReadOnly = readOnly;
        _applyDicts = applyDicts;
    }

    // applyDicts = false — только прочитать данные (сводка по организации), не трогая словари открытого пространства.
    public static Store Open(Database db, string workspaceId, string name, bool readOnly = false, bool applyDicts = true)
    {
        var store = new Store(db, workspaceId, name, readOnly, applyDicts);
        store.Reload();
        return store;
    }

    // Словари — общий реестр программы: при переключении между своим и чужим пространством накладываем словари активного.
    public void Activate() => Dicts.Apply(Data.Dicts);

    private bool Deny()
    {
        if (!ReadOnly) return false;
        Blocked?.Invoke();
        return true;
    }

    // ── Загрузка ─────────────────────────────────────────
    public void Reload()
    {
        using var c = Db.Open();
        var ws = ("$ws", (object?)WorkspaceId);
        var data = new WorkspaceData
        {
            Groups = Database.Query(c, "SELECT id, name FROM groups WHERE workspace_id = $ws ORDER BY sort, rowid",
                r => new Group { Id = r.GetString(0), Name = r.GetString(1) }, ws),
            Children = Database.Query(c, "SELECT id, group_id, name, birth_date, note, tpmpk, relatives FROM children WHERE workspace_id = $ws ORDER BY rowid",
                r => new Child
                {
                    Id = r.GetString(0), GroupId = r.GetString(1), Name = r.GetString(2), BirthDate = r.GetString(3),
                    Note = r.GetString(4), Tpmpk = r.GetString(5), Relatives = ParseRelatives(r.GetString(6)),
                }, ws),
            Periods = Database.Query(c, "SELECT id, year, point FROM periods WHERE workspace_id = $ws ORDER BY sort, rowid",
                r => new Period { Id = r.GetString(0), Year = r.GetString(1), Point = r.GetString(2) }, ws),
        };
        foreach (var (child, period, item, value) in Database.Query(c, "SELECT child_id, period_id, item_id, value FROM scores WHERE workspace_id = $ws",
                     r => (r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3)), ws))
            ScoreBucket(data, child, period)[item] = value;
        foreach (var (child, period, text) in Database.Query(c, "SELECT child_id, period_id, text FROM notes WHERE workspace_id = $ws",
                     r => (r.GetString(0), r.GetString(1), r.GetString(2)), ws))
            Bucket(data.Notes, child)[period] = text;
        foreach (var (child, period, json) in Database.Query(c, "SELECT child_id, period_id, data FROM programs WHERE workspace_id = $ws",
                     r => (r.GetString(0), r.GetString(1), r.GetString(2)), ws))
            Bucket(data.Programs, child)[period] = JsonSerializer.Deserialize<ChildProgram>(json, Json) ?? new ChildProgram();

        var settings = Database.Query(c, "SELECT key, value FROM settings WHERE workspace_id = $ws", r => (r.GetString(0), r.GetString(1)), ws)
            .ToDictionary(x => x.Item1, x => x.Item2);
        if (settings.GetValueOrDefault("library.custom") == "1")
            data.Library = Database.Query(c, "SELECT item_id, text FROM library WHERE workspace_id = $ws", r => (r.GetString(0), r.GetString(1)), ws)
                .ToDictionary(x => x.Item1, x => x.Item2);
        data.Dicts = settings.TryGetValue("dicts", out var dj) && JsonNode.Parse(dj) is JsonObject o ? o : [];
        Ui = settings.TryGetValue("ui", out var uj) ? JsonSerializer.Deserialize<UiState>(uj, Json) ?? new UiState() : new UiState();

        Data = data;
        if (_applyDicts) Dicts.Apply(Data.Dicts);
        Changed?.Invoke();
    }

    public static List<Relative> ParseRelatives(string json)
    {
        try
        {
            var arr = JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json) as JsonArray ?? [];
            return arr.OfType<JsonObject>().Select(NormalizeRelative).ToList();
        }
        catch (JsonException) { return []; }
    }

    // Записи из версии 1.1 веб-приложения хранили один телефон и одну почту строкой — переводим в списки.
    public static Relative NormalizeRelative(JsonObject r)
    {
        string S(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
        List<Contact> List(string key, string fallbackKey, string fallbackKind)
        {
            if (r[key] is JsonArray a)
                return a.OfType<JsonObject>().Select(x => new Contact { Id = S(x["id"]) is { Length: > 0 } id ? id : Ids.New(), Kind = S(x["kind"]), Value = S(x["value"]) }).ToList();
            var old = S(r[fallbackKey]);
            return old.Length > 0 ? [new Contact { Kind = fallbackKind, Value = old }] : [];
        }
        return new Relative
        {
            Id = S(r["id"]) is { Length: > 0 } id ? id : Ids.New(),
            Role = S(r["role"]), Name = S(r["name"]), Note = S(r["note"]),
            Legal = r["legal"] is JsonValue lv && lv.TryGetValue<bool>(out var legal) && legal,
            Phones = List("phones", "phone", M.PhoneKinds.FirstOrDefault() ?? ""),
            Emails = List("emails", "email", ""),
            Addresses = List("addresses", "", ""),
        };
    }

    private static Dictionary<string, string> ScoreBucket(WorkspaceData d, string child, string period)
    {
        var byPeriod = Bucket(d.Scores, child);
        if (!byPeriod.TryGetValue(period, out var s)) byPeriod[period] = s = [];
        return s;
    }

    private static Dictionary<string, T> Bucket<T>(Dictionary<string, Dictionary<string, T>> map, string key)
    {
        if (!map.TryGetValue(key, out var v)) map[key] = v = [];
        return v;
    }

    // ── Запись ───────────────────────────────────────────
    private void Persist(Action<SqliteConnection, SqliteTransaction> write)
    {
        try
        {
            using var c = Db.Open();
            using var tx = c.BeginTransaction();
            write(c, tx);
            tx.Commit();
            SaveError = null;
        }
        catch (Exception e)
        {
            SaveError = e.Message;
        }
    }

    private void Commit(Action<SqliteConnection, SqliteTransaction> write)
    {
        Persist(write);
        Changed?.Invoke();
    }

    private int Exec(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object?)[] args) =>
        Database.Exec(c, tx, sql, [("$ws", WorkspaceId), .. args]);

    private void WriteGroup(SqliteConnection c, SqliteTransaction tx, Group g, int sort) =>
        Exec(c, tx, "INSERT INTO groups(workspace_id, id, name, sort) VALUES ($ws, $id, $name, $sort) ON CONFLICT(workspace_id, id) DO UPDATE SET name = excluded.name",
            ("$id", g.Id), ("$name", g.Name), ("$sort", sort));

    private void WriteChild(SqliteConnection c, SqliteTransaction tx, Child ch) =>
        Exec(c, tx, """
            INSERT INTO children(workspace_id, id, group_id, name, birth_date, note, tpmpk, relatives)
            VALUES ($ws, $id, $g, $name, $b, $note, $t, $rel)
            ON CONFLICT(workspace_id, id) DO UPDATE SET group_id = excluded.group_id, name = excluded.name, birth_date = excluded.birth_date,
              note = excluded.note, tpmpk = excluded.tpmpk, relatives = excluded.relatives
            """,
            ("$id", ch.Id), ("$g", ch.GroupId), ("$name", ch.Name), ("$b", ch.BirthDate), ("$note", ch.Note), ("$t", ch.Tpmpk),
            ("$rel", JsonSerializer.Serialize(ch.Relatives, Json)));

    private void WritePeriod(SqliteConnection c, SqliteTransaction tx, Period p, int sort) =>
        Exec(c, tx, "INSERT OR IGNORE INTO periods(workspace_id, id, year, point, sort) VALUES ($ws, $id, $y, $p, $sort)",
            ("$id", p.Id), ("$y", p.Year), ("$p", p.Point), ("$sort", sort));

    private void WriteScore(SqliteConnection c, SqliteTransaction tx, string child, string period, string item, string? value)
    {
        if (value is null)
            Exec(c, tx, "DELETE FROM scores WHERE workspace_id = $ws AND child_id = $c AND period_id = $p AND item_id = $i", ("$c", child), ("$p", period), ("$i", item));
        else
            Exec(c, tx, "INSERT INTO scores(workspace_id, child_id, period_id, item_id, value) VALUES ($ws, $c, $p, $i, $v) ON CONFLICT(workspace_id, child_id, period_id, item_id) DO UPDATE SET value = excluded.value",
                ("$c", child), ("$p", period), ("$i", item), ("$v", value));
    }

    private void WriteNote(SqliteConnection c, SqliteTransaction tx, string child, string period, string text) =>
        Exec(c, tx, "INSERT INTO notes(workspace_id, child_id, period_id, text) VALUES ($ws, $c, $p, $t) ON CONFLICT(workspace_id, child_id, period_id) DO UPDATE SET text = excluded.text",
            ("$c", child), ("$p", period), ("$t", text));

    private void WriteProgram(SqliteConnection c, SqliteTransaction tx, string child, string period, ChildProgram prog) =>
        Exec(c, tx, "INSERT INTO programs(workspace_id, child_id, period_id, data) VALUES ($ws, $c, $p, $d) ON CONFLICT(workspace_id, child_id, period_id) DO UPDATE SET data = excluded.data",
            ("$c", child), ("$p", period), ("$d", JsonSerializer.Serialize(prog, Json)));

    private void WriteSetting(SqliteConnection c, SqliteTransaction tx, string key, string? value)
    {
        if (value is null) Exec(c, tx, "DELETE FROM settings WHERE workspace_id = $ws AND key = $k", ("$k", key));
        else Exec(c, tx, "INSERT INTO settings(workspace_id, key, value) VALUES ($ws, $k, $v) ON CONFLICT(workspace_id, key) DO UPDATE SET value = excluded.value", ("$k", key), ("$v", value));
    }

    private void WriteLibrary(SqliteConnection c, SqliteTransaction tx, Dictionary<string, string>? library)
    {
        Exec(c, tx, "DELETE FROM library WHERE workspace_id = $ws");
        WriteSetting(c, tx, "library.custom", library is null ? null : "1");
        if (library is null) return;
        foreach (var (k, v) in library)
            Exec(c, tx, "INSERT INTO library(workspace_id, item_id, text) VALUES ($ws, $i, $t)", ("$i", k), ("$t", v));
    }

    private void DeleteChildRows(SqliteConnection c, SqliteTransaction tx, string childId)
    {
        foreach (var table in new[] { "scores", "notes", "programs" })
            Exec(c, tx, $"DELETE FROM {table} WHERE workspace_id = $ws AND child_id = $c", ("$c", childId));
        Exec(c, tx, "DELETE FROM children WHERE workspace_id = $ws AND id = $c", ("$c", childId));
    }

    private void WriteAll(SqliteConnection c, SqliteTransaction tx, WorkspaceData d, bool wipe)
    {
        if (wipe)
            foreach (var table in new[] { "scores", "notes", "programs", "library", "children", "groups", "periods" })
                Exec(c, tx, $"DELETE FROM {table} WHERE workspace_id = $ws");
        var sort = Data.Groups.Count;
        foreach (var g in d.Groups) WriteGroup(c, tx, g, sort++);
        foreach (var ch in d.Children) WriteChild(c, tx, ch);
        sort = wipe ? 0 : Data.Periods.Count;
        foreach (var p in d.Periods) WritePeriod(c, tx, p, sort++);
        foreach (var (child, byPeriod) in d.Scores)
            foreach (var (period, items) in byPeriod)
                foreach (var (item, value) in items) WriteScore(c, tx, child, period, item, value);
        foreach (var (child, byPeriod) in d.Notes)
            foreach (var (period, text) in byPeriod) WriteNote(c, tx, child, period, text);
        foreach (var (child, byPeriod) in d.Programs)
            foreach (var (period, prog) in byPeriod) WriteProgram(c, tx, child, period, prog);
    }

    // ── Действия ─────────────────────────────────────────
    public void SetUi(Action<UiState> patch)
    {
        patch(Ui);
        // выбор руководителя на экранах не записывается в настройки сотрудника
        if (ReadOnly) { UiChanged?.Invoke(); return; }
        Persist((c, tx) => WriteSetting(c, tx, "ui", JsonSerializer.Serialize(Ui, Json)));
        UiChanged?.Invoke();
    }

    public Group AddGroup(string name)
    {
        if (Deny()) return new Group { Name = name.Trim() };
        var group = new Group { Id = Ids.New(), Name = name.Trim() };
        var newPeriods = Data.Periods.Count == 0 ? Calc.YearPeriods(Calc.CurrentAcademicYear()) : [];
        Data.Groups.Add(group);
        Data.Periods.AddRange(newPeriods);
        Commit((c, tx) =>
        {
            WriteGroup(c, tx, group, Data.Groups.Count - 1);
            for (var i = 0; i < newPeriods.Count; i++) WritePeriod(c, tx, newPeriods[i], i);
        });
        return group;
    }

    public void RenameGroup(string id, string name)
    {
        if (Deny()) return;
        var g = Data.Group(id);
        if (g is null) return;
        g.Name = name.Trim();
        Commit((c, tx) => WriteGroup(c, tx, g, Data.Groups.IndexOf(g)));
    }

    public void RemoveGroup(string id)
    {
        if (Deny()) return;
        var kids = Data.Children.Where(c => c.GroupId == id).Select(c => c.Id).ToList();
        Data.Groups.RemoveAll(g => g.Id == id);
        Data.Children.RemoveAll(c => c.GroupId == id);
        foreach (var k in kids) { Data.Scores.Remove(k); Data.Notes.Remove(k); Data.Programs.Remove(k); }
        Commit((c, tx) =>
        {
            foreach (var k in kids) DeleteChildRows(c, tx, k);
            Exec(c, tx, "DELETE FROM groups WHERE workspace_id = $ws AND id = $id", ("$id", id));
        });
    }

    public Child AddChild(Child child)
    {
        if (Deny()) return child;
        if (string.IsNullOrEmpty(child.Id)) child.Id = Ids.New();
        Data.Children.Add(child);
        Commit((c, tx) => WriteChild(c, tx, child));
        return child;
    }

    public void AddChildren(string groupId, IEnumerable<string> names)
    {
        if (Deny()) return;
        var list = names.Select(n => new Child { Id = Ids.New(), GroupId = groupId, Name = n }).ToList();
        Data.Children.AddRange(list);
        Commit((c, tx) => { foreach (var ch in list) WriteChild(c, tx, ch); });
    }

    public void UpdateChild(string id, Action<Child> patch)
    {
        if (Deny()) return;
        var ch = Data.Child(id);
        if (ch is null) return;
        patch(ch);
        Commit((c, tx) => WriteChild(c, tx, ch));
    }

    public void RemoveChild(string id)
    {
        if (Deny()) return;
        Data.Children.RemoveAll(c => c.Id == id);
        Data.Scores.Remove(id); Data.Notes.Remove(id); Data.Programs.Remove(id);
        Commit((c, tx) => DeleteChildRows(c, tx, id));
    }

    public void AddYear(string year)
    {
        if (Deny()) return;
        if (Data.Periods.Any(p => p.Year == year)) return;
        var periods = Calc.YearPeriods(year);
        var start = Data.Periods.Count;
        Data.Periods.AddRange(periods);
        Commit((c, tx) => { for (var i = 0; i < periods.Count; i++) WritePeriod(c, tx, periods[i], start + i); });
    }

    public void RemoveYear(string year)
    {
        if (Deny()) return;
        var gone = Data.Periods.Where(p => p.Year == year).Select(p => p.Id).ToHashSet();
        Data.Periods.RemoveAll(p => gone.Contains(p.Id));
        foreach (var byPeriod in Data.Scores.Values) foreach (var id in gone) byPeriod.Remove(id);
        foreach (var byPeriod in Data.Notes.Values) foreach (var id in gone) byPeriod.Remove(id);
        foreach (var byPeriod in Data.Programs.Values) foreach (var id in gone) byPeriod.Remove(id);
        Commit((c, tx) =>
        {
            foreach (var id in gone)
            {
                foreach (var table in new[] { "scores", "notes", "programs" })
                    Exec(c, tx, $"DELETE FROM {table} WHERE workspace_id = $ws AND period_id = $p", ("$p", id));
                Exec(c, tx, "DELETE FROM periods WHERE workspace_id = $ws AND id = $p", ("$p", id));
            }
        });
    }

    public void SetScore(string childId, string periodId, string itemId, string? value)
    {
        if (Deny()) return;
        var bucket = ScoreBucket(Data, childId, periodId);
        if (value is null) bucket.Remove(itemId); else bucket[itemId] = value;
        Commit((c, tx) => WriteScore(c, tx, childId, periodId, itemId, value));
    }

    // Несколько отметок разом: null стирает пробу.
    public void SetMany(string childId, string periodId, IReadOnlyDictionary<string, string?> patch)
    {
        if (Deny()) return;
        var bucket = ScoreBucket(Data, childId, periodId);
        foreach (var (k, v) in patch) { if (v is null) bucket.Remove(k); else bucket[k] = v; }
        Commit((c, tx) => { foreach (var (k, v) in patch) WriteScore(c, tx, childId, periodId, k, v); });
    }

    public void SetNote(string childId, string periodId, string text)
    {
        if (Deny()) return;
        Bucket(Data.Notes, childId)[periodId] = text;
        Commit((c, tx) => WriteNote(c, tx, childId, periodId, text));
    }

    public void SetProgram(string childId, string periodId, Action<ChildProgram> patch)
    {
        if (Deny()) return;
        var byPeriod = Bucket(Data.Programs, childId);
        if (!byPeriod.TryGetValue(periodId, out var prog)) byPeriod[periodId] = prog = new ChildProgram();
        patch(prog);
        Commit((c, tx) => WriteProgram(c, tx, childId, periodId, prog));
    }

    public void SetExercise(string itemId, string text)
    {
        if (Deny()) return;
        // первая правка превращает образцы в свою библиотеку
        var first = Data.Library is null;
        Data.Library ??= new Dictionary<string, string>(M.ExampleLibrary);
        Data.Library[itemId] = text;
        var lib = Data.Library;
        Commit((c, tx) =>
        {
            if (first) WriteLibrary(c, tx, lib);
            else Exec(c, tx, "INSERT INTO library(workspace_id, item_id, text) VALUES ($ws, $i, $t) ON CONFLICT(workspace_id, item_id) DO UPDATE SET text = excluded.text", ("$i", itemId), ("$t", text));
        });
    }

    public void SetLibrary(Dictionary<string, string>? library)
    {
        if (Deny()) return;
        Data.Library = library is null ? null : new Dictionary<string, string>(library);
        Commit((c, tx) => WriteLibrary(c, tx, Data.Library));
    }

    private void SaveDicts()
    {
        Dicts.Apply(Data.Dicts);
        Commit((c, tx) => WriteSetting(c, tx, "dicts", Data.Dicts.Count == 0 ? null : Data.Dicts.ToJsonString()));
    }

    // Правка словаря: значение, совпавшее с умолчанием, не хранится.
    public void SetDictValue(string dictId, string row, string field, string value, string def)
    {
        if (Deny()) return;
        var dict = Data.Dicts[dictId] as JsonObject ?? [];
        var fields = dict[row] as JsonObject ?? [];
        if (value == def) fields.Remove(field); else fields[field] = value;
        dict.Remove(row);
        if (fields.Count > 0) dict[row] = fields.DeepClone();
        Data.Dicts.Remove(dictId);
        if (dict.Count > 0) Data.Dicts[dictId] = dict.DeepClone();
        SaveDicts();
    }

    public void SetDictList(string dictId, List<string> list)
    {
        if (Deny()) return;
        Data.Dicts.Remove(dictId);
        Data.Dicts[dictId] = new JsonArray(list.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());
        SaveDicts();
    }

    public void ResetDict(string dictId)
    {
        if (Deny()) return;
        Data.Dicts.Remove(dictId);
        SaveDicts();
    }

    // Восстановление из копии: все данные пространства заменяются содержимым файла.
    public void ReplaceAll(WorkspaceData next)
    {
        if (Deny()) return;
        Data = new WorkspaceData();
        Persist((c, tx) =>
        {
            WriteAll(c, tx, next, wipe: true);
            WriteLibrary(c, tx, next.Library);
            WriteSetting(c, tx, "dicts", next.Dicts.Count == 0 ? null : next.Dicts.ToJsonString());
        });
        Data = next;
        Dicts.Apply(Data.Dicts);
        Changed?.Invoke();
    }

    // Добавление группы из Excel или примера: существующие данные не трогаются.
    public void Merge(WorkspaceData part)
    {
        if (Deny()) return;
        var newPeriods = part.Periods.Where(p => Data.Periods.All(q => q.Id != p.Id)).ToList();
        var toWrite = new WorkspaceData
        {
            Groups = part.Groups, Children = part.Children, Periods = newPeriods,
            Scores = part.Scores, Notes = part.Notes, Programs = part.Programs,
        };
        Persist((c, tx) => WriteAll(c, tx, toWrite, wipe: false));
        Data.Groups.AddRange(part.Groups);
        Data.Children.AddRange(part.Children);
        Data.Periods.AddRange(newPeriods);
        foreach (var (k, v) in part.Scores) Data.Scores[k] = v;
        foreach (var (k, v) in part.Notes) Data.Notes[k] = v;
        foreach (var (k, v) in part.Programs) Data.Programs[k] = v;
        Changed?.Invoke();
    }

    // Очистка: удаляются группы, дети, баллы, срезы, своя библиотека и правки словарей (как в веб-версии).
    // Само рабочее пространство, его пароль и выбор на экранах остаются.
    public void Clear()
    {
        if (Deny()) return;
        Data = new WorkspaceData();
        Persist((c, tx) =>
        {
            foreach (var table in new[] { "scores", "notes", "programs", "library", "children", "groups", "periods" })
                Exec(c, tx, $"DELETE FROM {table} WHERE workspace_id = $ws");
            WriteSetting(c, tx, "library.custom", null);
            WriteSetting(c, tx, "dicts", null);
        });
        Dicts.Apply(Data.Dicts);
        Changed?.Invoke();
    }

    // Текущие группа и срез с запасным вариантом, если сохранённый выбор удалён.
    public Group? SelectedGroup => Data.Group(Ui.GroupId) ?? Data.Groups.FirstOrDefault();
    public Period? SelectedPeriod => Data.Period(Ui.PeriodId) ?? Data.Periods.FirstOrDefault();
}
