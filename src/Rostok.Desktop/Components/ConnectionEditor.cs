using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Rostok.Controls;
using Rostok.Core.Storage;
using Rostok.Desktop.Services;

namespace Rostok.Desktop.Components;

// Настройка подключения к базе данных: путь к файлу SQLite на этом компьютере или в общей папке сети.
// Используется в «Администрирование → Подключение» и на экране входа (если база недоступна).
public sealed class ConnectionEditor : StackPanel
{
    private readonly Action _onConnected;
    private readonly TextBox _path;
    private readonly StackPanel _statusBox = new();

    public ConnectionEditor(Action onConnected, bool compact = false)
    {
        _onConnected = onConnected;
        var settings = AppHost.Settings;

        var current = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        current.Children.Add(Ui.Caps("Текущая база"));
        current.Children.Add(Ui.Num(settings.EffectiveDatabasePath, Theme.Ink, 13).With(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 4, 0, 4); }));
        if (AppHost.DbError is null && AppHost.Db is not null)
        {
            var count = SafeCount();
            current.Children.Add(Ui.Status(count is null ? "Подключено." : $"Подключено · рабочих пространств: {count}", true));
        }
        else current.Children.Add(Ui.Status($"Нет подключения: {AppHost.DbError}", false));
        current.Children.Add(Ui.Faint(settings.UsesDeploymentPath
            ? "Путь по умолчанию для этой установки (appsettings.json рядом с программой)."
            : "Свой путь этого компьютера (хранится в настройках пользователя Windows)."));
        Children.Add(current);

        _path = Ui.Input(settings.EffectiveDatabasePath, @"\\СЕРВЕР\Росток\rostok.db");
        _path.FontFamily = Theme.MonoFont;
        var browse = Ui.IconButton(IconKind.Folder, "Выбрать или создать файл базы", Browse);
        browse.Margin = new Thickness(8, 0, 0, 0);
        var pathRow = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        pathRow.Children.Add(browse);
        pathRow.Children.Add(_path);
        Children.Add(Ui.Field("Путь к файлу базы данных", pathRow));

        var actions = Ui.Row(8,
            Ui.Primary("Подключиться", Connect, IconKind.Plug),
            Ui.Ghost("Проверить подключение", Check),
            Ui.TextAction("вернуть путь по умолчанию", () => { _path.Text = AppSettings.ResolvePath(AppSettings.DeploymentDatabasePath()); SetStatus("Путь по умолчанию подставлен — нажмите «Подключиться».", true); }));
        actions.Margin = new Thickness(0, 12, 0, 0);
        Children.Add(actions);
        _statusBox.Margin = new Thickness(0, 8, 0, 0);
        Children.Add(_statusBox);

        if (!compact)
        {
            Children.Add(Ui.HelpNote(Ui.Rich(
                Ui.Run("Работа в локальной сети. ", Theme.Ink, FontWeights.SemiBold),
                Ui.Run("Положите файл базы в общую папку, доступную всем сотрудникам на запись, и укажите на каждом компьютере путь вида "),
                Ui.Run(@"\\СЕРВЕР\Росток\rostok.db", Theme.Ink, mono: true),
                Ui.Run(". Каждый сотрудник входит под своим логином и работает в своих рабочих пространствах: группы, дети, баллы и настройки не пересекаются. Если файла по указанному пути нет, он будет создан автоматически."))
                .With(t => t.FontSize = 13)));
        }
    }

    private static int? SafeCount()
    {
        try { return new Workspaces(AppHost.Db!).List().Count; } catch (Exception) { return null; }
    }

    private void Browse()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Файл базы данных «Ростка»",
            Filter = "База «Ростка» (*.db)|*.db|Все файлы|*.*",
            FileName = "rostok.db",
            OverwritePrompt = false,
            CheckFileExists = false,
            CheckPathExists = true,
        };
        try { dlg.InitialDirectory = Path.GetDirectoryName(AppSettings.ResolvePath(_path.Text)); } catch (Exception) { /* без начальной папки */ }
        if (dlg.ShowDialog(Window.GetWindow(this)) == true) _path.Text = dlg.FileName;
    }

    private void SetStatus(string text, bool ok)
    {
        _statusBox.Children.Clear();
        _statusBox.Children.Add(Ui.Status(text, ok));
    }

    private string? Resolved()
    {
        try { return AppSettings.ResolvePath(_path.Text); }
        catch (Exception) { SetStatus("Путь указан неверно.", false); return null; }
    }

    private void Check()
    {
        var p = Resolved();
        if (p is null) return;
        var (ok, message) = Database.Test(p, create: false);
        SetStatus(message, ok);
    }

    private void Connect()
    {
        var p = Resolved();
        if (p is null) return;
        var (ok, message) = Database.Test(p, create: true);
        if (!ok) { SetStatus(message, false); return; }
        var deployment = AppSettings.ResolvePath(AppSettings.DeploymentDatabasePath());
        var connected = AppHost.Reconnect(string.Equals(p, deployment, StringComparison.OrdinalIgnoreCase) ? null : p);
        if (!connected) { SetStatus($"Не удалось подключиться: {AppHost.DbError}", false); return; }
        SetStatus(message, true);
        _onConnected();
    }
}
