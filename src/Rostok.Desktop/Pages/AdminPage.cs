using System.Windows;
using System.Windows.Controls;
using Rostok.Controls;
using Rostok.Core;
using Rostok.Core.Storage;
using Rostok.Desktop.Components;
using Rostok.Desktop.Services;

namespace Rostok.Desktop.Pages;

// «Администрирование»: данные, словари, компоненты, рабочее пространство, учётная запись, пользователи, подключение к базе.
public sealed class AdminPage(Route route) : PageBase(route)
{
    private static readonly (string Id, string Label, string Subtitle)[] Tabs =
    [
        ("data", "Данные", "Резервная копия, загрузка прежнего файла Excel, учебные годы. Данные хранятся в базе, в вашем рабочем пространстве, — раз в неделю выгружайте резервную копию."),
        ("dicts", "Словари", "Все словарные значения приложения: названия уровней и этапов, разделов и проб, итогов, направлений работы, подсказки для контактов родителей. Правки действуют только в вашем рабочем пространстве."),
        ("ui", "Компоненты", "Библиотека компонентов «Росток»: из неё собраны все экраны. Каждый элемент — вживую и во всех состояниях."),
        ("workspace", "Рабочее пространство", "Название открытого рабочего пространства, его удаление и журнал просмотров. Все группы, дети, баллы, упражнения, словари и настройки хранятся только в нём."),
        ("account", "Учётная запись", "Ваш логин, роль и пароль для входа в программу."),
        ("users", "Пользователи", "Пользователи программы и их роли: администратор видит рабочие пространства всех пользователей, пользователь — только свои. Здесь же — владельцы пространств."),
        ("connection", "Подключение", "Файл базы данных, с которым работает программа на этом компьютере: локально или в общей папке локальной сети."),
    ];

    // В режиме просмотра чужого пространства — словари (для чтения), витрина компонентов, учётная запись и пользователи.
    private static readonly string[] ViewTabs = ["dicts", "ui", "account", "users"];

    private DataTab? _data;
    private DictsTab? _dicts;
    private ComponentsTab? _kit;
    private WorkspaceTab? _ws;
    private AccountTab? _account;
    private UsersTab? _users;

    protected override UIElement Build()
    {
        // «Пользователи» — только администраторам
        var tabs = Tabs.Where(t => (!ViewOnly || ViewTabs.Contains(t.Id)) && (t.Id != "users" || AppHost.IsAdmin)).ToArray();
        var tab = tabs.FirstOrDefault(t => t.Id == Route.Part(1));
        if (tab.Id is null) tab = tabs[0];
        UIElement body = tab.Id switch
        {
            "dicts" => (_dicts ??= new DictsTab(Refresh)).Build(),
            "account" => (_account ??= new AccountTab(Refresh)).Build(),
            "users" => (_users ??= new UsersTab(Refresh)).Build(),
            "ui" => (_kit ??= new ComponentsTab()).Build(),
            "workspace" => (_ws ??= new WorkspaceTab(Refresh)).Build(),
            "connection" => ConnectionTab(),
            _ => (_data ??= new DataTab(Refresh)).Build(),
        };
        return Page(
            PageHeader("Администрирование", tab.Subtitle),
            new TabBar(tabs.Select(t => new TabSpec(t.Id, t.Label)), tab.Id, k => Go($"/admin/{k}")),
            new Border { Margin = new Thickness(0, 20, 0, 0), Child = body });
    }

    private static UIElement ConnectionTab()
    {
        var editor = new ConnectionEditor(() =>
        {
            // другая база — свои пользователи и пространства: возвращаемся ко входу
            AppHost.SignOut();
        });
        editor.MaxWidth = 720;
        editor.HorizontalAlignment = HorizontalAlignment.Left;
        return Ui.Card(Ui.VStack(0, Ui.BlockHead(Ui.H2("Подключение к базе данных")),
            Ui.Faint("После подключения к другой базе программа вернётся ко входу: в новой базе свои пользователи и рабочие пространства.").Margin(0, 0, 0, 16).With(t => t.MaxWidth = 720), editor), top: false);
    }
}

// ── Данные ─────────────────────────────────────────────
public sealed class DataTab(Action refresh)
{
    private (bool Ok, string Text)? _status;
    private bool _busy, _askClear;
    private string? _askYear;
    private static Store S => AppHost.Store!;
    private static WorkspaceData D => S.Data;

