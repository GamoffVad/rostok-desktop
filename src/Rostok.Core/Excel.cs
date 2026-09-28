using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using ExcelDataReader;

namespace Rostok.Core;

public sealed record Sheet(string Name, List<object?[]> Rows, int[]? Widths = null);

public sealed record LegacyImport(WorkspaceData Part, int Children, int Cells, string Group);

// Excel: загрузка прежнего файла «Динамика речевого развития», выгрузки протоколов и динамики, шаблон библиотеки упражнений.
public static class Excel
{
    static Excel() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    // Колонки прежнего файла: лист → раздел, первая строка детей, пробы по порядку колонок с C.
    private static readonly (Regex Re, int FirstRow, (string Section, int Count)[] Sections)[] Legacy =
    [
        (new Regex("^звук", RegexOptions.IgnoreCase), 4, [("sound", 13)]),
        (new Regex(@"^л\.?\s*г\.?\s*с", RegexOptions.IgnoreCase), 3, [("lex", 12), ("gram", 6), ("coh", 3)]),
        (new Regex("^фонетика", RegexOptions.IgnoreCase), 3, [("phon", 13)]),
    ];
    // В прежнем файле 3 — норма, 0 — нарушено; в приложении шкала зеркальная.
    private static readonly string[] OldSound = ["broken", "stage", "auto", "norm"];

    // Все листы книги (.xls или .xlsx) как таблицы значений.
    public static List<(string Name, List<object?[]> Grid)> ReadWorkbook(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var sheets = new List<(string, List<object?[]>)>();
        do
        {
            var grid = new List<object?[]>();
            while (reader.Read())
            {
                var row = new object?[reader.FieldCount];
                for (var i = 0; i < row.Length; i++) row[i] = reader.GetValue(i);
                grid.Add(row);
            }
            sheets.Add((reader.Name, grid));
        } while (reader.NextResult());
        return sheets;
    }

    private static object? Cell(List<object?[]> grid, int r, int c) => r < grid.Count && c < grid[r].Length ? grid[r][c] : null;

    public static LegacyImport ImportLegacyWorkbook(string path)
    {
        var book = ReadWorkbook(path);
        var summary = book.FirstOrDefault(s => Regex.IsMatch(s.Name, "динамика", RegexOptions.IgnoreCase));
        if (summary.Grid is null) throw new InvalidOperationException("В файле нет листа «динамика…»: это не файл мониторинга речевого развития.");

        var title = Convert.ToString(Cell(summary.Grid, 0, 0)) ?? "";
        var gm = Regex.Match(title, @"групп[аы]\s*№?\s*[A-Za-z0-9_-]+", RegexOptions.IgnoreCase);
        var groupName = gm.Success ? gm.Value : Regex.Replace(Path.GetFileName(path), @"\.xlsx?$", "", RegexOptions.IgnoreCase);
        if (groupName.StartsWith('г')) groupName = "Г" + groupName[1..];
        var ym = Regex.Match(title, @"(20\d{2})\s*[-–]\s*20\d{2}");
        var startYear = ym.Success ? int.Parse(ym.Groups[1].Value) : DateTime.Today.Year - 1;

        // ФИ детей: колонка B, строки 5–34 сводного листа
        var rows = new List<(int Index, string Name)>();
        for (var r = 4; r < 34; r++)
        {
            var name = (Convert.ToString(Cell(summary.Grid, r, 1)) ?? "").Trim();
            if (name.Length > 0) rows.Add((r - 4, name));
        }
        if (rows.Count == 0) throw new InvalidOperationException("На листе «динамика» не найдены фамилии детей (колонка B, с 5-й строки).");

        var group = new Group { Id = Ids.New(), Name = groupName };
        var children = rows.Select(r => (Child: new Child { Id = Ids.New(), GroupId = group.Id, Name = r.Name }, Row: r.Index)).ToList();
        var periods = Calc.YearPeriods($"{startYear}–{startYear + 1}").Concat(Calc.YearPeriods($"{startYear + 1}–{startYear + 2}")).ToList();
        var scores = children.ToDictionary(c => c.Child.Id, _ => new Dictionary<string, Dictionary<string, string>>());
        var cells = 0;

        foreach (var (sheetName, grid) in book)
        {
            var clean = Regex.Replace(sheetName.Trim(), @"\s+", " ");
            var legacy = Legacy.FirstOrDefault(l => l.Re.IsMatch(clean));
            var m = Regex.Match(clean, @"(НГ|КГ)\s*(\d)\s*г", RegexOptions.IgnoreCase);
            if (legacy.Re is null || !m.Success) continue;
            var idx = (int.Parse(m.Groups[2].Value) - 1) * 2 + (m.Groups[1].Value.ToUpperInvariant() == "НГ" ? 0 : 1);
            if (idx < 0 || idx >= periods.Count) continue;
            var period = periods[idx];
            foreach (var (child, rowIndex) in children)
            {
                var col = 2;
                foreach (var (sectionId, count) in legacy.Sections)
                {
                    var section = M.SectionById[sectionId];
                    var items = section.Items.Take(count).ToList();
                    for (var i = 0; i < items.Count; i++)
                    {
                        var raw = Cell(grid, legacy.FirstRow + rowIndex, col + i);
                        if (raw is null || raw is string { Length: 0 }) continue;
                        if (!double.TryParse(Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture),
                                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var num)) continue;
                        var old = (int)Math.Max(0, Math.Min(3, Math.Round(num, MidpointRounding.AwayFromZero)));
                        if (!scores[child.Id].TryGetValue(period.Id, out var target)) scores[child.Id][period.Id] = target = [];
                        target[items[i].Id] = section.Kind == Kinds.Sound ? OldSound[old] : (3 - old).ToString();
                        cells++;
                    }
                    col += count;
                }
            }
        }

