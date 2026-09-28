using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]

namespace Rostok.Controls;

// Флажок библиотеки: свой квадрат 18×18 с галочкой; пробел переключает, фокус виден.
public class Checkbox : CheckBox
{
    static Checkbox() => DefaultStyleKeyProperty.OverrideMetadata(typeof(Checkbox), new FrameworkPropertyMetadata(typeof(Checkbox)));

    public Checkbox() { }

    public Checkbox(string? label, bool isChecked, Action<bool>? onChange = null)
    {
        Content = label;
        IsChecked = isChecked;
        if (onChange is not null)
        {
            Checked += (_, _) => onChange(true);
            Unchecked += (_, _) => onChange(false);
        }
    }
}

public sealed record DropdownOption(object? Value, string Label);

public enum DropdownVariant { Plain, Light }

// Выпадающий список: plain — для фильтров над контентом, light — для форм. Системный вид не используется.
public class Dropdown : ComboBox
{
    static Dropdown() => DefaultStyleKeyProperty.OverrideMetadata(typeof(Dropdown), new FrameworkPropertyMetadata(typeof(Dropdown)));

    public static readonly DependencyProperty VariantProperty = DependencyProperty.Register(nameof(Variant), typeof(DropdownVariant), typeof(Dropdown), new FrameworkPropertyMetadata(DropdownVariant.Plain));
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(Dropdown),
        new FrameworkPropertyMetadata("не выбрано", (o, _) => ((Dropdown)o).UpdateText()));
    private static readonly DependencyPropertyKey DisplayTextKey = DependencyProperty.RegisterReadOnly(nameof(DisplayText), typeof(string), typeof(Dropdown), new FrameworkPropertyMetadata(""));
    public static readonly DependencyProperty DisplayTextProperty = DisplayTextKey.DependencyProperty;
    private static readonly DependencyPropertyKey IsPlaceholderKey = DependencyProperty.RegisterReadOnly(nameof(IsPlaceholder), typeof(bool), typeof(Dropdown), new FrameworkPropertyMetadata(true));
    public static readonly DependencyProperty IsPlaceholderProperty = IsPlaceholderKey.DependencyProperty;

    public DropdownVariant Variant { get => (DropdownVariant)GetValue(VariantProperty); set => SetValue(VariantProperty, value); }
    public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
    public string DisplayText => (string)GetValue(DisplayTextProperty);
    public bool IsPlaceholder => (bool)GetValue(IsPlaceholderProperty);

    // Выбор пользователя (не срабатывает, когда значение ставится из кода).
    public event Action<object?>? Picked;

    private bool _silent;

    public Dropdown()
    {
        DisplayMemberPath = nameof(DropdownOption.Label);
        SelectedValuePath = nameof(DropdownOption.Value);
        UpdateText();
    }

    public Dropdown(IEnumerable<DropdownOption> options, object? value, Action<object?>? onPick = null, DropdownVariant variant = DropdownVariant.Plain, string? placeholder = null, string? label = null)
        : this()
    {
        Variant = variant;
        if (placeholder is not null) Placeholder = placeholder;
        SetOptions(options, value);
        if (onPick is not null) Picked += onPick;
        if (label is not null) Ui.AutomationName(this, label);
    }

    public static Dropdown Of<T>(IEnumerable<(T Value, string Label)> options, T? value, Action<T> onPick, DropdownVariant variant = DropdownVariant.Plain, string? placeholder = null) =>
        new(options.Select(o => new DropdownOption(o.Value, o.Label)), value, v => onPick((T)v!), variant, placeholder);

    public void SetOptions(IEnumerable<DropdownOption> options, object? value)
    {
        _silent = true;
        ItemsSource = options.ToList();
        SelectedValue = value;
        _silent = false;
        UpdateText();
    }

    public object? Value
    {
        get => SelectedValue;
        set
        {
            _silent = true;
            SelectedValue = value;
            _silent = false;
            UpdateText();
        }
    }

    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectionChanged(e);
        UpdateText();
        if (!_silent && SelectedItem is DropdownOption o) Picked?.Invoke(o.Value);
    }

    private void UpdateText()
    {
        var o = SelectedItem as DropdownOption;
        SetValue(DisplayTextKey, o?.Label ?? Placeholder);
        SetValue(IsPlaceholderKey, o is null);
    }

    // Колесо мыши над закрытым списком прокручивает страницу, а не меняет выбор.
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (IsDropDownOpen) base.OnMouseWheel(e);
    }
}

