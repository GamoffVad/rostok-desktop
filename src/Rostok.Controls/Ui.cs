using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Rostok.Controls;

// Присоединённые свойства библиотеки и фабрика элементов: экраны собираются из этих кирпичиков,
// как в веб-версии — из классов index.css (.btn-primary, .field, .caps…).
public static class Ui
{
    // ── Присоединённые свойства ──────────────────────────
    // Активное состояние: выбранная вкладка, строка списка, нажатая отметка (aria-selected / aria-pressed / aria-current).
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.RegisterAttached("IsActive", typeof(bool), typeof(Ui), new FrameworkPropertyMetadata(false));
    public static bool GetIsActive(DependencyObject o) => (bool)o.GetValue(IsActiveProperty);
    public static void SetIsActive(DependencyObject o, bool v) => o.SetValue(IsActiveProperty, v);

    public static readonly DependencyProperty IsDangerProperty = DependencyProperty.RegisterAttached("IsDanger", typeof(bool), typeof(Ui), new FrameworkPropertyMetadata(false));
    public static bool GetIsDanger(DependencyObject o) => (bool)o.GetValue(IsDangerProperty);
    public static void SetIsDanger(DependencyObject o, bool v) => o.SetValue(IsDangerProperty, v);

    public static readonly DependencyProperty IsAccentProperty = DependencyProperty.RegisterAttached("IsAccent", typeof(bool), typeof(Ui), new FrameworkPropertyMetadata(false));
    public static bool GetIsAccent(DependencyObject o) => (bool)o.GetValue(IsAccentProperty);
    public static void SetIsAccent(DependencyObject o, bool v) => o.SetValue(IsAccentProperty, v);

    // Уровень 0–3 для окраски нажатой отметки; −1 — без уровня.
    public static readonly DependencyProperty LevelProperty = DependencyProperty.RegisterAttached("Level", typeof(int), typeof(Ui), new FrameworkPropertyMetadata(-1));
    public static int GetLevel(DependencyObject o) => (int)o.GetValue(LevelProperty);
    public static void SetLevel(DependencyObject o, int v) => o.SetValue(LevelProperty, v);

    public static readonly DependencyProperty IsInvalidProperty = DependencyProperty.RegisterAttached("IsInvalid", typeof(bool), typeof(Ui), new FrameworkPropertyMetadata(false));
    public static bool GetIsInvalid(DependencyObject o) => (bool)o.GetValue(IsInvalidProperty);
    public static void SetIsInvalid(DependencyObject o, bool v) => o.SetValue(IsInvalidProperty, v);

    public static readonly DependencyProperty HasTextProperty = DependencyProperty.RegisterAttached("HasText", typeof(bool), typeof(Ui), new FrameworkPropertyMetadata(false));
    public static bool GetHasText(DependencyObject o) => (bool)o.GetValue(HasTextProperty);
    public static void SetHasText(DependencyObject o, bool v) => o.SetValue(HasTextProperty, v);

