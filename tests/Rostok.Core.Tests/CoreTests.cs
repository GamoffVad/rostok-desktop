using Rostok.Core;
using Rostok.Core.Storage;

namespace Rostok.Core.Tests;

public class CalcTests
{
    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.59, 0)]
    [InlineData(0.6, 1)]
    [InlineData(1.59, 1)]
    [InlineData(1.6, 2)]
    [InlineData(2.09, 2)]
    [InlineData(2.1, 3)]
    [InlineData(3.0, 3)]
    public void LevelThresholdsMirrorExcel(double mean, int level) => Assert.Equal(level, Calc.LevelOf(mean));

    [Fact]
    public void FormatsLikeWebVersion()
    {
        Assert.Equal("1,46", Calc.Fmt(1.4615));
        Assert.Equal("—", Calc.Fmt(null));
        Assert.Equal("−0,51", Calc.FmtDelta(-0.51));
        Assert.Equal("+0,20", Calc.FmtDelta(0.2));
    }

    [Theory]
    [InlineData("2020-06-08", "2026-09-28", "6 лет 3 мес.")]
    [InlineData("2021-09-28", "2026-09-28", "5 лет")]
    [InlineData("2025-08-01", "2026-09-28", "1 год 1 мес.")]
    [InlineData("2023-01-01", "2026-09-28", "3 года 8 мес.")]
    public void AgeText(string birth, string at, string expected)
    {
        var a = Calc.AgeAt(birth, DateTime.Parse(at))!;
        var y = a.Years;
        var word = y % 10 == 1 && y % 100 != 11 ? "год" : y % 10 >= 2 && y % 10 <= 4 && (y % 100 < 12 || y % 100 > 14) ? "года" : "лет";
        Assert.Equal(expected, a.Months > 0 ? $"{y} {word} {a.Months} мес." : $"{y} {word}");
    }

    [Fact]
    public void OutcomeRules()
    {
        Assert.Equal("norm", Calc.Outcome(2, 0.4)!.Id);
        Assert.Equal("major", Calc.Outcome(2.8, 1.7)!.Id);
        Assert.Equal("minor", Calc.Outcome(2, 1.6)!.Id);
        Assert.Equal("none", Calc.Outcome(2, 1.9)!.Id);
        Assert.Equal("worse", Calc.Outcome(1.5, 2)!.Id);
        Assert.Null(Calc.Outcome(null, 1));
    }
}

// Пример строится тем же генератором, что и в веб-версии: средние баллы совпадают до сотых.
public class DemoParityTests
{
    private static readonly string[] Expected =
    [
        "1,39/0,90/139 | 0,79/0,64/139 | 0,44/0,27/139", "2,62/1,76/139 | 2,25/1,54/139 | 1,74/1,34/139",
        "1,31/0,84/139 | 0,76/0,63/139 | 0,39/0,42/139", "2,22/1,54/139 | 1,32/0,87/139 | 0,70/0,49/139",
        "1,66/1,01/139 | 1,42/1,03/139 | 1,43/1,07/139", "2,42/1,61/139 | 2,27/1,49/139 | 2,26/1,58/139",
        "1,67/1,37/139 | 0,98/0,80/139 | 0,31/0,37/139", "2,26/1,51/139 | 1,58/1,44/139 | 1,43/1,02/139",
        "1,90/1,28/139 | 1,37/1,08/139 | 0,73/0,50/139", "2,48/1,74/139 | 1,66/1,18/139 | 0,82/0,56/139",
        "1,55/1,27/139 | 1,29/0,70/139 | 0,56/0,47/139", "1,82/1,31/139 | 1,48/1,04/139 | 1,03/0,74/139",
    ];

