using KnitErp.Application.Organizations;
using KnitErp.Application.Structure;
using KnitErp.Application.Workspace;
using KnitErp.Domain.Common;

namespace KnitErp.IntegrationTests;

/// <summary>D83: правка задач, праздники по ст. 112 ТК РФ и дни рождения коллег с согласия, личные настройки календаря.</summary>
public sealed class CalendarTests(SqlTestHost host) : IClassFixture<SqlTestHost>
{
    private static int _innSeed = 860_000_000;

    [SqlFact]
    public async Task Tasks_are_edited_with_note_and_keep_done_mark()
    {
        var org = await CreateOrgAsync();
        await using var s = host.As(org.OwnerUserId, org.OrganizationId);
        var list = await s.Personal.SaveTaskAsync(new TaskItem("", "Заказать пряжу", null, null, false, false, null));
        var id = list.Single().Id;
        await s.Personal.SetTaskDoneAsync(id, true);
        list = await s.Personal.SaveTaskAsync(new TaskItem(id, "Заказать пряжу у «Пехорки»", new DateOnly(2026, 10, 12), new TimeOnly(10, 0), true, true,
            "50 кг меринос"));
        var task = list.Single();
        Assert.Equal(("Заказать пряжу у «Пехорки»", "50 кг меринос", true, new TimeOnly(10, 0)), (task.Title, task.Note, task.Done, task.Time!.Value));
        Assert.Equal("workspace.remind_date", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            s.Personal.SaveTaskAsync(task with { Date = null }))).Code);
    }

    [SqlFact]
    public async Task Holidays_and_consented_birthdays_follow_personal_settings()
    {
        var org = await CreateOrgAsync();
        long anna, boris;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var shop = await s.Structure.CreateDepartmentAsync("Вязальный цех", null);
            var position = await s.Structure.CreatePositionAsync("Вязальщица");
            anna = await s.Employees.HireAsync(new EmployeeCommand("001", "Иванова", "Анна", null, shop, position, new DateOnly(2020, 1, 1)));
            boris = await s.Employees.HireAsync(new EmployeeCommand("002", "Петров", "Борис", null, shop, position, new DateOnly(2020, 1, 1)));
            var rows = (await s.Employees.ListAsync(new EmployeeFilter())).Employees;

            // Возраст проверяется; согласие без даты — нельзя.
            Assert.Equal("hr.employee.birth_date", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
                s.Employees.SetBirthdayAsync(anna, new DateOnly(2020, 1, 1), false, rows.Single(e => e.Id == anna).RowVersion))).Code);
            Assert.Equal("hr.employee.birthday_share", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
                s.Employees.SetBirthdayAsync(anna, null, true, rows.Single(e => e.Id == anna).RowVersion))).Code);

            // Анна согласна (день рождения 29 февраля), Борис — нет.
            await s.Employees.SetBirthdayAsync(anna, new DateOnly(1992, 2, 29), true, rows.Single(e => e.Id == anna).RowVersion);
            await s.Employees.SetBirthdayAsync(boris, new DateOnly(1990, 10, 9), false, rows.Single(e => e.Id == boris).RowVersion);
        }

        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var january = await s.Personal.GetCalendarEventsAsync(new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31));
            Assert.Equal(8, january.Count(e => e.Kind == CalendarEventKind.Holiday));
            Assert.Contains(january, e => e.Date == new DateOnly(2027, 1, 7) && e.Title == "Рождество Христово");

            // 29 февраля в невисокосном году — 28 февраля; год не показывается.
            var february = await s.Personal.GetCalendarEventsAsync(new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 28));
            var bday = Assert.Single(february, e => e.Kind == CalendarEventKind.Birthday);
            Assert.Equal((new DateOnly(2027, 2, 28), "Анна Иванова"), (bday.Date, bday.Title));

            // Без согласия Бориса нет ни в календаре, ни в уведомлениях (сегодня 09.10 — его день рождения).
            Assert.DoesNotContain(await s.Personal.GetCalendarEventsAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)),
                e => e.Kind == CalendarEventKind.Birthday);
            Assert.DoesNotContain(await s.Notifications.ListAsync(), n => n.Text.Contains("день рождения"));

            // Личная настройка: праздники и дни рождения можно скрыть.
            await s.Personal.SaveCalendarSettingsAsync(new CalendarSettings(ShowHolidays: false, ShowBirthdays: false));
            Assert.Empty(await s.Personal.GetCalendarEventsAsync(new DateOnly(2027, 1, 1), new DateOnly(2027, 2, 28)));
            await s.Personal.SaveCalendarSettingsAsync(new CalendarSettings());

            var rows = (await s.Employees.ListAsync(new EmployeeFilter())).Employees;
            await s.Employees.SetBirthdayAsync(boris, new DateOnly(1990, 10, 9), true, rows.Single(e => e.Id == boris).RowVersion);
            Assert.Contains(await s.Notifications.ListAsync(), n => n.Text == "Сегодня день рождения: Борис Петров");
            Assert.Equal("workspace.calendar_range", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
                s.Personal.GetCalendarEventsAsync(new DateOnly(2026, 1, 1), new DateOnly(2028, 1, 1)))).Code);
        }

        // Другая организация не видит дней рождения этой.
        var other = await CreateOrgAsync();
        await using (var s = host.As(other.OwnerUserId, other.OrganizationId))
        {
            Assert.DoesNotContain(await s.Personal.GetCalendarEventsAsync(new DateOnly(2026, 10, 1), new DateOnly(2027, 3, 1)),
                e => e.Kind == CalendarEventKind.Birthday);
        }
    }

    private async Task<CreatedOrganization> CreateOrgAsync()
    {
        int[] w = [2, 4, 10, 3, 5, 9, 4, 6, 8];
        var body = Interlocked.Increment(ref _innSeed).ToString("D9");
        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            sum += (body[i] - '0') * w[i];
        }

        var inn = body + (sum % 11 % 10);
        await using var s = host.As(null, null);
        return await s.Organizations.CreateWithOwnerAsync(new CreateOrganizationCommand(
            $"Тестовая организация {inn}", $"Тест {inn}", inn, null, false, "Europe/Moscow", $"owner-{inn}-{Guid.NewGuid():N}@test.local", $"Владелец {inn}", "RU"));
    }
}
