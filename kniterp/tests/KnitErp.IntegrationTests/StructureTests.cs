using KnitErp.Application.Access;
using KnitErp.Application.Common;
using KnitErp.Application.Organizations;
using KnitErp.Application.Structure;
using KnitErp.Domain.Access;
using KnitErp.Domain.Audit;
using KnitErp.Domain.Common;
using KnitErp.Domain.Structure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.IntegrationTests;

/// <summary>Структура фабрики: подразделения, должности, сотрудники и область данных по подразделениям.</summary>
public sealed class StructureTests(SqlTestHost host) : IClassFixture<SqlTestHost>
{
    private static int _innSeed = 400_000_000;
    private static readonly DateOnly Hired = new(2026, 1, 15);

    [SqlFact]
    public async Task Owner_builds_structure_and_hires_employee_with_audit()
    {
        var org = await CreateOrganizationAsync();
        await using var s = host.As(org.OwnerUserId, org.OrganizationId);

        var production = await s.Structure.CreateDepartmentAsync("Производство", null);
        var knitting = await s.Structure.CreateDepartmentAsync("Вязальный цех", production);
        var position = await s.Structure.CreatePositionAsync("Вязальщица");
        var id = await s.Employees.HireAsync(new EmployeeCommand("0001", "Иванова", "Мария", null, knitting, position, Hired));

        var structure = await s.Structure.GetAsync();
        Assert.Equal(["Производство", "Вязальный цех"], structure.Departments.Select(d => d.Name));
        Assert.Equal(1, structure.Departments.Single(d => d.Id == knitting).Depth);
        Assert.Equal("Производство / Вязальный цех", structure.Departments.Single(d => d.Id == knitting).Path);
        Assert.Equal(1, structure.Departments.Single(d => d.Id == knitting).EmployeeCount);

        var list = await s.Employees.ListAsync(new EmployeeFilter());
        var row = Assert.Single(list.Employees);
        Assert.Equal((id, "Вязальный цех", "Вязальщица", "Работает"), (row.Id, row.DepartmentName, row.PositionName, row.StatusName));

        await using var db = host.NewDb();
        Assert.True(await db.AuditEntries.AnyAsync(e => e.OrganizationId == org.OrganizationId && e.Action == AuditActions.EmployeeHired
                                                         && e.EntityId == id.ToString()));
    }

    [SqlFact]
    public async Task Duplicates_and_cycles_are_rejected()
    {
        var org = await CreateOrganizationAsync();
        await using var s = host.As(org.OwnerUserId, org.OrganizationId);
        var root = await s.Structure.CreateDepartmentAsync("Производство", null);
        var child = await s.Structure.CreateDepartmentAsync("Цех", root);
        var position = await s.Structure.CreatePositionAsync("Мастер");
        await s.Employees.HireAsync(new EmployeeCommand("0001", "Петров", "Пётр", null, child, position, Hired));

        Assert.Equal("structure.department.duplicate",
            (await Assert.ThrowsAsync<BusinessRuleException>(() => s.Structure.CreateDepartmentAsync("Цех", null))).Code);
        Assert.Equal("structure.position.duplicate",
            (await Assert.ThrowsAsync<BusinessRuleException>(() => s.Structure.CreatePositionAsync("Мастер"))).Code);
        Assert.Equal("hr.employee.number_taken",
            (await Assert.ThrowsAsync<BusinessRuleException>(() =>
                s.Employees.HireAsync(new EmployeeCommand("0001", "Сидоров", "Семён", null, child, position, Hired)))).Code);

        var rootDto = (await s.Structure.GetAsync()).Departments.Single(d => d.Id == root);
        Assert.Equal("structure.department.cycle",
            (await Assert.ThrowsAsync<BusinessRuleException>(() => s.Structure.MoveDepartmentAsync(root, child, rootDto.RowVersion))).Code);
        Assert.Equal("structure.department.has_children",
            (await Assert.ThrowsAsync<BusinessRuleException>(() => s.Structure.ArchiveDepartmentAsync(root, rootDto.RowVersion))).Code);
    }

    [SqlFact]
    public async Task Database_rejects_reference_to_another_organization()
    {
        var a = await CreateOrganizationAsync();
        var b = await CreateOrganizationAsync();
        long foreignDepartment;
        await using (var s = host.As(b.OwnerUserId, b.OrganizationId))
        {
            foreignDepartment = await s.Structure.CreateDepartmentAsync("Чужой цех", null);
        }

        await using (var s = host.As(a.OwnerUserId, a.OrganizationId))
        {
            var position = await s.Structure.CreatePositionAsync("Швея");
            await Assert.ThrowsAsync<NotFoundException>(() =>
                s.Employees.HireAsync(new EmployeeCommand("0001", "Иванова", "Анна", null, foreignDepartment, position, Hired)));
            await Assert.ThrowsAsync<NotFoundException>(() => s.Structure.CreateDepartmentAsync("Участок", foreignDepartment));

            // Даже в обход сервиса база не даст сослаться на подразделение другой организации.
            await using var db = host.NewDb();
            db.Employees.Add(Employee.Hire(a.OrganizationId, "0002", "Обход", "Сервиса", null, foreignDepartment, position, Hired));
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.IsType<SqlException>(ex.InnerException);
        }
    }

