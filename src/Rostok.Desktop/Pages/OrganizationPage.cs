using System.Windows;
using System.Windows.Controls;
using Rostok.Controls;
using Rostok.Core;
using Rostok.Core.Storage;
using Rostok.Desktop.Services;

namespace Rostok.Desktop.Pages;

// «Организация» — экран руководителя (старшего специалиста): рабочие пространства всех сотрудников базы.
// Пространство сотрудника открывается только для просмотра, без его пароля; открытие и сброс пароля пишутся в журнал.
public sealed class OrganizationPage(Route route) : PageBase(route)
{
    private string? _resetFor;
    private (bool Ok, string Text)? _status;

    private sealed record Row(WorkspaceInfo Info, OrgSummaryRow Summary, bool Own);

    private static List<Row> Load()
    {
        var own = AppHost.OwnStore!;
        var db = AppHost.Db!;
        return new Workspaces(db).List().Select(w =>
        {
            var isOwn = w.Id == own.WorkspaceId;
            // чужие пространства читаются без словарей и без записи — сводка ничего не меняет
            var data = isOwn ? own.Data : Store.Open(db, w.Id, w.Name, readOnly: true, applyDicts: false).Data;
            return new Row(w, OrgSummary.Build(data), isOwn);
        }).ToList();
    }

    private static string Pct(double? v) => v is null ? "—" : $"{Math.Round(v.Value * 100)}%";

    protected override UIElement Build()
    {
        var rows = Load();
        var employees = rows.Where(r => !r.Own).ToList();
        var export = Ui.Ghost("Выгрузить сводку в Excel", () => Export(rows), IconKind.Download);
        var page = Page(PageHeader("Организация",
            "Рабочие пространства всех сотрудников в этой базе. Пространство открывается только для просмотра — без пароля сотрудника; изменить в нём ничего нельзя, а открытие записывается в журнал сотрудника.",
            export));
        if (_status is { } st) page.Children.Add(Ui.Status(st.Text, st.Ok).Margin(0, 0, 0, 12));

        var measured = rows.Sum(r => r.Summary.Measured);
        var improved = rows.Sum(r => r.Summary.Improved);
        page.Children.Add(Ui.Card(Stats(
            Stat("Сотрудников", StatValue(employees.Count.ToString()), $"без руководителей · пространств всего: {rows.Count}"),
            Stat("Детей", StatValue(rows.Sum(r => r.Summary.Children).ToString()), $"групп и списков: {rows.Sum(r => r.Summary.Groups)}"),
            Stat("Положительная динамика", StatValue(measured > 0 ? $"{Math.Round(improved * 100.0 / measured)}%" : "—"),
                measured > 0 ? $"{improved} из {measured} детей, обследованных на двух срезах" : "нужны два среза с данными")), top: false, padTop: 4));

        var t = new Tbl(Col.Star(1.4, min: 200), Col.Auto(Align.Right), Col.Auto(Align.Right), Col.Auto(Align.Right), Col.Auto(Align.Right),
            Col.Auto(Align.Right), Col.Auto(Align.Right), Col.Auto(), Col.Auto(Align.Right)) { MinWidth = 1000 };
        t.Header("Сотрудник", "Групп", "Детей", "Срезов", "Заполнено", "Речь: балл", "Динамика", "Последний вход", "");
        foreach (var r in rows)
        {
            var s = r.Summary;
            var name = new StackPanel();
            name.Children.Add(Ui.Text(r.Info.Name, null, Theme.Ink, 13, FontWeights.SemiBold, wrap: true));
            var tags = new List<string>();
            if (r.Own) tags.Add("вы");
            if (r.Info.IsSupervisor) tags.Add("руководитель");
            if (tags.Count > 0) name.Children.Add(Ui.Muted(string.Join(" · ", tags), 11.5));
            var filled = s.LastPeriod is null ? (object)Ui.Faint("нет данных") : Ui.VStack(0, Ui.Num(Pct(s.LastFilled), Theme.Ink2), Ui.Muted(s.LastPeriod.Short, 11));
            var speech = s.MeanStart is null
                ? (object)Ui.Faint("—")
                : Ui.HStack(6, Ui.Num($"{Calc.Fmt(s.MeanStart)} → {Calc.Fmt(s.MeanEnd)}").With(x => x.VerticalAlignment = VerticalAlignment.Center), Delta.Of(s.MeanStart, s.MeanEnd));
            var dyn = s.ImprovedShare is null ? (object)Ui.Faint("—") : Ui.VStack(0, Ui.Num(Pct(s.ImprovedShare), Theme.Ink2), Ui.Muted($"{s.Improved} из {s.Measured}", 11));
            var last = r.Info.LastOpenedAt is { } lo ? Ui.Num(lo.ToString("dd.MM.yyyy HH:mm"), Theme.Ink3, 12) : (object)Ui.Faint("не входил");
            UIElement actions;
            if (r.Own) actions = Ui.Faint("ваше пространство");
            else
            {
                var info = r.Info;
                actions = Ui.HStack(4,
                    Ui.TextAction("открыть для просмотра", () => AppHost.ViewAs(info)),
                    Ui.Muted(" · ", 12).With(x => x.VerticalAlignment = VerticalAlignment.Center),
                    Ui.TextAction("сбросить пароль", () => { _resetFor = info.Id; _status = null; Refresh(); }));
            }
            t.Row(name, Ui.Num(s.Groups.ToString()), Ui.Num(s.Children.ToString()), Ui.Num(s.PeriodsWithData.ToString()), filled, speech, dyn, last, actions);
        }
        page.Children.Add(Ui.Card(Ui.VStack(0,
            Ui.BlockHead(Ui.H2("Сотрудники"), Ui.Faint("речь: средний балл на начало → конец по детям, обследованным на обоих срезах; меньше — ближе к норме")),
            Tbl.Scroll(t))));

        var target = rows.FirstOrDefault(r => r.Info.Id == _resetFor && !r.Own);
        if (target is not null) page.Children.Add(ResetForm(target.Info));

        page.Children.Add(Ui.HelpNote(Ui.Faint("Роль руководителя назначается в «Администрирование → Организация» паролем администратора базы. Данные в базе не зашифрованы: ограничьте доступ к папке с базой сотрудниками учреждения.")));
        return page;
    }

