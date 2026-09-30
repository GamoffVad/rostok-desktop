using System.Windows;
using System.Windows.Controls;
using Rostok.Controls;
using Rostok.Core;
using Rostok.Desktop.Components;
using Rostok.Desktop.Services;

namespace Rostok.Desktop.Pages;

// «Дети»: список группы и состояние обследования на выбранном срезе.
public sealed class ChildrenPage(Route route) : PageBase(route)
{
    private string? _mode;          // child | group
    private string _addMode = "one"; // one | list
    private string _names = "";
    private string _groupName = "";
    private string? _confirmId;
    private Child _one = NewDraft();
    private string? _settingsFor;
    private string _settingsName = "";
    private bool _askGroup;

    private static Child NewDraft() => new() { Relatives = [Relatives.New("мама")] };

    protected override UIElement Build()
    {
        var group = S.SelectedGroup;
        var period = S.SelectedPeriod;
        var kids = D.ChildrenOf(group?.Id);

        var page = Page(PageHeader("Дети", "Список группы и состояние обследования на выбранном срезе. Нажмите на фамилию, чтобы открыть карту ребёнка.",
            ViewOnly ? null : Ui.Primary("Добавить детей", () => { _mode = _mode == "child" ? null : "child"; Refresh(); }, IconKind.Plus),
            ViewOnly ? null : Ui.Ghost("Новая группа", () => { _mode = _mode == "group" ? null : "group"; Refresh(); })));
        if (ViewOnly) _mode = null;

        if (_mode == "child" && group is not null) page.Children.Add(AddChildForm(group));
        if (_mode == "group") page.Children.Add(AddGroupForm());

        page.Children.Add(Filters(true, GroupFilter(group), PeriodFilter(period), new FilterCard("Детей в группе", FilterCard.Value(kids.Count.ToString()))));

        if (kids.Count == 0)
            page.Children.Add(Ui.Empty("В группе пока нет детей. Добавьте фамилии списком — можно вставить колонку из Excel."));
        else
            page.Children.Add(Tbl.Scroll(ChildrenTable(kids, period)));

        if (group is not null && !ViewOnly) page.Children.Add(GroupSettings(group, kids.Count));
        return page;
    }

    private Tbl ChildrenTable(List<Child> kids, Period? period)
    {
        var t = new Tbl(Col.Px(46), Col.Star(1.2, min: 150), Col.Auto(), Col.Star(1.4, min: 150), Col.Auto(Align.Right),
            Col.Auto(Align.Center), Col.Auto(Align.Center), Col.Auto(Align.Right)) { Margin = new Thickness(0, 0, 0, 0) };
        t.Header(["№", "Ребёнок", "Возраст", "Родители", "Заполнено", .. M.Blocks.Select(b => (object?)b.Title), ""]);
        for (var i = 0; i < kids.Count; i++)
        {
            var c = kids[i];
            var s = D.ScoresOf(c.Id, period?.Id);
            var (filled, total) = Calc.TotalProgress(s);
            var id = c.Id;
            object parents = c.Relatives.Count > 0
                ? Ui.Text(string.Join(", ", c.Relatives.Select(r => r.Role.Length > 0 ? r.Role : "контакт")), null, Theme.Ink2, 13, wrap: true)
                    .Tip(string.Join("\n", c.Relatives.Select(r => $"{r.Role}: {r.Name} {Relatives.PhonesText(r)}")))
                : ViewOnly ? Ui.Faint("нет") : Ui.TextAction("добавить", () => Go($"/child/{id}"));
            var blockCells = M.Blocks.Select(b =>
            {
                var m = Calc.BlockMean(b.Sections, s);
                return (object?)Ui.HStack(6, new Level(Calc.LevelOf(m)), Ui.Num(Calc.Fmt(m), Theme.Ink3).With(x => x.VerticalAlignment = VerticalAlignment.Center));
            });
            UIElement actions = ViewOnly
                ? Ui.TextAction("карта ребёнка", () => Go($"/child/{id}"))
                : _confirmId == c.Id
                ? Ui.HStack(4, Ui.TextAction("удалить с баллами", () => { S.RemoveChild(id); _confirmId = null; Refresh(); }, danger: true), Ui.Muted(" · ", 12).With(x => x.VerticalAlignment = VerticalAlignment.Center), Ui.TextAction("отмена", () => { _confirmId = null; Refresh(); }))
                : Ui.HStack(4, Ui.TextAction("обследовать", () => Go("/exam", ("child", id))), Ui.Muted(" · ", 12).With(x => x.VerticalAlignment = VerticalAlignment.Center), Ui.TextAction("удалить", () => { _confirmId = id; Refresh(); }));
            t.Row([
                Ui.Num((i + 1).ToString(), Theme.Ink3),
                Ui.Link(c.Name, () => Go($"/child/{id}")),
                Calc.AgeText(c.BirthDate) is { Length: > 0 } age ? age : Ui.Faint("не указан"),
                parents,
                Ui.Num($"{filled}/{total}", filled == total ? Theme.Ok : Theme.Ink2),
                .. blockCells,
                actions,
            ]);
        }
        return t;
    }