// Карточка фильтра: подпись капсом и значение (.filter). Wide — не уже 320 px.
public class FilterCard : StackPanel
{
    public bool Wide { get; set; }

    public FilterCard(string label, UIElement body, bool wide = false)
    {
        Wide = wide;
        Margin = new Thickness(0, 8, 0, 8);
        Children.Add(Ui.Caps(label));
        Children.Add(body);
    }

    // Значение без выбора: «12», «139 из 151 проб» — жирным, как значение списка.
    public static FrameworkElement Value(string text, bool mono = true) => new TextBlock
    {
        Text = text,
        FontFamily = mono ? Theme.MonoFont : Theme.UiFont,
        FontSize = mono ? 15 : 14,
        FontWeight = mono ? FontWeights.Bold : FontWeights.Normal,
        Foreground = Theme.Ink,
        MinHeight = 44,
        Padding = new Thickness(0, 12, 0, 0),
    };
}

// Выбор файла: наша кнопка открывает системный диалог (его заменить нельзя),
// а файл можно просто перетащить на кнопку — она подсвечивается.
public class FilePick : Button
{
    public string Filter { get; set; } = "Все файлы|*.*";
    public event Action<string>? FileSelected;

    public FilePick()
    {
        Style = Ui.Style("GhostButton");
        AllowDrop = true;
        Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = Filter };
            if (dlg.ShowDialog(Window.GetWindow(this)) == true) FileSelected?.Invoke(dlg.FileName);
        };
        DragEnter += (_, e) => { if (IsEnabled && e.Data.GetDataPresent(DataFormats.FileDrop)) Ui.SetIsActive(this, true); };
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        DragLeave += (_, _) => Ui.SetIsActive(this, false);
        Drop += (_, e) =>
        {
            Ui.SetIsActive(this, false);
            if (IsEnabled && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) FileSelected?.Invoke(files[0]);
        };
    }

    public FilePick(object content, string filter, Action<string> onFile) : this()
    {
        Content = content;
        Filter = filter;
        FileSelected += onFile;
    }
}

public sealed record TabSpec(string Key, string Label, string? Count = null, bool CountFull = false);

// Вкладки: Sub — подчёркнутая строка (.subtabs), Type — вкладки разделов со счётчиком «13/25» (.type-tabs).
public class TabBar : DashBorder
{
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    public event Action<string>? Selected;
    public bool IsType { get; }

    public TabBar(IEnumerable<TabSpec> tabs, string? selected, Action<string>? onSelect = null, bool type = false)
    {
        IsType = type;
        Sides = Sides.Bottom;
        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _row, Focusable = false };
        Child = scroll;
        if (onSelect is not null) Selected += onSelect;
        SetTabs(tabs, selected);
    }

    public void SetTabs(IEnumerable<TabSpec> tabs, string? selected)
    {
        _row.Children.Clear();
        var first = true;
        foreach (var t in tabs)
        {
            object content = t.Label;
            if (t.Count is not null)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new TextBlock { Text = t.Label, VerticalAlignment = VerticalAlignment.Center });
                sp.Children.Add(new TextBlock
                {
                    Text = t.Count, FontFamily = Theme.MonoFont, FontSize = 11, FontWeight = FontWeights.Normal,
                    Foreground = t.CountFull ? Theme.Ok : Theme.Ink3, Margin = new Thickness(6, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                });
                content = sp;
            }
            var b = new Button { Style = Ui.Style(IsType ? "TypeTab" : "SubTab"), Content = content, Margin = new Thickness(first ? 0 : (IsType ? 18 : 20), 0, 0, 0) };
            Ui.SetIsActive(b, t.Key == selected);
            var key = t.Key;
            b.Click += (_, _) => Selected?.Invoke(key);
            _row.Children.Add(b);
            first = false;
        }
    }
}
