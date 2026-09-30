using System.Windows;
using System.Windows.Controls;
using Rostok.Controls;
using Rostok.Core;
using Rostok.Desktop.Services;

namespace Rostok.Desktop.Pages;

// «Справка по методике»: как считаются баллы, уровни и итог.
public sealed class HelpPage(Route route) : PageBase(route)
{
    private static FrameworkElement Chapter(string num, string title) => new WrapPanel
    {
        Margin = new Thickness(0, 34, 0, 8),
        Children =
        {
            Ui.Num(num, Theme.Accent, 25.6, FontWeights.Medium).With(t => t.Margin = new Thickness(0, 0, 12, 0)),
            Ui.Text(title, null, Theme.Ink, 24, FontWeights.Bold).With(t => t.VerticalAlignment = VerticalAlignment.Bottom),
        },
    };

    private static TextBlock P(string text) => Ui.Body(text).With(t => { t.Margin = new Thickness(0, 10, 0, 10); t.LineHeight = 23; });

    private static FrameworkElement Formula(string text) => new Border
    {
        Background = Theme.Sheet, BorderBrush = Theme.Accent, BorderThickness = new Thickness(1, 0, 0, 0), Margin = new Thickness(0, 12, 0, 12),
        Child = new DashBorder { Sides = Sides.Top | Sides.Right | Sides.Bottom, Padding = new Thickness(14, 12, 14, 12), Child = Ui.Num(text, Theme.Ink2, 13).With(t => t.TextWrapping = TextWrapping.Wrap) },
    };

    private static FrameworkElement Bullets(params string[] items)
    {
        var s = new StackPanel { Margin = new Thickness(0, 10, 0, 10) };
        foreach (var i in items)
        {
            var g = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(Ui.Body("•"));
            var t = Ui.Body(i);
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            s.Children.Add(g);
        }
        return s;
    }

    protected override UIElement Build()
    {
        var prose = new StackPanel { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        prose.Children.Add(Chapter("01", "Единая шкала"));
        prose.Children.Add(P("Каждая проба оценивается от 0 до 3. Шкала одна для речевого блока и нейродиагностики — так, как она описана в листе параметров исходного файла:"));
        var t1 = new Tbl(Col.Px(60), Col.Star());
        t1.Header("Балл", "Значение");
        foreach (var s in M.Scale) t1.Row(new Level(s.Score), s.Label);
        prose.Children.Add(t1);
        prose.Children.Add(Ui.HelpNote(Ui.Rich(Ui.Run("В прежнем файле протоколы велись в обратную сторону: 3 — норма, 0 — нарушено. При загрузке Excel баллы переводятся автоматически (3 − x), поэтому "), Ui.Run("снижение", weight: FontWeights.Bold), Ui.Run(" балла теперь означает улучшение.")).With(t => t.FontSize = 13), amber: true));

        prose.Children.Add(Chapter("02", "Звукопроизношение"));
        prose.Children.Add(P("Для звука отмечается этап работы. Каждый этап имеет балл по той же шкале; пустые звуки в расчёт не входят — как функция СЧЁТЗ в Excel."));
        var t2 = new Tbl(Col.Px(60), Col.Star(), Col.Auto(Align.Right));
        t2.Header("Знак", "Этап", "Балл");
        foreach (var s in M.SoundStates) t2.Row(new Level(s.Score, s.Label, s.Mark), s.Label, Ui.Num(s.Score.ToString()));
        prose.Children.Add(t2);

        prose.Children.Add(Chapter("03", "От баллов к уровню"));
        prose.Children.Add(P("По разделу считается среднее по заполненным пробам, затем среднее переводится в уровень 0–3:"));
        prose.Children.Add(Formula("среднее = сумма баллов / число заполненных проб\n\nуровень 0 (норма)                 среднее < 0,6\nуровень 1 (формируется)     0,6 ≤ среднее < 1,6\nуровень 2 (нужна коррекция) 1,6 ≤ среднее < 2,1\nуровень 3 (выраженное)            среднее ≥ 2,1"));
        prose.Children.Add(Ui.Rich(Ui.Run("Это зеркальное отражение формулы из Excel "), Ui.Run("ЕСЛИ(ср≤0,9;0;ЕСЛИ(ср≤1,4;1;ЕСЛИ(ср≤2,4;2;3)))", mono: true), Ui.Run(": пороги те же, перевёрнута только шкала.")).With(t => t.LineHeight = 23));

        prose.Children.Add(Chapter("04", "Итог коррекционной работы"));
        prose.Children.Add(P("Сравнивается средний балл по блоку на двух срезах (обычно начало и конец года):"));
        prose.Children.Add(Formula("возрастная норма            уровень на конец = 0\nзначительное улучшение      балл снизился на 1,0 и больше\nулучшение                   балл снизился на 0,3–0,99\nбез выраженной динамики     изменение меньше 0,3\nотрицательная динамика      балл вырос на 0,3 и больше"));

        prose.Children.Add(Chapter("05", "Что исправлено по сравнению с Excel"));
        prose.Children.Add(Bullets(
            "Подсчёт детей с уровнем 1 в сводной брал сдвинутый диапазон (строки 6–35 вместо 5–34) — первый ребёнок терялся.",
            "В диаграмме «Звукопроизношение, начало года» стояла ссылка на столбец фонетики.",
            "Пороги уровня различались на разных листах (2,4 и 2,9), а на листах звуков не было уровня 0. Теперь правило одно.",
            "Пустые срезы давали #ДЕЛ/0! — здесь пустой раздел просто не участвует в расчёте.",
            "Данные для диаграммы результативности вводились вручную — теперь все графики строятся из баллов.",
            "Фамилии вводятся один раз; новый учебный год добавляется кнопкой, без копирования листов."));

        prose.Children.Add(Chapter("06", "Вход, рабочие пространства и конфиденциальность"));
        prose.Children.Add(P("Данные хранятся в файле базы SQLite: на этом компьютере или в общей папке локальной сети («Администрирование → Подключение»). В программу входят по логину и паролю. После входа пользователь видит свои рабочие пространства — отдельные хранилища со своими группами, детьми, баллами, упражнениями, словарями и настройками — и может создать новое. Пространства других пользователей ему не видны."));
        prose.Children.Add(P("Пароль хранится только в виде хеша, узнать его нельзя. Забытый пароль задаёт заново администратор. Раз в неделю выгружайте резервную копию в разделе «Данные» и храните её так же, как остальную закрытую документацию специалиста; доступ к папке с базой ограничьте сотрудниками учреждения."));

        prose.Children.Add(Chapter("07", "Администраторы и журнал просмотров"));
        prose.Children.Add(P("В каждой базе есть главный администратор: при создании базы — логин admin и пароль admin, которые нужно сразу сменить («Администрирование → Учётная запись»). Администраторы добавляют пользователей и назначают роли — «администратор» или «пользователь» — на вкладке «Администрирование → Пользователи»; там же сбрасывают забытые пароли и передают пространства другим пользователям."));
        prose.Children.Add(P("Администратор видит все рабочие пространства базы и сводку по ним на экране «Организация». Чужое пространство он открывает только для просмотра: видны все экраны, отчёты и выгрузки, но изменить ничего нельзя. Каждое открытие записывается в журнал, который владелец видит в «Администрирование → Рабочее пространство». Разграничение действует в программе, сами данные в файле не зашифрованы — поэтому доступ к папке с базой нужно ограничить правами Windows."));

        return Page(PageHeader("Справка по методике", "Как считаются баллы, уровни и итог — и чем расчёт отличается от прежней таблицы Excel."), prose);
    }
}
