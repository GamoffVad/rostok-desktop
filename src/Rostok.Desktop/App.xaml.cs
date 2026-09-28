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
        if (e.Args.Contains("--uninstall")) { UninstallWindow.Run(); return; }
        if (Shots.TryRun(e.Args)) return;
#if DEBUG
        AppHost.Start();
#else
        // без действующей лицензии этого компьютера программа сначала просит активацию
        if (Rostok.Licensing.License.IsActivated()) AppHost.Start();
        else new ActivationWindow(AppHost.Start).Show();
#endif
    }

    // Необработанная ошибка не закрывает программу молча: показываем текст, данные уже записаны построчно.
    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Dialogs.Error("Непредвиденная ошибка", e.Exception.Message);
    }
}
