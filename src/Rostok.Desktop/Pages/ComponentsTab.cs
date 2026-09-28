using System.Windows;
using System.Windows.Controls;
using Rostok.Controls;
using Rostok.Core;

namespace Rostok.Desktop.Pages;

// Витрина библиотеки компонентов (Rostok.Controls): каждый элемент вживую и во всех состояниях.
// Новые экраны собираются только из этих элементов.
public sealed class ComponentsTab
{
    private object? _dd = "b";
    private string _date = "2020-06-08";
    private string _empty = "";
    private bool _on = true, _off;
    private string? _file;
    private int _mark = 1;
    private string _sound = "auto";
    private string _tab = "a";
    private Action? _rerender;
    private readonly ContentControl _host = new();

    private static readonly DropdownOption[] Options =
        [new("a", "Группа № 5 «Рябинка»"), new("b", "Группа № 11, логопедическая"), new("c", "Группа № 7, старшая")];

    private static FrameworkElement Spec(string title, string note, UIElement body) =>
        new DashBorder
        {
            Sides = Sides.Top, Padding = new Thickness(0, 18, 0, 18),
            Child = Ui.VStack(0, Ui.BlockHead(Ui.H2(title), Ui.Num(note, Theme.Ink3, 12)), body),
        };

    private static Grid Grid3(params UIElement[] cells)
    {
        var g = new Grid();
        for (var i = 0; i < 3; i++)
        {
            if (i > 0) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
        }
        for (var i = 0; i < cells.Length; i++)
        {
            Grid.SetColumn(cells[i], i * 2);
            if (cells[i] is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Top;
            g.Children.Add(cells[i]);
        }
        return g;
    }

    public UIElement Build()
    {
        _rerender = () => _host.Content = Body();
        _host.Content = Body();
        return _host;
    }

    private UIElement Body()
    {
        var s = new StackPanel();
        s.Children.Add(Ui.Rich(Ui.Run("Подключение: "), Ui.Run("xmlns:c=\"clr-namespace:Rostok.Controls;assembly=Rostok.Controls\"", Theme.Ink3, mono: true, size: 12),
            Ui.Run(". Системные элементы Windows — выбор даты, флажок, выпадающий список, подсказка — в приложении не используются: у каждого есть своя версия в стиле «Тёплого мела»."))
            .With(t => { t.Foreground = Theme.Ink3; t.MaxWidth = 760; t.HorizontalAlignment = HorizontalAlignment.Left; t.Margin = new Thickness(0, 0, 0, 12); }));

        var disabled = Ui.Ghost("Недоступно");
        disabled.IsEnabled = false;
        s.Children.Add(Spec("Кнопки", "PrimaryButton · GhostButton · TextAction · IconButton", Ui.Row(14,
            Ui.Primary("Главное действие", null, IconKind.Plus),
            Ui.Ghost("Второстепенное", null, IconKind.Download),
            disabled,
            Ui.TextAction("действие в строке"),
            Ui.TextAction("удалить", null, danger: true),
            Ui.IconButton(IconKind.Pencil, "Изменить"),
            Ui.IconButton(IconKind.Trash, "Удалить", null, danger: true))));

        var pwd = new PasswordBox();
        Ui.SetPlaceholder(pwd, "пароль рабочего пространства");
        s.Children.Add(Spec("Поля ввода", "TextBox · PasswordBox", Ui.VStack(14,
            Grid3(Ui.Field("Обычное", Ui.Input("", "Фамилия Имя Отчество")), Ui.Field("Заполненное", Ui.Input("Воронова Мария Викторовна")), Ui.Field("Многострочное", Ui.Input("", "Заметки специалиста", multiline: true, rows: 2))),
            Grid3(Ui.Field("Пароль", pwd), Ui.Field("Ошибка", Ui.Input("31.02.2020").With(t => Ui.SetIsInvalid(t, true))), new Border()))));

        var plainBar = new FilterBar { Fit = true, Borders = Sides.Top };
        plainBar.Children.Add(new FilterCard("Над контентом — plain", new Dropdown(Options, _dd, v => { _dd = v; _rerender!(); }, label: "Группа")));
        s.Children.Add(Spec("Выпадающий список", "<c:Dropdown Variant=\"Plain | Light\" />", Grid3(
            plainBar,
            Ui.Field("В форме — light", new Dropdown(Options, _dd, v => { _dd = v; _rerender!(); }, DropdownVariant.Light, label: "Группа")),
            Ui.Field("Пустой", new Dropdown(Options, null, v => { _dd = v; _rerender!(); }, DropdownVariant.Light, "выберите", "Группа")))));

        var d1 = new DateField { Label = "Дата рождения", Max = Calc.TodayIso(), Value = _date };
        d1.ValueChanged += v => { _date = v; _rerender!(); };
        var d2 = new DateField { Label = "Дата", Value = _empty };
        d2.ValueChanged += v => _empty = v;
        s.Children.Add(Spec("Дата", "<c:DateField Value Min Max ValueChanged />", Grid3(
            Ui.Field("С датой", d1), Ui.Field("Пустая", d2),
            Ui.Rich(Ui.Run("Дату можно набрать цифрами — точки встанут сами — или выбрать в календаре. Стрелки двигают день, PageUp / PageDown — месяц, Shift + PageUp / PageDown — год, Enter — выбрать, Esc — закрыть. Выбрано: "), Ui.Run(_date.Length > 0 ? _date : "—", mono: true))
                .With(t => { t.FontSize = 13; t.Foreground = Theme.Faint; }))));

        s.Children.Add(Spec("Флажок", "<c:Checkbox IsChecked />", Ui.Row(14,
            new Checkbox("Законный представитель", _on, v => _on = v),
            new Checkbox("Не отмечен", _off, v => _off = v),
            new Checkbox("Недоступен", true) { IsEnabled = false })));

        s.Children.Add(Spec("Выбор файла", "<c:FilePick Filter FileSelected />", Ui.Row(14,
            new FilePick(Ui.Content(IconKind.Upload, "Выбрать или перетащить файл"), "Excel или копия (*.xls;*.xlsx;*.json)|*.xls;*.xlsx;*.json", f => { _file = f; _rerender!(); }),
            Ui.Faint(_file is null ? "Файл не выбран — перетащите его на кнопку" : $"Выбран: {Path.GetFileName(_file)}").With(t => t.VerticalAlignment = VerticalAlignment.Center))));

        var marks = Ui.HStack(6, Enumerable.Range(0, 4).Select(v =>
        {
            var b = Ui.Button("Mark", v.ToString(), () => { _mark = v; _rerender!(); });
            Ui.SetIsActive(b, _mark == v);
            Ui.SetLevel(b, v);
            return (UIElement)b;
        }).ToArray());
        var sounds = Ui.HStack(6, M.SoundStates.Select(o =>
        {
            var b = Ui.Button("Mark", o.Mark, () => { _sound = o.Value; _rerender!(); }, o.Label);
            Ui.SetIsActive(b, _sound == o.Value);
            Ui.SetLevel(b, o.Score);
            return (UIElement)b;
        }).ToArray());
        s.Children.Add(Spec("Отметки балла", "Mark · MarkWide · Ui.Level", Ui.Row(18, marks, sounds)));

        s.Children.Add(Spec("Уровни и динамика", "Level · Delta · DistBar · Legends.LevelLegend", Ui.VStack(12,
            Ui.Row(14, new Level(0), new Level(1), new Level(2), new Level(3), new Level(null), Delta.Of(2.1, 1.4), Delta.Of(1.2, 1.6), new DistBar([3, 5, 2, 2], 12) { Width = 240, VerticalAlignment = VerticalAlignment.Center }),
            Legends.LevelLegend())));

        s.Children.Add(Spec("Вкладки", "TabBar (Sub · Type)", Ui.VStack(8,
            new TabBar([new TabSpec("a", "Профиль"), new TabSpec("b", "Программа"), new TabSpec("c", "Заключение")], _tab, k => { _tab = k; _rerender!(); }),
            new TabBar([new TabSpec("s", "Звуки", "13/25"), new TabSpec("f", "Фонетика", "13/13", true)], "s", null, type: true))));

        s.Children.Add(Spec("Подсказка", "ToolTip", Ui.Row(14,
            Ui.TextAction("наведите на меня").Tip("Подсказка появляется при наведении и при фокусе с клавиатуры"),
            new Level(2, "средний балл 1,85"))));

        s.Children.Add(Spec("Графики", "Radar · Donut · TrendLine · Dumbbell · LevelColumns", Ui.VStack(20,
            TwoCols(
                new Radar([new("Звуки", 2.4, 1.6), new("Фонетика", 2, 1.2), new("Лексика", 1.8, 1), new("Грамматика", 2.2, 1.4), new("Связная речь", 1.6, 0.9)], "НГ", "КГ"),
                Ui.VStack(12,
                    new Donut([new("норма", 3, Theme.OutcomeColor("norm")), new("улучшение", 7, Theme.OutcomeColor("minor")), new("без динамики", 2, Theme.OutcomeColor("none"))], "12", "детей"),
                    new TrendLine([new("НГ 24/25", 2.4), new("КГ 24/25", 1.8), new("НГ 25/26", 1.3)]))),
            TwoCols(
                new Dumbbell([new("Память", 2.2, 1.6), new("Моторика", 1.8, 1.9), new("Гнозис", 2.0, 1.1)], "НГ", "КГ"),
                new LevelColumns([0, 3, 4, 5], [0, 9, 1, 2], "НГ", "КГ")))));

        var icons = new Flow { Gap = 6, RowGap = 6 };
        foreach (var k in Icon.AllKinds)
            icons.Children.Add(new DashBorder
            {
                Sides = Sides.All, Width = 132, MinHeight = 40, Padding = new Thickness(8, 0, 8, 0),
                Child = Ui.HStack(8, new Icon(k) { VerticalAlignment = VerticalAlignment.Center }, Ui.Text(k.ToString(), null, Theme.Ink2, 12).With(t => t.VerticalAlignment = VerticalAlignment.Center)).With(x => x.VerticalAlignment = VerticalAlignment.Center),
                ToolTip = $"IconKind.{k}",
            });
        s.Children.Add(Spec("Иконки", "<c:Icon Kind=\"…\" />", icons));
        return s;
    }

    private static Grid TwoCols(UIElement a, UIElement b)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(a);
        Grid.SetColumn(b, 2);
        g.Children.Add(b);
        return g;
    }
}
