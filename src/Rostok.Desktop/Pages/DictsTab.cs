using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Rostok.Controls;
using Rostok.Core;
using Rostok.Core.Storage;
using Rostok.Desktop.Services;

namespace Rostok.Desktop.Pages;

// «Администрирование → Словари»: все словарные значения; правка сохраняется сразу и видна на всех экранах.
public sealed class DictsTab(Action refresh)
{
    private bool _ask;
    private static Store S => AppHost.Store!;
    private static bool ViewOnly => S.ReadOnly;

    public UIElement Build()
    {
        var dict = Dicts.ById.GetValueOrDefault(S.Ui.DictId ?? "") ?? Dicts.All[0];
        var overrides = S.Data.Dicts;
        var changed = Dicts.ChangedCount(dict.Id, overrides);

        var nav = new StackPanel();
        foreach (var g in Dicts.All.Select(d => d.Group).Distinct())
        {
            nav.Children.Add(Ui.Caps(g).Margin(0, 12, 0, 6));
            foreach (var d in Dicts.All.Where(d => d.Group == g))
            {
                var n = Dicts.ChangedCount(d.Id, overrides);
                var row = new DockPanel();
                if (n > 0)
                {
                    var tag = Ui.Num(d.IsList ? "изм." : $"изм. {n}", Theme.Accent, 11, FontWeights.SemiBold).Tip("Есть изменения").With(t => t.VerticalAlignment = VerticalAlignment.Center);
                    DockPanel.SetDock(tag, Dock.Right);
                    row.Children.Add(tag);
                }
                row.Children.Add(new TextBlock { Text = d.Title, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                var b = new Button { Style = Ui.Style("TreeRow"), Content = row };
                Ui.SetIsActive(b, d.Id == dict.Id);
                var id = d.Id;
                b.Click += (_, _) => { S.SetUi(u => u.DictId = id); _ask = false; refresh(); };
                nav.Children.Add(b);
            }
        }

        var main = new StackPanel();
        UIElement? reset = changed > 0 && !ViewOnly
            ? _ask
                ? Ui.HStack(12, Ui.TextAction("да, вернуть все значения по умолчанию", () => { S.ResetDict(dict.Id); _ask = false; refresh(); }, danger: true), Ui.TextAction("отмена", () => { _ask = false; refresh(); }))
                : Ui.TextAction("вернуть значения по умолчанию", () => { _ask = true; refresh(); })
            : null;
        main.Children.Add(Ui.BlockHead(Ui.H2(dict.Title), reset));
        if (dict.Hint is not null) main.Children.Add(Ui.Faint(dict.Hint).With(t => { t.MaxWidth = 720; t.HorizontalAlignment = HorizontalAlignment.Left; t.Margin = new Thickness(0, 0, 0, 12); }));
        main.Children.Add(dict.IsList ? ListEditor(dict, overrides[dict.Id] as JsonArray) : TableEditor(dict, overrides[dict.Id] as JsonObject ?? []));
        main.Children.Add(Ui.HelpNote(Ui.Faint(ViewOnly
            ? "Словари сотрудника — только просмотр. Изменённые значения отмечены слева, под ними показано значение по умолчанию."
            : "Правки сохраняются сразу и видны на всех экранах, в отчётах и выгрузках. Очистите поле — вернётся значение по умолчанию (оно показано серым). Словари входят в резервную копию на вкладке «Данные».")).Margin(0, 20, 0, 0));

        var g2 = new Grid();
        g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(288) });
        g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        g2.ColumnDefinitions.Add(new ColumnDefinition());
        var side = Sticky.Attach(new Border { Child = nav });
        g2.Children.Add(side);
        Grid.SetColumn(main, 2);
        g2.Children.Add(main);
        return g2;
    }

