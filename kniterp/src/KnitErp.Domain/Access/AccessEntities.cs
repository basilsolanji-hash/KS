using KnitErp.Domain.Common;

namespace KnitErp.Domain.Access;

public enum UserStatus : byte
{
    Invited = 1,
    Active = 2,
    Archived = 4,
}

/// <summary>
/// Учётная запись человека. Не принадлежит одной организации: доступ к организациям даётся назначениями.
/// Удаления нет — только блокировка и архив (ТЗ §4.10 п.5).
/// </summary>
public sealed class UserAccount
{
    public const int EmailMaxLength = 254;
    public const int DisplayNameMaxLength = 200;

    private UserAccount()
    {
    }

    public long Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string NormalizedEmail { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public UserStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Меняется при блокировке и смене прав — по нему сервер закрывает старые сессии.</summary>
    public Guid SecurityStamp { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public static UserAccount Invite(string email, string displayName, DateTime nowUtc)
    {
        var e = email?.Trim() ?? string.Empty;
        if (e.Length is 0 or > EmailMaxLength || e.IndexOf('@') <= 0 || e.LastIndexOf('.') < e.IndexOf('@'))
        {
            throw new BusinessRuleException("user.email.invalid", "Укажите корректный email.");
        }

        var name = displayName?.Trim() ?? string.Empty;
        if (name.Length is 0 or > DisplayNameMaxLength)
        {
            throw new BusinessRuleException("user.name.invalid", "Укажите имя пользователя (до 200 символов).");
        }

        return new UserAccount
        {
            Email = e,
            NormalizedEmail = e.ToUpperInvariant(),
            DisplayName = name,
            Status = UserStatus.Invited,
            CreatedAtUtc = nowUtc,
            SecurityStamp = Guid.NewGuid(),
        };
    }

    public void Activate()
    {
        if (Status != UserStatus.Invited)
        {
            throw new BusinessRuleException("user.status.transition", "Активировать можно только приглашённого пользователя.");
        }

        Status = UserStatus.Active;
    }

    /// <summary>Смена штампа закрывает все сессии пользователя (блокировка, смена прав).</summary>
    public void RotateSecurityStamp() => SecurityStamp = Guid.NewGuid();
}

public enum MembershipStatus : byte
{
    Active = 1,
    Blocked = 2,
}

/// <summary>
/// Участие пользователя в организации. Блокировка действует в пределах организации:
/// у одного человека может быть несколько организаций (ORG 01), и администратор одной не блокирует его в другой.
/// </summary>
public sealed class OrganizationMember
{
    private OrganizationMember()
    {
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long UserId { get; private set; }
    public MembershipStatus Status { get; private set; }
    public DateTime JoinedAtUtc { get; private set; }
    public DateTime? BlockedAtUtc { get; private set; }
    public long? BlockedByUserId { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static OrganizationMember Join(long organizationId, long userId, DateTime nowUtc) => new()
    {
        OrganizationId = organizationId,
        UserId = userId,
        Status = MembershipStatus.Active,
        JoinedAtUtc = nowUtc,
    };

    public void Block(long byUserId, DateTime nowUtc)
    {
        if (Status == MembershipStatus.Blocked)
        {
            throw new BusinessRuleException("member.already_blocked", "Пользователь уже заблокирован.");
        }

        Status = MembershipStatus.Blocked;
        BlockedAtUtc = nowUtc;
        BlockedByUserId = byUserId;
    }

    public void Unblock()
    {
        if (Status != MembershipStatus.Blocked)
        {
            throw new BusinessRuleException("member.not_blocked", "Пользователь не заблокирован.");
        }

        Status = MembershipStatus.Active;
        BlockedAtUtc = null;
        BlockedByUserId = null;
    }
}

/// <summary>Роль организации — шаблон набора прав (ТЗ §4.6 п.4).</summary>
public sealed class Role
{
    private readonly List<RolePermission> _permissions = [];

    private Role()
    {
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool IsSystem { get; private set; }
    public IReadOnlyCollection<RolePermission> Permissions => _permissions;

    public bool IsAdministrative => IsSystem && SystemRoles.IsAdministrative(Code);

    /// <summary>Системная роль P0, наполненная из матрицы ТЗ §4.8.</summary>
    public static Role CreateSystem(long organizationId, string code)
    {
        var role = new Role
        {
            OrganizationId = organizationId,
            Code = code,
            Name = SystemRoles.NameOf(code),
            IsSystem = true,
        };

        foreach (var (permission, level) in RoleMatrixP0.PermissionsOf(code))
        {
            role._permissions.Add(new RolePermission(permission, level));
        }

        return role;
    }
}

public sealed class RolePermission
{
    private RolePermission()
    {
    }

    public RolePermission(string permissionCode, PermissionLevel level)
    {
        PermissionCode = permissionCode;
        Level = level;
    }

    public long RoleId { get; private set; }
    public string PermissionCode { get; private set; } = string.Empty;
    public PermissionLevel Level { get; private set; }
}

/// <summary>
/// Назначение прав (ТЗ §4.11 KA3654): роль или индивидуальное право, разрешение или запрет, область и срок.
/// </summary>
public sealed class RoleAssignment
{
    public const int ReasonMaxLength = 500;

    private RoleAssignment()
    {
    }

    public Guid Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long UserId { get; private set; }
    public long? RoleId { get; private set; }
    public string? PermissionCode { get; private set; }
    public bool IsDeny { get; private set; }
    public long? DepartmentId { get; private set; }
    public long? WarehouseId { get; private set; }
    public DateTime ValidFromUtc { get; private set; }
    public DateTime? ValidToUtc { get; private set; }
    public long GrantedByUserId { get; private set; }
    public DateTime GrantedAtUtc { get; private set; }
    public string? Reason { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public long? RevokedByUserId { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool IsActiveAt(DateTime nowUtc) =>
        RevokedAtUtc is null && ValidFromUtc <= nowUtc && (ValidToUtc is null || nowUtc < ValidToUtc);

    public static RoleAssignment ForRole(
        long organizationId, long userId, long roleId, long grantedBy, DateTime nowUtc,
        string? reason, DateTime? validToUtc = null, long? departmentId = null, long? warehouseId = null)
    {
        var a = new RoleAssignment
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            UserId = userId,
            RoleId = roleId,
            DepartmentId = departmentId,
            WarehouseId = warehouseId,
            ValidFromUtc = nowUtc,
            ValidToUtc = validToUtc,
            GrantedByUserId = grantedBy,
            GrantedAtUtc = nowUtc,
            Reason = NormalizeReason(reason),
        };
        GrantPolicy.EnsureValidPeriod(a.ValidFromUtc, a.ValidToUtc);
        return a;
    }

    public static RoleAssignment ForPermission(
        long organizationId, long userId, string permissionCode, bool isDeny, long grantedBy, DateTime nowUtc,
        string? reason, DateTime? validToUtc = null, long? departmentId = null, long? warehouseId = null)
    {
        if (!Permissions.IsKnown(permissionCode))
        {
            throw new BusinessRuleException("access.permission.unknown", $"Неизвестное право: {permissionCode}.");
        }

        var a = new RoleAssignment
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            UserId = userId,
            PermissionCode = permissionCode,
            IsDeny = isDeny,
            DepartmentId = departmentId,
            WarehouseId = warehouseId,
            ValidFromUtc = nowUtc,
            ValidToUtc = validToUtc,
            GrantedByUserId = grantedBy,
            GrantedAtUtc = nowUtc,
            Reason = NormalizeReason(reason),
        };
        GrantPolicy.EnsureValidPeriod(a.ValidFromUtc, a.ValidToUtc);
        GrantPolicy.EnsureReason(isDeny || Permissions.IsAdministrative(permissionCode), a.Reason);
        return a;
    }

    public void Revoke(long byUserId, DateTime nowUtc)
    {
        if (RevokedAtUtc is not null)
        {
            throw new BusinessRuleException("access.assignment.revoked", "Назначение уже отозвано.");
        }

        RevokedAtUtc = nowUtc;
        RevokedByUserId = byUserId;
    }

    private static string? NormalizeReason(string? reason)
    {
        var r = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (r is { Length: > ReasonMaxLength })
        {
            throw new BusinessRuleException("access.reason.too_long", $"Причина длиннее {ReasonMaxLength} символов.");
        }

        return r;
    }
}
