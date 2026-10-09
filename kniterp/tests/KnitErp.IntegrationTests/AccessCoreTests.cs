using KnitErp.Application.Access;
using KnitErp.Application.Common;
using KnitErp.Application.Organizations;
using KnitErp.Domain.Access;
using KnitErp.Domain.Audit;
using KnitErp.Domain.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.IntegrationTests;

/// <summary>Приёмка ядра доступа ТЗ §4.14 на настоящем SQL Server.</summary>
public sealed class AccessCoreTests(SqlTestHost host) : IClassFixture<SqlTestHost>
{
    private static int _innSeed = 100_000_000;

    [SqlFact]
    public async Task Organization_B_data_is_invisible_to_organization_A()
    {
        var a = await CreateOrganizationAsync();
        var b = await CreateOrganizationAsync();

        await using (var s = host.As(a.OwnerUserId, a.OrganizationId))
        {
            await Assert.ThrowsAsync<NotFoundException>(() => s.Organizations.GetByIdAsync(b.OrganizationId));
        }

        await using (var s = host.As(a.OwnerUserId, a.OrganizationId))
        {
            var users = await s.Access.ListUsersAsync();
            Assert.DoesNotContain(users, u => u.UserId == b.OwnerUserId);
        }

        await using (var s = host.As(a.OwnerUserId, a.OrganizationId))
        {
            await Assert.ThrowsAsync<NotFoundException>(() => s.Access.BlockAsync(b.OwnerUserId, "чужой"));
        }

        await using (var s = host.As(a.OwnerUserId, a.OrganizationId))
        {
            var audit = await s.Audit.ListAsync();
            Assert.NotEmpty(audit);
            Assert.DoesNotContain(audit, e => e.EntityId == b.OrganizationId.ToString() && e.EntityType == "Organization");
        }
    }

    [SqlFact]
    public async Task User_without_permission_gets_403_and_denial_is_audited()
    {
        var org = await CreateOrganizationAsync();
        var keeper = await InviteActiveAsync(org, SystemRoles.Storekeeper);

        await using (var s = host.As(keeper, org.OrganizationId))
        {
            var ex = await Assert.ThrowsAsync<AccessDeniedException>(() => s.Access.ListUsersAsync());
            Assert.Equal(Permissions.UserView, ex.PermissionCode);
            Assert.StartsWith("Недостаточно прав", ex.Message);
        }

        await using var db = host.NewDb();
        Assert.True(await db.AuditEntries.AnyAsync(e =>
            e.OrganizationId == org.OrganizationId && e.ActorUserId == keeper
            && e.Action == AuditActions.AccessDenied && e.After == Permissions.UserView));
    }

    [SqlFact]
    public async Task Administrator_cannot_grant_owner_or_rights_above_own()
    {
        var org = await CreateOrganizationAsync();
        var admin = await InviteActiveAsync(org, SystemRoles.Administrator);

        await using (var s = host.As(admin, org.OrganizationId))
        {
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                s.Access.InviteAsync(new InviteUserCommand("new-owner@test.local", "Новый владелец", SystemRoles.Owner, "тест")));
            Assert.Equal("access.admin_grant_denied", ex.Code);
        }

