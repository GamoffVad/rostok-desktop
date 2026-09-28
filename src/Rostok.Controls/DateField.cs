using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Rostok.Controls;

// Поле даты библиотеки «Росток» (в веб-версии — DatePicker): ввод по маске дд.мм.гггг и свой календарь вместо системного.
// Значение — строка ISO «ГГГГ-ММ-ДД» или "".
public class DateField : UserControl
{
    private static readonly string[] Months = ["январь", "февраль", "март", "апрель", "май", "июнь", "июль", "август", "сентябрь", "октябрь", "ноябрь", "декабрь"];
    private static readonly string[] MonthsGen = ["января", "февраля", "марта", "апреля", "мая", "июня", "июля", "августа", "сентября", "октября", "ноября", "декабря"];
    private static readonly string[] Weekdays = ["пн", "вт", "ср", "чт", "пт", "сб", "вс"];

    private readonly TextBox _input;
    private readonly Border _frame;
    private readonly Button _toggle;
    private readonly TextBlock _error;
    private readonly Popup _pop;
    private readonly Grid _grid = new();
    private readonly Dropdown _month;
    private readonly Dropdown _year;
    private readonly Button _todayBtn;
    private DateTime _cursor = DateTime.Today;
    private string _value = "";
    private bool _typing;

    public string? Min { get; set; }
    public string? Max { get; set; }
    public string Label { get; set; } = "Дата";
    public string InvalidText { get; set; } = "Такой даты нет";

    public event Action<string>? ValueChanged;

    public string Value
    {
        get => _value;
        set
        {
            _value = value ?? "";
            if (!_typing) { _input.Text = IsoToText(_value); SetError(""); }
        }
    }

