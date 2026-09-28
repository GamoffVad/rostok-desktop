using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rostok.Controls;
using Rostok.Licensing;

namespace Rostok.Desktop;

// Активация при запуске, если на этом компьютере нет действующей лицензии
// (например, папку с программой скопировали, а не установили установщиком).
public sealed class ActivationWindow : Window
{
    private bool _done;

    public ActivationWindow(Action onActivated)
    {
        Title = "Росток — активация";
        Icon = new BitmapImage(new Uri("pack://application:,,,/Rostok;component/Assets/rostok.ico"));
        Background = Theme.Paper;
        Width = 1000;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = Theme.UiFont;
        UseLayoutRounding = true;

        var side = new DockPanel { Background = Theme.ArtBg };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(40, 40, 40, 0) };
        brand.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Rostok;component/Assets/logo.png")), Width = 40, Height = 40 });
        brand.Children.Add(new StackPanel
        {
            Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Children = { new TextBlock { Text = "Росток", FontSize = 18, FontWeight = FontWeights.ExtraBold, Foreground = Theme.Ink }, new TextBlock { Text = "активация", FontSize = 11, Foreground = Theme.Ink3 } },
        });
        DockPanel.SetDock(brand, Dock.Top);
        side.Children.Add(brand);
        side.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Rostok.Controls;component/Assets/Art/setup-activation.jpg")), Stretch = Stretch.Uniform, Margin = new Thickness(32), OpacityMask = Theme.ArtMask() });

        var panel = new ActivationPanel { MaxWidth = 540, HorizontalAlignment = HorizontalAlignment.Left };
        var activate = Ui.Primary("Активировать", () =>
        {
            License.Save(panel.Key!, panel.Serial);
            _done = true;
            Close();
            onActivated();
        }, IconKind.Check);
        activate.IsEnabled = false;
        panel.ValidityChanged += ok => activate.IsEnabled = ok;
        activate.HorizontalAlignment = HorizontalAlignment.Left;
        activate.Margin = new Thickness(0, 22, 0, 0);

        var body = new StackPanel();
        body.Children.Add(Ui.H1("Активация «Ростка»"));
        body.Children.Add(Ui.Subtitle("На этом компьютере программа ещё не активирована. Сгенерируйте ключ компьютера, передайте его администратору и вставьте серийный номер, который он пришлёт.").With(t => t.MaxWidth = 540).Margin(0, 6, 0, 24));
        body.Children.Add(panel);
        body.Children.Add(activate);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(side);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new Border { Padding = new Thickness(52, 48, 52, 32), Child = body } };
        Grid.SetColumn(scroll, 1);
        grid.Children.Add(scroll);
        Content = grid;
        Closed += (_, _) => { if (!_done) Application.Current.Shutdown(); };
    }
}