    [Fact]
    public void DemoMatchesWebVersion()
    {
        var d = Demo.Build(new DateTime(2026, 9, 28));
        var rows = d.Children.Select(c => string.Join(" | ", d.Periods.Take(3).Select(p =>
        {
            var s = d.ScoresOf(c.Id, p.Id);
            return $"{Calc.Fmt(Calc.BlockMean(M.Blocks[0].Sections, s))}/{Calc.Fmt(Calc.BlockMean(M.Blocks[1].Sections, s))}/{Calc.TotalProgress(s).Filled}";
        }))).ToArray();
        Assert.Equal(Expected, rows);
        Assert.Equal("2020-06-08", d.Children[1].BirthDate);

        var s2 = d.ScoresOf(d.Children[1].Id, d.Periods[2].Id);
        Assert.Equal("1,62 1,92 1,67 2,17 1,33 1,21 — 1,50 — 1,80 1,17 1,18 1,20 1,36 1,60 1,00 — —",
            string.Join(" ", M.AllSections.Select(s => Calc.Fmt(Calc.Stats(s, s2).Mean))));
        var res = Calc.Outcome(Calc.SpeechMean(d.ScoresOf(d.Children[1].Id, d.Periods[0].Id)), Calc.SpeechMean(s2))!;
        Assert.Equal("minor", res.Id);
        Assert.Equal(0.875641025641025, res.Delta, 12);
    }

    [Fact]
    public void ReportAndProgramBuild()
    {
        var d = Demo.Build(new DateTime(2026, 9, 28));
        var c = d.Children[1];
        var text = ReportBuilder.Build(c, d.Groups[0], d.Periods[2], d.ScoresOf(c.Id, d.Periods[2].Id), d.Periods[1], d.ScoresOf(c.Id, d.Periods[1].Id), null);
        Assert.StartsWith("ЗАКЛЮЧЕНИЕ ПО РЕЗУЛЬТАТАМ ОБСЛЕДОВАНИЯ", text);
        Assert.Contains("РЕЧЕВОЕ РАЗВИТИЕ", text);
        var plan = CorrectionProgram.Build(d.ScoresOf(c.Id, d.Periods[2].Id), d.ProgramOf(c.Id, d.Periods[2].Id));
        Assert.True(plan.Total > 0);
        Assert.Equal(plan.Total, plan.Active);
    }
}

