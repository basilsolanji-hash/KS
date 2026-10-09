using KnitErp.Domain.Access;
using KnitErp.Domain.Audit;
using KnitErp.Domain.Organizations;
using KnitErp.Domain.Structure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnitErp.Infrastructure.Persistence;

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> b)
    {
        b.ToTable("organizations", t =>
        {
            t.HasCheckConstraint("ck_organizations_inn", "LEN([Inn]) = 10 AND [Inn] NOT LIKE '%[^0-9]%'");
            t.HasCheckConstraint("ck_organizations_kpp", "[Kpp] IS NULL OR LEN([Kpp]) = 9");
            t.HasCheckConstraint("ck_organizations_kpp_verified", "[KppVerified] = 0 OR [Kpp] IS NOT NULL");
            t.HasCheckConstraint("ck_organizations_currency", "LEN([CurrencyCode]) = 3");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.FullName).HasMaxLength(Organization.NameMaxLength).IsRequired();
        b.Property(x => x.ShortName).HasMaxLength(Organization.ShortNameMaxLength).IsRequired();
        b.Property(x => x.Inn).HasColumnType("char(10)").IsRequired();
        b.Property(x => x.Kpp).HasColumnType("char(9)");
        b.Property(x => x.ActualAddress).HasMaxLength(Organization.AddressMaxLength);
        b.Property(x => x.TimeZoneId).HasMaxLength(Organization.TimeZoneMaxLength).IsRequired();
        b.Property(x => x.CurrencyCode).HasColumnType("char(3)").IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.Ignore(x => x.PrintableKpp);
        b.HasIndex(x => x.Inn).IsUnique().HasDatabaseName("ux_organizations_inn");
    }
}

internal sealed class UserAccountConfiguration : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(EntityTypeBuilder<UserAccount> b)
    {
        b.ToTable("users", t =>
        {
            t.HasCheckConstraint("ck_users_status", "[Status] IN (1, 2, 4)");
            t.HasCheckConstraint("ck_users_failed_sign_in", "[FailedSignInCount] >= 0");
            t.HasCheckConstraint("ck_users_2fa_key", "[TwoFactorEnabled] = 0 OR [AuthenticatorKey] IS NOT NULL");
            t.HasCheckConstraint("ck_users_setup_token",
                "([SetupTokenHash] IS NULL AND [SetupTokenExpiresAtUtc] IS NULL) OR ([SetupTokenHash] IS NOT NULL AND [SetupTokenExpiresAtUtc] IS NOT NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.Email).HasMaxLength(UserAccount.EmailMaxLength).IsRequired();
        b.Property(x => x.NormalizedEmail).HasMaxLength(UserAccount.EmailMaxLength).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(UserAccount.DisplayNameMaxLength).IsRequired();
        b.Property(x => x.Status).HasConversion<byte>();
        b.Property(x => x.PasswordHash).HasColumnType("varchar(256)");
        b.Property(x => x.AuthenticatorKey).HasColumnType("varchar(64)");
        b.Property(x => x.SetupTokenHash).HasColumnType("binary(32)");
        b.Property(x => x.RowVersion).IsRowVersion();
        b.Ignore(x => x.HasPassword);
        b.HasIndex(x => x.NormalizedEmail).IsUnique().HasDatabaseName("ux_users_normalized_email");
        b.HasIndex(x => x.SetupTokenHash).IsUnique().HasFilter("[SetupTokenHash] IS NOT NULL").HasDatabaseName("ux_users_setup_token");
    }
}

internal sealed class OrganizationMemberConfiguration : IEntityTypeConfiguration<OrganizationMember>
{
    public void Configure(EntityTypeBuilder<OrganizationMember> b)
    {
        b.ToTable("organization_members", t =>
        {
            t.HasCheckConstraint("ck_organization_members_status", "[Status] IN (1, 2)");
            t.HasCheckConstraint("ck_organization_members_blocked", "[Status] <> 2 OR [BlockedAtUtc] IS NOT NULL");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.Status).HasConversion<byte>();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.OrganizationId, x.UserId }).IsUnique().HasDatabaseName("ux_organization_members_org_user");
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.ToTable("roles");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.Code).HasMaxLength(64).IsRequired();
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Ignore(x => x.IsAdministrative);
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.OrganizationId, x.Code }).IsUnique().HasDatabaseName("ux_roles_org_code");
        b.HasMany(x => x.Permissions).WithOne().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Permissions).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_permissions");
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> b)
    {
        b.ToTable("role_permissions", t => t.HasCheckConstraint("ck_role_permissions_level", "[Level] IN (1, 2, 3, 10, 11)"));
        b.HasKey(x => new { x.RoleId, x.PermissionCode });
        b.Property(x => x.PermissionCode).HasMaxLength(100).IsRequired();
        b.Property(x => x.Level).HasConversion<byte>();
    }
}