    // Подсказка в пустом поле (placeholder). У поля пароля дополнительно отслеживается, введено ли что-то.
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached("Placeholder", typeof(string), typeof(Ui),
        new FrameworkPropertyMetadata("", (o, _) =>
        {
            if (o is PasswordBox pb)
            {
                pb.PasswordChanged -= OnPasswordChanged;
                pb.PasswordChanged += OnPasswordChanged;
            }
        }));
    public static string GetPlaceholder(DependencyObject o) => (string)o.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject o, string v) => o.SetValue(PlaceholderProperty, v);
    private static void OnPasswordChanged(object sender, RoutedEventArgs e) { if (sender is PasswordBox pb) SetHasText(pb, pb.Password.Length > 0); }

    // ── Стили ───────────────────────────────────────────
    public static Style Style(string key) => (Style)Application.Current.FindResource(key);

    // ── Текст ───────────────────────────────────────────
    public static TextBlock Text(string text, string? style = null, Brush? color = null, double? size = null, FontWeight? weight = null, bool wrap = false)
    {
        var t = new TextBlock { Text = text };
        if (style is not null) t.Style = Style(style);
        if (color is not null) t.Foreground = color;
        if (size is not null) t.FontSize = size.Value;
        if (weight is not null) t.FontWeight = weight.Value;
        if (wrap) t.TextWrapping = TextWrapping.Wrap;
        return t;
    }

    public static TextBlock H1(string text) => Text(text, "H1");
    public static TextBlock H2(string text) => Text(text, "H2");
    // Подзаголовок капсом: «РЕБЁНОК», «ГРУППЫ ПРОБ» (text-transform: uppercase)
    public static TextBlock Caps(string text) => Text(Upper(text), "Caps");
    public static TextBlock H3(string text) => Text(Upper(text), "H3");
    public static TextBlock Subtitle(string text) => Text(text, "Subtitle");
    public static TextBlock Faint(string text) => Text(text, "FaintText");
    public static TextBlock Body(string text) => Text(text, "Body");
    public static TextBlock Num(string text, Brush? color = null, double size = 13, FontWeight? weight = null) =>
        Text(text, "Num", color, size, weight);
    public static TextBlock Muted(string text, double size = 13) => Text(text, null, Theme.Ink3, size);

    public static string Upper(string s) => s.ToUpper(CultureInfo.GetCultureInfo("ru-RU"));

    public static Run Run(string text, Brush? color = null, FontWeight? weight = null, bool mono = false, double? size = null)
    {
        var r = new Run(text);
        if (color is not null) r.Foreground = color;
        if (weight is not null) r.FontWeight = weight.Value;
        if (mono) r.FontFamily = Theme.MonoFont;
        if (size is not null) r.FontSize = size.Value;
        return r;
    }

    public static TextBlock Rich(params Inline[] inlines)
    {
        var t = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Theme.Ink2, FontSize = 14 };
        t.Inlines.AddRange(inlines);
        return t;
    }

    // ── Кнопки ──────────────────────────────────────────
    public static object Content(IconKind? icon, string text, double gap = 8)
    {
        if (icon is null) return text;
        var s = new StackPanel { Orientation = Orientation.Horizontal };
        s.Children.Add(new Icon(icon.Value) { VerticalAlignment = VerticalAlignment.Center });
        s.Children.Add(new TextBlock { Text = text, Margin = new Thickness(gap, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return s;
    }

    public static Button Button(string style, object content, Action? onClick = null, string? tip = null)
    {
        var b = new Button { Style = Style(style), Content = content };
        if (onClick is not null) b.Click += (_, _) => onClick();
        if (tip is not null) b.ToolTip = tip;
        return b;
    }

    public static Button Primary(string text, Action? onClick = null, IconKind? icon = null) => Button("PrimaryButton", Content(icon, text), onClick);
    public static Button Ghost(string text, Action? onClick = null, IconKind? icon = null) => Button("GhostButton", Content(icon, text), onClick);
    public static Button Danger(string text, Action? onClick = null, IconKind? icon = null) => Button("DangerButton", Content(icon, text), onClick);

    public static Button TextAction(string text, Action? onClick = null, bool danger = false) =>
        Button(danger ? "TextActionDanger" : "TextAction", new TextBlock { Text = text }, onClick);

    public static Button Link(string text, Action onClick) => Button("NameLink", new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, onClick);

    public static Button IconButton(IconKind icon, string tip, Action? onClick = null, bool danger = false)
    {
        var b = Button("IconButton", new Icon(icon), onClick, tip);
        SetIsDanger(b, danger);
        AutomationName(b, tip);
        return b;
    }

    public static void AutomationName(DependencyObject o, string name) => System.Windows.Automation.AutomationProperties.SetName(o, name);

    // ── Поля ────────────────────────────────────────────
    public static TextBox Input(string value = "", string placeholder = "", Action<string>? onChange = null, bool multiline = false, int rows = 1)
    {
        var t = new TextBox { Text = value };
        SetPlaceholder(t, placeholder);
        if (multiline)
        {
            t.AcceptsReturn = true;
            t.TextWrapping = TextWrapping.Wrap;
            t.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            t.MinHeight = Math.Max(38, 16 + rows * 20);
        }
        if (onChange is not null) t.TextChanged += (_, _) => onChange(t.Text);
        return t;
    }

    // Подпись поля капсом над элементом (.field + .field-label)
    public static FrameworkElement Field(string label, UIElement control, double? width = null)
    {
        var s = new StackPanel();
        var l = Caps(label);
        l.Margin = new Thickness(0, 0, 0, 5);
        s.Children.Add(l);
        s.Children.Add(control);
        if (width is not null) s.Width = width.Value;
        return s;
    }

    // ── Раскладка ───────────────────────────────────────
    public static Stack VStack(double spacing, params UIElement?[] children)
    {
        var s = new Stack { Spacing = spacing };
        foreach (var c in children) if (c is not null) s.Children.Add(c);
        return s;
    }

    public static Stack HStack(double spacing, params UIElement?[] children)
    {
        var s = new Stack { Spacing = spacing, Orientation = Orientation.Horizontal };
        foreach (var c in children) if (c is not null) s.Children.Add(c);
        return s;
    }

    public static Flow Row(double gap, params UIElement?[] children)
    {
        var f = new Flow { Gap = gap, RowGap = gap };
        foreach (var c in children) if (c is not null) f.Children.Add(c);
        return f;
    }

    // Раздел страницы (.card): пунктир сверху и отступы 24 px; первый раздел — без линии.
    public static DashBorder Card(UIElement child, bool top = true, double padTop = 24, double padBottom = 24) => new()
    {
        Sides = top ? Sides.Top : Sides.None,
        Padding = new Thickness(0, padTop, 0, padBottom),
        Child = child,
    };

    // Заголовок блока: слева заголовок, справа — действия (.block-head)
    public static FrameworkElement BlockHead(UIElement title, UIElement? right = null, double marginBottom = 12)
    {
        var g = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, marginBottom) };
        if (right is FrameworkElement fe)
        {
            fe.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(fe, Dock.Right);
            g.Children.Add(fe);
        }
        if (title is FrameworkElement t) t.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(title);
        return g;
    }

    public static FrameworkElement Line(double marginTop = 0, double marginBottom = 0, bool dashed = true, Brush? color = null) => new DashBorder
    {
        Sides = Sides.Top, Dashed = dashed, Stroke = color ?? (dashed ? Theme.Dash : Theme.Line),
        Margin = new Thickness(0, marginTop, 0, marginBottom), Height = 1,
    };

    // Заметка с акцентной чертой слева (.help-note)
    public static FrameworkElement HelpNote(UIElement content, bool amber = false)
    {
        return new Border
        {
            BorderBrush = amber ? Theme.Amber : Theme.Accent,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(14, 2, 0, 2),
            Margin = new Thickness(0, 14, 0, 14),
            MaxWidth = 640,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = content,
        };
    }

    // Строка состояния под действием: «Сохранено», «Не удалось прочитать файл» (.status ok/bad)
    public static TextBlock Status(string text, bool ok) =>
        Text(text, null, ok ? Theme.Ok : Theme.Danger, 13, FontWeights.SemiBold, wrap: true);

    // Пустое состояние (.empty)
    public static FrameworkElement Empty(string text, UIElement? action = null)
    {
        var s = new StackPanel { Margin = new Thickness(0, 28, 0, 28), MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
        var t = Faint(text);
        t.Margin = new Thickness(0, 0, 0, 14);
        s.Children.Add(t);
        if (action is FrameworkElement a)
        {
            a.HorizontalAlignment = HorizontalAlignment.Left;
            s.Children.Add(a);
        }
        return s;
    }

    public static T With<T>(this T el, Action<T> setup) where T : DependencyObject
    {
        setup(el);
        return el;
    }

    public static T Margin<T>(this T el, double left, double top, double right, double bottom) where T : FrameworkElement
    {
        el.Margin = new Thickness(left, top, right, bottom);
        return el;
    }

    public static T Tip<T>(this T el, string? tip) where T : FrameworkElement
    {
        if (!string.IsNullOrEmpty(tip)) el.ToolTip = tip;
        return el;
    }

    // Отложенный вызов: запись текста в базу — через полсекунды после последнего нажатия клавиши.
    public static Action<T> Debounce<T>(Action<T> action, int ms = 400)
    {
        DispatcherTimer? timer = null;
        T last = default!;
        return value =>
        {
            last = value;
            timer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            timer.Stop();
            timer.Tick -= Fire;
            timer.Tick += Fire;
            timer.Start();
        };
        void Fire(object? s, EventArgs e)
        {
            timer!.Stop();
            action(last);
        }
    }

    public static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var n = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }

    public static bool IsTyping(object? source) =>
        source is TextBox or PasswordBox || (source is DependencyObject d && Descendants(d).Any(x => x is TextBox { IsKeyboardFocused: true }));

    public static Cursor Hand => Cursors.Hand;
}
