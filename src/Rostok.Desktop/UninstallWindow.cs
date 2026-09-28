using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Rostok.Controls;
using Rostok.Core.Storage;
using Rostok.Licensing;

namespace Rostok.Desktop;

// Удаление программы из «Программы и компоненты» (Rostok.exe --uninstall).
// База данных не удаляется никогда: в ней данные всех сотрудников.
public sealed class UninstallWindow : Window
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Rostok";

    public static void Run()
    {
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
        {
            try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--uninstall") { UseShellExecute = true, Verb = "runas" }); }
            catch (Exception) { /* пользователь отказался от повышения прав */ }
            Application.Current.Shutdown();
            return;
        }
        var w = new UninstallWindow();
        Application.Current.MainWindow = w;
        w.Show();
    }

    private UninstallWindow()
    {
        Title = "Удаление «Ростка»";
        Icon = new BitmapImage(new Uri("pack://application:,,,/Rostok;component/Assets/rostok.ico"));
        Background = Theme.Paper;
        Width = 640;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = Theme.UiFont;

        var dir = AppContext.BaseDirectory.TrimEnd('\\');
        var db = AppSettings.Load().EffectiveDatabasePath;
        var license = new Checkbox("Удалить также лицензию и настройки этого компьютера", false);
        var status = new ContentControl();
        var body = new StackPanel { Margin = new Thickness(40, 36, 40, 32) };
        body.Children.Add(Ui.H1("Удалить «Росток»?"));
        body.Children.Add(Ui.Subtitle("Будут удалены файлы программы, ярлыки и запись в «Программы и компоненты».").Margin(0, 6, 0, 16));
        body.Children.Add(Ui.Rich(Ui.Run("Папка программы: ", Theme.Ink3), Ui.Run(dir, Theme.Ink, mono: true, size: 12.5)));
        body.Children.Add(Ui.HelpNote(Ui.Rich(Ui.Run("База данных не удаляется — в ней данные всех сотрудников: ", Theme.Ink2), Ui.Run(db, Theme.Ink, mono: true, size: 12.5)).With(t => t.FontSize = 13), amber: true));
        body.Children.Add(license);
        var remove = Ui.Danger("Удалить", () =>
        {
            try
            {
                Remove(dir, license.IsChecked == true);
                Application.Current.Shutdown();
            }
            catch (Exception e) { status.Content = Ui.Status($"Не удалось удалить: {e.Message}", false); }
        }, IconKind.Trash);
        body.Children.Add(Ui.Row(8, remove, Ui.Ghost("Отмена", () => Application.Current.Shutdown())).Margin(0, 20, 0, 0));
        body.Children.Add(status);
        Content = body;
        Closed += (_, _) => Application.Current.Shutdown();
    }

    private static void Remove(string dir, bool dropLicense)
    {
        foreach (var lnk in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Росток.lnk"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Росток.lnk"),
        })
            if (File.Exists(lnk)) File.Delete(lnk);
        Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        if (dropLicense)
            foreach (var f in new[] { License.MachineFile, License.UserFile, AppSettings.UserFile })
                if (File.Exists(f)) File.Delete(f);

        // Папку с запущенной программой удаляет командный файл — через пару секунд после выхода.
        // Если база лежит в папке программы (Data\rostok.db), папка Data остаётся.
        var dataDb = Path.Combine(dir, "Data", "rostok.db");
        var keepData = File.Exists(dataDb) && AppSettings.Load().EffectiveDatabasePath.Equals(dataDb, StringComparison.OrdinalIgnoreCase);
        var bat = Path.Combine(Path.GetTempPath(), $"rostok-uninstall-{Guid.NewGuid():N}.cmd");
        var lines = new List<string> { "@echo off", "chcp 65001 > nul", "ping 127.0.0.1 -n 3 > nul" };
        if (keepData)
        {
            lines.Add($"for /d %%d in (\"{dir}\\*\") do if /i not \"%%~nxd\"==\"Data\" rmdir /s /q \"%%d\"");
            lines.Add($"del /f /q \"{dir}\\*.*\"");
        }
        else lines.Add($"rmdir /s /q \"{dir}\"");
        lines.Add("del \"%~f0\"");
        File.WriteAllLines(bat, lines, new System.Text.UTF8Encoding(false));
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"") { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath() });
    }
}
