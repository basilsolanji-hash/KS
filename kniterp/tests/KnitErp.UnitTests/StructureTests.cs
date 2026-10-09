using KnitErp.Domain.Common;
using KnitErp.Domain.Structure;

namespace KnitErp.UnitTests;

public sealed class StructureTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 5, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Subtree_includes_nested_departments_only()
    {
        // 1 Производство → 2 Вязальный цех → 3 Участок А; 4 Склад отдельно.
        var tree = new DepartmentTree([(1, null), (2, 1), (3, 2), (4, null)]);

        Assert.Equal(new HashSet<long> { 2, 3 }, tree.WithDescendants([2]));
        Assert.Equal(new HashSet<long> { 1, 2, 3 }, tree.WithDescendants([1]));
        Assert.Empty(tree.WithDescendants([99]));
        Assert.Equal([2L, 1L], tree.AncestorsOf(3));
        Assert.Empty(tree.AncestorsOf(4));
    }

    [Fact]
    public void Department_cannot_be_moved_into_its_own_subtree()
    {
        var tree = new DepartmentTree([(1, null), (2, 1), (3, 2)]);
        var root = Department.Create(10, "Производство", null, Now);
        SetId(root, 1);

        var ex = Assert.Throws<BusinessRuleException>(() => root.MoveTo(3, tree.AncestorsOf(3)));
        Assert.Equal("structure.department.cycle", ex.Code);
        Assert.Equal("structure.department.cycle", Assert.Throws<BusinessRuleException>(() => root.MoveTo(1, [])).Code);
    }

    [Fact]
    public void Department_with_people_or_children_is_not_archived()
    {
        var d = Department.Create(10, "Цех", null, Now);
        Assert.Equal("structure.department.has_employees", Assert.Throws<BusinessRuleException>(() => d.Archive(0, 3, Now)).Code);
        Assert.Equal("structure.department.has_children", Assert.Throws<BusinessRuleException>(() => d.Archive(1, 0, Now)).Code);

        d.Archive(0, 0, Now);
        Assert.True(d.IsArchived);
        Assert.Equal("structure.department.archived", Assert.Throws<BusinessRuleException>(() => d.Rename("Новый")).Code);
    }

    [Fact]
    public void Rename_reports_change_and_ignores_same_name()
    {
        var d = Department.Create(10, "  Вязальный цех ", null, Now);
        Assert.Equal("Вязальный цех", d.Name);
        Assert.Null(d.Rename("Вязальный цех"));
        var change = d.Rename("Цех вязания");
        Assert.Equal(("Вязальный цех", "Цех вязания"), (change!.Before, change.After));
    }

    [Theory]
    [InlineData("")]
    [InlineData("12 34")]
    [InlineData("123456789012345678901")]
    public void Invalid_personnel_number_is_rejected(string number)
    {
        var ex = Assert.Throws<BusinessRuleException>(() => Hire(number));
        Assert.Equal("hr.employee.number_invalid", ex.Code);
    }

    [Fact]
    public void Personnel_number_is_normalized()
    {
        Assert.Equal("ВЦ-0042", Hire(" вц-0042 ").PersonnelNumber);
    }

    [Fact]
    public void Employee_goes_on_leave_returns_and_is_dismissed_once()
    {
        var e = Hire("0001");
        Assert.Equal(("Работает", "В отпуске"), Pair(e.SetOnLeave(true)));
        Assert.Equal(("В отпуске", "Работает"), Pair(e.SetOnLeave(false)));
        Assert.Equal("hr.employee.status_unchanged", Assert.Throws<BusinessRuleException>(() => e.SetOnLeave(false)).Code);

        Assert.Equal("hr.employee.dismiss_before_hire",
            Assert.Throws<BusinessRuleException>(() => e.Dismiss(new DateOnly(2025, 12, 31))).Code);
        e.Dismiss(new DateOnly(2026, 10, 9));
        Assert.Equal(EmploymentStatus.Dismissed, e.Status);
        Assert.Equal("hr.employee.dismissed", Assert.Throws<BusinessRuleException>(() => e.SetOnLeave(true)).Code);
        Assert.Equal("hr.employee.dismissed", Assert.Throws<BusinessRuleException>(() => e.LinkUser(5)).Code);
    }

    [Fact]
    public void Update_lists_only_changed_fields()
    {
        var e = Hire("0001");
        var changes = e.Update("0001", "Иванова", "Мария", "Петровна", departmentId: 2, positionId: 1);
        Assert.Equal(["FullName", "DepartmentId"], changes.Select(c => c.Field));
        Assert.Equal("Иванова Мария Петровна", e.FullName);
    }

    [Fact]
    public void Employee_links_only_one_account()
    {
        var e = Hire("0001");
        e.LinkUser(5);
        e.LinkUser(5);
        Assert.Equal("hr.employee.has_user", Assert.Throws<BusinessRuleException>(() => e.LinkUser(6)).Code);
    }

    private static Employee Hire(string number) =>
        Employee.Hire(10, number, "Иванова", "Мария", null, departmentId: 1, positionId: 1, new DateOnly(2026, 1, 15));

    private static (string?, string?) Pair(KnitErp.Domain.Organizations.FieldChange c) => (c.Before, c.After);

    private static void SetId(Department d, long id) =>
        typeof(Department).GetProperty(nameof(Department.Id))!.SetValue(d, id);
}
