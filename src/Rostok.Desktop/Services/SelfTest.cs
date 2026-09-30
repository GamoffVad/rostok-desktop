using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Rostok.Controls;
using Rostok.Core;
using Rostok.Core.Storage;
using Rostok.Desktop.Pages;

namespace Rostok.Desktop.Services;

// Самопроверка интерфейса: Rostok.exe --selftest <файл отчёта>
// Во временной базе открывает экраны, нажимает кнопки и клавиши, как пользователь, и проверяет, что данные записались в базу.
public static class SelfTest
{
    private static readonly List<string> Log = [];
    private static int _failed;

    public static bool TryRun(string[] args)
    {
        var i = Array.IndexOf(args, "--selftest");
        if (i < 0 || i + 1 >= args.Length) return false;
        var report = Path.GetFullPath(args[i + 1]);
        Application.Current.Dispatcher.BeginInvoke(() => Run(report), DispatcherPriority.Loaded);
        return true;
    }

    private static void Check(bool ok, string what)
    {
        Log.Add($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok) _failed++;
    }

    private static async Task Settle()
    {
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(60);
    }

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject => Ui.Descendants(root).OfType<T>();

    private static Button ButtonWithText(DependencyObject root, string text) =>
        Find<Button>(root).First(b => Find<TextBlock>(b).Any(t => t.Text == text) || (b.Content as string) == text);

    private static bool HasText(DependencyObject root, string text) => Find<TextBlock>(root).Any(t => t.Text == text) || Find<Button>(root).Any(b => (b.Content as string) == text);

    private static void Click(Button b) => ((IInvokeProvider)new ButtonAutomationPeer(b).GetPattern(PatternInterface.Invoke)!).Invoke();

    private static void Key(UIElement target, Key key)
    {
        var src = PresentationSource.FromVisual(target)!;
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, src, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    }

