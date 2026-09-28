using System.Windows;
using Microsoft.Win32;

namespace Rostok.Desktop.Services;

// Системные окна сообщений и выбора файла. Подтверждения удаления сделаны в самих экранах — как в веб-версии.
public static class Dialogs
{
    public static void Error(string title, string text) =>
        MessageBox.Show(Application.Current?.MainWindow ?? null!, text, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public static string? SaveFile(string fileName, string filter)
    {
        var dlg = new SaveFileDialog { FileName = Safe(fileName), Filter = filter, OverwritePrompt = true };
        return dlg.ShowDialog(Application.Current?.MainWindow) == true ? dlg.FileName : null;
    }

    // Имя файла без символов, недопустимых в Windows: «Группа № 5 «Рябинка»» остаётся читаемым.
    public static string Safe(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, ' ');
        return name.Trim();
    }

    public static void OpenFolderOf(string path)
    {
        try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\""); } catch (Exception) { /* без проводника */ }
    }
}
