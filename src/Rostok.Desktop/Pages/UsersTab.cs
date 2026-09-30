using System.Windows;
using System.Windows.Controls;
using Rostok.Controls;
using Rostok.Core.Storage;
using Rostok.Desktop.Services;

namespace Rostok.Desktop.Pages;

// Поля формы в ряд равной ширины с промежутком 12 px.
internal static class FormRow
{
    public static Grid Of(params UIElement[] cells)
    {
        var g = new Grid();
        for (var i = 0; i < cells.Length; i++)
        {
            if (i > 0) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(cells[i], i * 2);
            if (cells[i] is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Top;
            g.Children.Add(cells[i]);
        }
        return g;
    }

    public static PasswordBox Password(string placeholder)
    {
        var p = new PasswordBox();
        Ui.SetPlaceholder(p, placeholder);
        return p;
    }
}

// «Администрирование → Учётная запись»: логин, роль и смена своего пароля для входа.
public sealed class AccountTab(Action refresh)
{
    private (bool Ok, string Text)? _status;

    public UIElement Build()
    {
        AppHost.RefreshUser();
        var me = AppHost.User!;
        var users = new Users(AppHost.Db!);
        var page = new StackPanel { MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };

        var info = new Grid();
        info.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        info.ColumnDefinitions.Add(new ColumnDefinition());
        var rows = new (string Label, string Value)[] { ("Пользователь", me.DisplayName), ("Логин", me.Login), ("Роль", me.RoleTitle) };
        for (var i = 0; i < rows.Length; i++)
        {
            info.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = Ui.Caps(rows[i].Label).Margin(0, 6, 0, 6);
            var v = Ui.Text(rows[i].Value, null, Theme.Ink, 14, i == 0 ? FontWeights.SemiBold : FontWeights.Normal).Margin(0, 4, 0, 4);
            Grid.SetRow(l, i);
            Grid.SetRow(v, i);
            Grid.SetColumn(v, 1);
            info.Children.Add(l);
            info.Children.Add(v);
        }
        page.Children.Add(Ui.Card(Ui.VStack(8,
            Ui.BlockHead(Ui.H2("Учётная запись"), null, 0),
            info,
            Ui.Faint(me.IsAdmin
                ? "Вы видите рабочие пространства всех пользователей: свои — для работы, чужие — только для просмотра, с записью в журнал владельца."
                : "Вы видите только свои рабочие пространства. Логин и роль меняет администратор.").With(t => t.TextWrapping = TextWrapping.Wrap)), top: false, padTop: 0));

        if (users.HasDefaultPassword(me))
            page.Children.Add(Ui.HelpNote(Ui.Faint($"Сейчас у вас стандартный пароль «{UserRoles.DefaultPassword}» — смените его, чтобы никто другой не вошёл как главный администратор."), amber: true).Margin(0, 0, 0, 12));

        var cur = FormRow.Password("текущий пароль");
        var next = FormRow.Password($"не короче {Passwords.MinLength} символов");
        var rep = FormRow.Password("ещё раз");
        var change = Ui.Ghost("Сменить пароль", () =>
        {
            var err = Passwords.Validate(next.Password, rep.Password);
            if (err is not null) { _status = (false, err); refresh(); return; }
            try { users.ChangePassword(me.Id, cur.Password, next.Password); _status = (true, "Пароль изменён."); }
            catch (Exception e) { _status = (false, e.Message); }
            refresh();
        }, IconKind.Lock);
        page.Children.Add(Ui.Card(Ui.VStack(12,
            Ui.BlockHead(Ui.H2("Пароль для входа"), null, 0),
            Ui.Field("Текущий пароль", cur),
            FormRow.Of(Ui.Field("Новый пароль", next), Ui.Field("Повторите новый пароль", rep)),
            change.With(b => b.HorizontalAlignment = HorizontalAlignment.Left),
            _status is { } st ? Ui.Status(st.Text, st.Ok) : null)));
        return page;
    }
}

// «Администрирование → Пользователи» (только администраторам): пользователи, их роли и пароли, владельцы рабочих пространств.
public sealed class UsersTab(Action refresh)
{
    private (bool Ok, string Text)? _status;
    private string? _editFor, _resetFor, _deleteFor;
    private string _newRole = UserRoles.User;