    private UIElement TableEditor(Dict dict, JsonObject overrides)
    {
        var box = new StackPanel();
        string? sectionId = null;
        if (dict.FilterBySection)
        {
            sectionId = M.AllSections.Any(s => s.Id == S.Ui.DictSection) ? S.Ui.DictSection : M.AllSections[0].Id;
            var dd = new Dropdown(M.AllSections.Select(s => new DropdownOption(s.Id, $"{(s.BlockId == "neuro" ? "Нейро · " : "")}{s.Title}")), sectionId,
                v => { S.SetUi(u => u.DictSection = (string?)v); refresh(); }, DropdownVariant.Light, label: "Раздел");
            box.Children.Add(Ui.Field("Раздел", dd).With(f => { f.MaxWidth = 460; f.HorizontalAlignment = HorizontalAlignment.Left; f.Margin = new Thickness(0, 0, 0, 14); }));
        }
        var rows = dict.Rows().Where(r => sectionId is null || r.Section == sectionId).ToList();
        var cols = new List<Col> { Col.Auto() };
        cols.AddRange(dict.Fields.Select(f => f.Narrow ? Col.Px(180) : Col.Star(1, min: 140)));
        var t = new Tbl([.. cols]) { CellPadding = new Thickness(0, 8, 8, 8), HeadPadding = new Thickness(0, 8, 8, 8) };
        t.Header([new Cell("Код"), .. dict.Fields.Select(f => (object?)f.Label)]);
        foreach (var row in rows)
        {
            var rowOv = overrides[row.Key] as JsonObject;
            var isChanged = rowOv is { Count: > 0 };
            var lead = Ui.HStack(4, row.Level is not null ? new Level(row.Level) : null, Ui.Num(row.Lead, Theme.Ink3).With(x => x.VerticalAlignment = VerticalAlignment.Center));
            var leadCell = new Border
            {
                BorderBrush = isChanged ? Theme.Accent : Brushes.Transparent, BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(10, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Stretch, Child = lead,
            };
            var cells = new List<object?> { leadCell };
            foreach (var f in dict.Fields)
            {
                var def = Dicts.DefaultValue(dict.Id, row.Key, f.Key);
                var ov = rowOv?[f.Key] is JsonValue jv && jv.TryGetValue<string>(out var sv) ? sv : null;
                var input = Ui.Input(ov ?? def, def);
                input.FontSize = 13;
                input.Padding = new Thickness(7, 6, 7, 6);
                if (f.Max is { } max) input.MaxLength = max;
                else { input.TextWrapping = TextWrapping.Wrap; input.AcceptsReturn = f.Multiline; }
                if (!f.Multiline) input.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) e.Handled = true; };
                var key = row.Key;
                var save = Ui.Debounce<string>(v => S.SetDictValue(dict.Id, key, f.Key, f.Multiline ? v : v.Replace("\r", "").Replace("\n", " "), def));
                if (ViewOnly) input.IsReadOnly = true;
                else input.TextChanged += (_, _) => save(input.Text);
                var cell = new StackPanel();
                cell.Children.Add(input);
                if (ov is not null && ov != def) cell.Children.Add(Ui.Text($"по умолчанию: {(def.Length > 0 ? def : "—")}", null, Theme.Faint, 11.5, wrap: true).Margin(0, 3, 0, 0));
                cells.Add(cell);
            }
            t.Row([.. cells]);
        }
        // без горизонтальной прокрутки: длинные значения переносятся по ширине колонки
        box.Children.Add(t);
        return box;
    }

    private UIElement ListEditor(Dict dict, JsonArray? value)
    {
        var list = value is not null ? value.Select(x => x?.GetValue<string>() ?? "").ToList() : Dicts.DefaultList(dict.Id);
        void Save(List<string> next, bool rerender)
        {
            S.SetDictList(dict.Id, next);
            if (rerender) refresh();
        }
        var box = new StackPanel { MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
        var saveText = Ui.Debounce<List<string>>(l => Save(l, false));
        for (var i = 0; i < list.Count; i++)
        {
            var idx = i;
            var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            foreach (var _ in Enumerable.Range(0, 3)) { g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) }); }
            g.Children.Add(Ui.Num((i + 1).ToString(), Theme.Ink3, 12).With(t => { t.HorizontalAlignment = HorizontalAlignment.Right; t.VerticalAlignment = VerticalAlignment.Center; }));
            var input = Ui.Input(list[i]);
            Grid.SetColumn(input, 2);
            g.Children.Add(input);
            if (ViewOnly)
            {
                input.IsReadOnly = true;
                box.Children.Add(g);
                continue;
            }
            input.TextChanged += (_, _) => { list[idx] = input.Text; saveText([.. list]); };
            Button Small(string content, string tip, Action a, bool enabled = true, bool danger = false)
            {
                var b = Ui.Button("IconButton", content, a, tip);
                b.Width = b.Height = 34;
                b.IsEnabled = enabled;
                Ui.SetIsDanger(b, danger);
                return b;
            }
            var up = Small("↑", "Выше", () => { (list[idx - 1], list[idx]) = (list[idx], list[idx - 1]); Save(list, true); }, i > 0);
            var down = Small("↓", "Ниже", () => { (list[idx + 1], list[idx]) = (list[idx], list[idx + 1]); Save(list, true); }, i < list.Count - 1);
            var del = Ui.IconButton(IconKind.Trash, "Удалить", () => { list.RemoveAt(idx); Save(list, true); }, danger: true);
            del.Width = del.Height = 34;
            Grid.SetColumn(up, 4); Grid.SetColumn(down, 6); Grid.SetColumn(del, 8);
            g.Children.Add(up); g.Children.Add(down); g.Children.Add(del);
            box.Children.Add(g);
        }
        if (!ViewOnly) box.Children.Add(Ui.Ghost("Добавить значение", () => { list.Add(""); Save(list, true); }, IconKind.Plus).With(b => b.HorizontalAlignment = HorizontalAlignment.Left));
        return box;
    }
}
