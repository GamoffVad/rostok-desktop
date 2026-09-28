namespace Rostok.Core;

// Учебный пример: вымышленные дети, воспроизводимые баллы (генератор с фиксированным зерном — тот же, что в веб-версии).
public static class Demo
{
    private static readonly string[] Names =
    [
        "Белкина Соня", "Воронов Лев", "Грачёва Мила", "Дроздов Тимур", "Ежова Варя", "Зайцев Платон",
        "Калинина Ева", "Лисицын Марк", "Орлова Алиса", "Синицын Гриша", "Соколова Тая", "Чижов Рома",
    ];
    private static readonly HashSet<string> MainSounds = ["s_s", "s_sj", "s_z", "s_zj", "s_c", "s_sh", "s_zh", "s_sch", "s_ch", "s_l", "s_lj", "s_r", "s_rj"];
    private static readonly string[] SoundByScore = ["norm", "diff", "auto", "stage"];
    private static readonly string[] Dad = ["Андрей", "Сергей", "Илья", "Павел", "Олег", "Роман"];
    private static readonly string[] Mom = ["Анна", "Мария", "Елена", "Ольга", "Наталья", "Ирина"];

    private static Func<double> Rng(long seed)
    {
        var s = seed;
        return () => { s = (s * 1664525 + 1013904223) % 4294967296; return s / 4294967296.0; };
    }

    private static int Clamp(double v) => (int)Math.Max(0, Math.Min(3, Math.Round(v, MidpointRounding.AwayFromZero)));

    public static WorkspaceData Build(DateTime? nowArg = null)
    {
        var now = nowArg ?? DateTime.Today;
        var rand = Rng(11);
        var y = now.Month >= 8 ? now.Year : now.Year - 1;
        var periods = Calc.YearPeriods($"{y - 1}–{y}").Concat(Calc.YearPeriods($"{y}–{y + 1}")).ToList();
        var group = new Group { Id = Ids.New(), Name = "Группа № 5 «Рябинка» (пример)" };
        var children = Names.Select((name, i) => new Child
        {
            Id = Ids.New(), GroupId = group.Id, Name = name,
            BirthDate = $"{y - 6}-{(i * 5 % 12) + 1:00}-{(i * 7 % 27) + 1:00}",
        }).ToList();

        // вымышленные контакты; номера из несуществующего диапазона +7 900 000-…
        Contact Phone(int i, int n, string kind) => new() { Kind = kind, Value = $"+7 900 000-{10 + i:00}-0{n}" };
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            var surname = child.Name.Split(' ')[0];
            var baseName = surname.EndsWith('а') ? surname[..^1] : surname;
            child.Relatives =
            [
                new Relative
                {
                    Role = "мама", Name = $"{baseName}а {Mom[i % 6]} Викторовна", Legal = true, Note = i % 3 == 0 ? "Удобно звонить после 18:00" : "",
                    Phones = [Phone(i, 1, "мобильный"), .. i % 2 == 1 ? new[] { Phone(i, 4, "рабочий") } : []],
                    Emails = i % 3 == 1 ? [new Contact { Value = $"mama{i + 1}@example.ru" }] : [],
                    Addresses = [new Contact { Kind = "проживания", Value = $"г. Примерск, ул. Садовая, д. {i + 3}, кв. {12 + i * 7}" }],
                },
                new Relative
                {
                    Role = "папа", Name = $"{baseName} {Dad[i % 6]} Николаевич", Legal = true,
                    Phones = [Phone(i, 2, "мобильный")],
                    Addresses = i % 4 == 2 ? [new Contact { Kind = "проживания", Value = $"г. Примерск, пр. Мира, д. {20 + i}, кв. {5 + i}" }] : [],
                },
                .. i % 4 == 1 ? new[]
                {
                    new Relative
                    {
                        Role = "бабушка", Name = $"{baseName}а Галина Петровна", Legal = false, Note = "Забирает из сада по вторникам и четвергам",
                        Phones = [Phone(i, 3, "мобильный"), Phone(i, 5, "домашний")],
                        Addresses = [new Contact { Kind = "проживания", Value = $"г. Примерск, ул. Лесная, д. {7 + i}" }],
                    },
                } : [],
            ];
        }

        var scores = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();
        foreach (var child in children)
        {
            var severity = 0.9 + rand() * 1.8;
            var pace = rand() < 0.2 ? 0.05 : 0.3 + rand() * 0.5;
            scores[child.Id] = [];
            // три заполненных среза: НГ и КГ прошлого года, НГ текущего
            for (var step = 0; step < 3; step++)
            {
                var level = severity - pace * step;
                var data = new Dictionary<string, string>();
                foreach (var section in M.AllSections)
                {
                    var weight = section.BlockId == "neuro" ? 0.7 : 1;
                    foreach (var item in section.Items)
                    {
                        var v = Clamp(level * weight + (rand() - 0.5) * 1.6);
                        switch (section.Kind)
                        {
                            case Kinds.Scale: data[item.Id] = v.ToString(); break;
                            case Kinds.Sound:
                                if (!MainSounds.Contains(item.Id)) continue;
                                data[item.Id] = step == 0 && v == 3 && rand() > 0.5 ? "broken" : SoundByScore[v];
                                break;
                            case Kinds.Side: data[item.Id] = rand() > 0.82 ? "left" : "right"; break;
                            case Kinds.YesNo: data[item.Id] = rand() > 0.9 - level * 0.05 ? "yes" : "no"; break;
                            default: data[item.Id] = level > 2 && rand() > 0.5 ? "chaotic" : "formed"; break;
                        }
                    }
                }
                scores[child.Id][periods[step].Id] = data;
            }
        }

        // пример своих рекомендаций в программе коррекции на последнем заполненном срезе
        var programs = children.ToDictionary(c => c.Id, _ => new Dictionary<string, ChildProgram>
        {
            [periods[2].Id] = new ChildProgram
            {
                Threshold = 2,
                Recs = "Индивидуальные занятия с логопедом 2 раза в неделю, подгрупповые — 1 раз в неделю.\nЗанятия с педагогом-психологом по развитию произвольной регуляции — 1 раз в неделю.",
                Home = "Артикуляционная гимнастика перед зеркалом ежедневно по 5–7 минут.\nЧитать ребёнку каждый день и обсуждать прочитанное: кто? что делал? почему?\nИграть в «Назови ласково», «Один — много» по дороге в детский сад.",
            },
        });

        return new WorkspaceData { Groups = [group], Children = children, Periods = periods, Scores = scores, Programs = programs };
    }
}
