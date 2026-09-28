using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Rostok.Setup;

// Запуск программы после установки — от имени обычного пользователя.
// Установщик работает с правами администратора; если запустить программу напрямую, она тоже получит эти права
// (и базу, и настройки создаст от администратора), а системный вызов «открыть файл» из такого процесса иногда подвисает.
// Поэтому просим открыть программу сам рабочий стол Windows — как при двойном щелчке по ярлыку.
internal static class UserLauncher
{
    public static bool Launch(string exe)
    {
        if (!File.Exists(exe)) return false;
        var dir = Path.GetDirectoryName(exe)!;
        if (!IsElevated()) return Direct(exe, dir);
        return ViaDesktop(exe, dir) || ViaExplorer(exe, dir) || Direct(exe, dir);
    }

    // Проверка способа запуска через рабочий стол (режим --launch-test <exe>)
    public static bool TestDesktop(string exe) => ViaDesktop(exe, Path.GetDirectoryName(exe)!);

    private static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    // Рабочий стол Проводника (IShellWindows → IShellDispatch2.ShellExecute): программа запускается процессом Проводника.
    private static bool ViaDesktop(string exe, string dir)
    {
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"));
            if (type is null) return false;
            dynamic windows = Activator.CreateInstance(type)!;
            object location = 0;            // CSIDL_DESKTOP
            object empty = Type.Missing;
            dynamic? desktop = windows.FindWindowSW(ref location, ref empty, 8 /* SWC_DESKTOP */, out int _, 1 /* SWFO_NEEDDISPATCH */);
            if (desktop is null) return false;
            dynamic shell = desktop.Document.Application;
            shell.ShellExecute(exe, "", dir, "open", 1);
            Marshal.FinalReleaseComObject(windows);
            return true;
        }
        catch (Exception) { return false; }
    }

    private static bool ViaExplorer(string exe, string dir)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{exe}\"") { UseShellExecute = false, WorkingDirectory = dir });
            return true;
        }
        catch (Exception) { return false; }
    }

    private static bool Direct(string exe, string dir)
    {
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = dir });
            return true;
        }
        catch (Exception) { return false; }
    }
}