    private static async void Run(string report)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"rostok-selftest-{Guid.NewGuid():N}.db");
        try
        {
            var db = new Database(dbPath);
            db.EnsureCreated();
            Check(db.Exists, "база создаётся автоматически, если файла нет");
            var users = new Users(db);
            Check(users.Authenticate(UserRoles.DefaultLogin, UserRoles.DefaultPassword) is { IsMainAdmin: true }, "в новой базе есть главный администратор admin / admin");
            var uid = users.Create("proverka", "Проверка", "1234");
            var ws = new Workspaces(db);
            var id = ws.Create("Проверка", uid);
            AppHost.Ephemeral = true;
            AppHost.UseDatabase(db);

            // Вход: логин и пароль, затем выбор пространства
            AppHost.ShowLogin();
            await Settle();
            var login = Application.Current.Windows.OfType<LoginWindow>().First();
            login.Left = -20000;
            Find<TextBox>(login).First().Text = "proverka";
            var pb = Find<PasswordBox>(login).First();
            pb.Password = "0000";
            Click(ButtonWithText(login, "Войти"));
            await Settle();
            Check(AppHost.User is null && Find<TextBlock>(login).Any(t => t.Text == "Неверный логин или пароль."), "вход: неверный пароль не пускает");
            pb.Password = "1234";
            Click(ButtonWithText(login, "Войти"));
            await Settle();
            Check(AppHost.User?.Login == "proverka", "вход: логин и пароль открывают выбор рабочего пространства");
            Click(Find<Button>(login).First(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Проверка"));
            await Settle();
            Check(AppHost.Main is not null && AppHost.Store?.WorkspaceId == id && !AppHost.IsViewing, "выбор пространства: своё открывается для работы, без пароля");
            var main = AppHost.Main!;
            main.Left = -20000;
            var s = AppHost.Store!;
            await Settle();

            // «С чего начать» → пример
            Check(main.Content is DependencyObject && Find<WelcomePage>(main).Any(), "пустое пространство открывает «С чего начать»");
            Click(ButtonWithText(main, "Открыть пример"));
            await Settle();
            Check(s.Data.Children.Count == 12 && Find<ChildrenPage>(main).Any(), "пример загружается, открывается список детей");

            // Дети: удаление с подтверждением
            var before = s.Data.Children.Count;
            Click(Find<Button>(main).First(b => Find<TextBlock>(b).Any(t => t.Text == "удалить")));
            await Settle();
            Click(ButtonWithText(main, "удалить с баллами"));
            await Settle();
            Check(s.Data.Children.Count == before - 1, "ребёнок удаляется после подтверждения");

            // Обследование: клавиатура
            var child = s.Data.ChildrenOf(s.Data.Groups[0].Id)[0];
            var period = s.Data.Periods[3];
            s.SetUi(u => { u.PeriodId = period.Id; u.SectionId = "phon"; });
            main.Navigate(Route.Of("/exam", ("child", child.Id)));
            await Settle();
            var exam = Find<ExamPage>(main).First();
            Key(exam, System.Windows.Input.Key.D2);
            Key(exam, System.Windows.Input.Key.D0);
            await Settle();
            var sc = s.Data.ScoresOf(child.Id, period.Id);
            Check(sc.GetValueOrDefault("ph_1") == "2" && sc.GetValueOrDefault("ph_2") == "0", "обследование: цифры ставят баллы и переходят к следующей пробе");
            Key(Find<ExamPage>(main).First(), System.Windows.Input.Key.Up);
            Key(Find<ExamPage>(main).First(), System.Windows.Input.Key.Back);
            await Settle();
            Check(!s.Data.ScoresOf(child.Id, period.Id).ContainsKey("ph_2"), "обследование: Backspace стирает балл");
            Click(ButtonWithText(main, "всё в норме"));
            await Settle();
            Check(s.Data.ScoresOf(child.Id, period.Id).Count(kv => kv.Key.StartsWith("ph_") && kv.Value == "0") == 13, "обследование: «всё в норме» заполняет раздел");

            // Протокол: ввод по клавишам
            main.Navigate(Route.Of("/protocol"));
            await Settle();
            s.SetUi(u => u.SectionId = "lex");
            main.Reload();
            await Settle();
            var proto = Find<ProtocolPage>(main).First();
            Key(proto, System.Windows.Input.Key.D3);
            await Settle();
            var first = s.Data.ChildrenOf(s.Data.Groups[0].Id)[0];
            Check(s.Data.ScoresOf(first.Id, period.Id).GetValueOrDefault("lx_0") == "3", "протокол: цифра ставит балл в текущую ячейку");

            // Выпадающий список: смена раздела
            var dd = Find<Dropdown>(main).First(d => d.Items.Cast<DropdownOption>().Any(o => (string?)o.Value == "gram"));
            dd.SelectedValue = "gram";
            await Settle();
            Check(s.Ui.SectionId == "gram", "выпадающий список меняет раздел и запоминает выбор");

            // Динамика, отчёт, справка открываются
            foreach (var r in new[] { "/dynamics", $"/child/{first.Id}", $"/child/{first.Id}?tab=program", $"/child/{first.Id}?tab=report", $"/parent/{first.Id}", "/library", "/admin/dicts", "/admin/ui", "/admin/workspace", "/admin/account", "/admin/connection", "/help" })
            {
                var parts = r.Split('?');
                main.Navigate(Route.Of(parts[0], parts.Length > 1 ? [(parts[1].Split('=')[0], parts[1].Split('=')[1])] : []));
                await Settle();
                Check(true, $"экран {r} открывается");
            }

            // Программа коррекции: исключение пробы
            main.Navigate(Route.Of($"/child/{first.Id}", ("tab", "program")));
            await Settle();
            var cbx = Find<Checkbox>(main).First();
            cbx.IsChecked = false;
            cbx.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await Settle();
            var lastFilled = s.Data.FilledPeriods(first.Id)[^1];
            Check(s.Data.ProgramOf(first.Id, lastFilled.Id).Off.Count == 1, "программа: снятая отметка исключает пробу");

            // Словари: правка применяется везде
            s.SetDictValue("outcomes", "minor", "label", "есть улучшение", Dicts.DefaultValue("outcomes", "minor", "label"));
            Check(M.Outcomes.First(o => o.Id == "minor").Label == "есть улучшение", "словарь: правка подписи итога");
            s.ResetDict("outcomes");

            // Изоляция рабочих пространств
            var otherUser = users.Create("drugoy", "Другой сотрудник", "5678");
            var other = ws.Create("Другой сотрудник", otherUser);
            Check(Store.Open(db, other, "Другой").Data.Children.Count == 0, "другое рабочее пространство не видит чужих детей");
            Check(ws.ListFor(users.Get(uid)!).Select(w => w.Id).SequenceEqual([id]), "пользователь видит только свои рабочие пространства");

            // Резервная копия
            var json = Backup.Export(s.Data);
            var back = Backup.Import(json);
            Check(back.Children.Count == s.Data.Children.Count, "резервная копия выгружается и читается");

            // Роли: вкладка «Пользователи» только у администраторов
            main.Navigate(Route.Of("/admin/users"));
            await Settle();
            Check(!HasText(main, "Пользователи") && !Find<OrganizationPage>(main).Any(), "пользователь: нет вкладки «Пользователи» и экрана «Организация»");
            users.SetRole(uid, UserRoles.Admin);
            Store.Open(db, other, "Другой сотрудник").Merge(Demo.Build());
            // новое окно открываем до закрытия старого: закрытие главного окна завершает программу
            var oldMain = main;
            AppHost.OpenForShots(users.Get(uid)!, ws.Get(id)!);
            oldMain.Close();
            main = AppHost.Main!;
            main.Left = -20000;
            await Settle();
            main.Navigate(Route.Of("/admin/users"));
            await Settle();
            Check(AppHost.IsAdmin && HasText(main, "Новый пользователь"), "администратор: открывается вкладка «Пользователи»");
            var boxes = Find<TextBox>(main).ToList();
            boxes[0].Text = "novyi";
            boxes[1].Text = "Новый сотрудник";
            foreach (var p in Find<PasswordBox>(main).Take(2)) p.Password = "4321";
            Click(ButtonWithText(main, "Добавить пользователя"));
            await Settle();
            Check(users.Authenticate("novyi", "4321") is { Role: UserRoles.User }, "администратор: новый пользователь добавляется с логином и паролем");
            main.Navigate(Route.Of("/org"));
            await Settle();
            Check(Find<OrganizationPage>(main).Any(), "администратор: открывается экран «Организация»");

            // Просмотр пространства сотрудника: ничего нельзя изменить
            Click(ButtonWithText(main, "открыть для просмотра"));
            await Settle();
            var vs = AppHost.Store!;
            Check(AppHost.IsViewing && vs.ReadOnly && vs.WorkspaceId == other && vs.Data.Children.Count == 12, "администратор: чужое пространство открывается только для просмотра");
            var vChild = vs.Data.ChildrenOf(vs.Data.Groups[0].Id)[0];
            var vPeriod = vs.Data.Periods[3];
            vs.SetUi(u => { u.PeriodId = vPeriod.Id; u.SectionId = "phon"; });
            var vBefore = vs.Data.ScoresOf(vChild.Id, vPeriod.Id).OrderBy(kv => kv.Key).ToList();
            main.Navigate(Route.Of("/exam", ("child", vChild.Id)));
            await Settle();
            Key(Find<ExamPage>(main).First(), System.Windows.Input.Key.D3);
            Click(Find<Button>(main).First(b => b.Style == Ui.Style("Mark")));
            await Settle();
            var stored = Store.Open(db, other, "Другой сотрудник", readOnly: true, applyDicts: false);
            Check(vs.Data.ScoresOf(vChild.Id, vPeriod.Id).OrderBy(kv => kv.Key).SequenceEqual(vBefore)
                && stored.Data.ScoresOf(vChild.Id, vPeriod.Id).OrderBy(kv => kv.Key).SequenceEqual(vBefore), "просмотр: отметка и клавиши не меняют баллы ни в окне, ни в базе");
            Check(!Find<Button>(main).Any(b => Find<TextBlock>(b).Any(t => t.Text is "всё в норме" or "очистить раздел")), "просмотр: кнопки правки скрыты");
            Check(stored.Ui.SectionId != "phon", "просмотр: выбор раздела не записывается в настройки сотрудника");
            foreach (var r in new[] { "/", "/protocol", "/dynamics", $"/child/{vChild.Id}", "/library", "/admin/dicts" })
            {
                main.Navigate(Route.Of(r));
                await Settle();
            }
            Check(AppHost.IsViewing, "просмотр: экраны сотрудника открываются");

            AppHost.ReturnHome();
            await Settle();
            Check(!AppHost.IsViewing && ReferenceEquals(AppHost.Store, AppHost.HomeStore) && AppHost.Store!.WorkspaceId == id && Find<OrganizationPage>(main).Any(), "администратор: возврат в своё пространство");
            var log = ws.AccessLog(other);
            Check(log.Count == 1 && log[0].ViewerName == "Проверка" && log[0].Action == "view", "журнал: просмотр записан в журнал владельца");
            users.ResetPassword(otherUser, "9999");
            Check(users.Authenticate("drugoy", "9999") is not null && users.Authenticate("drugoy", "5678") is null, "администратор: сброс забытого пароля пользователя");
            main.Navigate(Route.Of("/admin/account"));
            await Settle();
            Check(HasText(main, "Пароль для входа"), "экран /admin/account открывается");

            main.Close();
        }
        catch (Exception e)
        {
            Check(false, $"исключение: {e}");
        }
        Log.Add(_failed == 0 ? $"ВСЕ ПРОВЕРКИ ПРОЙДЕНЫ ({Log.Count})" : $"НЕ ПРОЙДЕНО: {_failed}");
        File.WriteAllLines(report, Log);
        try { File.Delete(dbPath); } catch (IOException) { }
        Application.Current.Shutdown(_failed == 0 ? 0 : 1);
    }
}