    public UIElement Build()
    {
        var years = D.Years;
        string NextYear()
        {
            var y = years.Count > 0 ? int.Parse(years[^1][..4]) + 1 : DateTime.Today.Year;
            return $"{y}–{y + 1}";
        }
        int FilledIn(string year)
        {
            var ids = D.Periods.Where(p => p.Year == year).Select(p => p.Id).ToList();
            return D.Children.Count(c => ids.Any(id => D.HasScores(c.Id, id)));
        }

        var page = new StackPanel();
        if (_status is { } st) page.Children.Add(Ui.Status(st.Text, st.Ok).Margin(0, 0, 0, 10));

        page.Children.Add(Ui.Card(Ui.VStack(0,
            Ui.BlockHead(Ui.H2("Резервная копия"), Ui.Num($"групп {D.Groups.Count} · детей {D.Children.Count}", Theme.Faint, 13)),
            Ui.Row(8,
                Ui.Primary("Выгрузить копию", Export, IconKind.Download),
                new FilePick(Ui.Content(IconKind.Upload, "Восстановить из копии"), "Резервная копия «Ростка» (*.json)|*.json", Restore)),
            Ui.Faint("Восстановление заменяет все данные текущего рабочего пространства содержимым файла. Подходит и копия из веб-версии «Ростка».").Margin(0, 10, 0, 0).With(t => t.MaxWidth = 620)), top: false, padTop: 0));

        page.Children.Add(Ui.Card(Ui.VStack(0,
            Ui.BlockHead(Ui.H2("Прежний файл Excel")),
            Ui.Body("Книга «Динамика речевого развития» (листы «Звук…», «Л.Г.С.…», «Фонетика…») загружается как новая группа: переносятся фамилии и все проставленные баллы. В прежнем файле 3 означало норму — при загрузке шкала переводится в принятую здесь (0 — норма).").With(t => { t.MaxWidth = 640; t.HorizontalAlignment = HorizontalAlignment.Left; t.Margin = new Thickness(0, 0, 0, 12); }),
            Ui.Row(8,
                new FilePick(Ui.Content(IconKind.Upload, _busy ? "Читаю файл…" : "Загрузить файл Excel"), "Книга Excel (*.xls;*.xlsx)|*.xls;*.xlsx", ImportXls) { IsEnabled = !_busy },
                Ui.Ghost("Добавить группу-пример", () => { S.Merge(Demo.Build()); _status = (true, "Добавлена группа-пример с вымышленными детьми."); refresh(); })))));

        var t = new Tbl(Col.Auto(), Col.Star(), Col.Auto(Align.Right), Col.Auto(Align.Right)) { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        t.Header("Учебный год", "Срезы", "Детей с данными", "");
        foreach (var y in years)
        {
            var year = y;
            UIElement act = _askYear == y
                ? Ui.HStack(4, Ui.TextAction("удалить год и его баллы", () => { S.RemoveYear(year); _askYear = null; refresh(); }, danger: true), Ui.Muted(" · ", 12).With(x => x.VerticalAlignment = VerticalAlignment.Center), Ui.TextAction("отмена", () => { _askYear = null; refresh(); }))
                : Ui.TextAction("удалить", () => { _askYear = year; refresh(); });
            t.Row(Ui.Num(y, Theme.Ink, 13, FontWeights.SemiBold), "НГ — начало года · КГ — конец года", Ui.Num(FilledIn(y).ToString()), act);
        }
        if (years.Count == 0) t.Row(new Cell(Ui.Faint("пока нет"), 4));
        var add = Ui.Ghost($"Добавить {NextYear()}", () => { S.AddYear(NextYear()); refresh(); }, IconKind.Plus);
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Margin = new Thickness(0, 14, 0, 0);
        page.Children.Add(Ui.Card(Ui.VStack(0, Ui.BlockHead(Ui.H2("Учебные годы и срезы")), Tbl.Scroll(t), add)));

        UIElement clear = _askClear
            ? Ui.Row(8,
                Ui.Danger("Да, удалить все группы, детей и баллы", () => { S.Clear(); _askClear = false; _status = (true, "Все данные рабочего пространства удалены."); AppHost.Main?.Reload(); }, IconKind.Trash),
                Ui.Ghost("Отмена", () => { _askClear = false; refresh(); }))
            : Ui.Row(8, Ui.Ghost("Удалить все данные", () => { _askClear = true; refresh(); }, IconKind.Trash));
        page.Children.Add(Ui.Card(Ui.VStack(0, Ui.BlockHead(Ui.H2("Очистка")), clear, Ui.Faint("Удаляются только данные вашего рабочего пространства; пространства других сотрудников не затрагиваются.").Margin(0, 10, 0, 0))));
        return page;
    }

    private void Export()
    {
        var path = Dialogs.SaveFile($"Росток — копия {DateTime.Today:yyyy-MM-dd}.json", "Резервная копия «Ростка» (*.json)|*.json");
        if (path is null) return;
        try
        {
            File.WriteAllText(path, Backup.Export(D));
            _status = (true, $"Копия сохранена: {path}");
        }
        catch (Exception e) { _status = (false, $"Не удалось сохранить копию: {e.Message}"); }
        refresh();
    }

    private void Restore(string path)
    {
        try
        {
            var data = Backup.Import(File.ReadAllText(path));
            S.ReplaceAll(data);
            _status = S.SaveError is null ? (true, $"Копия восстановлена: групп — {data.Groups.Count}, детей — {data.Children.Count}.") : (false, S.SaveError);
        }
        catch (FormatException) { _status = (false, "Это не резервная копия «Ростка»: выберите файл .json, выгруженный из «Ростка»."); }
        catch (Exception e) { _status = (false, e.Message); }
        refresh();
    }

    private void ImportXls(string path)
    {
        _busy = true;
        refresh();
        try
        {
            var res = Excel.ImportLegacyWorkbook(path);
            S.Merge(res.Part);
            _status = (true, $"Загружено: «{res.Group}», детей — {res.Children}, отметок — {res.Cells}.");
        }
        catch (Exception e) { _status = (false, e.Message is { Length: > 0 } m ? m : "Не удалось прочитать файл Excel."); }
        _busy = false;
        refresh();
    }
}

// ── Рабочее пространство ──────────────────────────────
public sealed class WorkspaceTab(Action refresh)
{
    private (bool Ok, string Text)? _nameStatus, _delStatus;
    private bool _askDelete;
    private static Store S => AppHost.Store!;