    private FrameworkElement ResetForm(WorkspaceInfo w)
    {
        var next = new PasswordBox();
        var rep = new PasswordBox();
        Ui.SetPlaceholder(next, $"не короче {Workspaces.MinPassword} символов");
        Ui.SetPlaceholder(rep, "ещё раз");
        var grid = new Grid { MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(Ui.Field("Новый пароль", next));
        var rf = Ui.Field("Повторите", rep);
        Grid.SetColumn(rf, 2);
        grid.Children.Add(rf);
        void Reset()
        {
            var err = Workspaces.ValidatePassword(next.Password, rep.Password);
            if (err is not null) { _status = (false, err); Refresh(); return; }
            var own = AppHost.OwnStore!;
            new Workspaces(AppHost.Db!).ResetPassword(w.Id, next.Password, own.WorkspaceId, own.WorkspaceName);
            _status = (true, $"Пароль пространства «{w.Name}» изменён. Сообщите новый пароль сотруднику; в его журнале появится запись о сбросе.");
            _resetFor = null;
            Refresh();
        }
        return ChildrenPage.FormBox(Ui.VStack(12,
            Ui.H2($"Сброс пароля: {w.Name}"),
            Ui.Faint("Сотрудник забыл пароль? Задайте новый — старый пароль не нужен. Данные пространства не меняются."),
            grid,
            Ui.Row(8, Ui.Primary("Сбросить пароль", Reset, IconKind.Lock), Ui.Ghost("Отмена", () => { _resetFor = null; Refresh(); }))));
    }

    private static void Export(List<Row> rows)
    {
        var path = Dialogs.SaveFile($"Росток — сводка по организации {DateTime.Today:yyyy-MM-dd}.xlsx", "Книга Excel (*.xlsx)|*.xlsx");
        if (path is null) return;
        var data = new List<object?[]>
        {
            new object?[] { $"Сводка по организации · {DateTime.Today:dd.MM.yyyy}" },
            new object?[] { "Сотрудник", "Роль", "Групп", "Детей", "Срезов с данными", "Последний срез", "Заполнено, %", "Начало", "Конец", "Речь: балл на начало", "Речь: балл на конец", "Обследовано на двух срезах", "Положительная динамика", "Положительная динамика, %", "Последний вход" },
        };
        foreach (var r in rows)
        {
            var s = r.Summary;
            data.Add([
                r.Info.Name, r.Info.IsSupervisor ? "руководитель" : "сотрудник", s.Groups, s.Children, s.PeriodsWithData, s.LastPeriod?.Label,
                s.LastFilled is null ? null : Math.Round(s.LastFilled.Value * 100), s.Start?.Label, s.End?.Label,
                s.MeanStart is null ? null : Math.Round(s.MeanStart.Value, 2), s.MeanEnd is null ? null : Math.Round(s.MeanEnd.Value, 2),
                s.Measured, s.Improved, s.ImprovedShare is null ? null : Math.Round(s.ImprovedShare.Value * 100),
                r.Info.LastOpenedAt?.ToString("dd.MM.yyyy HH:mm"),
            ]);
        }
        try { Excel.ExportSheets(path, [new Sheet("Сводка", data, [32, 14, 8, 8, 10, 16, 12, 16, 16, 12, 12, 12, 12, 12, 18])]); Dialogs.OpenFolderOf(path); }
        catch (Exception e) { Dialogs.Error("Не удалось сохранить файл", e.Message); }
    }
}