    private FrameworkElement AddChildForm(Group group)
    {
        var tabs = new TabBar([new TabSpec("one", "Один ребёнок с родителями"), new TabSpec("list", "Списком фамилий")], _addMode, k => { _addMode = k; Refresh(); }, type: true);
        UIElement body;
        if (_addMode == "one")
        {
            var name = Ui.Input(_one.Name, "", v => _one.Name = v);
            var birth = new DateField { Label = "Дата рождения", Min = Calc.BirthMin(), Max = Calc.TodayIso(), Value = _one.BirthDate };
            birth.ValueChanged += v => _one.BirthDate = v;
            var tpmpk = Ui.Input(_one.Tpmpk, "необязательно", v => _one.Tpmpk = v);
            var submit = Ui.Primary($"Добавить в «{group.Name}»", () =>
            {
                if (_one.Name.Trim().Length == 0) return;
                S.AddChild(new Child { GroupId = group.Id, Name = _one.Name.Trim(), BirthDate = _one.BirthDate, Tpmpk = _one.Tpmpk.Trim(), Relatives = Relatives.Clean(_one.Relatives) });
                _one = NewDraft();
                _mode = null;
                Refresh();
            });
            submit.IsEnabled = _one.Name.Trim().Length > 0;
            name.TextChanged += (_, _) => submit.IsEnabled = name.Text.Trim().Length > 0;
            body = Ui.VStack(12,
                ChildFields(Ui.Field("Фамилия и имя ребёнка", name), Ui.Field("Дата рождения", birth), Ui.Field("Заключение ТПМПК", tpmpk)),
                Ui.H3("Родители и родственники").Margin(0, 8, 0, 0),
                new RelativesEditor(_one.Relatives, () => { }),
                Ui.Row(8, submit, Ui.Ghost("Отмена", () => { _mode = null; Refresh(); })));
            Dispatcher.BeginInvoke(() => name.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        }
        else
        {
            var names = Ui.Input(_names, "Белкина Соня\nВоронов Лев", v => _names = v, multiline: true, rows: 5);
            names.MinHeight = 120;
            body = Ui.VStack(12,
                Ui.Field("Фамилия и имя — по одному ребёнку на строку", names),
                Ui.Faint("Родителей и родственников можно добавить потом — в карте ребёнка."),
                Ui.Row(8, Ui.Primary($"Добавить в «{group.Name}»", () =>
                {
                    var list = _names.Split('\n').Select(s => System.Text.RegularExpressions.Regex.Replace(s, @"^\s*\d+[.)]?\s*", "").Trim()).Where(s => s.Length > 0).ToList();
                    if (list.Count == 0) return;
                    S.AddChildren(group.Id, list);
                    _names = "";
                    _mode = null;
                    Refresh();
                }), Ui.Ghost("Отмена", () => { _mode = null; Refresh(); })));
            Dispatcher.BeginInvoke(() => names.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        }
        return FormBox(Ui.VStack(12, tabs, body));
    }

    private FrameworkElement AddGroupForm()
    {
        var name = Ui.Input(_groupName, "Группа № 7, старшая", v => _groupName = v);
        void Create()
        {
            if (_groupName.Trim().Length == 0) return;
            var g = S.AddGroup(_groupName);
            S.SetUi(u => u.GroupId = g.Id);
            _groupName = "";
            _mode = null;
            Refresh();
        }
        name.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Create(); };
        Dispatcher.BeginInvoke(() => name.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        return FormBox(Ui.VStack(12, Ui.Field("Название группы", name), Ui.Row(8, Ui.Primary("Создать", Create), Ui.Ghost("Отмена", () => { _mode = null; Refresh(); }))));
    }

    // Форма на подложке (.form)
    public static Border FormBox(UIElement child) => new()
    {
        Background = Theme.FormBg, CornerRadius = new CornerRadius(2), Padding = new Thickness(20), Margin = new Thickness(0, 0, 0, 18), Child = child,
    };

    // Поля ребёнка в одну строку: ФИ | дата рождения | ТПМПК (.child-fields)
    public static Grid ChildFields(UIElement name, UIElement birth, UIElement tpmpk, UIElement? note = null)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var (el, col) in new[] { (name, 0), (birth, 2), (tpmpk, 4) })
        {
            if (el is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(el, col);
            g.Children.Add(el);
        }
        if (note is not null)
        {
            Grid.SetRow(note, 1);
            Grid.SetColumnSpan(note, 5);
            if (note is FrameworkElement n) n.Margin = new Thickness(0, 10, 0, 0);
            g.Children.Add(note);
        }
        return g;
    }

    private FrameworkElement GroupSettings(Group group, int count)
    {
        if (_settingsFor != group.Id) { _settingsFor = group.Id; _settingsName = group.Name; _askGroup = false; }
        var name = Ui.Input(_settingsName);
        name.Width = 420;
        var save = Ui.Ghost("Сохранить название", () => { S.RenameGroup(group.Id, _settingsName); Refresh(); });
        save.IsEnabled = _settingsName.Trim().Length > 0 && _settingsName != group.Name;
        name.TextChanged += (_, _) => { _settingsName = name.Text; save.IsEnabled = _settingsName.Trim().Length > 0 && _settingsName != group.Name; };
        var row = new Flow { Gap = 8, AlignBottom = true };
        row.Children.Add(Ui.Field("Название", name));
        row.Children.Add(save);
        if (_askGroup)
        {
            row.Children.Add(Ui.Danger($"Удалить группу и {count} детей", () => { S.RemoveGroup(group.Id); _askGroup = false; Go("/"); }, IconKind.Trash));
            row.Children.Add(Ui.Ghost("Отмена", () => { _askGroup = false; Refresh(); }));
        }
        else row.Children.Add(Ui.Ghost("Удалить группу", () => { _askGroup = true; Refresh(); }, IconKind.Trash));
        return Ui.Card(Ui.VStack(0, Ui.BlockHead(Ui.H2("Настройки группы")), row), top: true);
    }
}
