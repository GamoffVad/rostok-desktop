using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Rostok.Controls;
using Rostok.Core.Storage;
using Rostok.Desktop.Services;

namespace Rostok.Desktop.Pages;

// «Администрирование → Организация»: пароль администратора базы и роль «Руководитель».
// Роль меняется только с паролем администратора — сотрудник не может выдать её себе сам.
public sealed class OrgTab(Action refresh)
{
    private string? _admin;       // пароль администратора, введённый в этом открытии вкладки
    private (bool Ok, string Text)? _status;
    private static Store S => AppHost.Store!;

    public UIElement Build()
    {
        var ws = new Workspaces(AppHost.Db!);
        var page = new StackPanel { MaxWidth = 880, HorizontalAlignment = HorizontalAlignment.Left };
        if (_status is { } st) page.Children.Add(Ui.Status(st.Text, st.Ok).Margin(0, 0, 0, 10));
        var hasAdmin = ws.HasAdminPassword();
        page.Children.Add(Ui.Card(AdminPasswordBlock(ws, hasAdmin), top: false, padTop: 0));
        page.Children.Add(Ui.Card(RolesBlock(ws, hasAdmin)));
        page.Children.Add(Ui.HelpNote(Ui.Faint("Данные в базе не зашифрованы: разграничение действует в программе. Доступ к самой папке с базой в сети ограничьте сотрудниками учреждения (права Windows на общую папку).")));
        return page;
    }

    private UIElement AdminPasswordBlock(Workspaces ws, bool hasAdmin)
    {
        var box = new StackPanel();
        box.Children.Add(Ui.BlockHead(Ui.H2("Пароль администратора базы")));
        var cur = new PasswordBox();
        var next = new PasswordBox();
        var rep = new PasswordBox();
        Ui.SetPlaceholder(cur, "текущий пароль администратора");
        Ui.SetPlaceholder(next, $"не короче {Workspaces.MinPassword} символов");
        Ui.SetPlaceholder(rep, "ещё раз");
        if (!hasAdmin)
            box.Children.Add(Ui.HelpNote(Ui.Faint("Пароль администратора ещё не задан. Задайте его сейчас, пока этого не сделал кто-то другой: только с ним можно назначать руководителей. Сообщите пароль тому, кто отвечает за программу в учреждении."), amber: true));
        else
            box.Children.Add(Ui.Faint("Пароль задан. Сменить его можно, зная текущий.").Margin(0, 0, 0, 12));
        var fields = Ui.VStack(12,
            hasAdmin ? Ui.Field("Текущий пароль администратора", cur) : null,
            Ui.Field(hasAdmin ? "Новый пароль" : "Пароль администратора", next),
            Ui.Field("Повторите", rep));
        fields.MaxWidth = 420;
        fields.HorizontalAlignment = HorizontalAlignment.Left;
        box.Children.Add(fields);
        var set = Ui.Ghost(hasAdmin ? "Сменить пароль администратора" : "Задать пароль администратора", () =>
        {
            var err = Workspaces.ValidatePassword(next.Password, rep.Password);
            if (err is not null) { _status = (false, err); refresh(); return; }
            try
            {
                ws.SetAdminPassword(hasAdmin ? cur.Password : null, next.Password);
                _admin = next.Password;
                _status = (true, hasAdmin ? "Пароль администратора изменён." : "Пароль администратора задан. Теперь можно назначить руководителя.");
            }
            catch (Exception e) { _status = (false, e.Message); }
            refresh();
        }, IconKind.Lock);
        set.HorizontalAlignment = HorizontalAlignment.Left;
        set.Margin = new Thickness(0, 12, 0, 0);
        box.Children.Add(set);
        return box;
    }

    private UIElement RolesBlock(Workspaces ws, bool hasAdmin)
    {
        var box = new StackPanel();
        box.Children.Add(Ui.BlockHead(Ui.H2("Руководители")));
        box.Children.Add(Ui.Faint("Руководитель (старший специалист) входит в своё пространство своим паролем и видит пространства всех сотрудников — только для просмотра: сводка, дети, баллы, динамика, отчёты. Может сбросить сотруднику забытый пароль. Каждое открытие записывается в журнал сотрудника.")
            .With(t => { t.MaxWidth = 760; t.HorizontalAlignment = HorizontalAlignment.Left; t.Margin = new Thickness(0, 0, 0, 14); }));
        if (!hasAdmin) box.Children.Add(Ui.Faint("Сначала задайте пароль администратора."));
        else if (_admin is null)
        {
            var unlock = new PasswordBox { Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
            Ui.SetPlaceholder(unlock, "пароль администратора базы");
            void Open()
            {
                if (ws.VerifyAdmin(unlock.Password)) { _admin = unlock.Password; _status = null; }
                else _status = (false, "Пароль администратора указан неверно.");
                refresh();
            }
            unlock.KeyDown += (_, e) => { if (e.Key == Key.Enter) Open(); };
            var row = Ui.Row(8, Ui.Field("Чтобы изменить роли, введите пароль администратора", unlock), Ui.Ghost("Открыть", Open, IconKind.Lock));
            row.AlignBottom = true;
            box.Children.Add(row);
        }

        var t = new Tbl(Col.Star(1.4), Col.Auto(Align.Right), Col.Auto(), Col.Auto()) { Margin = new Thickness(0, 14, 0, 0) };
        t.Header("Рабочее пространство", "Детей", "Последний вход", "Руководитель");
        foreach (var w in ws.List())
        {
            var check = new Checkbox { IsChecked = w.IsSupervisor, IsEnabled = _admin is not null, Content = w.IsSupervisor ? "да" : "нет" };
            var info = w;
            check.Click += (_, _) =>
            {
                try
                {
                    var on = check.IsChecked == true;
                    ws.SetRole(info.Id, on ? Roles.Supervisor : Roles.Employee, _admin!);
                    _status = (true, on
                        ? $"«{info.Name}» — руководитель. Роль начнёт действовать при следующем входе в это пространство."
                        : $"«{info.Name}» больше не руководитель (со следующего входа).");
                }
                catch (Exception e) { _status = (false, e.Message); }
                refresh();
            };
            var name = Ui.VStack(0, Ui.Text(w.Name, null, Theme.Ink, 13, FontWeights.SemiBold), w.Id == S.WorkspaceId ? Ui.Muted("ваше пространство", 11.5) : null);
            t.Row(name, Ui.Num(w.Children.ToString()), w.LastOpenedAt is { } lo ? Ui.Num(lo.ToString("dd.MM.yyyy"), Theme.Ink3, 12) : Ui.Faint("—"), check);
        }
        box.Children.Add(t);
        return box;
    }
}
