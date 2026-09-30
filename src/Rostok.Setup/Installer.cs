using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;
using Rostok.Licensing;

namespace Rostok.Setup;

// SystemChanges = false — только для самопроверки: файлы и настройки без лицензии, ярлыков, прав и записи в реестр.
public sealed record InstallOptions(string InstallDir, string DatabasePath, bool DesktopShortcut, bool StartMenuShortcut, string MachineKey, string Serial, bool SystemChanges = true);

// Установка: распаковка пакета, настройка базы, лицензия, ярлыки и запись в «Программы и компоненты».
public static class Installer
{
    public const string AppName = "Росток";
    public const string ExeName = "Rostok.exe";
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Rostok";

    public static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public static string DefaultInstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName);
    public static string DefaultDatabasePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Rostok", "rostok.db");

    public static bool HasPayload => PayloadOverride is not null || Assembly.GetExecutingAssembly().GetManifestResourceNames().Contains("Rostok.Setup.Payload.zip");

    // Пакет из файла вместо встроенного — для самопроверки отладочной сборки.
    internal static string? PayloadOverride { get; set; }

    private static Stream? OpenPayload() => PayloadOverride is not null
        ? File.OpenRead(PayloadOverride)
        : Assembly.GetExecutingAssembly().GetManifestResourceStream("Rostok.Setup.Payload.zip");

    // Уже установленная версия — чтобы предложить ту же папку и тот же путь к базе.
    public static (string? Dir, string? Version) Existing()
    {
        using var key = Registry.LocalMachine.OpenSubKey(UninstallKey);
        return (key?.GetValue("InstallLocation") as string, key?.GetValue("DisplayVersion") as string);
    }

    public static string? ExistingDatabasePath(string dir)
    {
        try
        {
            var file = Path.Combine(dir, "appsettings.json");
            if (!File.Exists(file)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            return doc.RootElement.TryGetProperty("DatabasePath", out var p) ? Environment.ExpandEnvironmentVariables(p.GetString() ?? "") : null;
        }
        catch (Exception) { return null; }
    }

    // Нет окна и через полторы секунды — не запускается, а висит без окна.
    private static bool IsWindowless(Process p)
    {
        if (p.MainWindowHandle != IntPtr.Zero) return false;
        if (DateTime.Now - p.StartTime < TimeSpan.FromSeconds(10)) Thread.Sleep(1500);
        p.Refresh();
        return !p.HasExited && p.MainWindowHandle == IntPtr.Zero;
    }

    public static void Install(InstallOptions o, IProgress<(double Value, string Text)> progress)
    {
        using var payload = OpenPayload()
            ?? throw new InvalidOperationException("В установщик не встроен пакет программы. Соберите его скриптом build\\build-installer.ps1.");

        progress.Report((0.02, "Проверяю, не запущен ли «Росток»…"));
        var target = Path.GetFullPath(Path.Combine(o.InstallDir, ExeName));
        foreach (var p in Process.GetProcessesByName("Rostok"))
        {
            try
            {
                if (!string.Equals(p.MainModule?.FileName, target, StringComparison.OrdinalIgnoreCase)) continue;
                // программа прежней версии могла остаться запущенной без окна (закрыли окно входа) — её закрываем сами
                if (IsWindowless(p))
                {
                    progress.Report((0.03, "Закрываю «Росток», оставшийся запущенным без окна…"));
                    p.Kill();
                    p.WaitForExit(5000);
                    continue;
                }
                throw new InvalidOperationException("«Росток» сейчас открыт. Закройте программу и нажмите «Установить» ещё раз.");
            }
            catch (System.ComponentModel.Win32Exception) { /* чужой процесс без доступа — пропускаем */ }
        }

        Directory.CreateDirectory(o.InstallDir);
        using (var zip = new ZipArchive(payload, ZipArchiveMode.Read))
        {
            var total = zip.Entries.Sum(e => e.Length);
            long done = 0;
            foreach (var entry in zip.Entries)
            {
                var dest = Path.GetFullPath(Path.Combine(o.InstallDir, entry.FullName));
                if (!dest.StartsWith(Path.GetFullPath(o.InstallDir), StringComparison.OrdinalIgnoreCase)) continue;
                if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(dest); continue; }
                // база из пакета не затирает уже существующую
                if (entry.FullName.Replace('\\', '/').Equals("Data/rostok.db", StringComparison.OrdinalIgnoreCase) && File.Exists(dest)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, overwrite: true);
                done += entry.Length;
                progress.Report((0.05 + 0.75 * done / Math.Max(1, total), $"Копирую {entry.FullName}"));
            }
        }

        progress.Report((0.82, "Настраиваю подключение к базе данных…"));
        File.WriteAllText(Path.Combine(o.InstallDir, "appsettings.json"),
            JsonSerializer.Serialize(new { DatabasePath = o.DatabasePath }, new JsonSerializerOptions { WriteIndented = true }));
        var dbDir = Path.GetDirectoryName(o.DatabasePath);
        if (!string.IsNullOrEmpty(dbDir) && !o.DatabasePath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            Directory.CreateDirectory(dbDir);
            if (o.SystemChanges) AllowUsers(dbDir);
        }
        if (!o.SystemChanges)
        {
            progress.Report((1, "Готово"));
            return;
        }

        progress.Report((0.86, "Сохраняю лицензию…"));
        var dataDir = Path.GetDirectoryName(License.MachineFile)!;
        Directory.CreateDirectory(dataDir);
        AllowUsers(dataDir);
        License.Save(o.MachineKey, o.Serial);

        progress.Report((0.9, "Создаю ярлыки…"));
        var exe = Path.Combine(o.InstallDir, ExeName);
        if (o.DesktopShortcut) Shortcut.Create(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), $"{AppName}.lnk"), exe, "Мониторинг развития ребёнка");
        if (o.StartMenuShortcut) Shortcut.Create(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), $"{AppName}.lnk"), exe, "Мониторинг развития ребёнка");

        progress.Report((0.95, "Регистрирую программу в Windows…"));
        using (var key = Registry.LocalMachine.CreateSubKey(UninstallKey))
        {
            key.SetValue("DisplayName", AppName);
            key.SetValue("DisplayVersion", Version);
            key.SetValue("Publisher", AppName);
            key.SetValue("DisplayIcon", exe);
            key.SetValue("InstallLocation", o.InstallDir);
            key.SetValue("InstallDate", DateTime.Today.ToString("yyyyMMdd"));
            key.SetValue("UninstallString", $"\"{exe}\" --uninstall");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", (int)(DirSize(o.InstallDir) / 1024), RegistryValueKind.DWord);
        }
        progress.Report((1, "Готово"));
    }

    // Папка базы и лицензии доступна на запись всем пользователям компьютера: программа запускается не от администратора.
    private static void AllowUsers(string dir)
    {
        try
        {
            var info = new DirectoryInfo(dir);
            var acl = info.GetAccessControl();
            var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            acl.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.Modify,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            info.SetAccessControl(acl);
        }
        catch (Exception) { /* без прав на изменение — база останется доступной администратору */ }
    }

    private static long DirSize(string dir) =>
        new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
}

// Ярлык Windows (.lnk) через IShellLink.
internal static class Shortcut
{
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    public static void Create(string lnk, string target, string description)
    {
        var link = (IShellLinkW)new ShellLink();
        link.SetPath(target);
        link.SetWorkingDirectory(Path.GetDirectoryName(target)!);
        link.SetDescription(description);
        link.SetIconLocation(target, 0);
        Directory.CreateDirectory(Path.GetDirectoryName(lnk)!);
        ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Save(lnk, false);
    }
}
