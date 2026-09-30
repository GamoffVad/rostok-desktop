using Microsoft.Data.Sqlite;
using Rostok.Core.Storage;

namespace Rostok.Core.Tests;

// Пользователи и роли: вход по логину, главный администратор, пространства пользователей, просмотр без изменений, журнал, сводка.
public class AccessTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rostok-acc-" + Guid.NewGuid().ToString("N"));

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
        // база версии 1.1: у пространств свои пароли и роль, пароль администратора базы в meta
        var (hash, salt) = Passwords.Hash("1234");
        using (var c = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = $"""
                CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT);
                INSERT INTO meta VALUES ('admin_hash', 'x'), ('admin_salt', 'x'), ('admin_iterations', '1');
                CREATE TABLE workspaces (id TEXT PRIMARY KEY, name TEXT NOT NULL, password_hash TEXT NOT NULL, password_salt TEXT NOT NULL,
                  iterations INTEGER NOT NULL, created_at TEXT NOT NULL, last_opened_at TEXT, role TEXT NOT NULL DEFAULT 'employee');
                INSERT INTO workspaces VALUES ('w1', 'Иванова', '{hash}', '{salt}', {Passwords.Iterations}, '2026-09-28T10:00:00', NULL, 'employee');
                INSERT INTO workspaces VALUES ('w2', 'admin', '{hash}', '{salt}', {Passwords.Iterations}, '2026-09-28T10:00:00', NULL, 'supervisor');
                CREATE TABLE groups (workspace_id TEXT NOT NULL REFERENCES workspaces (id) ON DELETE CASCADE, id TEXT NOT NULL, name TEXT NOT NULL, sort INTEGER NOT NULL DEFAULT 0, PRIMARY KEY (workspace_id, id));
                INSERT INTO groups VALUES ('w1', 'g1', 'Группа № 1', 0);
                """;
            cmd.ExecuteNonQuery();
        }
        var db = new Database(path);
        db.EnsureCreated();
        db.EnsureCreated(); // повторный запуск ничего не меняет

        var users = new Users(db);
        Assert.Equal(3, users.List().Count);
        Assert.NotNull(users.Authenticate("admin", "admin"));
        // прежний пароль пространства — теперь пароль пользователя, логин — название пространства
        var ivanova = users.Authenticate("Иванова", "1234");
        Assert.NotNull(ivanova);
        Assert.Equal(UserRoles.User, ivanova!.Role);
        var boss = users.Authenticate("admin 2", "1234");
        Assert.Equal(UserRoles.Admin, boss!.Role);

        var ws = new Workspaces(db);
        Assert.Equal(["w1"], ws.ListFor(ivanova).Select(w => w.Id));
        Assert.Equal(2, ws.ListFor(boss).Count);
        Assert.Single(Store.Open(db, "w1", "Иванова").Data.Groups);
        // колонки паролей пространств удалены: новое пространство создаётся без пароля
        ws.Create("Новое", ivanova.Id);
        using var check = db.Open();
        Assert.Null(Database.Scalar(check, null, "SELECT value FROM meta WHERE key = 'admin_hash'"));
    }

    [Fact]
    public void UsersAndRoles()
    {
        var db = NewDb();
        var users = new Users(db);
        var main = users.MainAdmin();
        var id = users.Create("petrova", "Петрова А. С.", "пароль");
        Assert.Throws<InvalidOperationException>(() => users.Create("PETROVA", "Двойник", "пароль"));
        Assert.Throws<InvalidOperationException>(() => users.Create("короткий", "", "12"));
        Assert.Null(users.Authenticate("petrova", "не тот"));
        Assert.Equal("Петрова А. С.", users.Authenticate("Petrova", "пароль")!.Name);

        users.SetRole(id, UserRoles.Admin);
        Assert.True(users.Get(id)!.IsAdmin);
        Assert.Throws<InvalidOperationException>(() => users.SetRole(main.Id, UserRoles.User));
        Assert.Throws<InvalidOperationException>(() => users.Delete(main.Id));
        Assert.Throws<ArgumentException>(() => users.SetRole(id, UserRoles.MainAdmin));

        users.ResetPassword(id, "новый");
        Assert.True(users.Verify(id, "новый"));
        Assert.Throws<InvalidOperationException>(() => users.ChangePassword(id, "старый", "ещё"));
        users.ChangePassword(main.Id, "admin", "надёжный");
        Assert.False(users.HasDefaultPassword(users.MainAdmin()));

        // удалённый пользователь: его пространства с данными переходят главному администратору
        var ws = new Workspaces(db);
        var w = ws.Create("Петрова", id);
        users.Delete(id);
        Assert.Equal(main.Id, ws.Get(w)!.OwnerId);
        Assert.Single(users.List());
    }

    [Fact]
    public void AdminViewIsLogged()
    {
        var db = NewDb();
        var users = new Users(db);
        var ws = new Workspaces(db);
        var user = users.Get(users.Create("ivanova", "Иванова", "1234"))!;
        var admin = users.MainAdmin();
        var w = ws.Get(ws.Create("Иванова", user.Id))!;
        Assert.True(Workspaces.CanEdit(user, w));
        Assert.False(Workspaces.CanEdit(admin, w));
        Assert.True(Workspaces.CanOpen(admin, w));
        Assert.False(Workspaces.CanOpen(users.Get(users.Create("other", "", "1234"))!, w));
        ws.LogAccess(w.Id, admin.Id, admin.DisplayName, "view");
        var log = Assert.Single(ws.AccessLog(w.Id));
        Assert.Equal("просмотр пространства", log.ActionText);
        Assert.Equal("Главный администратор", log.ViewerName);
    }

    [Fact]
    public void ReadOnlyStoreChangesNothing()
    {
        var db = NewDb();
        var id = new Workspaces(db).Create("Иванова", new Users(db).MainAdmin().Id);
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
