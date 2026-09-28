using System.Windows;
using Rostok.Controls;

namespace Rostok.Setup;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Theme.Initialize();
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
        w.Show();
    }
}
