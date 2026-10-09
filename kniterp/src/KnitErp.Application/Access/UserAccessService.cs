using KnitErp.Application.Common;
using KnitErp.Domain.Access;
using KnitErp.Domain.Audit;
using KnitErp.Domain.Common;
using KnitErp.Domain.Structure;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.Application.Access;

public sealed record UserRowDto(
    long UserId,
    string DisplayName,
    string Email,
    UserStatus AccountStatus,
    MembershipStatus MembershipStatus,
    IReadOnlyList<string> RoleCodes,
    IReadOnlyList<string> RoleNames,
    bool HasPassword,
    bool TwoFactorEnabled,
    string ScopeText,
    bool IsCurrentUser,
    long? DepartmentId = null);

/// <summary>Строка итоговых прав «на человеческом языке» для блока «Было → Станет».</summary>
public sealed record PermissionLineDto(string PermissionCode, string Label, PermissionLevel Level)
{
    public string Text => Level switch
    {
        PermissionLevel.Scoped => $"{Label} — своя область",
        PermissionLevel.ReadOnly => $"{Label} — только чтение",
        PermissionLevel.OwnOnly => $"{Label} — только свои данные",
        _ => Label,
    };
}

public sealed record AccessPreviewDto(
    IReadOnlyList<PermissionLineDto> Before,
    IReadOnlyList<PermissionLineDto> After,
    string ScopeText,
    bool IsAdministrative,
    bool ReasonRequired);

/// <summary>Роль в форме. Allowed = false: выдать её текущий пользователь не может (сервер всё равно проверит).</summary>
public sealed record RoleOptionDto(string Code, string Name, bool IsAdministrative, bool Allowed, string? DeniedReason);

/// <summary>Кто вошёл и что ему доступно — для меню. Защитой не является: каждое действие проверяет сервер.</summary>
public sealed record CurrentAccessDto(
    long UserId,
    string DisplayName,
    string OrganizationName,
    string TimeZoneId,
    IReadOnlyList<string> RoleCodes,
    IReadOnlyList<string> RoleNames,
    IReadOnlySet<string> Granted,
    IReadOnlySet<string> OwnOnly)
{
    public bool Has(string permissionCode) => Granted.Contains(permissionCode);

    public bool HasOwnOnly(string permissionCode) => Granted.Contains(permissionCode) || OwnOnly.Contains(permissionCode);
}

/// <summary>
/// Приглашение. DepartmentId — область роли (только это подразделение и вложенные); EmployeeId — карточка сотрудника,
/// к которой привязывается учётная запись (имя тогда берётся из карточки).
/// </summary>
public sealed record InviteUserCommand(
    string Email, string DisplayName, string RoleCode, string? Reason, long? DepartmentId = null, long? EmployeeId = null);

public sealed record ChangeRoleCommand(long UserId, string NewRoleCode, string? Reason, long? DepartmentId = null);

/// <summary>
/// Итог приглашения. Токен ссылки показывается администратору один раз и не хранится.
/// Пользователю, у которого уже есть пароль (участник другой организации), ссылка не нужна.
/// </summary>
public sealed record InvitationResult(long UserId, string? SetupToken, DateTime? ExpiresAtUtc);

public sealed record RoleMatrixDto(IReadOnlyList<RoleColumnDto> Roles, IReadOnlyList<MatrixRowDto> Rows);

public sealed record RoleColumnDto(long RoleId, string Code, string Name);

public sealed record MatrixRowDto(string PermissionCode, string Label, IReadOnlyList<PermissionLevel> Levels);

/// <summary>Пользователи, роли и назначения прав организации (ТЗ §4.6–4.14).</summary>
public sealed class UserAccessService(IKnitErpDbContext db, IAccessGuard guard, ICurrentUser currentUser, IClock clock)
{
    public async Task<IReadOnlyList<UserRowDto>> ListUsersAsync(CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.UserView, ct);
        var now = clock.UtcNow;

        var members = await db.OrganizationMembers.AsNoTracking()
            .Where(m => m.OrganizationId == ctx.OrganizationId)
            .Join(db.Users.AsNoTracking(), m => m.UserId, u => u.Id, (m, u) => new { m, u })
            .OrderBy(x => x.u.DisplayName)
            .ToListAsync(ct);