    [SqlFact]
    public async Task Department_head_sees_only_own_department_and_nested()
    {
        var org = await CreateOrganizationAsync();
        long knitting, sewing, plot, position;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            knitting = await s.Structure.CreateDepartmentAsync("Вязальный цех", null);
            plot = await s.Structure.CreateDepartmentAsync("Участок А", knitting);
            sewing = await s.Structure.CreateDepartmentAsync("Швейный цех", null);
            position = await s.Structure.CreatePositionAsync("Оператор");
            await s.Employees.HireAsync(new EmployeeCommand("1", "Вязова", "Вера", null, knitting, position, Hired));
            await s.Employees.HireAsync(new EmployeeCommand("2", "Участкова", "Ульяна", null, plot, position, Hired));
            await s.Employees.HireAsync(new EmployeeCommand("3", "Швецова", "Шура", null, sewing, position, Hired));
        }

        var head = await InviteActiveAsync(org, SystemRoles.DepartmentHead, departmentId: knitting);

        await using (var s = host.As(head, org.OrganizationId))
        {
            var list = await s.Employees.ListAsync(new EmployeeFilter());
            Assert.True(list.IsScoped);
            Assert.Equal(["Вязова", "Участкова"], list.Employees.Select(e => e.LastName));
            Assert.False(list.CanEdit);

            var structure = await s.Structure.GetAsync();
            Assert.Equal(["Вязальный цех", "Участок А"], structure.Departments.Select(d => d.Name));
        }

        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var users = await s.Access.ListUsersAsync();
            Assert.Equal("«Вязальный цех» и вложенные", users.Single(u => u.UserId == head).ScopeText);
        }
    }

    [SqlFact]
    public async Task Department_head_without_department_sees_nothing()
    {
        var org = await CreateOrganizationAsync();
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var d = await s.Structure.CreateDepartmentAsync("Цех", null);
            var p = await s.Structure.CreatePositionAsync("Оператор");
            await s.Employees.HireAsync(new EmployeeCommand("1", "Иванов", "Иван", null, d, p, Hired));
        }

        var head = await InviteActiveAsync(org, SystemRoles.DepartmentHead);
        await using var hs = host.As(head, org.OrganizationId);
        Assert.Empty((await hs.Employees.ListAsync(new EmployeeFilter())).Employees);
    }

    [SqlFact]
    public async Task Employee_role_sees_only_own_card_and_invite_links_account()
    {
        var org = await CreateOrganizationAsync();
        long anna;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var d = await s.Structure.CreateDepartmentAsync("Цех", null);
            var p = await s.Structure.CreatePositionAsync("Швея");
            anna = await s.Employees.HireAsync(new EmployeeCommand("1", "Анина", "Анна", null, d, p, Hired));
            await s.Employees.HireAsync(new EmployeeCommand("2", "Другая", "Дарья", null, d, p, Hired));
            Assert.Equal(2, (await s.Employees.ListWithoutAccountAsync()).Count);
        }

        long user;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var invitation = await s.Access.InviteAsync(new InviteUserCommand($"anna-{Guid.NewGuid():N}@test.local", "", SystemRoles.Employee,
                null, EmployeeId: anna));
            user = invitation.UserId;
            Assert.DoesNotContain(await s.Employees.ListWithoutAccountAsync(), e => e.Id == anna);
        }

        await using (var db = host.NewDb())
        {
            var account = await db.Users.SingleAsync(u => u.Id == user);
            Assert.Equal("Анина Анна", account.DisplayName);
            account.Activate();
            await db.SaveChangesAsync();
        }

        await using (var s = host.As(user, org.OrganizationId))
        {
            var list = await s.Employees.ListAsync(new EmployeeFilter());
            Assert.True(list.OwnOnly);
            Assert.Equal(anna, Assert.Single(list.Employees).Id);
            await Assert.ThrowsAsync<AccessDeniedException>(() => s.Structure.GetAsync());
        }
    }

    [SqlFact]
    public async Task Dismissal_blocks_linked_account_in_same_transaction()
    {
        var org = await CreateOrganizationAsync();
        long employee, position, dep;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            dep = await s.Structure.CreateDepartmentAsync("Склад", null);
            position = await s.Structure.CreatePositionAsync("Кладовщик");
            employee = await s.Employees.HireAsync(new EmployeeCommand("7", "Складов", "Степан", null, dep, position, Hired));
        }

        var user = await InviteActiveAsync(org, SystemRoles.Storekeeper, employeeId: employee);
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var row = (await s.Employees.ListAsync(new EmployeeFilter())).Employees.Single(e => e.Id == employee);
            Assert.Equal("access.reason.required", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
                s.Employees.DismissAsync(employee, new DateOnly(2026, 10, 9), null, row.RowVersion))).Code);
            await s.Employees.DismissAsync(employee, new DateOnly(2026, 10, 9), "По собственному желанию", row.RowVersion);
        }

        await using (var db = host.NewDb())
        {
            var member = await db.OrganizationMembers.SingleAsync(m => m.OrganizationId == org.OrganizationId && m.UserId == user);
            Assert.Equal(MembershipStatus.Blocked, member.Status);
            var dismissed = await db.Employees.SingleAsync(e => e.Id == employee);
            Assert.Equal(EmploymentStatus.Dismissed, dismissed.Status);
        }

        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            Assert.Empty((await s.Employees.ListAsync(new EmployeeFilter())).Employees);
            Assert.Single((await s.Employees.ListAsync(new EmployeeFilter(IncludeDismissed: true))).Employees);
            var depDto = (await s.Structure.GetAsync()).Departments.Single(d => d.Id == dep);
            await s.Structure.ArchiveDepartmentAsync(dep, depDto.RowVersion);
            Assert.DoesNotContain((await s.Structure.GetAsync()).Departments, d => d.Id == dep);
        }
    }

    [SqlFact]
    public async Task Dismissing_last_owner_is_rejected_and_nothing_changes()
    {
        var org = await CreateOrganizationAsync();
        long employee;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var d = await s.Structure.CreateDepartmentAsync("Дирекция", null);
            var p = await s.Structure.CreatePositionAsync("Директор");
            employee = await s.Employees.HireAsync(new EmployeeCommand("1", "Владельцев", "Влад", null, d, p, Hired));
        }

        await using (var db = host.NewDb())
        {
            (await db.Employees.SingleAsync(e => e.Id == employee)).LinkUser(org.OwnerUserId);
            await db.SaveChangesAsync();
        }

        var second = await InviteActiveAsync(org, SystemRoles.Administrator);
        await using (var s = host.As(second, org.OrganizationId))
        {
            var row = (await s.Employees.ListAsync(new EmployeeFilter())).Employees.Single(e => e.Id == employee);
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                s.Employees.DismissAsync(employee, new DateOnly(2026, 10, 9), "Тест", row.RowVersion));
            Assert.Equal("access.admin_grant_denied", ex.Code);
        }

        await using var check = host.NewDb();
        Assert.Equal(EmploymentStatus.Working, (await check.Employees.SingleAsync(e => e.Id == employee)).Status);
    }

    [SqlFact]
    public async Task Stale_employee_edit_is_a_conflict_and_transfer_is_audited_by_name()
    {
        var org = await CreateOrganizationAsync();
        long id, cutting, sewing, position;
        byte[] version;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            cutting = await s.Structure.CreateDepartmentAsync("Раскрой", null);
            sewing = await s.Structure.CreateDepartmentAsync("Пошив", null);
            position = await s.Structure.CreatePositionAsync("Швея");
            id = await s.Employees.HireAsync(new EmployeeCommand("5", "Иголкина", "Ирина", null, cutting, position, Hired));
            version = (await s.Employees.ListAsync(new EmployeeFilter())).Employees.Single().RowVersion;
        }

        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            await s.Employees.UpdateAsync(id, new EmployeeCommand("5", "Иголкина", "Ирина", null, sewing, position, Hired), version);
        }

        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
                s.Employees.UpdateAsync(id, new EmployeeCommand("5", "Иголкина", "Ирина", "Игоревна", cutting, position, Hired), version));
        }

        await using var db = host.NewDb();
        var transfer = await db.AuditEntries.SingleAsync(e => e.Action == AuditActions.EmployeeChanged && e.EntityId == id.ToString());
        Assert.Equal(("Раскрой", "Пошив", "Перевод в подразделение"), (transfer.Before, transfer.After, transfer.Reason));
    }

    private async Task<CreatedOrganization> CreateOrganizationAsync()
    {
        var inn = NextValidInn();
        await using var s = host.As(null, null);
        return await s.Organizations.CreateWithOwnerAsync(new CreateOrganizationCommand(
            $"Тестовая организация {inn}", $"Тест {inn}", inn, null, false, "Europe/Moscow",
            $"owner-{inn}@test.local", $"Владелец {inn}"));
    }

    private async Task<long> InviteActiveAsync(CreatedOrganization org, string roleCode, long? departmentId = null, long? employeeId = null)
    {
        long id;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            id = (await s.Access.InviteAsync(new InviteUserCommand($"{roleCode}-{Guid.NewGuid():N}@test.local",
                SystemRoles.NameOf(roleCode), roleCode, SystemRoles.IsAdministrative(roleCode) ? "Тест" : null,
                departmentId, employeeId))).UserId;
        }

        await using var db = host.NewDb();
        (await db.Users.SingleAsync(u => u.Id == id)).Activate();
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
