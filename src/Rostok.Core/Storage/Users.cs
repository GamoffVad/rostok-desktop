namespace Rostok.Core.Storage;

public static class UserRoles
{
    // Главный администратор: есть в каждой базе (при создании — логин admin, пароль admin), его нельзя удалить или понизить.
    public const string MainAdmin = "main_admin";
    // Администратор: видит рабочие пространства всех пользователей и управляет пользователями.
    public const string Admin = "admin";
    // Пользователь: видит только свои рабочие пространства.
    public const string User = "user";

    public const string DefaultLogin = "admin";
    public const string DefaultPassword = "admin";

    public static string Title(string role) => role switch
    {
        MainAdmin => "главный администратор",
        Admin => "администратор",
        _ => "пользователь",
    };
}

public sealed record UserInfo(string Id, string Login, string Name, string Role, DateTime CreatedAt, DateTime? LastLoginAt, int Workspaces)
{
    public bool IsMainAdmin => Role == UserRoles.MainAdmin;
    public bool IsAdmin => Role is UserRoles.MainAdmin or UserRoles.Admin;
    public string RoleTitle => UserRoles.Title(Role);
    public string DisplayName => Name.Length > 0 ? Name : Login;
}

// Пользователи программы: вход по логину и паролю. Роль определяет, какие рабочие пространства видны.
public sealed class Users(Database db)
{
    public List<UserInfo> List()
    {
        using var c = db.Open();
        return Database.Query(c,
            "SELECT u.id, u.login, u.name, u.role, u.created_at, u.last_login_at, (SELECT COUNT(*) FROM workspaces w WHERE w.owner_id = u.id) FROM users u " +
            "ORDER BY CASE u.role WHEN 'main_admin' THEN 0 WHEN 'admin' THEN 1 ELSE 2 END, u.name COLLATE NOCASE, u.login COLLATE NOCASE",
            r => new UserInfo(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), DateTime.Parse(r.GetString(4)),
                r.IsDBNull(5) ? null : DateTime.Parse(r.GetString(5)), r.GetInt32(6)));
    }

    public UserInfo? Get(string id) => List().FirstOrDefault(u => u.Id == id);

    public UserInfo MainAdmin() => List().First(u => u.IsMainAdmin);

    // Проверка логина и пароля; при успехе запоминается время входа.
    public UserInfo? Authenticate(string login, string password)
    {
        using var c = db.Open();
        var row = Database.Query(c, "SELECT id, password_hash, password_salt, iterations FROM users WHERE login = $l COLLATE NOCASE",
            r => (Id: r.GetString(0), Hash: r.GetString(1), Salt: r.GetString(2), It: r.GetInt32(3)), ("$l", login.Trim())).FirstOrDefault();
        if (row.Id is null || !Passwords.Matches(password, row.Hash, row.Salt, row.It)) return null;
        Database.Exec(c, null, "UPDATE users SET last_login_at = $at WHERE id = $id", ("$at", DateTime.Now.ToString("s")), ("$id", row.Id));
        return Get(row.Id);
    }

    public bool Verify(string id, string password)
    {
        using var c = db.Open();
        var row = Database.Query(c, "SELECT password_hash, password_salt, iterations FROM users WHERE id = $id",
            r => (Hash: r.GetString(0), Salt: r.GetString(1), It: r.GetInt32(2)), ("$id", id)).FirstOrDefault();
        return row.Hash is not null && Passwords.Matches(password, row.Hash, row.Salt, row.It);
    }

    // У главного администратора всё ещё пароль по умолчанию — программа напоминает сменить его.
    public bool HasDefaultPassword(UserInfo user) => user.IsMainAdmin && Verify(user.Id, UserRoles.DefaultPassword);

    public string? ValidateLogin(string login, string? exceptId = null)
    {
        login = login.Trim();
        if (login.Length == 0) return "Введите логин.";
        if (login.Length > 60) return "Логин слишком длинный: не больше 60 символов.";
        if (List().Any(u => u.Id != exceptId && string.Equals(u.Login, login, StringComparison.CurrentCultureIgnoreCase)))
            return "Пользователь с таким логином уже есть.";
        return null;
    }

    public string Create(string login, string name, string password, string role = UserRoles.User)
    {
        if (role is not (UserRoles.Admin or UserRoles.User)) throw new ArgumentException("Неизвестная роль", nameof(role));
        var error = ValidateLogin(login);
        if (error is not null) throw new InvalidOperationException(error);
        if (password.Length < Passwords.MinLength) throw new InvalidOperationException($"Пароль — не короче {Passwords.MinLength} символов.");
        using var c = db.Open();
        return Insert(c, null, login.Trim(), name.Trim(), password, role);
    }

    internal static string Insert(Microsoft.Data.Sqlite.SqliteConnection c, Microsoft.Data.Sqlite.SqliteTransaction? tx, string login, string name, string password, string role)
    {
        var (hash, salt) = Passwords.Hash(password);
        return InsertHashed(c, tx, login, name, role, hash, salt, Passwords.Iterations);
    }

    internal static string InsertHashed(Microsoft.Data.Sqlite.SqliteConnection c, Microsoft.Data.Sqlite.SqliteTransaction? tx, string login, string name, string role, string hash, string salt, int iterations)
    {
        var id = Ids.New() + Ids.New();
        Database.Exec(c, tx,
            "INSERT INTO users(id, login, name, role, password_hash, password_salt, iterations, created_at) VALUES ($id, $l, $n, $r, $h, $s, $it, $at)",
            ("$id", id), ("$l", login), ("$n", name), ("$r", role), ("$h", hash), ("$s", salt), ("$it", iterations), ("$at", DateTime.Now.ToString("s")));
        return id;
    }

    public void Update(string id, string login, string name)
    {
        var error = ValidateLogin(login, id);
        if (error is not null) throw new InvalidOperationException(error);
        using var c = db.Open();
        Database.Exec(c, null, "UPDATE users SET login = $l, name = $n WHERE id = $id", ("$l", login.Trim()), ("$n", name.Trim()), ("$id", id));
    }

    public void SetRole(string id, string role)
    {
        if (role is not (UserRoles.Admin or UserRoles.User)) throw new ArgumentException("Неизвестная роль", nameof(role));
        if (Get(id) is not { } u) throw new InvalidOperationException("Пользователь не найден.");
        if (u.IsMainAdmin) throw new InvalidOperationException("Роль главного администратора изменить нельзя.");
        using var c = db.Open();
        Database.Exec(c, null, "UPDATE users SET role = $r WHERE id = $id", ("$r", role), ("$id", id));
    }

    public void ChangePassword(string id, string current, string next)
    {
        if (!Verify(id, current)) throw new InvalidOperationException("Текущий пароль указан неверно.");
        SetPassword(id, next);
    }

    // Администратор задаёт пользователю новый пароль без старого.
    public void ResetPassword(string id, string next) => SetPassword(id, next);

    private void SetPassword(string id, string next)
    {
        if (next.Length < Passwords.MinLength) throw new InvalidOperationException($"Пароль — не короче {Passwords.MinLength} символов.");
        var (hash, salt) = Passwords.Hash(next);
        using var c = db.Open();
        Database.Exec(c, null, "UPDATE users SET password_hash = $h, password_salt = $s, iterations = $it WHERE id = $id",
            ("$h", hash), ("$s", salt), ("$it", Passwords.Iterations), ("$id", id));
    }

    // Удаление пользователя: его рабочие пространства с данными переходят главному администратору.
    public void Delete(string id)
    {
        if (Get(id) is not { } u) return;
        if (u.IsMainAdmin) throw new InvalidOperationException("Главного администратора удалить нельзя.");
        var main = MainAdmin();
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        Database.Exec(c, tx, "UPDATE workspaces SET owner_id = $m WHERE owner_id = $id", ("$m", main.Id), ("$id", id));
        Database.Exec(c, tx, "DELETE FROM users WHERE id = $id", ("$id", id));
        tx.Commit();
    }
}