    public UIElement Build()
    {
        var ws = new Workspaces(AppHost.Db!);
        var page = new StackPanel { MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };

        var name = Ui.Input(S.WorkspaceName);
        var saveName = Ui.Ghost("Сохранить название", () =>
        {
            try
            {
                ws.Rename(S.WorkspaceId, name.Text);
                S.WorkspaceName = name.Text.Trim();
                if (AppHost.Main is { } w) w.Title = $"Росток — {S.WorkspaceName}";
                _nameStatus = (true, "Название сохранено.");
                AppHost.Main?.Reload();
                return;
            }
            catch (Exception e) { _nameStatus = (false, e.Message); }
            refresh();
        });
        page.Children.Add(Ui.Card(Ui.VStack(12,
            Ui.BlockHead(Ui.H2("Название"), null, 0),
            Ui.Field("Название рабочего пространства", name),
            saveName.With(b => b.HorizontalAlignment = HorizontalAlignment.Left),
            _nameStatus is { } ns ? Ui.Status(ns.Text, ns.Ok) : null), top: false, padTop: 0));

        var del = new StackPanel();
        del.Children.Add(Ui.BlockHead(Ui.H2("Удаление рабочего пространства")));
        del.Children.Add(Ui.Faint("Удаляется само пространство и все его данные: группы, дети, баллы, программы, упражнения, словари и настройки. Отменить нельзя — сначала выгрузите резервную копию на вкладке «Данные».").With(t => t.Margin = new Thickness(0, 0, 0, 12)));
        if (_askDelete)
        {
            del.Children.Add(Ui.Row(8,
                Ui.Danger("Да, удалить пространство и все данные", () =>
                {
                    try { ws.Delete(S.WorkspaceId); AppHost.Settings.LastWorkspaceId = null; AppHost.SwitchWorkspace(); }
                    catch (Exception e) { _delStatus = (false, e.Message); refresh(); }
                }, IconKind.Trash),
                Ui.Ghost("Отмена", () => { _askDelete = false; _delStatus = null; refresh(); })));
            if (_delStatus is { } ds) del.Children.Add(Ui.Status(ds.Text, ds.Ok).Margin(0, 10, 0, 0));
        }
        else del.Children.Add(Ui.Ghost("Удалить рабочее пространство", () => { _askDelete = true; refresh(); }, IconKind.Trash).With(b => b.HorizontalAlignment = HorizontalAlignment.Left));
        page.Children.Add(Ui.Card(del));

        // журнал: когда администраторы открывали это пространство
        var log = ws.AccessLog(S.WorkspaceId);
        var journal = new StackPanel();
        journal.Children.Add(Ui.BlockHead(Ui.H2("Журнал просмотров администраторами")));
        if (log.Count == 0) journal.Children.Add(Ui.Faint("Администраторы ещё не открывали это пространство."));
        else
        {
            var t = new Tbl(Col.Auto(), Col.Star(), Col.Star());
            t.Header("Когда", "Кто", "Что");
            foreach (var e in log) t.Row(Ui.Num(e.At.ToString("dd.MM.yyyy HH:mm"), Theme.Ink3, 12), e.ViewerName, e.ActionText);
            journal.Children.Add(t);
            journal.Children.Add(Ui.Faint($"Показаны последние {log.Count} записей.").Margin(0, 8, 0, 0));
        }
        page.Children.Add(Ui.Card(journal));
        return page;
    }
}
