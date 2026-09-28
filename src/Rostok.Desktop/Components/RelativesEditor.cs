using System.Windows;
using System.Windows.Controls;
using Rostok.Controls;
using Rostok.Core;

namespace Rostok.Desktop.Components;

// Родители и родственники ребёнка. Одна сетка на всю карточку: вид | значение | действие.
// Вид — выпадающий список из словаря или неизменяемая подпись («эл. почта», «примечание»).
// Кем приходится, виды телефонов и адресов — словари («Администрирование → Словари»).
public sealed class RelativesEditor : StackPanel
{
    private readonly List<Relative> _list;
    private readonly Action _onChange;

    public RelativesEditor(List<Relative> list, Action onChange)
    {
        _list = list;
        _onChange = onChange;
        Render();
    }

    private void Changed(bool rerender)
    {
        if (rerender) Render();
        _onChange();
    }

    // Варианты из словаря; значение не из словаря (старая запись или удалённый вариант) остаётся выбранным.
    private static IEnumerable<DropdownOption> DictOptions(List<string> list, string current) =>
        list.Concat(current.Length > 0 && !list.Contains(current) ? [current] : Array.Empty<string>()).Select(v => new DropdownOption(v, v));

    private static Grid Line(UIElement kind, UIElement value, UIElement? action, bool head = false)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        var va = head ? VerticalAlignment.Bottom : VerticalAlignment.Center;
        if (kind is FrameworkElement k) k.VerticalAlignment = va;
        if (value is FrameworkElement v) v.VerticalAlignment = va;
        g.Children.Add(kind);
        Grid.SetColumn(value, 2);
        g.Children.Add(value);
        if (action is FrameworkElement a)
        {
            a.VerticalAlignment = va;
            Grid.SetColumn(a, 4);
            g.Children.Add(a);
        }
        return g;
    }

    private static TextBlock KindTag(string text) => Ui.Caps(text).With(t => t.Margin = new Thickness(11, 0, 0, 0));

    private Button DeleteBtn(string label, Action onClick)
    {
        var b = Ui.IconButton(IconKind.Trash, label, onClick, danger: true);
        return b;
    }

    private void Render()
    {
        Children.Clear();
        for (var i = 0; i < _list.Count; i++)
        {
            var r = _list[i];
            var box = new DashBorder { Sides = i == 0 ? Sides.None : Sides.Top, Padding = new Thickness(0, i == 0 ? 0 : 12, 0, 12) };
            var s = new StackPanel();

            var role = new Dropdown(DictOptions(M.Roles, r.Role), r.Role, v => { r.Role = (string)v!; Changed(false); }, DropdownVariant.Light, "выберите", "Кем приходится");
            var name = Ui.Input(r.Name, "Фамилия Имя Отчество", t => { r.Name = t; Changed(false); });
            var idx = i;
            s.Children.Add(Line(Ui.Field("Кем приходится", role), Ui.Field("ФИО", name),
                DeleteBtn($"Удалить контакт {i + 1}", () => { _list.Remove(r); Changed(true); }), head: true));

            foreach (var p in r.Phones)
            {
                var kind = new Dropdown(DictOptions(M.PhoneKinds, p.Kind), p.Kind, v => { p.Kind = (string)v!; Changed(false); }, DropdownVariant.Light, "вид", "Телефон: вид");
                var input = Ui.Input(p.Value, "+7 …", t => { p.Value = t; Changed(false); });
                s.Children.Add(Line(kind, input, DeleteBtn("Удалить телефон", () => { r.Phones.Remove(p); Changed(true); })));
            }
            foreach (var e in r.Emails)
            {
                var input = Ui.Input(e.Value, "name@example.ru", t => { e.Value = t; Changed(false); });
                s.Children.Add(Line(KindTag("эл. почта"), input, DeleteBtn("Удалить почту", () => { r.Emails.Remove(e); Changed(true); })));
            }
            foreach (var a in r.Addresses)
            {
                var kind = new Dropdown(DictOptions(M.AddressKinds, a.Kind), a.Kind, v => { a.Kind = (string)v!; Changed(false); }, DropdownVariant.Light, "вид", "Адрес: вид");
                var input = Ui.Input(a.Value, "Город, улица, дом, квартира", t => { a.Value = t; Changed(false); }, multiline: true);
                input.MinHeight = 38;
                s.Children.Add(Line(kind, input, DeleteBtn("Удалить адрес", () => { r.Addresses.Remove(a); Changed(true); })));
            }
            var note = Ui.Input(r.Note, "Удобное время для связи, кто забирает ребёнка…", t => { r.Note = t; Changed(false); });
            s.Children.Add(Line(KindTag("примечание"), note, null));

            var legal = new Checkbox("Законный представитель", r.Legal, v => { r.Legal = v; Changed(false); });
            var adds = Ui.HStack(16,
                Ui.TextAction("+ телефон", () => { r.Phones.Add(Relatives.Entry(M.PhoneKinds.Count > 0 ? M.PhoneKinds[r.Phones.Count % M.PhoneKinds.Count] : "")); Changed(true); }),
                Ui.TextAction("+ почта", () => { r.Emails.Add(Relatives.Entry()); Changed(true); }),
                Ui.TextAction("+ адрес", () => { r.Addresses.Add(Relatives.Entry(M.AddressKinds.Count > 0 ? M.AddressKinds[r.Addresses.Count % M.AddressKinds.Count] : "")); Changed(true); }));
            var foot = new DockPanel { Margin = new Thickness(188, 0, 46, 0) };
            DockPanel.SetDock(adds, Dock.Right);
            foot.Children.Add(adds);
            foot.Children.Add(legal);
            s.Children.Add(foot);
            _ = idx;
            box.Child = s;
            Children.Add(box);
        }

        var add = Ui.Ghost("Добавить родителя или родственника", () => { _list.Add(Relatives.New(Relatives.NextRole(_list))); Changed(true); }, IconKind.Plus);
        var actions = Ui.Row(16, add, Ui.Faint("Варианты «Кем приходится», видов телефонов и адресов — в «Администрирование → Словари».").With(t => t.VerticalAlignment = VerticalAlignment.Center));
        Children.Add(new DashBorder { Sides = Sides.Top, Padding = new Thickness(0, 12, 0, 0), Child = actions });
    }
}
