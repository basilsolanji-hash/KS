using KnitErp.Domain.Access;
using KnitErp.Domain.Common;

namespace KnitErp.UnitTests;

public class EffectivePermissionsTests
{
    private static EffectivePermissionSet RoleSet(string role) =>
        EffectivePermissionSet.Compute(
            RoleMatrixP0.PermissionsOf(role).Select(p => new PermissionGrant(p.PermissionCode, p.Level, false)));

    [Fact]
    public void Empty_set_denies_everything() =>
        Assert.All(Permissions.All, c => Assert.False(EffectivePermissionSet.Empty.Has(c)));

    [Fact]
    public void Deny_beats_allow()
    {
        var set = EffectivePermissionSet.Compute([
            new PermissionGrant(Permissions.CatalogEdit, PermissionLevel.Full, false),
            new PermissionGrant(Permissions.CatalogEdit, PermissionLevel.Full, true),
        ]);
        Assert.False(set.Has(Permissions.CatalogEdit));
        Assert.True(set.IsDenied(Permissions.CatalogEdit));
    }

    [Fact]
    public void Union_takes_the_strongest_level()
    {
        var set = EffectivePermissionSet.Compute([
            new PermissionGrant(Permissions.DepartmentView, PermissionLevel.ReadOnly, false),
            new PermissionGrant(Permissions.DepartmentView, PermissionLevel.Full, false),
        ]);
        Assert.Equal(PermissionLevel.Full, set.LevelOf(Permissions.DepartmentView));
    }

    [Fact]
    public void ByGrant_and_OwnOnly_do_not_grant_organization_data()
    {
        var admin = RoleSet(SystemRoles.Administrator);
        Assert.False(admin.Has(Permissions.PersonalDataView));

        var employee = RoleSet(SystemRoles.Employee);
        Assert.False(employee.Has(Permissions.AuditLogView));
        Assert.True(employee.HasOwnOnly(Permissions.AuditLogView));
    }

    [Fact]
    public void Individual_grant_opens_ByGrant_permission()
    {
        var grants = RoleMatrixP0.PermissionsOf(SystemRoles.Administrator)
            .Select(p => new PermissionGrant(p.PermissionCode, p.Level, false))
            .Append(new PermissionGrant(Permissions.PersonalDataView, PermissionLevel.Full, false));
        Assert.True(EffectivePermissionSet.Compute(grants).Has(Permissions.PersonalDataView));
    }

    [Fact]
    public void Scoped_assignment_limits_data_to_its_warehouse()
    {
        var set = EffectivePermissionSet.Compute([
            new PermissionGrant(Permissions.WarehouseDocumentCreate, PermissionLevel.Full, false, WarehouseId: 7),
        ]);
        var scope = set.ScopeOf(Permissions.WarehouseDocumentCreate);
        Assert.False(scope.All);
        Assert.True(scope.CoversWarehouse(7));
        Assert.False(scope.CoversWarehouse(8));
    }

    [Fact]
    public void Scoped_level_without_area_means_nothing()
    {
        var set = EffectivePermissionSet.Compute([
            new PermissionGrant(Permissions.WarehouseReportView, PermissionLevel.Scoped, false),
        ]);
        Assert.True(set.ScopeOf(Permissions.WarehouseReportView).IsEmpty);
    }

    [Fact]
    public void Administrator_cannot_grant_owner_role()
    {
        var admin = RoleSet(SystemRoles.Administrator);
        var ex = Assert.Throws<BusinessRuleException>(() => GrantPolicy.EnsureCanGrantAdministrative(admin, true));
        Assert.Equal("access.admin_grant_denied", ex.Code);
    }

    [Fact]
    public void Administrator_cannot_grant_rights_above_own()
    {
        var admin = RoleSet(SystemRoles.Administrator);
        // Старший кладовщик проводит складские документы — у администратора этого права нет.
        var ex = Assert.Throws<BusinessRuleException>(() =>
            GrantPolicy.EnsureNoEscalation(admin, RoleMatrixP0.PermissionsOf(SystemRoles.SeniorStorekeeper)));
        Assert.Equal("access.escalation", ex.Code);
    }

    [Fact]
    public void Owner_can_grant_senior_storekeeper_despite_separation_of_duties() =>
        GrantPolicy.EnsureNoEscalation(RoleSet(SystemRoles.Owner), RoleMatrixP0.PermissionsOf(SystemRoles.SeniorStorekeeper));

    [Fact]
    public void Administrator_can_grant_accountant()
    {
        var admin = RoleSet(SystemRoles.Administrator);
        GrantPolicy.EnsureNoEscalation(admin, RoleMatrixP0.PermissionsOf(SystemRoles.Accountant));
    }

    [Fact]
    public void Last_owner_cannot_be_removed() =>
        Assert.Equal("access.last_owner",
            Assert.Throws<BusinessRuleException>(() => GrantPolicy.EnsureOwnerRemains(0)).Code);

    [Fact]
    public void Deny_requires_reason()
    {
        var now = new DateTime(2026, 10, 9, 8, 0, 0, DateTimeKind.Utc);
        var ex = Assert.Throws<BusinessRuleException>(() =>
            RoleAssignment.ForPermission(1, 2, Permissions.PriceView, isDeny: true, grantedBy: 1, now, reason: " "));
        Assert.Equal("access.reason.required", ex.Code);
    }

    [Fact]
    public void Assignment_validity_period_is_checked()
    {
        var now = new DateTime(2026, 10, 9, 8, 0, 0, DateTimeKind.Utc);
        Assert.Throws<BusinessRuleException>(() =>
            RoleAssignment.ForRole(1, 2, 3, 1, now, null, validToUtc: now.AddMinutes(-1)));

        var temporary = RoleAssignment.ForRole(1, 2, 3, 1, now, "Поддержка", validToUtc: now.AddHours(2));
        Assert.True(temporary.IsActiveAt(now.AddHours(1)));
        Assert.False(temporary.IsActiveAt(now.AddHours(2)));
    }
}
