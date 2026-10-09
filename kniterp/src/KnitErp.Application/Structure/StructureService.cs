using KnitErp.Application.Access;
using KnitErp.Application.Common;
using KnitErp.Domain.Access;
using KnitErp.Domain.Audit;
using KnitErp.Domain.Common;
using KnitErp.Domain.Organizations;
using KnitErp.Domain.Structure;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.Application.Structure;

public sealed record DepartmentDto(
    long Id, long? ParentId, string Name, int Depth, string Path, int EmployeeCount, bool IsArchived, byte[] RowVersion);

public sealed record PositionDto(long Id, string Name, int EmployeeCount, bool IsArchived, byte[] RowVersion);

public sealed record StructureDto(IReadOnlyList<DepartmentDto> Departments, IReadOnlyList<PositionDto> Positions, bool CanEdit, bool IsScoped);

/// <summary>Область данных по подразделениям: null — вся организация, иначе видимые подразделения (с вложенными).</summary>
internal static class StructureScope
{
    public static async Task<HashSet<long>?> VisibleDepartmentsAsync(
        IKnitErpDbContext db, AccessContext ctx, string permissionCode, CancellationToken ct)
    {
        switch (ctx.Permissions.LevelOf(permissionCode))
        {
            case PermissionLevel.Full or PermissionLevel.ReadOnly:
                return null;
            case PermissionLevel.Scoped:
                var tree = await LoadTreeAsync(db, ctx.OrganizationId, ct);
                return tree.WithDescendants(ctx.Permissions.ScopeOf(permissionCode).DepartmentIds);
            default:
                return [];
        }
    }

    public static async Task<DepartmentTree> LoadTreeAsync(IKnitErpDbContext db, long organizationId, CancellationToken ct) =>
        new((await db.Departments.AsNoTracking().Where(d => d.OrganizationId == organizationId)
            .Select(d => new { d.Id, d.ParentId }).ToListAsync(ct)).Select(d => (d.Id, d.ParentId)));
}

/// <summary>Подразделения и должности организации (ТЗ: структура фабрики, MVP 1.0).</summary>
public sealed class StructureService(IKnitErpDbContext db, IAccessGuard guard, ICurrentUser currentUser, IClock clock)
{
    public async Task<StructureDto> GetAsync(bool includeArchived = false, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.DepartmentView, ct);
        var visible = await StructureScope.VisibleDepartmentsAsync(db, ctx, Permissions.DepartmentView, ct);

        var departments = await db.Departments.AsNoTracking()
            .Where(d => d.OrganizationId == ctx.OrganizationId && (includeArchived || !d.IsArchived))
            .ToListAsync(ct);
        var counts = await db.Employees.AsNoTracking()
            .Where(e => e.OrganizationId == ctx.OrganizationId && e.Status != EmploymentStatus.Dismissed)
            .GroupBy(e => e.DepartmentId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var positions = await db.Positions.AsNoTracking()
            .Where(p => p.OrganizationId == ctx.OrganizationId && (includeArchived || !p.IsArchived))
            .OrderBy(p => p.Name).ToListAsync(ct);
        var positionCounts = await db.Employees.AsNoTracking()
            .Where(e => e.OrganizationId == ctx.OrganizationId && e.Status != EmploymentStatus.Dismissed)
            .GroupBy(e => e.PositionId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var rows = Flatten(departments, counts).Where(d => visible is null || visible.Contains(d.Id)).ToList();
        return new StructureDto(
            rows,
            positions.Select(p => new PositionDto(p.Id, p.Name, positionCounts.GetValueOrDefault(p.Id), p.IsArchived, p.RowVersion)).ToList(),
            ctx.Permissions.Has(Permissions.DepartmentEdit),
            visible is not null);
    }

    public async Task<long> CreateDepartmentAsync(string name, long? parentId, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.DepartmentEdit, ct);
        if (parentId is { } p)
        {
            await RequireActiveDepartmentAsync(ctx, p, ct);
        }

        var department = Department.Create(ctx.OrganizationId, name, parentId, clock.UtcNow);
        await EnsureDepartmentNameFreeAsync(ctx, department.Name, null, ct);
        db.Departments.Add(department);
        await db.SaveChangesAsync(ct);
        Audit(ctx, AuditActions.DepartmentCreated, nameof(Department), department.Id, null, department.Name, null);
        await db.SaveChangesAsync(ct);
        return department.Id;
    }