        await using (var s = host.As(admin, org.OrganizationId))
        {
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                s.Access.InviteAsync(new InviteUserCommand("senior@test.local", "Старший кладовщик", SystemRoles.SeniorStorekeeper, null)));
            Assert.Equal("access.escalation", ex.Code);
        }

        await using (var s = host.As(admin, org.OrganizationId))
        {
            var id = await s.Access.InviteAsync(new InviteUserCommand("buh@test.local", "Бухгалтер", SystemRoles.Accountant, null));
            Assert.True(id > 0);
        }
    }

    [SqlFact]
    public async Task Last_owner_cannot_be_demoted()
    {
        var org = await CreateOrganizationAsync();

        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                s.Access.ChangeRoleAsync(new ChangeRoleCommand(org.OwnerUserId, SystemRoles.Administrator, "тест")));
            Assert.Equal("access.last_owner", ex.Code);
        }

        // Со вторым владельцем понижение первого разрешено.
        var second = await InviteActiveAsync(org, SystemRoles.Owner, "Второй владелец");
        await using (var s = host.As(second, org.OrganizationId))
        {
            await s.Access.ChangeRoleAsync(new ChangeRoleCommand(org.OwnerUserId, SystemRoles.Administrator, "Передача управления"));
        }
    }

    [SqlFact]
    public async Task Role_change_is_audited_with_before_and_after()
    {
        var org = await CreateOrganizationAsync();
        var user = await InviteActiveAsync(org, SystemRoles.Storekeeper);

        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            await s.Access.ChangeRoleAsync(new ChangeRoleCommand(user, SystemRoles.SeniorStorekeeper, "Повышение"));
        }

        await using var db = host.NewDb();
        var entry = await db.AuditEntries.Where(e => e.OrganizationId == org.OrganizationId
                && e.Action == AuditActions.RoleGranted && e.EntityId == user.ToString())
            .OrderByDescending(e => e.Id).FirstAsync();
        Assert.Equal(SystemRoles.NameOf(SystemRoles.Storekeeper), entry.Before);
        Assert.Equal(SystemRoles.NameOf(SystemRoles.SeniorStorekeeper), entry.After);
        Assert.Equal("Повышение", entry.Reason);
        Assert.Equal(org.OwnerUserId, entry.ActorUserId);
    }

    [SqlFact]
    public async Task Blocked_user_loses_access_on_next_request()
    {
        var org = await CreateOrganizationAsync();
        var accountant = await InviteActiveAsync(org, SystemRoles.Accountant);

        await using (var s = host.As(accountant, org.OrganizationId))
        {
            await s.Organizations.GetCurrentAsync();
        }

        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            await s.Access.BlockAsync(accountant, "Увольнение");
        }

        await using (var s = host.As(accountant, org.OrganizationId))
        {
            await Assert.ThrowsAsync<AccessDeniedException>(() => s.Organizations.GetCurrentAsync());
        }
    }

    [SqlFact]
    public async Task Audit_log_cannot_be_changed_by_application_or_sql()
    {
        var org = await CreateOrganizationAsync();

        await using (var db = host.NewDb())
        {
            var entry = await db.AuditEntries.FirstAsync(e => e.OrganizationId == org.OrganizationId);
            db.AuditEntries.Remove(entry);
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }

        await using (var db = host.NewDb())
        {
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [kniterp].[audit_log] SET [Reason] = N'подмена' WHERE [OrganizationId] = {org.OrganizationId}"));
        }

        await using (var db = host.NewDb())
        {
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM [kniterp].[audit_log] WHERE [OrganizationId] = {org.OrganizationId}"));
        }
    }

    [SqlFact]
    public async Task Stale_edit_of_requisites_is_a_conflict_not_an_overwrite()
    {
        var org = await CreateOrganizationAsync();
        OrganizationDto first;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            first = await s.Organizations.GetCurrentAsync();
        }

        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            await s.Organizations.UpdateRequisitesAsync(new UpdateRequisitesCommand("Электросталь", "Europe/Moscow", first.RowVersion));
        }

        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
                s.Organizations.UpdateRequisitesAsync(new UpdateRequisitesCommand("Москва", "Europe/Moscow", first.RowVersion)));
        }
    }

    [SqlFact]
    public async Task Duplicate_inn_is_rejected_by_database()
    {
        var org = await CreateOrganizationAsync();
        string inn;
        await using (var db = host.NewDb())
        {
            inn = (await db.Organizations.SingleAsync(o => o.Id == org.OrganizationId)).Inn;
        }

        await using var s = host.As(null, null);
        await Assert.ThrowsAsync<DbUpdateException>(() => s.Organizations.CreateWithOwnerAsync(
            new CreateOrganizationCommand("Дубль", "Дубль", inn, null, false, "Europe/Moscow", "dup@test.local", "Дубль")));
    }

    private async Task<CreatedOrganization> CreateOrganizationAsync()
    {
        var inn = NextValidInn();
        await using var s = host.As(null, null);
        return await s.Organizations.CreateWithOwnerAsync(new CreateOrganizationCommand(
            $"Тестовая организация {inn}", $"Тест {inn}", inn, null, false, "Europe/Moscow",
            $"owner-{inn}@test.local", $"Владелец {inn}"));
    }

    /// <summary>Приглашение от имени владельца и активация (вход через Identity появится в следующем срезе).</summary>
    private async Task<long> InviteActiveAsync(CreatedOrganization org, string roleCode, string? name = null)
    {
        long id;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            id = await s.Access.InviteAsync(new InviteUserCommand(
                $"{roleCode}-{Guid.NewGuid():N}@test.local", name ?? SystemRoles.NameOf(roleCode), roleCode,
                SystemRoles.IsAdministrative(roleCode) ? "Тест" : null));
        }

        await using var db = host.NewDb();
        var user = await db.Users.SingleAsync(u => u.Id == id);
        user.Activate();
        await db.SaveChangesAsync();
        return id;
    }

    private static string NextValidInn()
    {
        int[] w = [2, 4, 10, 3, 5, 9, 4, 6, 8];
        var body = Interlocked.Increment(ref _innSeed).ToString("D9");
        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            sum += (body[i] - '0') * w[i];
        }

        return body + (sum % 11 % 10);
    }
}