        var part = new WorkspaceData { Groups = [group], Children = children.Select(c => c.Child).ToList(), Periods = periods, Scores = scores };
        return new LegacyImport(part, children.Count, cells, groupName);
    }

    public static void ExportSheets(string path, IEnumerable<Sheet> sheets)
    {
        using var wb = new XLWorkbook();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in sheets)
        {
            var name = Regex.Replace(sheet.Name.Length > 31 ? sheet.Name[..31] : sheet.Name, @"[\\/?*\[\]:]", " ");
            var unique = name;
            for (var n = 2; !used.Add(unique); n++) unique = $"{(name.Length > 28 ? name[..28] : name)} {n}";
            var ws = wb.Worksheets.Add(unique);
            for (var r = 0; r < sheet.Rows.Count; r++)
            {
                var row = sheet.Rows[r];
                for (var c = 0; c < row.Length; c++)
                {
                    var cell = ws.Cell(r + 1, c + 1);
                    switch (row[c])
                    {
                        case null: break;
                        case int i: cell.Value = i; break;
                        case double d: cell.Value = d; break;
                        case string s:
                            cell.Value = s;
                            if (s.Contains('\n')) cell.Style.Alignment.WrapText = true;
                            break;
                        default: cell.Value = Convert.ToString(row[c]); break;
                    }
                }
            }
            if (sheet.Widths is not null)
                for (var c = 0; c < sheet.Widths.Length; c++) ws.Column(c + 1).Width = sheet.Widths[c];
        }
        wb.SaveAs(path);
    }

    // Шаблон библиотеки упражнений: одна строка — одна проба, упражнения в ячейке с новой строки.
    public static void ExportLibraryTemplate(string path, IReadOnlyDictionary<string, string> library)
    {
        var rows = new List<object?[]> { new object?[] { "Блок", "Раздел", "Группа проб", "Проба", "Код (не менять)", "Упражнения — каждое с новой строки (Alt+Enter)" } };
        foreach (var block in M.Blocks)
            foreach (var section in block.Sections)
                foreach (var group in section.Groups)
                    foreach (var item in group.Items)
                        rows.Add([block.Title, section.Title, group.Title, item.Label, item.Id, library.GetValueOrDefault(item.Id) ?? ""]);
        ExportSheets(path, [new Sheet("Упражнения", rows, [16, 28, 26, 44, 10, 90])]);
    }

    // Загрузка заполненного шаблона: обновляются пробы с непустой ячейкой упражнений, остальные не трогаются.
    public static Dictionary<string, string> ImportLibraryTemplate(string path, ISet<string> knownIds)
    {
        var grid = ReadWorkbook(path).First().Grid;
        static string Str(object? v) => (Convert.ToString(v) ?? "").Trim();
        var head = grid.FindIndex(r => r.Any(c => Regex.IsMatch(Str(c), "^код", RegexOptions.IgnoreCase)));
        if (head < 0) throw new InvalidOperationException("Не найдена колонка «Код»: загрузите шаблон, выгруженный с этого экрана.");
        var idCol = Array.FindIndex(grid[head], c => Regex.IsMatch(Str(c), "^код", RegexOptions.IgnoreCase));
        var textCol = Array.FindIndex(grid[head], c => Regex.IsMatch(Str(c), "^упражн", RegexOptions.IgnoreCase));
        if (textCol < 0) throw new InvalidOperationException("Не найдена колонка «Упражнения».");
        var patch = new Dictionary<string, string>();
        foreach (var row in grid.Skip(head + 1))
        {
            var id = idCol < row.Length ? Str(row[idCol]) : "";
            var text = textCol < row.Length ? Str(row[textCol]).Replace("\r\n", "\n") : "";
            if (id.Length > 0 && text.Length > 0 && knownIds.Contains(id)) patch[id] = text;
        }
        return patch;
    }
}
