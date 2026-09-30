using System.Windows;
using System.Windows.Controls;
using Rostok.Controls;
using Rostok.Core;
using Rostok.Core.Storage;
using Rostok.Desktop.Services;

namespace Rostok.Desktop.Pages;

// «Организация» — экран администратора: рабочие пространства всех пользователей базы.
// Чужое пространство открывается только для просмотра; открытие записывается в журнал владельца.
public sealed class OrganizationPage(Route route) : PageBase(route)
{
    private sealed record Row(WorkspaceInfo Info, OrgSummaryRow Summary, bool Own);

    private static List<Row> Load()
    {
        var home = AppHost.HomeStore;
        var me = AppHost.User!;
        var db = AppHost.Db!;
        return new Workspaces(db).List().Select(w =>
        {
            // пространства читаются без словарей и без записи — сводка ничего не меняет
            var data = w.Id == home?.WorkspaceId && !home.ReadOnly ? home.Data : Store.Open(db, w.Id, w.Name, readOnly: true, applyDicts: false).Data;
            return new Row(w, OrgSummary.Build(data), w.OwnerId == me.Id);
        }).ToList();
    }

    private static string Pct(double? v) => v is null ? "—" : $"{Math.Round(v.Value * 100)}%";

    protected override UIElement Build()
    {
        var rows = Load();
        var owners = rows.Select(r => r.Info.OwnerId).Distinct().Count();
        var export = Ui.Ghost("Выгрузить сводку в Excel", () => Export(rows), IconKind.Download);
        var page = Page(PageHeader("Организация",
            "Рабочие пространства всех пользователей базы. Чужое пространство открывается только для просмотра: изменить в нём ничего нельзя, а открытие записывается в журнал владельца.",
            export));

        var measured = rows.Sum(r => r.Summary.Measured);
        var improved = rows.Sum(r => r.Summary.Improved);
        page.Children.Add(Ui.Card(Stats(
            Stat("Пространств", StatValue(rows.Count.ToString()), $"у пользователей: {owners}"),
            Stat("Детей", StatValue(rows.Sum(r => r.Summary.Children).ToString()), $"групп и списков: {rows.Sum(r => r.Summary.Groups)}"),
            Stat("Положительная динамика", StatValue(measured > 0 ? $"{Math.Round(improved * 100.0 / measured)}%" : "—"),
                measured > 0 ? $"{improved} из {measured} детей, обследованных на двух срезах" : "нужны два среза с данными")), top: false, padTop: 4));

        var t = new Tbl(Col.Star(1.4, min: 200), Col.Auto(Align.Right), Col.Auto(Align.Right), Col.Auto(Align.Right), Col.Auto(Align.Right),
            Col.Auto(Align.Right), Col.Auto(Align.Right), Col.Auto(), Col.Auto(Align.Right)) { MinWidth = 1000 };
        t.Header("Пространство · владелец", "Групп", "Детей", "Срезов", "Заполнено", "Речь: балл", "Динамика", "Открывалось", "");
        foreach (var r in rows)
        {
            var s = r.Summary;
            var name = new StackPanel();
            name.Children.Add(Ui.Text(r.Info.Name, null, Theme.Ink, 13, FontWeights.SemiBold, wrap: true));
            name.Children.Add(Ui.Muted(r.Own ? $"{r.Info.OwnerName} · ваше" : r.Info.OwnerName, 11.5));
            var filled = s.LastPeriod is null ? (object)Ui.Faint("нет данных") : Ui.VStack(0, Ui.Num(Pct(s.LastFilled), Theme.Ink2), Ui.Muted(s.LastPeriod.Short, 11));
            var speech = s.MeanStart is null
                ? (object)Ui.Faint("—")
                : Ui.HStack(6, Ui.Num($"{Calc.Fmt(s.MeanStart)} → {Calc.Fmt(s.MeanEnd)}").With(x => x.VerticalAlignment = VerticalAlignment.Center), Delta.Of(s.MeanStart, s.MeanEnd));
            var dyn = s.ImprovedShare is null ? (object)Ui.Faint("—") : Ui.VStack(0, Ui.Num(Pct(s.ImprovedShare), Theme.Ink2), Ui.Muted($"{s.Improved} из {s.Measured}", 11));
            var last = r.Info.LastOpenedAt is { } lo ? Ui.Num(lo.ToString("dd.MM.yyyy HH:mm"), Theme.Ink3, 12) : (object)Ui.Faint("не открывалось");
            var info = r.Info;
            UIElement actions = r.Info.Id == S.WorkspaceId
                ? Ui.Faint("открыто сейчас")
                : Ui.TextAction(r.Own ? "открыть" : "открыть для просмотра", () => AppHost.ViewAs(info));
            t.Row(name, Ui.Num(s.Groups.ToString()), Ui.Num(s.Children.ToString()), Ui.Num(s.PeriodsWithData.ToString()), filled, speech, dyn, last, actions);
        }
        page.Children.Add(Ui.Card(Ui.VStack(0,
            Ui.BlockHead(Ui.H2("Рабочие пространства"), Ui.Faint("речь: средний балл на начало → конец по детям, обследованным на обоих срезах; меньше — ближе к норме")),
            Tbl.Scroll(t))));

        page.Children.Add(Ui.HelpNote(Ui.Faint("Роли пользователей и владельцы пространств — в «Администрирование → Пользователи». Данные в базе не зашифрованы: ограничьте доступ к папке с базой сотрудниками учреждения.")));
        return page;
    }

    private static void Export(List<Row> rows)
    {
        var path = Dialogs.SaveFile($"Росток — сводка по организации {DateTime.Today:yyyy-MM-dd}.xlsx", "Книга Excel (*.xlsx)|*.xlsx");
        if (path is null) return;
        var data = new List<object?[]>
        {
            new object?[] { $"Сводка по организации · {DateTime.Today:dd.MM.yyyy}" },
            new object?[] { "Пространство", "Владелец", "Групп", "Детей", "Срезов с данными", "Последний срез", "Заполнено, %", "Начало", "Конец", "Речь: балл на начало", "Речь: балл на конец", "Обследовано на двух срезах", "Положительная динамика", "Положительная динамика, %", "Последнее открытие" },
        };
        foreach (var r in rows)
        {
            var s = r.Summary;
            data.Add([
                r.Info.Name, r.Info.OwnerName, s.Groups, s.Children, s.PeriodsWithData, s.LastPeriod?.Label,
                s.LastFilled is null ? null : Math.Round(s.LastFilled.Value * 100), s.Start?.Label, s.End?.Label,
                s.MeanStart is null ? null : Math.Round(s.MeanStart.Value, 2), s.MeanEnd is null ? null : Math.Round(s.MeanEnd.Value, 2),
                s.Measured, s.Improved, s.ImprovedShare is null ? null : Math.Round(s.ImprovedShare.Value * 100),
                r.Info.LastOpenedAt?.ToString("dd.MM.yyyy HH:mm"),
            ]);
        }
        try { Excel.ExportSheets(path, [new Sheet("Сводка", data, [32, 26, 8, 8, 10, 16, 12, 16, 16, 12, 12, 12, 12, 12, 18])]); Dialogs.OpenFolderOf(path); }
        catch (Exception e) { Dialogs.Error("Не удалось сохранить файл", e.Message); }
    }
}