    private static readonly DropdownOption[] RoleOptions =
    [
        new(UserRoles.User, "Пользователь"),
        new(UserRoles.Admin, "Администратор"),
    ];

    private void Done(bool ok, string text)
    {
        _status = (ok, text);
        refresh();
    }

    public UIElement Build()
    {
        if (!AppHost.IsAdmin) return Ui.Empty("Пользователями управляют администраторы.");
        var db = AppHost.Db!;
        var users = new Users(db);
        var me = AppHost.User!;
        var list = users.List();
        var page = new StackPanel();
        if (_status is { } st) page.Children.Add(Ui.Status(st.Text, st.Ok).Margin(0, 0, 0, 12));
        if (list.FirstOrDefault(u => u.IsMainAdmin) is { } main && users.HasDefaultPassword(main))
            page.Children.Add(Ui.HelpNote(Ui.Faint($"У главного администратора ({main.Login}) стандартный пароль «{UserRoles.DefaultPassword}». Войдите под ним и смените пароль на вкладке «Учётная запись»."), amber: true).Margin(0, 0, 0, 14));

        page.Children.Add(Ui.Card(NewUserForm(users), top: false, padTop: 0));

        // ── пользователи ──
        var t = new Tbl(Col.Star(1.2, min: 220), Col.Px(220), Col.Auto(Align.Right), Col.Auto(), Col.Auto(Align.Right)) { MinWidth = 860 };
        t.Header("Пользователь", "Роль", "Пространств", "Последний вход", "");
        foreach (var u in list)
        {
            var self = u.Id == me.Id;
            var name = Ui.VStack(0,
                Ui.Text(u.DisplayName, null, Theme.Ink, 13, FontWeights.SemiBold, wrap: true),
                Ui.Muted($"логин {u.Login}{(self ? " · это вы" : "")}", 11.5));
            // роль главного администратора и свою роль изменить нельзя
            object role = u.IsMainAdmin || self
                ? Ui.Text(u.RoleTitle, null, Theme.Ink2, 13)
                : new Dropdown(RoleOptions, u.Role, v =>
                {
                    try { users.SetRole(u.Id, (string)v!); Done(true, $"{u.DisplayName}: роль — {UserRoles.Title((string)v!)}."); }
                    catch (Exception e) { Done(false, e.Message); }
                }, DropdownVariant.Light, label: $"Роль: {u.DisplayName}");
            var last = u.LastLoginAt is { } lo ? Ui.Num(lo.ToString("dd.MM.yyyy HH:mm"), Theme.Ink3, 12) : (object)Ui.Faint("не входил");
            var id = u.Id;
            // сведения главного администратора меняет только он сам
            var acts = new List<UIElement>();
            if (!u.IsMainAdmin || me.IsMainAdmin) acts.Add(Ui.TextAction("изменить", () => { _editFor = id; _resetFor = _deleteFor = null; _status = null; refresh(); }));
            // пароль главного администратора меняет только он сам; свой пароль — на вкладке «Учётная запись»
            if (!u.IsMainAdmin && !self)
            {
                acts.Add(Ui.TextAction("сбросить пароль", () => { _resetFor = id; _editFor = _deleteFor = null; _status = null; refresh(); }));
                acts.Add(Ui.TextAction("удалить", () => { _deleteFor = id; _editFor = _resetFor = null; _status = null; refresh(); }, danger: true));
            }
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            for (var i = 0; i < acts.Count; i++)
            {
                if (i > 0) row.Children.Add(Ui.Muted(" · ", 12).With(x => x.VerticalAlignment = VerticalAlignment.Center));
                row.Children.Add(acts[i]);
            }
            t.Row(name, role, Ui.Num(u.Workspaces.ToString()), last, row);
        }
        var usersCard = Ui.VStack(0, Ui.BlockHead(Ui.H2("Пользователи"), Ui.Num($"всего {list.Count}", Theme.Faint, 13)), Tbl.Scroll(t));
        if (list.FirstOrDefault(u => u.Id == _editFor) is { } edit) usersCard.Children.Add(EditForm(users, edit));
        if (list.FirstOrDefault(u => u.Id == _resetFor) is { } reset) usersCard.Children.Add(ResetForm(users, reset));
        if (list.FirstOrDefault(u => u.Id == _deleteFor) is { } del) usersCard.Children.Add(DeleteForm(users, del));
        page.Children.Add(Ui.Card(usersCard));

        page.Children.Add(Ui.Card(WorkspacesCard(list)));
        page.Children.Add(Ui.HelpNote(Ui.Faint("Администратор видит рабочие пространства всех пользователей и открывает чужие только для просмотра — каждое открытие записывается в журнал владельца. Пользователь видит только свои пространства. Данные в базе не зашифрованы: доступ к папке с базой ограничьте сотрудниками учреждения.")));
        return page;
    }