    public async Task RenameDepartmentAsync(long id, string name, byte[] rowVersion, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.DepartmentEdit, ct);
        var department = await LoadDepartmentAsync(ctx, id, rowVersion, ct);
        var change = department.Rename(name);
        if (change is null)
        {
            return;
        }

        await EnsureDepartmentNameFreeAsync(ctx, department.Name, id, ct);
        Audit(ctx, AuditActions.DepartmentChanged, nameof(Department), id, change.Before, change.After, "Название");
        await SaveAsync(ct);
    }

    public async Task MoveDepartmentAsync(long id, long? parentId, byte[] rowVersion, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.DepartmentEdit, ct);
        var department = await LoadDepartmentAsync(ctx, id, rowVersion, ct);
        string? parentName = null;
        var ancestors = new List<long>();
        if (parentId is { } p)
        {
            parentName = (await RequireActiveDepartmentAsync(ctx, p, ct)).Name;
            ancestors = (await StructureScope.LoadTreeAsync(db, ctx.OrganizationId, ct)).AncestorsOf(p);
        }

        var oldParentName = department.ParentId is { } old
            ? await db.Departments.Where(d => d.Id == old).Select(d => d.Name).SingleAsync(ct)
            : null;
        if (department.MoveTo(parentId, ancestors) is null)
        {
            return;
        }

        Audit(ctx, AuditActions.DepartmentChanged, nameof(Department), id, oldParentName ?? "верхний уровень",
            parentName ?? "верхний уровень", "Перенос");
        await SaveAsync(ct);
    }

    public async Task ArchiveDepartmentAsync(long id, byte[] rowVersion, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.DepartmentEdit, ct);
        var department = await LoadDepartmentAsync(ctx, id, rowVersion, ct);
        var children = await db.Departments.CountAsync(d => d.OrganizationId == ctx.OrganizationId && d.ParentId == id && !d.IsArchived, ct);
        var employees = await db.Employees.CountAsync(e => e.OrganizationId == ctx.OrganizationId && e.DepartmentId == id
                                                           && e.Status != EmploymentStatus.Dismissed, ct);
        department.Archive(children, employees, clock.UtcNow);
        Audit(ctx, AuditActions.DepartmentArchived, nameof(Department), id, department.Name, "в архиве", null);
        await SaveAsync(ct);
    }

    public async Task<long> CreatePositionAsync(string name, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.DepartmentEdit, ct);
        var position = Position.Create(ctx.OrganizationId, name, clock.UtcNow);
        await EnsurePositionNameFreeAsync(ctx, position.Name, null, ct);
        db.Positions.Add(position);
        await db.SaveChangesAsync(ct);
        Audit(ctx, AuditActions.PositionCreated, nameof(Position), position.Id, null, position.Name, null);
        await db.SaveChangesAsync(ct);
        return position.Id;
    }

    public async Task RenamePositionAsync(long id, string name, byte[] rowVersion, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.DepartmentEdit, ct);
        var position = await LoadPositionAsync(ctx, id, rowVersion, ct);
        var change = position.Rename(name);
        if (change is null)
        {
            return;
        }

        await EnsurePositionNameFreeAsync(ctx, position.Name, id, ct);
        Audit(ctx, AuditActions.PositionChanged, nameof(Position), id, change.Before, change.After, "Название");
        await SaveAsync(ct);
    }

    public async Task ArchivePositionAsync(long id, byte[] rowVersion, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.DepartmentEdit, ct);
        var position = await LoadPositionAsync(ctx, id, rowVersion, ct);
        var employees = await db.Employees.CountAsync(e => e.OrganizationId == ctx.OrganizationId && e.PositionId == id
                                                           && e.Status != EmploymentStatus.Dismissed, ct);
        position.Archive(employees);
        Audit(ctx, AuditActions.PositionArchived, nameof(Position), id, position.Name, "в архиве", null);
        await SaveAsync(ct);
    }

    /// <summary>Дерево в порядке обхода: родитель, затем его вложенные по алфавиту; путь — для выпадающих списков.</summary>
    private static IEnumerable<DepartmentDto> Flatten(List<Department> all, Dictionary<long, int> counts)
    {
        var ids = all.Select(d => d.Id).ToHashSet();
        var byParent = all.ToLookup(d => d.ParentId is { } p && ids.Contains(p) ? p : (long?)null);

        IEnumerable<DepartmentDto> Walk(long? parentId, int depth, string prefix)
        {
            foreach (var d in byParent[parentId].OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var path = prefix.Length == 0 ? d.Name : $"{prefix} / {d.Name}";
                yield return new DepartmentDto(d.Id, d.ParentId, d.Name, depth, path, counts.GetValueOrDefault(d.Id), d.IsArchived, d.RowVersion);
                foreach (var child in Walk(d.Id, depth + 1, path))
                {
                    yield return child;
                }
            }
        }

        return Walk(null, 0, string.Empty);
    }

    private async Task<Department> RequireActiveDepartmentAsync(AccessContext ctx, long id, CancellationToken ct)
    {
        var d = await db.Departments.SingleOrDefaultAsync(x => x.Id == id && x.OrganizationId == ctx.OrganizationId, ct)
                ?? throw new NotFoundException("Подразделение");
        return d.IsArchived ? throw new BusinessRuleException("structure.department.archived", "Подразделение в архиве.") : d;
    }

    private async Task<Department> LoadDepartmentAsync(AccessContext ctx, long id, byte[] rowVersion, CancellationToken ct)
    {
        var d = await db.Departments.SingleOrDefaultAsync(x => x.Id == id && x.OrganizationId == ctx.OrganizationId, ct)
                ?? throw new NotFoundException("Подразделение");
        return d.RowVersion.AsSpan().SequenceEqual(rowVersion) ? d : throw new ConcurrencyConflictException();
    }

    private async Task<Position> LoadPositionAsync(AccessContext ctx, long id, byte[] rowVersion, CancellationToken ct)
    {
        var p = await db.Positions.SingleOrDefaultAsync(x => x.Id == id && x.OrganizationId == ctx.OrganizationId, ct)
                ?? throw new NotFoundException("Должность");
        return p.RowVersion.AsSpan().SequenceEqual(rowVersion) ? p : throw new ConcurrencyConflictException();
    }

    private async Task EnsureDepartmentNameFreeAsync(AccessContext ctx, string name, long? exceptId, CancellationToken ct)
    {
        if (await db.Departments.AnyAsync(d => d.OrganizationId == ctx.OrganizationId && !d.IsArchived && d.Name == name && d.Id != exceptId, ct))
        {
            throw new BusinessRuleException("structure.department.duplicate", $"Подразделение «{name}» уже есть.");
        }
    }

    private async Task EnsurePositionNameFreeAsync(AccessContext ctx, string name, long? exceptId, CancellationToken ct)
    {
        if (await db.Positions.AnyAsync(p => p.OrganizationId == ctx.OrganizationId && !p.IsArchived && p.Name == name && p.Id != exceptId, ct))
        {
            throw new BusinessRuleException("structure.position.duplicate", $"Должность «{name}» уже есть.");
        }
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException();
        }
    }

    private void Audit(AccessContext ctx, string action, string entityType, long id, string? before, string? after, string? reason) =>
        db.AuditEntries.Add(AuditEntry.Create(clock.UtcNow, ctx.OrganizationId, ctx.UserId, action, entityType, id.ToString(),
            before, after, reason, currentUser.CorrelationId));
}
