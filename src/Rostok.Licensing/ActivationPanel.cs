using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Rostok.Controls;

namespace Rostok.Licensing;

// Активация: ключ компьютера → администратор → серийный номер. Общий блок для установщика и программы.
public sealed class ActivationPanel : StackPanel
{
    private readonly TextBox _key;
    private readonly TextBox _serial;
    private readonly StackPanel _keyBox = new() { Visibility = Visibility.Collapsed };
    private readonly Button _generate;
    private readonly Button _copy;
    private readonly ContentControl _status = new();

    public string? Key { get; private set; }
    public string Serial => _serial.Text.Trim();
    public bool IsValid { get; private set; }
    private readonly TextBlock _restored;
    public event Action<bool>? ValidityChanged;

    public ActivationPanel()
    {
        Children.Add(Step("1", "Ключ этого компьютера"));
        _restored = Ui.Status("", true).With(t => { t.Visibility = Visibility.Collapsed; t.Margin = new Thickness(0, 0, 0, 10); });
        Children.Add(_restored);
        _generate = Compact(Ui.Primary("Сгенерировать ключ", Generate, IconKind.Key));
        _generate.HorizontalAlignment = HorizontalAlignment.Left;
        Children.Add(_generate);

        // компактная строка ключа: поле и кнопка одной высоты 38 px, отступ от кнопки «Сгенерировать» — 12 px
        _key = new TextBox { IsReadOnly = true, FontFamily = Theme.MonoFont, FontSize = 14, FontWeight = FontWeights.SemiBold, Padding = new Thickness(9, 7, 9, 7), MinHeight = 38, Foreground = Theme.Accent };
        _copy = Compact(Ui.Ghost("Скопировать", Copy, IconKind.Copy));
        _copy.Margin = new Thickness(8, 0, 0, 0);
        _keyBox.Margin = new Thickness(0, 12, 0, 0);
        var keyRow = new DockPanel();
        DockPanel.SetDock(_copy, Dock.Right);
        keyRow.Children.Add(_copy);
        keyRow.Children.Add(_key);
        _keyBox.Children.Add(keyRow);
        _keyBox.Children.Add(Ui.Faint("Передайте ключ администратору — по почте или в мессенджере. Ключ рассчитан по материнской плате этого компьютера: серийный номер подойдёт только к нему.").Margin(0, 6, 0, 0).With(t => t.FontSize = 12.5));
        Children.Add(_keyBox);

        Children.Add(Step("2", "Серийный номер от администратора").Margin(0, 22, 0, 8));
        _serial = Ui.Input("", "Вставьте серийный номер, который прислал администратор", null, multiline: true, rows: 3);
        _serial.FontFamily = Theme.MonoFont;
        _serial.FontSize = 12.5;
        _serial.MinHeight = 66;
        _serial.TextChanged += (_, _) => Check();
        Children.Add(_serial);
        var paste = Ui.TextAction("вставить из буфера обмена", () =>
        {
            try { if (Clipboard.ContainsText()) _serial.Text = Clipboard.GetText().Trim(); } catch (Exception) { /* буфер занят */ }
        });
        Children.Add(Ui.Row(16, paste, _status).Margin(0, 2, 0, 0));

        // повторная установка или обновление: ранее выданные ключ и серийный номер показываются сразу
        if (License.HasRequest()) Loaded += async (_, _) => { if (Key is null) await RestoreAsync(); };
    }

    // Подставляет ключ этого компьютера и, если он уже активирован, сохранённый серийный номер.
    public async Task RestoreAsync()
    {
        await GenerateAsync(save: false);
        if (Key is null) return;
        var saved = License.Saved(Key);
        if (saved is not null && Serial.Length == 0) _serial.Text = saved.Serial;
        _restored.Text = saved is not null
            ? $"Этот компьютер уже активирован {saved.ActivatedAt:dd.MM.yyyy}: ключ и серийный номер подставлены — можно продолжать."
            : "Ключ этого компьютера уже был сгенерирован. Вставьте серийный номер от администратора.";
        _restored.Visibility = Visibility.Visible;
    }

    // Кнопки блока — высотой с поле (38 px), чтобы блок был компактным
    private static Button Compact(Button b)
    {
        b.MinHeight = 38;
        b.Padding = new Thickness(14, 0, 14, 0);
        return b;
    }

    private static FrameworkElement Step(string num, string title) => new StackPanel
    {
        Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8),
        Children =
        {
            Ui.Num(num, Theme.Accent, 18, FontWeights.Medium).With(t => t.Margin = new Thickness(0, 0, 10, 0)),
            Ui.Caps(title).With(t => t.VerticalAlignment = VerticalAlignment.Center),
        },
    };

    private async void Generate() => await GenerateAsync();

    public async Task GenerateAsync() => await GenerateAsync(save: true);

    public async Task GenerateAsync(bool save)
    {
        _generate.IsEnabled = false;
        _generate.Content = Ui.Content(IconKind.Refresh, "Считываю данные платы…");
        try { Key = await Task.Run(License.MachineKey); }
        catch (Exception e) { Key = null; SetStatus($"Не удалось считать данные компьютера: {e.Message}", false); }
        _generate.IsEnabled = true;
        _generate.Content = Ui.Content(IconKind.Refresh, "Сгенерировать ключ ещё раз");
        if (Key is null) return;
        _key.Text = Key;
        _keyBox.Visibility = Visibility.Visible;
        if (save) License.SaveRequest(Key);
        Check();
    }

    private void Copy()
    {
        try
        {
            Clipboard.SetText(_key.Text);
            _copy.Content = Ui.Content(IconKind.Check, "Скопировано");
        }
        catch (Exception) { /* буфер занят другой программой */ }
    }

    private void SetStatus(string text, bool ok) => _status.Content = Ui.Status(text, ok).With(t => t.VerticalAlignment = VerticalAlignment.Center);

    public void SetSerial(string serial) => _serial.Text = serial;

    private void Check()
    {
        var was = IsValid;
        if (Serial.Length == 0) { _status.Content = null; IsValid = false; }
        else if (Key is null) { SetStatus("Сначала сгенерируйте ключ компьютера.", false); IsValid = false; }
        else
        {
            IsValid = License.Verify(Key, Serial);
            SetStatus(IsValid ? "Серийный номер подходит к этому компьютеру." : "Серийный номер не подходит к ключу этого компьютера.", IsValid);
        }
        Ui.SetIsInvalid(_serial, Serial.Length > 0 && !IsValid);
        if (was != IsValid) ValidityChanged?.Invoke(IsValid);
    }
}