    private UIElement NewUserForm(Users users)
    {
        var login = Ui.Input("", "например, ivanova");
        var name = Ui.Input("", "Иванова Мария Викторовна");
        var pwd = FormRow.Password($"не короче {Passwords.MinLength} символов");
        var rep = FormRow.Password("ещё раз");
        var role = new Dropdown(RoleOptions, _newRole, v => _newRole = (string)v!, DropdownVariant.Light, label: "Роль нового пользователя");
        void Add()
        {
            var err = users.ValidateLogin(login.Text) ?? Passwords.Validate(pwd.Password, rep.Password);
            if (err is not null) { Done(false, err); return; }
            try
            {
                users.Create(login.Text, name.Text, pwd.Password, _newRole);
                Done(true, $"Пользователь «{login.Text.Trim()}» добавлен ({UserRoles.Title(_newRole)}). Сообщите ему логин и пароль.");
            }
            catch (Exception e) { Done(false, e.Message); }
        }
        return Ui.VStack(12,
            Ui.BlockHead(Ui.H2("Новый пользователь"), null, 0),
            FormRow.Of(Ui.Field("Логин", login), Ui.Field("Фамилия, имя, должность", name)),
            FormRow.Of(Ui.Field("Пароль", pwd), Ui.Field("Повторите пароль", rep), Ui.Field("Роль", role)),
            Ui.Primary("Добавить пользователя", Add, IconKind.Plus).With(b => b.HorizontalAlignment = HorizontalAlignment.Left));
    }

    private FrameworkElement EditForm(Users users, UserInfo u)
    {
        var login = Ui.Input(u.Login);
        var name = Ui.Input(u.Name);
        void Save()
        {
            try
            {
                users.Update(u.Id, login.Text, name.Text);
                _editFor = null;
                AppHost.RefreshUser();
                Done(true, "Сведения о пользователе сохранены.");
            }
            catch (Exception e) { Done(false, e.Message); }
        }
        return Form($"Изменить: {u.DisplayName}", null,
            FormRow.Of(Ui.Field("Логин", login), Ui.Field("Фамилия, имя, должность", name)),
            Ui.Row(8, Ui.Primary("Сохранить", Save), Ui.Ghost("Отмена", () => { _editFor = null; refresh(); })));
    }

    private FrameworkElement ResetForm(Users users, UserInfo u)
    {
        var next = FormRow.Password($"не короче {Passwords.MinLength} символов");
        var rep = FormRow.Password("ещё раз");
        void Reset()
        {
            var err = Passwords.Validate(next.Password, rep.Password);
            if (err is not null) { Done(false, err); return; }
            users.ResetPassword(u.Id, next.Password);
            _resetFor = null;
            Done(true, $"Пароль пользователя «{u.Login}» изменён. Сообщите ему новый пароль.");
        }
        return Form($"Сброс пароля: {u.DisplayName}", "Пользователь забыл пароль? Задайте новый — старый не нужен. Рабочие пространства и данные не меняются.",
            FormRow.Of(Ui.Field("Новый пароль", next), Ui.Field("Повторите", rep)),
            Ui.Row(8, Ui.Primary("Сбросить пароль", Reset, IconKind.Lock), Ui.Ghost("Отмена", () => { _resetFor = null; refresh(); })));
    }

