using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rostok.Core.Storage;

// Настройки этого компьютера: путь к базе данных и последнее открытое рабочее пространство.
// Порядок: %APPDATA%\Rostok\settings.json → appsettings.json рядом с программой → Data\rostok.db в папке программы.
// Данные и настройки сотрудника хранятся не здесь, а в его рабочем пространстве в базе.
public sealed class AppSettings
{
    public const string DefaultDatabasePath = @"Data\rostok.db";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public string? DatabasePath { get; set; }
    public string? LastWorkspaceId { get; set; }

    [JsonIgnore]
    public static string UserFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rostok", "settings.json");
    [JsonIgnore]
    public static string AppFile => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    // Путь из appsettings.json (его может заранее прописать администратор сети) или стандартный.
    public static string DeploymentDatabasePath()
    {
        try
        {
            if (File.Exists(AppFile))
            {
                var fromApp = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppFile));
                // одиночная обратная косая в JSON даёт управляющий символ вместо пути — такой путь не принимаем
                if (!string.IsNullOrWhiteSpace(fromApp?.DatabasePath) && !fromApp.DatabasePath.Any(char.IsControl)) return fromApp.DatabasePath!;
            }
        }
        catch (Exception) { /* повреждённый файл — стандартный путь */ }
        return DefaultDatabasePath;
    }

    public static AppSettings Load()
    {
        AppSettings? user = null;
        try
        {
            if (File.Exists(UserFile)) user = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(UserFile));
        }
        catch (Exception) { /* повреждённый файл — настройки по умолчанию */ }
        user ??= new AppSettings();
        if (string.IsNullOrWhiteSpace(user.DatabasePath)) user.DatabasePath = null;
        return user;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(UserFile)!);
        File.WriteAllText(UserFile, JsonSerializer.Serialize(this, Json));
    }

    // Путь, с которым работает программа: свой путь этого компьютера или путь развёртывания.
    public string EffectiveDatabasePath => ResolvePath(DatabasePath ?? DeploymentDatabasePath());

    public bool UsesDeploymentPath => DatabasePath is null;

    // Относительный путь — от папки программы; сетевой путь \\сервер\папка\файл.db остаётся как есть.
    public static string ResolvePath(string path)
    {
        var p = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        return Path.IsPathRooted(p) ? Path.GetFullPath(p) : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, p));
    }
}
