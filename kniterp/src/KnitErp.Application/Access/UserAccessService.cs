using KnitErp.Application.Common;
using KnitErp.Domain.Access;
using KnitErp.Domain.Audit;
using KnitErp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.Application.Access;

public sealed record UserRowDto(
    long UserId,
    string DisplayName,
    string Email,
    UserStatus AccountStatus,
    MembershipStatus MembershipStatus,
    IReadOnlyList<string> RoleCodes,
    IReadOnlyList<string> RoleNames);

public sealed record InviteUserCommand(string Email, string DisplayName, string RoleCode, string? Reason);

public sealed record ChangeRoleCommand(long UserId, string NewRoleCode, string? Reason);

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

        return members.Select(x =>
        {
            var roles = roleLinks.Where(r => r.UserId == x.u.Id).ToList();
            return new UserRowDto(x.u.Id, x.u.DisplayName, x.u.Email, x.u.Status, x.m.Status,
                roles.Select(r => r.Code).ToList(), roles.Select(r => r.Name).ToList());
        }).ToList();
    }

    /// <summary>
    /// Приглашение пользователя с ролью. Привязка к карточке сотрудника появится вместе с модулем «Сотрудники» (§4.10 п.1).
    /// </summary>
    public async Task<long> InviteAsync(InviteUserCommand cmd, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.UserManage, ct);
        var role = await FindRoleAsync(ctx, cmd.RoleCode, ct);
        await EnsureMayGrantRoleAsync(ctx, role, cmd.Reason, ct);

        var now = clock.UtcNow;
        await using var tx = await db.BeginTransactionAsync(ct);

        var normalized = (cmd.Email ?? string.Empty).Trim().ToUpperInvariant();
        var user = await db.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
        if (user is null)
        {
            user = UserAccount.Invite(cmd.Email ?? string.Empty, cmd.DisplayName, now);
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }
        else if (await db.OrganizationMembers.AnyAsync(m => m.OrganizationId == ctx.OrganizationId && m.UserId == user.Id, ct))
        {
            throw new BusinessRuleException("access.user.exists", "Этот пользователь уже есть в организации.");
        }

        db.OrganizationMembers.Add(OrganizationMember.Join(ctx.OrganizationId, user.Id, now));
        db.RoleAssignments.Add(RoleAssignment.ForRole(ctx.OrganizationId, user.Id, role.Id, ctx.UserId, now, cmd.Reason));
        db.AuditEntries.Add(Audit(ctx, AuditActions.UserInvited, user.Id, null, user.Email, null));
        db.AuditEntries.Add(Audit(ctx, role.IsAdministrative ? AuditActions.AdminRoleGranted : AuditActions.RoleGranted,
            user.Id, "нет доступа", role.Name, cmd.Reason));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return user.Id;
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

        if (currentRoles.Count == 1 && currentRoles[0].Id == newRole.Id)
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

        db.RoleAssignments.Add(RoleAssignment.ForRole(ctx.OrganizationId, cmd.UserId, newRole.Id, ctx.UserId, now, cmd.Reason));
        var user = await db.Users.SingleAsync(u => u.Id == cmd.UserId, ct);
        user.RotateSecurityStamp();

        db.AuditEntries.Add(Audit(ctx, newRole.IsAdministrative ? AuditActions.AdminRoleGranted : AuditActions.RoleGranted,
            cmd.UserId, string.Join(", ", currentRoles.Select(r => r.Name)), newRole.Name, cmd.Reason));
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

        roles = roles.OrderBy(r =>
        {
            var i = SystemRoles.Ordered.ToList().IndexOf(r.Code);
            return i < 0 ? int.MaxValue : i;
        }).ThenBy(r => r.Name).ToList();

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