    private FrameworkElement DeleteForm(Users users, UserInfo u)
    {
        void Delete()
        {
            try
            {
                users.Delete(u.Id);
                _deleteFor = null;
                Done(true, $"Пользователь «{u.Login}» удалён." + (u.Workspaces > 0 ? " Его рабочие пространства переданы главному администратору." : ""));
            }
            catch (Exception e) { Done(false, e.Message); }
        }
        return Form($"Удалить пользователя: {u.DisplayName}",
            u.Workspaces > 0
                ? $"Рабочие пространства пользователя ({u.Workspaces}) со всеми данными перейдут главному администратору — их можно передать другому пользователю ниже."
                : "У пользователя нет рабочих пространств.",
            null,
            Ui.Row(8, Ui.Danger("Удалить пользователя", Delete, IconKind.Trash), Ui.Ghost("Отмена", () => { _deleteFor = null; refresh(); })));
    }

    private static FrameworkElement Form(string title, string? hint, UIElement? fields, UIElement buttons)
    {
        var box = ChildrenPage.FormBox(Ui.VStack(12,
            Ui.H2(title),
            hint is null ? null : Ui.Faint(hint).With(t => t.TextWrapping = TextWrapping.Wrap),
            fields, buttons));
        box.Margin = new Thickness(0, 16, 0, 0);
        box.MaxWidth = 720;
        box.HorizontalAlignment = HorizontalAlignment.Left;
        return box;
    }

    // Владельцы рабочих пространств: пространство можно передать другому пользователю.
    private UIElement WorkspacesCard(List<UserInfo> users)
    {
        var ws = new Workspaces(AppHost.Db!);
        var list = ws.List();
        var owners = users.Select(u => new DropdownOption(u.Id, $"{u.DisplayName} ({u.Login})")).ToList();
        var t = new Tbl(Col.Star(1.2, min: 220), Col.Px(300), Col.Auto(Align.Right), Col.Auto()) { MinWidth = 820 };
        t.Header("Рабочее пространство", "Владелец", "Детей", "Последнее открытие");
        foreach (var w in list)
        {
            var info = w;
            var owner = new Dropdown(owners, w.OwnerId, v =>
            {
                var to = (string)v!;
                if (to == info.OwnerId) return;
                ws.SetOwner(info.Id, to);
                var name = users.First(u => u.Id == to).DisplayName;
                // своё открытое пространство отдали другому — править его больше нельзя: к выбору пространства
                if (info.Id == AppHost.HomeStore?.WorkspaceId && to != AppHost.User!.Id) { AppHost.SwitchWorkspace(); return; }
                Done(true, $"Пространство «{info.Name}» передано: {name}.");
            }, DropdownVariant.Light, label: $"Владелец: {w.Name}");
            var last = w.LastOpenedAt is { } lo ? Ui.Num(lo.ToString("dd.MM.yyyy HH:mm"), Theme.Ink3, 12) : (object)Ui.Faint("не открывалось");
            t.Row(Ui.Text(w.Name, null, Theme.Ink, 13, FontWeights.SemiBold, wrap: true), owner, Ui.Num(w.Children.ToString()), last);
        }
        if (list.Count == 0) t.Row(new Cell(Ui.Faint("пространств пока нет"), 4));
        return Ui.VStack(0,
            Ui.BlockHead(Ui.H2("Рабочие пространства"), Ui.Faint("владелец работает в пространстве; остальные администраторы — только смотрят")),
            Tbl.Scroll(t));
    }
}
