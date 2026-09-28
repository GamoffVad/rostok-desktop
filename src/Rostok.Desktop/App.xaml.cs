using System.Windows;
using System.Windows.Threading;
using Rostok.Controls;
using Rostok.Desktop.Services;

namespace Rostok.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Theme.Initialize();
        DispatcherUnhandledException += OnUnhandled;
        if (!Shots.TryRun(e.Args)) AppHost.Start();
    }

    // Необработанная ошибка не закрывает программу молча: показываем текст, данные уже записаны построчно.
    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Dialogs.Error("Непредвиденная ошибка", e.Exception.Message);
    }
}
