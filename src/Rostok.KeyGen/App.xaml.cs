using System.Windows;
using Rostok.Controls;

namespace Rostok.KeyGen;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Theme.Initialize();
        var w = new KeyGenWindow();
        MainWindow = w;
        var i = Array.IndexOf(e.Args, "--shot");
        if (i >= 0 && i + 1 < e.Args.Length)
        {
            w.Left = -20000;
            w.ShowActivated = false;
            w.Show();
            _ = w.SaveShot(e.Args[i + 1]);
            return;
        }
        w.Show();
    }
}