public class StorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rostok-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void CreatesDatabaseAutomatically()
    {
        var db = new Database(Path.Combine(_dir, "sub", "rostok.db"));
        Assert.False(db.Exists);
        db.EnsureCreated();
        Assert.True(db.Exists);
        db.EnsureCreated(); // повторно — без ошибок
        Assert.Empty(new Workspaces(db).List());
    }

    [Fact]
    public void WorkspacesAreIsolatedAndPasswordProtected()
    {
        var db = new Database(Path.Combine(_dir, "rostok.db"));
        db.EnsureCreated();
        var ws = new Workspaces(db);
        var a = ws.Create("Иванова И. И.", "secret1");
        var b = ws.Create("Петрова П. П.", "secret2");
        Assert.NotNull(ws.ValidateName("иванова и. и."));
        Assert.True(ws.Verify(a, "secret1"));
        Assert.False(ws.Verify(a, "secret2"));

        var sa = Store.Open(db, a, "Иванова");
        sa.Merge(Demo.Build());
        sa.SetUi(u => u.SectionId = "lex");
        var sb = Store.Open(db, b, "Петрова");
        Assert.Empty(sb.Data.Groups);

        var again = Store.Open(db, a, "Иванова");
        Assert.Single(again.Data.Groups);
        Assert.Equal(12, again.Data.Children.Count);
        Assert.Equal("lex", again.Ui.SectionId);
        Assert.Equal(12 * 3, again.Data.Scores.Values.Sum(p => p.Count));

        ws.ChangePassword(a, "secret1", "новый");
        Assert.True(ws.Verify(a, "новый"));
        ws.Delete(b, "secret2");
        Assert.Single(ws.List());
    }

    [Fact]
    public void ActionsPersistRowByRow()
    {
        var db = new Database(Path.Combine(_dir, "rostok.db"));
        db.EnsureCreated();
        var id = new Workspaces(db).Create("Тест", "1234");
        var s = Store.Open(db, id, "Тест");
        var g = s.AddGroup("Группа № 1");
        s.AddChildren(g.Id, ["Иванов Ваня", "Петров Петя"]);
        var child = s.Data.ChildrenOf(g.Id)[0];
        var period = s.Data.Periods[0];
        s.SetScore(child.Id, period.Id, "ph_1", "2");
        s.SetNote(child.Id, period.Id, "устаёт к концу");
        s.SetProgram(child.Id, period.Id, p => { p.Threshold = 1; p.Off.Add("ph_1"); });
        s.SetExercise("ph_1", "Игра «Поймай звук»");
        s.SetDictValue("outcomes", "minor", "label", "есть улучшение", Dicts.DefaultValue("outcomes", "minor", "label"));
        s.UpdateChild(child.Id, c => c.Relatives = [Relatives.New("мама")]);

        var r = Store.Open(db, id, "Тест");
        Assert.Equal("2", r.Data.ScoresOf(child.Id, period.Id)["ph_1"]);
        Assert.Equal("устаёт к концу", r.Data.NoteOf(child.Id, period.Id));
        Assert.Equal(1, r.Data.ProgramOf(child.Id, period.Id).Threshold);
        Assert.Equal("Игра «Поймай звук»", r.Data.LibraryOf()["ph_1"]);
        Assert.Equal(M.ExampleLibrary.Count, r.Data.LibraryOf().Count);
        Assert.Equal("есть улучшение", M.Outcomes.First(o => o.Id == "minor").Label);
        Assert.Equal("мама", r.Data.Child(child.Id)!.Relatives[0].Role);

        r.ResetDict("outcomes");
        Assert.Equal("улучшение", M.Outcomes.First(o => o.Id == "minor").Label);
        r.RemoveYear(period.Year);
        Assert.Empty(Store.Open(db, id, "Тест").Data.Scores.Values.SelectMany(p => p.Values));
    }

    [Fact]
    public void BackupRoundTripKeepsWebFormat()
    {
        var d = Demo.Build();
        var json = Backup.Export(d);
        Assert.Contains("\"groupId\"", json);
        var back = Backup.Import(json);
        Assert.Equal(d.Children.Count, back.Children.Count);
        var c = d.Children[0];
        Assert.Equal(d.ScoresOf(c.Id, d.Periods[0].Id).Count, back.ScoresOf(c.Id, d.Periods[0].Id).Count);
        Assert.Equal(d.ScoresOf(c.Id, d.Periods[0].Id)["ph_1"], back.ScoresOf(c.Id, d.Periods[0].Id)["ph_1"]);
        Assert.Equal(d.Children[0].Relatives[0].Phones[0].Value, back.Children[0].Relatives[0].Phones[0].Value);
        Assert.Throws<FormatException>(() => Backup.Import("{\"a\":1}"));
    }

    [Fact]
    public void ImportsLegacyExcelWorkbook()
    {
        // файл с настоящими фамилиями в репозиторий не кладём: путь задаётся переменной окружения
        var path = Environment.GetEnvironmentVariable("ROSTOK_LEGACY_XLS");
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        var res = Excel.ImportLegacyWorkbook(path);
        Assert.True(res.Children > 0);
        Assert.True(res.Cells > 0);
        Assert.StartsWith("Групп", res.Group);
    }

    [Fact]
    public void ExportsAndReadsLibraryTemplate()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "lib.xlsx");
        Excel.ExportLibraryTemplate(path, M.ExampleLibrary);
        var patch = Excel.ImportLibraryTemplate(path, M.ItemById.Keys.ToHashSet());
        Assert.Equal(M.ExampleLibrary.Count(kv => kv.Value.Trim().Length > 0), patch.Count);
    }
}