        var userIds = members.Select(x => x.u.Id).ToList();
        var roleLinks = await db.RoleAssignments.AsNoTracking()
            .Where(a => a.OrganizationId == ctx.OrganizationId && userIds.Contains(a.UserId) && a.RoleId != null
                        && a.RevokedAtUtc == null && a.ValidFromUtc <= now && (a.ValidToUtc == null || a.ValidToUtc > now))
            .Join(db.Roles.AsNoTracking(), a => a.RoleId, r => (long?)r.Id, (a, r) => new { a.UserId, r.Code, r.Name })
            .ToListAsync(ct);

        var scopes = await db.RoleAssignments.AsNoTracking()
            .Where(a => a.OrganizationId == ctx.OrganizationId && userIds.Contains(a.UserId) && a.RoleId != null
                        && a.RevokedAtUtc == null && a.ValidFromUtc <= now && (a.ValidToUtc == null || a.ValidToUtc > now))
            .Select(a => new { a.UserId, a.DepartmentId })
            .ToListAsync(ct);
        var departmentNames = await DepartmentNamesAsync(ctx.OrganizationId, scopes.Select(a => a.DepartmentId), ct);

        return members.Select(x =>
        {
            var roles = roleLinks.Where(r => r.UserId == x.u.Id).ToList();
            var userScopes = scopes.Where(a => a.UserId == x.u.Id).ToList();
            return new UserRowDto(x.u.Id, x.u.DisplayName, x.u.Email, x.u.Status, x.m.Status,
                roles.Select(r => r.Code).ToList(), roles.Select(r => r.Name).ToList(),
                x.u.PasswordHash != null, x.u.TwoFactorEnabled,
                DescribeScope(userScopes.Select(a => a.DepartmentId), departmentNames),
                x.u.Id == ctx.UserId,
                userScopes.Select(a => a.DepartmentId).FirstOrDefault(id => id is not null));
        }).ToList();
    }

    /// <summary>Что видно в меню. Источник — те же итоговые права, что проверяет сервер.</summary>
    public async Task<CurrentAccessDto> GetCurrentAccessAsync(CancellationToken ct = default)
    {
        var ctx = await guard.CurrentAsync(ct);
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == ctx.UserId, ct);
        var org = await db.Organizations.AsNoTracking().SingleAsync(o => o.Id == ctx.OrganizationId, ct);
        var roles = await ActiveRolesAsync(ctx.OrganizationId, ctx.UserId, ct);
        var ownOnly = Permissions.All.Where(c => !ctx.Permissions.Has(c) && ctx.Permissions.HasOwnOnly(c)).ToHashSet();
        return new CurrentAccessDto(ctx.UserId, user.DisplayName, org.ShortName, org.TimeZoneId, roles.Select(r => r.Code).ToList(), roles.Select(r => r.Name).ToList(),
            ctx.Permissions.GrantedCodes.ToHashSet(), ownOnly);
    }

    /// <summary>Роли организации для формы: какие текущий пользователь вправе выдать и почему нет.</summary>
    public async Task<IReadOnlyList<RoleOptionDto>> ListGrantableRolesAsync(CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.UserManage, ct);
        var roles = await db.Roles.AsNoTracking().Where(r => r.OrganizationId == ctx.OrganizationId).Include(r => r.Permissions).ToListAsync(ct);

        return OrderRoles(roles).Select(role =>
        {
            try
            {
                GrantPolicy.EnsureCanGrantAdministrative(ctx.Permissions, role.IsAdministrative);
                GrantPolicy.EnsureNoEscalation(ctx.Permissions, role.Permissions.Select(p => (p.PermissionCode, p.Level)));
                return new RoleOptionDto(role.Code, role.Name, role.IsAdministrative, true, null);
            }
            catch (BusinessRuleException ex)
            {
                return new RoleOptionDto(role.Code, role.Name, role.IsAdministrative, false, ex.Message);
            }
        }).ToList();
    }

    /// <summary>
    /// «Было → Станет» до сохранения: итоговые права пользователя сейчас и после назначения роли.
    /// Индивидуальные права и запреты сохраняются — смена роли их не трогает.
    /// </summary>
    public async Task<AccessPreviewDto> PreviewRoleAsync(long? userId, string roleCode, long? departmentId = null, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.UserManage, ct);
        var role = await FindRoleAsync(ctx, roleCode, ct);
        var before = EffectivePermissionSet.Empty;
        if (userId is { } id)
        {
            await RequireMemberAsync(ctx, id, ct);
            before = await guard.LoadAsync(id, ctx.OrganizationId, ct);
        }

        var after = await guard.PreviewWithRoleAsync(userId, ctx.OrganizationId, role, ct);
        var names = departmentId is { } dep ? new Dictionary<long, string> { [dep] = (await RequireScopeDepartmentAsync(ctx, dep, ct)).Name } : [];
        return new AccessPreviewDto(Lines(before), Lines(after), DescribeScope([departmentId], names), role.IsAdministrative, role.IsAdministrative);
    }

    private static List<PermissionLineDto> Lines(EffectivePermissionSet set) =>
        RoleMatrixP0.PermissionCodes
            .Select(code => set.Has(code)
                ? new PermissionLineDto(code, Permissions.Describe(code), set.LevelOf(code))
                : set.HasOwnOnly(code) ? new PermissionLineDto(code, Permissions.Describe(code), PermissionLevel.OwnOnly) : null)
            .OfType<PermissionLineDto>()
            .ToList();

    /// <summary>Назначение без подразделения действует на всю организацию; с подразделением — на него и вложенные.</summary>
    private static string DescribeScope(IEnumerable<long?> departmentIds, IReadOnlyDictionary<long, string> names)
    {
        var ids = departmentIds.ToList();
        if (ids.Count == 0)
        {
            return "—";
        }

        if (ids.Any(id => id is null))
        {
            return "Вся организация";
        }

        return string.Join(", ", ids.Select(id => names.TryGetValue(id!.Value, out var n) ? $"«{n}» и вложенные" : "подразделение").Distinct());
    }

    private async Task<Dictionary<long, string>> DepartmentNamesAsync(long organizationId, IEnumerable<long?> ids, CancellationToken ct)
    {
        var list = ids.OfType<long>().Distinct().ToList();
        return list.Count == 0
            ? []
            : await db.Departments.AsNoTracking().Where(d => d.OrganizationId == organizationId && list.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => d.Name, ct);
    }

    /// <summary>Подразделение-область должно быть своей организации и не в архиве.</summary>
    private async Task<Department> RequireScopeDepartmentAsync(AccessContext ctx, long departmentId, CancellationToken ct)
    {
        var d = await db.Departments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == departmentId && x.OrganizationId == ctx.OrganizationId, ct)
                ?? throw new NotFoundException("Подразделение");
        return d.IsArchived ? throw new BusinessRuleException("structure.department.archived", "Подразделение в архиве.") : d;
    }

    private static List<Role> OrderRoles(List<Role> roles) =>
        roles.OrderBy(r =>
        {
            var i = SystemRoles.Ordered.ToList().IndexOf(r.Code);
            return i < 0 ? int.MaxValue : i;
        }).ThenBy(r => r.Name).ToList();

    private async Task<List<Role>> ActiveRolesAsync(long organizationId, long userId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var roles = await db.RoleAssignments.AsNoTracking()
            .Where(a => a.OrganizationId == organizationId && a.UserId == userId && a.RoleId != null
                        && a.RevokedAtUtc == null && a.ValidFromUtc <= now && (a.ValidToUtc == null || a.ValidToUtc > now))
            .Join(db.Roles.AsNoTracking(), a => a.RoleId, r => (long?)r.Id, (a, r) => r)
            .ToListAsync(ct);
        return OrderRoles(roles);
    }

    /// <summary>
    /// Приглашение пользователя с ролью и ссылкой установки пароля на 72 часа.
    /// С карточкой сотрудника учётная запись привязывается к ней (§4.10 п.1).
    /// </summary>
    public async Task<InvitationResult> InviteAsync(InviteUserCommand cmd, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.UserManage, ct);
        var role = await FindRoleAsync(ctx, cmd.RoleCode, ct);
        await EnsureMayGrantRoleAsync(ctx, role, cmd.Reason, ct);
        var scopeName = cmd.DepartmentId is { } scopeDep ? (await RequireScopeDepartmentAsync(ctx, scopeDep, ct)).Name : null;

        Employee? employee = null;
        if (cmd.EmployeeId is { } employeeId)
        {
            employee = await db.Employees.SingleOrDefaultAsync(e => e.Id == employeeId && e.OrganizationId == ctx.OrganizationId, ct)
                       ?? throw new NotFoundException("Сотрудник");
        }

        var displayName = string.IsNullOrWhiteSpace(cmd.DisplayName) && employee is not null ? employee.FullName : cmd.DisplayName;
        var now = clock.UtcNow;
        await using var tx = await db.BeginTransactionAsync(ct);

        var normalized = (cmd.Email ?? string.Empty).Trim().ToUpperInvariant();
        var user = await db.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
        if (user is null)
        {
            user = UserAccount.Invite(cmd.Email ?? string.Empty, displayName, now);
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }
        else if (await db.OrganizationMembers.AnyAsync(m => m.OrganizationId == ctx.OrganizationId && m.UserId == user.Id, ct))
        {
            throw new BusinessRuleException("access.user.exists", "Этот пользователь уже есть в организации.");
        }

        var token = user.HasPassword ? null : user.IssueSetupToken(now);
        db.OrganizationMembers.Add(OrganizationMember.Join(ctx.OrganizationId, user.Id, now));
        db.RoleAssignments.Add(RoleAssignment.ForRole(ctx.OrganizationId, user.Id, role.Id, ctx.UserId, now, cmd.Reason, departmentId: cmd.DepartmentId));
        db.AuditEntries.Add(Audit(ctx, AuditActions.UserInvited, user.Id, null, user.Email, null));
        if (employee is not null)
        {
            employee.LinkUser(user.Id);
            db.AuditEntries.Add(AuditEntry.Create(now, ctx.OrganizationId, ctx.UserId, AuditActions.EmployeeLinkedToUser, nameof(Employee),
                employee.Id.ToString(), null, user.Email, null, currentUser.CorrelationId));
        }
        db.AuditEntries.Add(Audit(ctx, role.IsAdministrative ? AuditActions.AdminRoleGranted : AuditActions.RoleGranted,
            user.Id, "нет доступа", scopeName is null ? role.Name : $"{role.Name} («{scopeName}»)", cmd.Reason));
        if (token is not null)
        {
            db.AuditEntries.Add(Audit(ctx, AuditActions.InvitationIssued, user.Id, null, $"до {user.SetupTokenExpiresAtUtc:yyyy-MM-dd HH:mm} UTC", null));
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new InvitationResult(user.Id, token, user.SetupTokenExpiresAtUtc);
    }

    /// <summary>Новая ссылка приглашения, если старая истекла или потерялась. Старая перестаёт действовать.</summary>
    public async Task<InvitationResult> ReissueInvitationAsync(long userId, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.UserManage, ct);
        await RequireMemberAsync(ctx, userId, ct);
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        var token = user.IssueSetupToken(clock.UtcNow);
        db.AuditEntries.Add(Audit(ctx, AuditActions.InvitationIssued, userId, null, $"до {user.SetupTokenExpiresAtUtc:yyyy-MM-dd HH:mm} UTC", "повторная ссылка"));
        await db.SaveChangesAsync(ct);
        return new InvitationResult(userId, token, user.SetupTokenExpiresAtUtc);
    }

    /// <summary>
    /// Сброс двухфакторной аутентификации, если пользователь потерял телефон. Сессии пользователя закрываются,
    /// при следующем входе он подключит аутентификатор заново. Сброс у Владельца или Администратора — только Владелец.
    /// </summary>
    public async Task ResetTwoFactorAsync(long userId, string? reason, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.UserManage, ct);
        await RequireMemberAsync(ctx, userId, ct);
        if (userId == ctx.UserId)
        {
            throw new BusinessRuleException("access.2fa_reset.self", "Свою двухфакторную аутентификацию сбрасывает другой администратор.");
        }

        GrantPolicy.EnsureReason(required: true, reason);
        var targetIsAdmin = await HasActiveRoleAsync(ctx.OrganizationId, userId, SystemRoles.Owner, ct)
                            || await HasActiveRoleAsync(ctx.OrganizationId, userId, SystemRoles.Administrator, ct);
        GrantPolicy.EnsureCanGrantAdministrative(ctx.Permissions, targetIsAdmin);

        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        user.ResetTwoFactor();
        db.AuditEntries.Add(Audit(ctx, AuditActions.TwoFactorReset, userId, "включена", "сброшена", reason));
        await db.SaveChangesAsync(ct);
    }

    public async Task ChangeRoleAsync(ChangeRoleCommand cmd, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.UserManage, ct);
        await RequireMemberAsync(ctx, cmd.UserId, ct);
        var newRole = await FindRoleAsync(ctx, cmd.NewRoleCode, ct);
        var now = clock.UtcNow;

        var current = await db.RoleAssignments
            .Where(a => a.OrganizationId == ctx.OrganizationId && a.UserId == cmd.UserId && a.RoleId != null
                        && a.RevokedAtUtc == null && (a.ValidToUtc == null || a.ValidToUtc > now))
            .ToListAsync(ct);
        var currentRoleIds = current.Select(a => a.RoleId!.Value).ToList();
        var currentRoles = await db.Roles.Where(r => currentRoleIds.Contains(r.Id)).ToListAsync(ct);

        var scopeName = cmd.DepartmentId is { } scopeDep ? (await RequireScopeDepartmentAsync(ctx, scopeDep, ct)).Name : null;

        if (currentRoles.Count == 1 && currentRoles[0].Id == newRole.Id && current.Count == 1 && current[0].DepartmentId == cmd.DepartmentId)
        {
            throw new BusinessRuleException("access.role.unchanged", "Роль не изменилась.");
        }

        // Снять административную роль — тоже управление административными правами.
        var touchesAdmin = newRole.IsAdministrative || currentRoles.Any(r => r.IsAdministrative);
        GrantPolicy.EnsureCanGrantAdministrative(ctx.Permissions, touchesAdmin);
        await EnsureMayGrantRoleAsync(ctx, newRole, cmd.Reason, ct);

        if (currentRoles.Any(r => r.Code == SystemRoles.Owner) && newRole.Code != SystemRoles.Owner)
        {
            var owners = await CountActiveOwnersAsync(ctx.OrganizationId, excludeUserId: cmd.UserId, ct);
            GrantPolicy.EnsureOwnerRemains(owners);
        }

        foreach (var a in current)
        {
            a.Revoke(ctx.UserId, now);
        }

        db.RoleAssignments.Add(RoleAssignment.ForRole(ctx.OrganizationId, cmd.UserId, newRole.Id, ctx.UserId, now, cmd.Reason,
            departmentId: cmd.DepartmentId));
        var user = await db.Users.SingleAsync(u => u.Id == cmd.UserId, ct);
        user.RotateSecurityStamp();

        db.AuditEntries.Add(Audit(ctx, newRole.IsAdministrative ? AuditActions.AdminRoleGranted : AuditActions.RoleGranted,
            cmd.UserId, string.Join(", ", currentRoles.Select(r => r.Name)),
            scopeName is null ? newRole.Name : $"{newRole.Name} («{scopeName}»)", cmd.Reason));
        await db.SaveChangesAsync(ct);
    }

    public async Task BlockAsync(long userId, string? reason, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.UserManage, ct);
        var member = await RequireMemberAsync(ctx, userId, ct);
        if (userId == ctx.UserId)
        {
            throw new BusinessRuleException("access.block.self", "Нельзя заблокировать самого себя.");
        }

        var isOwner = await HasActiveRoleAsync(ctx.OrganizationId, userId, SystemRoles.Owner, ct);
        if (isOwner)
        {
            GrantPolicy.EnsureCanGrantAdministrative(ctx.Permissions, isAdministrative: true);
            GrantPolicy.EnsureOwnerRemains(await CountActiveOwnersAsync(ctx.OrganizationId, excludeUserId: userId, ct));
        }

        var now = clock.UtcNow;
        member.Block(ctx.UserId, now);
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        user.RotateSecurityStamp();
        db.AuditEntries.Add(Audit(ctx, AuditActions.UserBlocked, userId, "активен", "заблокирован", reason));
        await db.SaveChangesAsync(ct);
    }

    public async Task UnblockAsync(long userId, string? reason, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.UserManage, ct);
        var member = await RequireMemberAsync(ctx, userId, ct);
        member.Unblock();
        db.AuditEntries.Add(Audit(ctx, AuditActions.UserUnblocked, userId, "заблокирован", "активен", reason));
        await db.SaveChangesAsync(ct);
    }

    public async Task<RoleMatrixDto> GetRoleMatrixAsync(CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.UserView, ct);
        var roles = await db.Roles.AsNoTracking()
            .Where(r => r.OrganizationId == ctx.OrganizationId)
            .Include(r => r.Permissions)
            .ToListAsync(ct);

        roles = OrderRoles(roles);

        var rows = RoleMatrixP0.PermissionCodes.Select(code => new MatrixRowDto(
            code,
            Permissions.Describe(code),
            roles.Select(r => r.Permissions.FirstOrDefault(p => p.PermissionCode == code)?.Level ?? PermissionLevel.None).ToList()))
            .ToList();

        return new RoleMatrixDto(roles.Select(r => new RoleColumnDto(r.Id, r.Code, r.Name)).ToList(), rows);
    }

    private async Task EnsureMayGrantRoleAsync(AccessContext ctx, Role role, string? reason, CancellationToken ct)
    {
        try
        {
            GrantPolicy.EnsureCanGrantAdministrative(ctx.Permissions, role.IsAdministrative);
            GrantPolicy.EnsureNoEscalation(ctx.Permissions, role.Permissions.Select(p => (p.PermissionCode, p.Level)));
        }
        catch (BusinessRuleException ex)
        {
            db.AuditEntries.Add(Audit(ctx, AuditActions.AccessDenied, null, null, ex.Code, role.Code));
            await db.SaveChangesAsync(ct);
            throw;
        }

        GrantPolicy.EnsureReason(role.IsAdministrative, reason);
    }

    private async Task<Role> FindRoleAsync(AccessContext ctx, string roleCode, CancellationToken ct) =>
        await db.Roles.Include(r => r.Permissions)
            .SingleOrDefaultAsync(r => r.OrganizationId == ctx.OrganizationId && r.Code == roleCode, ct)
        ?? throw new NotFoundException("Роль");

    /// <summary>Пользователь чужой организации — «не найдено» (ТЗ §4.12).</summary>
    private async Task<OrganizationMember> RequireMemberAsync(AccessContext ctx, long userId, CancellationToken ct) =>
        await db.OrganizationMembers.SingleOrDefaultAsync(m => m.OrganizationId == ctx.OrganizationId && m.UserId == userId, ct)
        ?? throw new NotFoundException("Пользователь");

    private async Task<bool> HasActiveRoleAsync(long organizationId, long userId, string roleCode, CancellationToken ct)
    {
        var now = clock.UtcNow;
        return await db.RoleAssignments
            .Where(a => a.OrganizationId == organizationId && a.UserId == userId && a.RevokedAtUtc == null
                        && a.ValidFromUtc <= now && (a.ValidToUtc == null || a.ValidToUtc > now))
            .Join(db.Roles, a => a.RoleId, r => (long?)r.Id, (a, r) => r.Code)
            .AnyAsync(code => code == roleCode, ct);
    }

    private async Task<int> CountActiveOwnersAsync(long organizationId, long excludeUserId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        return await db.RoleAssignments
            .Where(a => a.OrganizationId == organizationId && a.UserId != excludeUserId && a.RevokedAtUtc == null
                        && a.ValidFromUtc <= now && (a.ValidToUtc == null || a.ValidToUtc > now))
            .Join(db.Roles.Where(r => r.Code == SystemRoles.Owner), a => a.RoleId, r => (long?)r.Id, (a, r) => a.UserId)
            .Join(db.OrganizationMembers.Where(m => m.OrganizationId == organizationId && m.Status == MembershipStatus.Active),
                userId => userId, m => m.UserId, (userId, m) => userId)
            .Join(db.Users.Where(u => u.Status == UserStatus.Active), userId => userId, u => u.Id, (userId, u) => userId)
            .Distinct()
            .CountAsync(ct);
    }

    private AuditEntry Audit(AccessContext ctx, string action, long? targetUserId, string? before, string? after, string? reason) =>
        AuditEntry.Create(clock.UtcNow, ctx.OrganizationId, ctx.UserId, action, nameof(UserAccount),
            targetUserId?.ToString(), before, after, reason, currentUser.CorrelationId);
}
