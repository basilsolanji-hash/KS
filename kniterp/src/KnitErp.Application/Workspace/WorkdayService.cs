using KnitErp.Application.Common;
using KnitErp.Domain.Audit;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.Application.Workspace;

/// <summary>Срочная задача: не выполнена, срок — сегодня или уже прошёл.</summary>
public sealed record UrgentTaskDto(string Id, string Title, DateOnly Date, TimeOnly? Time, bool Overdue);

/// <summary>
/// Рабочий день пользователя для нижней части меню (D72). StartedLocal — первый вход сегодня по журналу входов
/// (местное время организации); норма — NormMinutes. Время считает интерфейс от StartedLocal, чтобы не ходить в базу
/// каждую минуту. Задачи «сегодня» — личные задачи с датой сегодня; срочные — невыполненные со сроком сегодня или раньше.
/// </summary>
public sealed record WorkdayDto(
    DateTime? StartedLocal, string TimeZoneId, int NormMinutes, int TasksToday, int TasksDone, int OverdueCount,
    IReadOnlyList<UrgentTaskDto> Urgent)
{
    /// <summary>Отработано, осталось и процент нормы на момент <paramref name="nowLocal"/>.</summary>
    public (int Elapsed, int Remaining, int Percent) Progress(DateTime nowLocal)
    {
        var elapsed = StartedLocal is { } start ? Math.Max(0, (int)(nowLocal - start).TotalMinutes) : 0;
        var percent = NormMinutes == 0 ? 0 : Math.Min(100, elapsed * 100 / NormMinutes);
        return (elapsed, Math.Max(0, NormMinutes - elapsed), percent);
    }

    public int TasksPercent => TasksToday == 0 ? 0 : TasksDone * 100 / TasksToday;
}

/// <summary>
/// Показатели рабочего дня. Своя операция — свой DbContext (как у инструментов панели): блок в меню обновляется,
/// пока страница работает со своим. Только свои данные пользователя, права не нужны.
/// </summary>
public sealed class WorkdayService(IKnitErpDbContextFactory factory, ICurrentUser currentUser, IClock clock, PersonalToolsService personal)
{
    /// <summary>Норма рабочего дня по умолчанию (D72): 8 часов. График сотрудника — отдельным срезом.</summary>
    public const int DefaultNormMinutes = 8 * 60;

    public const int MaxUrgent = 3;

    public async Task<WorkdayDto> GetAsync(CancellationToken ct = default)
    {
        var userId = currentUser.UserId ?? throw new NotFoundException("Пользователь");
        var orgId = currentUser.OrganizationId ?? throw new NotFoundException("Организация");
        await using var db = factory.Create();
        var tz = await db.Organizations.AsNoTracking().Where(o => o.Id == orgId).Select(o => o.TimeZoneId).SingleAsync(ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(tz);
        var now = clock.UtcNow;
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(now, DateTimeKind.Utc), zone);
        var dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(nowLocal.Date, DateTimeKind.Unspecified), zone);

        var own = userId.ToString();
        var firstUtc = await db.AuditEntries.AsNoTracking()
            .Where(a => a.Action == AuditActions.SignedIn && a.OccurredAtUtc >= dayStartUtc
                        && (a.ActorUserId == userId || (a.EntityType == "UserAccount" && a.EntityId == own)))
            .OrderBy(a => a.OccurredAtUtc).Select(a => (DateTime?)a.OccurredAtUtc).FirstOrDefaultAsync(ct);
        DateTime? started = firstUtc is { } f ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(f, DateTimeKind.Utc), zone) : null;

        var today = DateOnly.FromDateTime(nowLocal);
        var tasks = await personal.GetTasksAsync(ct);
        var dated = tasks.Where(t => t.Date is not null).ToList();
        var urgent = dated.Where(t => !t.Done && t.Date <= today)
            .OrderBy(t => t.Date).ThenBy(t => t.Time ?? TimeOnly.MaxValue)
            .Select(t => new UrgentTaskDto(t.Id, t.Title, t.Date!.Value, t.Time, t.Date < today))
            .ToList();
        return new WorkdayDto(started, tz, DefaultNormMinutes, dated.Count(t => t.Date == today), dated.Count(t => t.Date == today && t.Done),
            urgent.Count(t => t.Overdue), urgent.Take(MaxUrgent).ToList());
    }
}
