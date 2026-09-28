using System.Windows;
using System.Windows.Controls;
using Rostok.Controls;
using Rostok.Core;
using Rostok.Desktop.Services;

namespace Rostok.Desktop.Pages;

// «С чего начать» — пока в рабочем пространстве нет ни одной группы.
public sealed class WelcomePage(Route route) : PageBase(route)
{
    private string _name = "";
    private string? _error;
    private bool _busy;

    protected override UIElement Build()
    {
        var steps = new StackPanel { MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 18, 0, 18) };
        var items = new (string Title, string Text)[]
        {
            ("Создайте группу и список детей", "Фамилии можно вставить списком из Excel или Word — по одной на строку."),
            ("Проведите обследование", "По одному ребёнку — на экране «Обследование», или всей группой в таблице — «Протокол группы». Клавиши 0–3 ставят балл и переходят к следующей пробе."),
            ("Смотрите динамику", "Сравнение начала и конца года по каждому ребёнку и группе, итог коррекционной работы, выгрузка в Excel."),
            ("Распечатайте заключение", "Черновик собирается из баллов: остаётся поправить формулировки."),
        };
        for (var i = 0; i < items.Length; i++)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(Ui.Num((i + 1).ToString(), Theme.Accent, 20, FontWeights.Medium));
            var t = new StackPanel();
            t.Children.Add(Ui.Text(items[i].Title, null, Theme.Ink, 14, FontWeights.SemiBold));
            t.Children.Add(Ui.Muted(items[i].Text).With(x => x.TextWrapping = TextWrapping.Wrap));
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            steps.Children.Add(new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 12, 0, 12), Child = g });
        }

        var name = Ui.Input(_name, "Группа № 11, логопедическая");
        var create = Ui.Primary("Создать группу", () => { if (name.Text.Trim().Length > 0) { S.AddGroup(name.Text); Go("/"); } }, IconKind.Plus);
        create.IsEnabled = _name.Trim().Length > 0;
        name.TextChanged += (_, _) => { _name = name.Text; create.IsEnabled = _name.Trim().Length > 0; };
        name.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter && name.Text.Trim().Length > 0) { S.AddGroup(name.Text); Go("/"); } };
        var pick = new FilePick(Ui.Content(IconKind.Upload, _busy ? "Читаю файл…" : "Загрузить прежний файл Excel"), "Книга Excel (*.xls;*.xlsx)|*.xls;*.xlsx", OnFile) { IsEnabled = !_busy };
        var demo = Ui.Ghost("Открыть пример", () => { S.Merge(Demo.Build()); Go("/"); });

        var form = new Border
        {
            Background = Theme.FormBg, CornerRadius = new CornerRadius(2), Padding = new Thickness(20), MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left,
            Child = Ui.VStack(12, Ui.Field("Название группы", name), Ui.Row(8, create, pick, demo), _error is null ? null : Ui.Status(_error, false)),
        };

        return Page(
            PageHeader("С чего начать", "«Росток» заменяет таблицу Excel: вы отмечаете баллы, а суммы, уровни, сводная динамика, графики и черновик заключения считаются сами. Данные хранятся в вашем рабочем пространстве."),
            steps,
            form,
            Ui.Faint("Прежний файл — это книга «Динамика речевого развития» с листами «Звук…», «Л.Г.С.…», «Фонетика…». Из неё переносятся дети и все проставленные баллы; шкала переводится автоматически.")
                .With(t => { t.MaxWidth = 620; t.HorizontalAlignment = HorizontalAlignment.Left; t.Margin = new Thickness(0, 18, 0, 0); }));
    }

    private void OnFile(string path)
    {
        _busy = true;
        _error = null;
        Refresh();
        try
        {
            var res = Excel.ImportLegacyWorkbook(path);
            S.Merge(res.Part);
            _busy = false;
            Go("/");
            return;
        }
        catch (Exception e) { _error = e.Message is { Length: > 0 } m ? m : "Не удалось прочитать файл."; }
        _busy = false;
        Refresh();
    }
}
