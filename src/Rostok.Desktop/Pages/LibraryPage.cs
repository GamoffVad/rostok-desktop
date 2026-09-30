using System.Windows;
using System.Windows.Controls;
using Rostok.Controls;
using Rostok.Core;
using Rostok.Desktop.Services;

namespace Rostok.Desktop.Pages;

// «Упражнения»: библиотека — к каждой диагностической пробе свой блок упражнений.
public sealed class LibraryPage(Route route) : PageBase(route)
{
    private (bool Ok, string Text)? _status;
    private string? _ask;
    private readonly Action<(string Item, string Text)> _save = Ui.Debounce<(string Item, string Text)>(x => S.SetExercise(x.Item, x.Text));

    // Разделы без балльной оценки в программу не попадают — упражнения к ним не нужны.
    private static bool WithExercises(Section s) => s.Kind is not (Kinds.YesNo or Kinds.Side);

    protected override UIElement Build()
    {
        var library = D.LibraryOf();
        var sections = M.AllSections.Where(WithExercises).ToList();
        var section = M.SectionById.GetValueOrDefault(S.Ui.LibrarySection ?? "") is { } cur && WithExercises(cur) ? cur : sections[0];
        int FilledIn(Section s) => s.Items.Count(i => CorrectionProgram.ExerciseLines(library.GetValueOrDefault(i.Id)).Count > 0);
        var totalFilled = sections.Sum(FilledIn);
        var totalItems = sections.Sum(s => s.Items.Count());

        var page = Page(PageHeader("Упражнения",
            ViewOnly
                ? "Библиотека владельца пространства: к каждой диагностической пробе — свой блок упражнений. Только просмотр; шаблон Excel можно выгрузить."
                : "Библиотека: к каждой диагностической пробе — свой блок упражнений. Из неё собирается программа коррекции ребёнка по выявленным дефицитам. Одна строка — одно упражнение; изменения сохраняются сразу.",
            Ui.Ghost("Шаблон Excel", ExportTemplate, IconKind.Download),
            ViewOnly ? null : new FilePick(Ui.Content(IconKind.Upload, "Загрузить из Excel"), "Книга Excel (*.xls;*.xlsx)|*.xls;*.xlsx", OnFile)));
        if (_status is { } st) page.Children.Add(Ui.Status(st.Text, st.Ok).Margin(0, 0, 0, 10));
        if (!ViewOnly) page.Children.Add(Ui.HelpNote(Ui.Rich(
            Ui.Run("Сейчас заполнено "), Ui.Run(totalFilled.ToString(), Theme.Ink, FontWeights.Bold, mono: true), Ui.Run(" из "), Ui.Run(totalItems.ToString(), mono: true), Ui.Run(" проб. "),
            Ui.Run(D.Library is null ? "Это образцы: замените их своими или очистите библиотеку. " : ""),
            Ui.Run("Удобно заполнять в Excel: выгрузите шаблон, впишите упражнения в последнюю колонку (новая строка в ячейке — Alt+Enter) и загрузите файл обратно.")).With(t => t.FontSize = 13)).Margin(0, 0, 0, 14));

        // ── список разделов ──
        var nav = new StackPanel();
        foreach (var b in M.Blocks)
        {
            nav.Children.Add(Ui.Caps(b.Title).Margin(0, 12, 0, 6));
            foreach (var s in b.Sections.Where(WithExercises))
            {
                var n = FilledIn(s);
                var all = s.Items.Count();
                var row = new DockPanel();
                var count = Ui.Num($"{n}/{all}", n == all ? Theme.Ok : Theme.Ink3, 11).With(t => t.VerticalAlignment = VerticalAlignment.Center);
                DockPanel.SetDock(count, Dock.Right);
                row.Children.Add(count);
                row.Children.Add(new TextBlock { Text = s.Short, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                var btn = new Button { Style = Ui.Style("TreeRow"), Content = row };
                Ui.SetIsActive(btn, s.Id == section.Id);
                var sid = s.Id;
                btn.Click += (_, _) => { S.SetUi(u => u.LibrarySection = sid); Refresh(); };
                nav.Children.Add(btn);
            }
        }
        UIElement confirm = _ask switch
        {
            "clear" => Ui.Row(12, Ui.TextAction("да, очистить все упражнения", () => { S.SetLibrary(new Dictionary<string, string>()); _ask = null; Refresh(); }, danger: true), Ui.TextAction("отмена", () => { _ask = null; Refresh(); })),
            "reset" => Ui.Row(12, Ui.TextAction("да, заменить образцами", () => { S.SetLibrary(new Dictionary<string, string>(M.ExampleLibrary)); _ask = null; Refresh(); }, danger: true), Ui.TextAction("отмена", () => { _ask = null; Refresh(); })),
            _ => Ui.Row(12, Ui.TextAction("вернуть образцы", () => { _ask = "reset"; Refresh(); }), Ui.TextAction("очистить всё", () => { _ask = "clear"; Refresh(); }, danger: true)),
        };
        if (!ViewOnly) nav.Children.Add(new Border { Margin = new Thickness(0, 16, 0, 0), Child = confirm });

        // ── пробы раздела ──
        var main = new StackPanel();
        main.Children.Add(Ui.BlockHead(Ui.H2(section.Title), Ui.Num($"{FilledIn(section)}/{section.Items.Count()}", Theme.Faint, 13)));
        foreach (var gr in section.Groups)
        {
            var g = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
            g.Children.Add(new Border { BorderBrush = Theme.Accent, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 6), Child = Ui.Caps(gr.Title) });
            foreach (var item in gr.Items)
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                var label = section.Kind == Kinds.Sound
                    ? Ui.Rich(Ui.Run("Звук "), Ui.Run($"[{item.Label}]", weight: FontWeights.Bold, mono: true)).With(t => { t.FontSize = 13.5; t.Foreground = Theme.Ink; })
                    : Ui.Text(item.Label, null, Theme.Ink, 13.5, wrap: true);
                label.Margin = new Thickness(0, 8, 0, 0);
                row.Children.Add(label);
                var box = Ui.Input(library.GetValueOrDefault(item.Id) ?? "", "Упражнения для этой пробы — каждое с новой строки", null, multiline: true, rows: 2);
                box.MinHeight = 56;
                var id = item.Id;
                if (ViewOnly) box.IsReadOnly = true;
                else box.TextChanged += (_, _) => _save((id, box.Text));
                Grid.SetColumn(box, 2);
                row.Children.Add(box);
                g.Children.Add(new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 10, 0, 10), Child = row });
            }
            main.Children.Add(g);
        }

        page.Children.Add(Split(Sticky.Attach(new Border { Child = nav }), main, new GridLength(288), new GridLength(1, GridUnitType.Star)));
        return page;
    }

    private void ExportTemplate()
    {
        var path = Dialogs.SaveFile("Росток — библиотека упражнений.xlsx", "Книга Excel (*.xlsx)|*.xlsx");
        if (path is null) return;
        try { Excel.ExportLibraryTemplate(path, D.LibraryOf()); Dialogs.OpenFolderOf(path); }
        catch (Exception e) { Dialogs.Error("Не удалось сохранить файл", e.Message); }
    }

    private void OnFile(string path)
    {
        try
        {
            var patch = Excel.ImportLibraryTemplate(path, M.ItemById.Keys.ToHashSet());
            var next = new Dictionary<string, string>(D.LibraryOf());
            foreach (var (k, v) in patch) next[k] = v;
            S.SetLibrary(next);
            _status = (true, $"Загружено упражнений для проб: {patch.Count}. Пустые ячейки шаблона ничего не стёрли.");
        }
        catch (Exception e) { _status = (false, e.Message is { Length: > 0 } m ? m : "Не удалось прочитать файл."); }
        Refresh();
    }
}
