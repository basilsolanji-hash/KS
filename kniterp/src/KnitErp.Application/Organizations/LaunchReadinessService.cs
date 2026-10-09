using KnitErp.Application.Access;
using KnitErp.Application.Common;
using KnitErp.Domain.Access;
using KnitErp.Domain.Structure;
using KnitErp.Domain.Warehousing;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.Application.Organizations;

/// <summary>Done: true — выполнено, false — нет, null — проверяется вручную (программа это не видит).</summary>
public sealed record ReadinessItem(string Id, string Title, bool? Done, string Detail, string? Link);

public sealed record LaunchReadinessDto(IReadOnlyList<ReadinessItem> Items)
{
    public int Checked => Items.Count(i => i.Done is not null);
    public int Completed => Items.Count(i => i.Done == true);
    public bool Ready => Items.All(i => i.Done != false);
}

/// <summary>
/// «Готовность к запуску» (допущение D58): что по данным базы уже сделано для начала работы фабрики в программе —
/// реквизиты, структура, сотрудники, пользователи с ролями, склады, номенклатура, поставщики, утверждённые начальные остатки.
/// Только чтение. Видит тот, кто может менять реквизиты организации (Владелец, Администратор).
/// </summary>
public sealed class LaunchReadinessService(IKnitErpDbContext db, IAccessGuard guard, IClock clock)
{
    public async Task<LaunchReadinessDto> GetAsync(CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.OrganizationEdit, ct);
        var org = ctx.OrganizationId;
        var now = clock.UtcNow;
        var items = new List<ReadinessItem>();

        var o = await db.Organizations.AsNoTracking().SingleAsync(x => x.Id == org, ct);
        var requisitesMissing = new List<string>();
        if (o.Kpp is not null && !o.KppVerified)
        {
            requisitesMissing.Add("КПП не сверен (D15)");
        }

        if (string.IsNullOrWhiteSpace(o.ActualAddress))
        {
            requisitesMissing.Add("нет фактического адреса");
        }

        items.Add(new("requisites", "Реквизиты организации", requisitesMissing.Count == 0,
            requisitesMissing.Count == 0 ? $"ИНН {o.Inn}{(o.Kpp is null ? "" : $", КПП {o.Kpp} сверен")}" : Capitalize(string.Join(", ", requisitesMissing)) + ".",
            ""));

        var departments = await db.Departments.CountAsync(d => d.OrganizationId == org && !d.IsArchived, ct);
        var positions = await db.Positions.CountAsync(p => p.OrganizationId == org && !p.IsArchived, ct);
        items.Add(new("structure", "Подразделения и должности", departments > 0 && positions > 0,
            $"Подразделений: {departments}, должностей: {positions}.", "structure"));

        var employees = await db.Employees.CountAsync(e => e.OrganizationId == org && e.Status != EmploymentStatus.Dismissed, ct);
        items.Add(new("employees", "Сотрудники", employees > 0,
            employees > 0 ? $"Работают: {employees}." : "Добавьте сотрудников вручную или загрузите из Excel.", "employees"));

        var members = await db.OrganizationMembers.AsNoTracking()
            .Where(m => m.OrganizationId == org && m.Status == MembershipStatus.Active)
            .Join(db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active), m => m.UserId, u => u.Id, (m, u) => m.UserId)
            .ToListAsync(ct);
        var assignments = await db.RoleAssignments.AsNoTracking()
            .Where(a => a.OrganizationId == org && a.RoleId != null && !a.IsDeny && a.RevokedAtUtc == null)
            .Join(db.Roles.AsNoTracking(), a => a.RoleId, r => r.Id, (a, r) => new { a.UserId, r.Code, a.ValidFromUtc, a.ValidToUtc })
            .ToListAsync(ct);
        var activeRoles = assignments
            .Where(a => members.Contains(a.UserId) && a.ValidFromUtc <= now && (a.ValidToUtc is null || a.ValidToUtc > now))
            .ToList();
        bool HasRole(string code) => activeRoles.Any(a => a.Code == code);
        var missingRoles = new[] { SystemRoles.SeniorStorekeeper, SystemRoles.Storekeeper }
            .Where(code => !HasRole(code)).Select(SystemRoles.NameOf).ToList();
        items.Add(new("users", "Пользователи склада с ролями", missingRoles.Count == 0,
            missingRoles.Count == 0
                ? $"Работают в программе: {members.Count} чел."
                : $"Нет активного пользователя с ролью: {string.Join(", ", missingRoles)}. Пригласите в разделе «Пользователи».",
            "users"));

        var approvers = activeRoles.Where(a => a.Code is SystemRoles.Owner or SystemRoles.DepartmentHead).Select(a => a.UserId).Distinct().Count();
        items.Add(new("approvers", "Двое могут утверждать начальные остатки", approvers >= 2,
            approvers >= 2
                ? $"Владелец или руководитель: {approvers} чел."
                : "Утверждает не автор документа (D05): нужен ещё Руководитель подразделения на случай отсутствия Владельца.",
            "users"));

        var warehouses = await db.Warehouses.AsNoTracking().Where(w => w.OrganizationId == org && !w.IsArchived)
            .Select(w => new { w.Id, w.Name }).ToListAsync(ct);
        items.Add(new("warehouses", "Склады", warehouses.Count > 0, $"Действующих складов: {warehouses.Count}.", "warehouses"));

        var itemCount = await db.Items.CountAsync(i => i.OrganizationId == org && !i.IsArchived, ct);
        items.Add(new("items", "Номенклатура", itemCount > 0,
            itemCount > 0 ? $"Позиций: {itemCount}." : "Заведите позиции вручную или загрузите из Excel.", "catalog"));

        var suppliers = await db.Counterparties.CountAsync(c => c.OrganizationId == org && !c.IsArchived && c.IsSupplier, ct);
        items.Add(new("suppliers", "Поставщики", suppliers > 0,
            suppliers > 0 ? $"Поставщиков: {suppliers}." : "Заведите поставщиков вручную или загрузите из Excel.", "counterparties"));

        var balances = await db.OpeningBalances.AsNoTracking().Where(b => b.OrganizationId == org)
            .Select(b => new { b.WarehouseId, b.Status }).ToListAsync(ct);
        var withoutBalances = warehouses.Where(w => !balances.Any(b => b.WarehouseId == w.Id && b.Status == OpeningBalanceStatus.Approved))
            .Select(w => w.Name).ToList();
        items.Add(new("opening", "Начальные остатки утверждены по всем складам", warehouses.Count > 0 && withoutBalances.Count == 0,
            warehouses.Count == 0 ? "Сначала заведите склады."
            : withoutBalances.Count == 0 ? "По каждому складу есть утверждённый документ."
            : $"Без утверждённых остатков: {string.Join(", ", withoutBalances.Take(10))}{(withoutBalances.Count > 10 ? "…" : "")}.",
            "opening-balances"));

        var waiting = balances.Count(b => b.Status == OpeningBalanceStatus.Submitted);
        items.Add(new("waiting", "Нет документов, ждущих утверждения", waiting == 0,
            waiting == 0 ? "Очередь утверждения пуста." : $"На утверждении: {waiting}.", "opening-balances"));

        items.Add(new("reconciliation", "Сверка и проверка целостности после ввода остатков", null,
            "Запустите «Сверку» и «Проверку целостности», запишите отпечаток в акт.", "reconciliation"));
        items.Add(new("backup", "Резервное копирование и копия мастер-ключа", null,
            "Ежедневная копия базы (deploy/backup-kniterp.sql), проверка восстановления, мастер-ключ в сейфе Владельца.", null));
        return new LaunchReadinessDto(items);
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