    public DateField()
    {
        _input = new TextBox
        {
            FontFamily = Theme.MonoFont,
            Padding = new Thickness(7, 8, 4, 8),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
        };
        Ui.SetPlaceholder(_input, "дд.мм.гггг");
        _input.TextChanged += OnType;
        _input.LostKeyboardFocus += (_, _) => { if (_input.Text.Length is > 0 and < 10) SetError("Введите дату полностью: дд.мм.гггг"); };
        InputMethod.SetIsInputMethodEnabled(_input, false);

        _toggle = new Button { Style = Ui.Style("ButtonBase"), Width = 36, Focusable = true };
        _toggle.Content = new DashBorder { Sides = Sides.Left, Child = new Icon(IconKind.Calendar) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, Width = 36, Background = Brushes.Transparent };
        _toggle.Foreground = Theme.Ink3;
        _toggle.MouseEnter += (_, _) => _toggle.Foreground = Theme.Accent;
        _toggle.MouseLeave += (_, _) => { if (!_pop!.IsOpen) _toggle.Foreground = Theme.Ink3; };
        _toggle.Click += (_, _) => { if (_pop!.IsOpen) Close(false); else Open(); };
        Ui.AutomationName(_toggle, "Открыть календарь");

        // рамка поля — как у обычного ввода; внутри поле без рамки и кнопка календаря
        var inner = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_toggle, Dock.Right);
        inner.Children.Add(_toggle);
        inner.Children.Add(_input);
        _frame = new Border { Background = Theme.Sheet, BorderBrush = Theme.Dash, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2), MinHeight = 38, Child = inner, SnapsToDevicePixels = true };
        _input.GotKeyboardFocus += (_, _) => UpdateFrame();
        _input.LostKeyboardFocus += (_, _) => UpdateFrame();
        var field = _frame;

        _error = new TextBlock { Foreground = Theme.Danger, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap };

        // ── календарь ──
        var prev = Ui.IconButton(IconKind.ChevronLeft, "Предыдущий месяц", () => Move(_cursor.AddMonths(-1)));
        var next = Ui.IconButton(IconKind.ChevronRight, "Следующий месяц", () => Move(_cursor.AddMonths(1)));
        prev.Width = prev.Height = next.Width = next.Height = 34;
        _month = new Dropdown(Months.Select((m, i) => new DropdownOption(i, m)), _cursor.Month - 1, v =>
        {
            var m = (int)v! + 1;
            Move(new DateTime(_cursor.Year, m, Math.Min(_cursor.Day, DateTime.DaysInMonth(_cursor.Year, m))));
        }, DropdownVariant.Light, label: "Месяц");
        _year = new Dropdown([], _cursor.Year, v =>
        {
            var y = (int)v!;
            Move(new DateTime(y, _cursor.Month, Math.Min(_cursor.Day, DateTime.DaysInMonth(y, _cursor.Month))));
        }, DropdownVariant.Light, label: "Год");
        var head = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        _month.Margin = new Thickness(6, 0, 3, 0);
        _year.Margin = new Thickness(3, 0, 6, 0);
        Grid.SetColumn(_month, 1); Grid.SetColumn(_year, 2); Grid.SetColumn(next, 3);
        head.Children.Add(prev); head.Children.Add(_month); head.Children.Add(_year); head.Children.Add(next);

        for (var i = 0; i < 7; i++) _grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 7; i++) _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _grid.PreviewKeyDown += OnGridKey;

        var clear = Ui.TextAction("очистить", () => { Commit(null); _input.Text = ""; Close(true); }, danger: true);
        _todayBtn = Ui.TextAction("сегодня", () => { Commit(DateTime.Today); Close(true); });
        var foot = new DockPanel();
        DockPanel.SetDock(_todayBtn, Dock.Right);
        foot.Children.Add(_todayBtn);
        foot.Children.Add(clear);
        var footBorder = new DashBorder { Sides = Sides.Top, Child = foot, Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(0, 4, 0, 0) };

        var popBody = new StackPanel();
        popBody.Children.Add(head);
        popBody.Children.Add(_grid);
        popBody.Children.Add(footBorder);
        _pop = new Popup
        {
            PlacementTarget = field, Placement = PlacementMode.Bottom, VerticalOffset = 4, AllowsTransparency = true, StaysOpen = false,
            Child = new DashBorder { Background = Theme.Sheet, Sides = Sides.All, Padding = new Thickness(10), Width = 320, Child = popBody },
        };
        _pop.Closed += (_, _) => _toggle.Foreground = Theme.Ink3;

        var root = new StackPanel();
        root.Children.Add(field);
        root.Children.Add(_error);
        root.Children.Add(_pop);
        Content = root;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && _pop.IsOpen) { e.Handled = true; Close(true); } };
    }

    // ── вспомогательные ──
    private static DateTime? FromIso(string? s) =>
        DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    private static string ToIso(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string IsoToText(string? s) => FromIso(s) is { } d ? d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : "";

    // «07031998» или «7.3.1998» → «07.03.1998» по мере набора
    private static string Mask(string raw)
    {
        var digits = new string(raw.Where(char.IsDigit).Take(8).ToArray());
        var parts = new[] { digits.Length > 0 ? digits[..Math.Min(2, digits.Length)] : "", digits.Length > 2 ? digits[2..Math.Min(4, digits.Length)] : "", digits.Length > 4 ? digits[4..] : "" };
        return string.Join(".", parts.Where(p => p.Length > 0));
    }

    private static DateTime? ParseText(string text) =>
        DateTime.TryParseExact(text, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private DateTime? MinD => FromIso(Min);
    private DateTime? MaxD => FromIso(Max);
    private bool OutOfRange(DateTime d) => (MinD is { } mn && d < mn) || (MaxD is { } mx && d > mx);

    private void SetError(string text)
    {
        _error.Text = text;
        _error.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateFrame();
    }

    private void UpdateFrame() =>
        _frame.BorderBrush = _error.Visibility == Visibility.Visible ? Theme.Danger : _input.IsKeyboardFocused ? Theme.Accent : Theme.Dash;

    private void Commit(DateTime? d)
    {
        if (d is null)
        {
            _value = "";
            SetError("");
            ValueChanged?.Invoke("");
            return;
        }
        if (OutOfRange(d.Value))
        {
            SetError(MaxD is { } mx && d > mx ? $"Не позже {IsoToText(Max)}" : $"Не раньше {IsoToText(Min)}");
            return;
        }
        SetError("");
        _value = ToIso(d.Value);
        if (!_typing) _input.Text = IsoToText(_value);
        ValueChanged?.Invoke(_value);
    }

    private void OnType(object sender, TextChangedEventArgs e)
    {
        if (_typing) return;
        _typing = true;
        var next = Mask(_input.Text);
        if (next != _input.Text)
        {
            _input.Text = next;
            _input.CaretIndex = next.Length;
        }
        SetError("");
        if (next.Length == 10)
        {
            var d = ParseText(next);
            if (d is not null) { Commit(d); _cursor = d.Value; }
            else SetError(InvalidText);
        }
        else if (next.Length == 0 && _value.Length > 0)
        {
            _value = "";
            ValueChanged?.Invoke("");
        }
        _typing = false;
    }

    private void Open()
    {
        var today = DateTime.Today;
        _cursor = FromIso(_value) ?? (MaxD is { } mx && today > mx ? mx : today);
        var top = (MaxD ?? today.AddYears(5)).Year;
        var bottom = (MinD ?? new DateTime(today.Year - 100, 1, 1)).Year;
        _year.SetOptions(Enumerable.Range(0, top - bottom + 1).Select(i => new DropdownOption(top - i, (top - i).ToString())), _cursor.Year);
        _todayBtn.Visibility = OutOfRange(today) ? Visibility.Collapsed : Visibility.Visible;
        _pop.IsOpen = true;
        _toggle.Foreground = Theme.Accent;
        Render();
    }

    private void Close(bool refocus)
    {
        _pop.IsOpen = false;
        if (refocus) _toggle.Focus();
    }

    private void Move(DateTime d)
    {
        _cursor = d;
        Render();
    }

    // Фокус держится на дне-курсоре — стрелки двигают его, как в таблице.
    private void Render()
    {
        _month.Value = _cursor.Month - 1;
        if (_year.Items.Cast<DropdownOption>().All(o => (int)o.Value! != _cursor.Year))
            _year.SetOptions(_year.Items.Cast<DropdownOption>().Append(new DropdownOption(_cursor.Year, _cursor.Year.ToString())).OrderByDescending(o => (int)o.Value!), _cursor.Year);
        else _year.Value = _cursor.Year;

        _grid.Children.Clear();
        for (var i = 0; i < 7; i++)
        {
            var wd = new TextBlock { Text = Ui.Upper(Weekdays[i]), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Theme.Ink3, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 6) };
            Grid.SetColumn(wd, i);
            _grid.Children.Add(wd);
        }
        var first = new DateTime(_cursor.Year, _cursor.Month, 1);
        var shift = ((int)first.DayOfWeek + 6) % 7;
        var start = first.AddDays(-shift);
        var selected = FromIso(_value);
        Button? focusBtn = null;
        for (var w = 0; w < 6; w++)
        {
            for (var d = 0; d < 7; d++)
            {
                var day = start.AddDays(w * 7 + d);
                var btn = DayButton(day, selected);
                Grid.SetRow(btn, w + 1);
                Grid.SetColumn(btn, d);
                _grid.Children.Add(btn);
                if (day == _cursor) focusBtn = btn;
            }
        }
        if (focusBtn is not null) Dispatcher.BeginInvoke(() => focusBtn.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private Button DayButton(DateTime day, DateTime? selected)
    {
        var off = day.Month != _cursor.Month;
        var disabled = OutOfRange(day);
        var isSel = selected == day;
        var isToday = day == DateTime.Today;
        var text = new TextBlock
        {
            Text = day.Day.ToString(), FontFamily = Theme.MonoFont, FontSize = 13,
            FontWeight = isSel || isToday ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = isSel ? Theme.Sheet : disabled ? Theme.Dash : off ? Theme.Faint : Theme.Ink,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var bg = new Border { Background = isSel ? Theme.Accent : Brushes.Transparent, CornerRadius = new CornerRadius(2), Child = text };
        var cell = new Grid();
        cell.Children.Add(bg);
        if (isToday && !isSel) cell.Children.Add(new Rectangle { Height = 2, Fill = Theme.Accent, VerticalAlignment = VerticalAlignment.Bottom });
        var focus = new Rectangle { Stroke = Theme.Accent, StrokeThickness = 2, RadiusX = 2, RadiusY = 2, Visibility = Visibility.Collapsed };
        cell.Children.Add(focus);
        var btn = new Button { Style = Ui.Style("ButtonBase"), Height = 34, Margin = new Thickness(1), Content = cell, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, Cursor = disabled ? Cursors.Arrow : Cursors.Hand };
        btn.Template = PlainTemplate();
        Ui.AutomationName(btn, $"{day.Day} {MonthsGen[day.Month - 1]} {day.Year}");
        btn.GotKeyboardFocus += (_, _) => focus.Visibility = Visibility.Visible;
        btn.LostKeyboardFocus += (_, _) => focus.Visibility = Visibility.Collapsed;
        if (!isSel && !disabled)
        {
            btn.MouseEnter += (_, _) => { bg.Background = Theme.Soft; text.Foreground = Theme.Accent; };
            btn.MouseLeave += (_, _) => { bg.Background = Brushes.Transparent; text.Foreground = off ? Theme.Faint : Theme.Ink; };
        }
        btn.Click += (_, _) =>
        {
            if (disabled) return;
            Commit(day);
            Close(true);
        };
        return btn;
    }

    private static ControlTemplate? _plain;
    private static ControlTemplate PlainTemplate()
    {
        if (_plain is not null) return _plain;
        var t = new ControlTemplate(typeof(Button));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        t.VisualTree = cp;
        return _plain = t;
    }

    private void OnGridKey(object sender, KeyEventArgs e)
    {
        var shiftDown = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        DateTime? next = e.Key switch
        {
            Key.Left => _cursor.AddDays(-1),
            Key.Right => _cursor.AddDays(1),
            Key.Up => _cursor.AddDays(-7),
            Key.Down => _cursor.AddDays(7),
            Key.PageUp => _cursor.AddMonths(shiftDown ? -12 : -1),
            Key.PageDown => _cursor.AddMonths(shiftDown ? 12 : 1),
            Key.Home => _cursor.AddDays(-(((int)_cursor.DayOfWeek + 6) % 7)),
            Key.End => _cursor.AddDays(6 - (((int)_cursor.DayOfWeek + 6) % 7)),
            _ => null,
        };
        if (next is not null)
        {
            e.Handled = true;
            Move(next.Value);
            return;
        }
        if (e.Key is Key.Enter or Key.Space)
        {
            e.Handled = true;
            if (!OutOfRange(_cursor)) { Commit(_cursor); Close(true); }
        }
    }
}
