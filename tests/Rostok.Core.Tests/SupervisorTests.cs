using Microsoft.Data.Sqlite;
using Rostok.Core.Storage;

namespace Rostok.Core.Tests;

// Старший специалист: пароль администратора базы, роль «руководитель», просмотр без паролей, журнал, сводка.
public class SupervisorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rostok-sup-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private Database NewDb()
    {
        var db = new Database(Path.Combine(_dir, "rostok.db"));
        db.EnsureCreated();
        return db;
    }

    [Fact]
    public void OldDatabaseIsMigrated()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "rostok.db");
        // база версии 1.0: пространства без колонки role
        using (var c = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE workspaces (id TEXT PRIMARY KEY, name TEXT NOT NULL, password_hash TEXT NOT NULL, password_salt TEXT NOT NULL,
                  iterations INTEGER NOT NULL, created_at TEXT NOT NULL, last_opened_at TEXT);
                INSERT INTO workspaces VALUES ('w1', 'Иванова', 'AAAA', 'AAAA', 1, '2026-09-28T10:00:00', NULL);
                """;
            cmd.ExecuteNonQuery();
        }
        var db = new Database(path);
        db.EnsureCreated();
        var list = new Workspaces(db).List();
        Assert.Single(list);
        Assert.Equal(Roles.Employee, list[0].Role);
        Assert.Empty(new Workspaces(db).AccessLog("w1"));
    }

    [Fact]
    public void RoleRequiresAdminPassword()
    {
        var ws = new Workspaces(NewDb());
        var id = ws.Create("Старший психолог", "1234");
        Assert.False(ws.HasAdminPassword());
        Assert.Throws<InvalidOperationException>(() => ws.SetRole(id, Roles.Supervisor, "любой"));
        ws.SetAdminPassword(null, "админ");
        Assert.True(ws.HasAdminPassword());
        Assert.Throws<InvalidOperationException>(() => ws.SetRole(id, Roles.Supervisor, "не тот"));
        ws.SetRole(id, Roles.Supervisor, "админ");
        Assert.True(ws.Get(id)!.IsSupervisor);
        // сменить пароль администратора можно только зная текущий
        Assert.Throws<InvalidOperationException>(() => ws.SetAdminPassword(null, "новый"));
        ws.SetAdminPassword("админ", "новый");
        Assert.True(ws.VerifyAdmin("новый"));
        Assert.False(ws.VerifyAdmin("админ"));
    }

    [Fact]
    public void SupervisorResetsPasswordAndItIsLogged()
    {
        var ws = new Workspaces(NewDb());
        var boss = ws.Create("Старший психолог", "1234");
        var emp = ws.Create("Иванова М. В.", "забыла");
        ws.ResetPassword(emp, "новый пароль", boss, "Старший психолог");
        Assert.True(ws.Verify(emp, "новый пароль"));
        Assert.False(ws.Verify(emp, "забыла"));
        ws.LogAccess(emp, boss, "Старший психолог", "view");
        var log = ws.AccessLog(emp);
        Assert.Equal(2, log.Count);
        Assert.Equal("просмотр пространства", log[0].ActionText);
        Assert.Equal("сброс пароля", log[1].ActionText);
        Assert.All(log, e => Assert.Equal("Старший психолог", e.ViewerName));
    }

    [Fact]
    public void ReadOnlyStoreChangesNothing()
    {
        var db = NewDb();
        var id = new Workspaces(db).Create("Иванова", "1234");
        var own = Store.Open(db, id, "Иванова");
        own.Merge(Demo.Build());
        var child = own.Data.Children[0];
        var period = own.Data.Periods[0];
        var before = own.Data.ScoresOf(child.Id, period.Id)["ph_1"];

        var view = Store.Open(db, id, "Иванова", readOnly: true);
        var blocked = 0;
        view.Blocked += () => blocked++;
        view.SetScore(child.Id, period.Id, "ph_1", before == "3" ? "0" : "3");
        view.RemoveChild(child.Id);
        view.AddGroup("Новая");
        view.SetUi(u => u.SectionId = "lex");
        view.Clear();
        Assert.Equal(4, blocked);
        Assert.Equal(before, view.Data.ScoresOf(child.Id, period.Id)["ph_1"]);
        Assert.Equal(12, view.Data.Children.Count);
        Assert.Equal("lex", view.Ui.SectionId);

        var fresh = Store.Open(db, id, "Иванова");
        Assert.Equal(before, fresh.Data.ScoresOf(child.Id, period.Id)["ph_1"]);
        Assert.Equal(12, fresh.Data.Children.Count);
        Assert.Single(fresh.Data.Groups);
        Assert.NotEqual("lex", fresh.Ui.SectionId);
    }

    [Fact]
    public void SummaryMatchesDynamicsRules()
    {
        var d = Demo.Build(new DateTime(2026, 9, 28));
        var row = OrgSummary.Build(d);
        Assert.Equal(1, row.Groups);
        Assert.Equal(12, row.Children);
        Assert.Equal(3, row.PeriodsWithData);
        Assert.Equal(d.Periods[2].Id, row.LastPeriod!.Id);
        Assert.Equal(d.Periods[0].Id, row.Start!.Id);
        Assert.Equal(d.Periods[1].Id, row.End!.Id);
        Assert.Equal(12, row.Measured);
        Assert.True(row.MeanEnd < row.MeanStart);
        Assert.InRange(row.LastFilled!.Value, 0.9, 1.0);
        var empty = OrgSummary.Build(new WorkspaceData());
        Assert.Equal(0, empty.Children);
        Assert.Equal(0, empty.Measured);
        Assert.Null(empty.LastPeriod);
    }
}
