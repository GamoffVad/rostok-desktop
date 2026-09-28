using System.Windows;
using Rostok.Controls;

namespace Rostok.Setup;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Theme.Initialize();
        var lt = Array.IndexOf(e.Args, "--launch-test");
        if (lt >= 0 && lt + 1 < e.Args.Length)
        {
            Environment.ExitCode = UserLauncher.TestDesktop(e.Args[lt + 1]) ? 0 : 2;
            Shutdown(Environment.ExitCode);
            return;
        }
        var w = new SetupWindow();
        MainWindow = w;
        var i = Array.IndexOf(e.Args, "--shots");
        if (i >= 0 && i + 1 < e.Args.Length)
        {
            w.Left = -20000;
            w.ShowActivated = false;
            w.Show();
            _ = w.SaveShots(e.Args[i + 1]);
            return;
        }
        var t = Array.IndexOf(e.Args, "--selftest");
        if (t >= 0 && t + 1 < e.Args.Length)
        {
            w.Left = -20000;
            w.ShowActivated = false;
            w.Show();
            var pl = Array.IndexOf(e.Args, "--payload");
            _ = w.SelfTest(e.Args[t + 1], pl >= 0 && pl + 1 < e.Args.Length ? e.Args[pl + 1] : null);
            return;
        }
        w.Show();
    }
}