internal sealed class RoleAssignmentConfiguration : IEntityTypeConfiguration<RoleAssignment>
{
    public void Configure(EntityTypeBuilder<RoleAssignment> b)
    {
        b.ToTable("role_assignments", t =>
        {
            // Либо роль, либо индивидуальное право — ровно одно (ТЗ §4.11).
            t.HasCheckConstraint("ck_role_assignments_target",
                "([RoleId] IS NOT NULL AND [PermissionCode] IS NULL) OR ([RoleId] IS NULL AND [PermissionCode] IS NOT NULL)");
            t.HasCheckConstraint("ck_role_assignments_deny", "[IsDeny] = 0 OR [PermissionCode] IS NOT NULL");
            t.HasCheckConstraint("ck_role_assignments_deny_reason", "[IsDeny] = 0 OR [Reason] IS NOT NULL");
            t.HasCheckConstraint("ck_role_assignments_period", "[ValidToUtc] IS NULL OR [ValidToUtc] > [ValidFromUtc]");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.PermissionCode).HasMaxLength(100);
        b.Property(x => x.Reason).HasMaxLength(RoleAssignment.ReasonMaxLength);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.GrantedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.RevokedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Department>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.DepartmentId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_role_assignments_department");
        b.HasIndex(x => new { x.OrganizationId, x.UserId }).HasDatabaseName("ix_role_assignments_org_user");
    }
}

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> b)
    {
        b.ToTable("audit_log", t => t.HasTrigger("tr_audit_log_immutable"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.Action).HasMaxLength(AuditEntry.ActionMaxLength).IsRequired();
        b.Property(x => x.EntityType).HasMaxLength(AuditEntry.EntityTypeMaxLength).IsRequired();
        b.Property(x => x.EntityId).HasMaxLength(AuditEntry.EntityIdMaxLength);
        b.Property(x => x.Before).HasMaxLength(AuditEntry.ValueMaxLength);
        b.Property(x => x.After).HasMaxLength(AuditEntry.ValueMaxLength);
        b.Property(x => x.Reason).HasMaxLength(AuditEntry.ReasonMaxLength);
        b.Property(x => x.CorrelationId).HasMaxLength(64);
        b.HasIndex(x => new { x.OrganizationId, x.OccurredAtUtc }).HasDatabaseName("ix_audit_log_org_time");
    }
}

internal sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> b)
    {
        b.ToTable("departments", t =>
        {
            t.HasCheckConstraint("ck_departments_archived", "[IsArchived] = 0 OR [ArchivedAtUtc] IS NOT NULL");
            t.HasCheckConstraint("ck_departments_not_self_parent", "[ParentId] IS NULL OR [ParentId] <> [Id]");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.Name).HasMaxLength(Department.NameMaxLength).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        // Составные ключи (организация, id): ссылка на подразделение другой организации невозможна в самой базе.
        b.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("ak_departments_org_id");
        b.HasOne<Department>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ParentId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_departments_parent");
        // Два действующих подразделения с одним названием в организации путают учёт; архивные не мешают.
        b.HasIndex(x => new { x.OrganizationId, x.Name }).IsUnique().HasFilter("[IsArchived] = 0")
            .HasDatabaseName("ux_departments_org_name_active");
    }
}

internal sealed class PositionConfiguration : IEntityTypeConfiguration<Position>
{
    public void Configure(EntityTypeBuilder<Position> b)
    {
        b.ToTable("positions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.Name).HasMaxLength(Position.NameMaxLength).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("ak_positions_org_id");
        b.HasIndex(x => new { x.OrganizationId, x.Name }).IsUnique().HasFilter("[IsArchived] = 0")
            .HasDatabaseName("ux_positions_org_name_active");
    }
}

internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> b)
    {
        b.ToTable("employees", t =>
        {
            t.HasCheckConstraint("ck_employees_status", "[Status] IN (1, 2, 9)");
            t.HasCheckConstraint("ck_employees_dismissed",
                "([Status] = 9 AND [DismissedOn] IS NOT NULL AND [DismissedOn] >= [HiredOn]) OR ([Status] <> 9 AND [DismissedOn] IS NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.PersonnelNumber).HasMaxLength(Employee.PersonnelNumberMaxLength).IsRequired();
        b.Property(x => x.LastName).HasMaxLength(Employee.NamePartMaxLength).IsRequired();
        b.Property(x => x.FirstName).HasMaxLength(Employee.NamePartMaxLength).IsRequired();
        b.Property(x => x.MiddleName).HasMaxLength(Employee.NamePartMaxLength);
        b.Property(x => x.Status).HasConversion<byte>();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.Ignore(x => x.FullName);
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Department>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.DepartmentId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_employees_department");
        b.HasOne<Position>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PositionId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_employees_position");
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.OrganizationId, x.PersonnelNumber }).IsUnique().HasDatabaseName("ux_employees_org_number");
        b.HasIndex(x => new { x.OrganizationId, x.UserId }).IsUnique().HasFilter("[UserId] IS NOT NULL")
            .HasDatabaseName("ux_employees_org_user");
        b.HasIndex(x => new { x.OrganizationId, x.DepartmentId }).HasDatabaseName("ix_employees_org_department");
    }
}
