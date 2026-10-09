using KnitErp.Domain.Access;
using KnitErp.Domain.Audit;
using KnitErp.Domain.Organizations;
using KnitErp.Domain.Catalog;
using KnitErp.Domain.Common;
using KnitErp.Domain.Structure;
using KnitErp.Domain.Warehousing;
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
        b.Property(x => x.WebsiteUrl).HasMaxLength(Organization.WebsiteMaxLength);
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
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.WarehouseId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_role_assignments_warehouse");
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

internal sealed class UnitOfMeasureConfiguration : IEntityTypeConfiguration<UnitOfMeasure>
{
    public void Configure(EntityTypeBuilder<UnitOfMeasure> b)
    {
        b.ToTable("units", t => t.HasCheckConstraint("ck_units_precision", $"[Precision] BETWEEN 0 AND {UnitOfMeasure.MaxPrecision}"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.Code).HasMaxLength(UnitOfMeasure.CodeMaxLength).IsRequired();
        b.Property(x => x.Name).HasMaxLength(UnitOfMeasure.NameMaxLength).IsRequired();
        b.Property(x => x.Symbol).HasMaxLength(UnitOfMeasure.SymbolMaxLength).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("ak_units_org_id");
        b.HasIndex(x => new { x.OrganizationId, x.Code }).IsUnique().HasDatabaseName("ux_units_org_code");
    }
}

internal sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> b)
    {
        b.ToTable("items", t =>
        {
            t.HasCheckConstraint("ck_items_type", "[Type] IN (1, 2, 3, 4, 5, 6, 9)");
            t.HasCheckConstraint("ck_items_archived", "[IsArchived] = 0 OR [ArchivedAtUtc] IS NOT NULL");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.Code).HasMaxLength(Item.CodeMaxLength).IsRequired();
        b.Property(x => x.Name).HasMaxLength(Item.NameMaxLength).IsRequired();
        b.Property(x => x.Description).HasMaxLength(Item.DescriptionMaxLength);
        b.Property(x => x.Type).HasConversion<byte>();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("ak_items_org_id");
        b.HasOne<UnitOfMeasure>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.UnitId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_items_unit");
        // Код уникален навсегда, включая архив: старые документы не должны указывать на другую вещь.
        b.HasIndex(x => new { x.OrganizationId, x.Code }).IsUnique().HasDatabaseName("ux_items_org_code");
        b.HasIndex(x => new { x.OrganizationId, x.Type, x.Name }).HasDatabaseName("ix_items_org_type_name");
    }
}

internal sealed class SiteConfiguration : IEntityTypeConfiguration<Site>
{
    public void Configure(EntityTypeBuilder<Site> b)
    {
        b.ToTable("sites");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.Name).HasMaxLength(Site.NameMaxLength).IsRequired();
        b.Property(x => x.Address).HasMaxLength(Site.AddressMaxLength);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("ak_sites_org_id");
        b.HasIndex(x => new { x.OrganizationId, x.Name }).IsUnique().HasFilter("[IsArchived] = 0")
            .HasDatabaseName("ux_sites_org_name_active");
    }
}

internal sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> b)
    {
        b.ToTable("warehouses");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.Name).HasMaxLength(Warehouse.NameMaxLength).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("ak_warehouses_org_id");
        b.HasOne<Site>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.SiteId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_warehouses_site");
        b.HasIndex(x => new { x.OrganizationId, x.Name }).IsUnique().HasFilter("[IsArchived] = 0")
            .HasDatabaseName("ux_warehouses_org_name_active");
    }
}

internal sealed class CounterpartyConfiguration : IEntityTypeConfiguration<Counterparty>
{
    public void Configure(EntityTypeBuilder<Counterparty> b)
    {
        b.ToTable("counterparties", t =>
        {
            t.HasCheckConstraint("ck_counterparties_role", "[IsSupplier] = 1 OR [IsCustomer] = 1");
            t.HasCheckConstraint("ck_counterparties_inn", "[Inn] IS NULL OR ((LEN([Inn]) = 10 OR LEN([Inn]) = 12) AND [Inn] NOT LIKE '%[^0-9]%')");
            t.HasCheckConstraint("ck_counterparties_kpp", "[Kpp] IS NULL OR (LEN([Kpp]) = 9 AND LEN([Inn]) = 10)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.Name).HasMaxLength(Counterparty.NameMaxLength).IsRequired();
        b.Property(x => x.Inn).HasColumnType("varchar(12)");
        b.Property(x => x.Kpp).HasColumnType("char(9)");
        b.Property(x => x.Comment).HasMaxLength(Counterparty.CommentMaxLength);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("ak_counterparties_org_id");
        // Филиалы одной организации отличаются КПП, поэтому уникальна пара ИНН + КПП среди действующих.
        b.HasIndex(x => new { x.OrganizationId, x.Inn, x.Kpp }).IsUnique()
            .HasFilter("[Inn] IS NOT NULL AND [IsArchived] = 0").HasDatabaseName("ux_counterparties_org_inn_kpp_active");
        b.HasIndex(x => new { x.OrganizationId, x.Name }).HasDatabaseName("ix_counterparties_org_name");
    }
}

internal sealed class OperationReasonConfiguration : IEntityTypeConfiguration<OperationReason>
{
    public void Configure(EntityTypeBuilder<OperationReason> b)
    {
        b.ToTable("operation_reasons", t => t.HasCheckConstraint("ck_operation_reasons_kind", "[Kind] IN (1, 2, 3, 4)"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.Kind).HasConversion<byte>();
        b.Property(x => x.Name).HasMaxLength(OperationReason.NameMaxLength).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("ak_operation_reasons_org_id");
        b.HasIndex(x => new { x.OrganizationId, x.Kind, x.Name }).IsUnique().HasFilter("[IsArchived] = 0")
            .HasDatabaseName("ux_operation_reasons_org_kind_name_active");
    }
}

internal sealed class DocumentCounterConfiguration : IEntityTypeConfiguration<DocumentCounter>
{
    public void Configure(EntityTypeBuilder<DocumentCounter> b)
    {
        b.ToTable("document_counters", t => t.HasCheckConstraint("ck_document_counters_last", "[LastNumber] >= 0"));
        b.HasKey(x => new { x.OrganizationId, x.Kind });
        b.Property(x => x.Kind).HasMaxLength(DocumentCounter.KindMaxLength);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class OpeningBalanceConfiguration : IEntityTypeConfiguration<OpeningBalance>
{
    public void Configure(EntityTypeBuilder<OpeningBalance> b)
    {
        b.ToTable("opening_balances", t =>
        {
            t.HasCheckConstraint("ck_opening_balances_status", "[Status] IN (1, 2, 3, 9)");
            t.HasCheckConstraint("ck_opening_balances_approved",
                "([Status] = 3 AND [ApprovedByUserId] IS NOT NULL AND [ApprovedAtUtc] IS NOT NULL AND [ApprovedByUserId] <> [CreatedByUserId])"
                + " OR ([Status] <> 3 AND [ApprovedByUserId] IS NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.Number).HasMaxLength(30).IsRequired();
        b.Property(x => x.Status).HasConversion<byte>();
        b.Property(x => x.Comment).HasMaxLength(OpeningBalance.CommentMaxLength);
        b.Property(x => x.ReturnReason).HasMaxLength(OpeningBalance.ReasonMaxLength);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("ak_opening_balances_org_id");
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.WarehouseId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_opening_balances_warehouse");
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.OrganizationId, x.Number }).IsUnique().HasDatabaseName("ux_opening_balances_org_number");
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_lines");
    }
}

internal sealed class OpeningBalanceLineConfiguration : IEntityTypeConfiguration<OpeningBalanceLine>
{
    public void Configure(EntityTypeBuilder<OpeningBalanceLine> b)
    {
        b.ToTable("opening_balance_lines", t => t.HasCheckConstraint("ck_opening_balance_lines_quantity", "[Quantity] > 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.Quantity).HasColumnType("decimal(18,6)");
        b.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.DocumentId, x.ItemId }).IsUnique().HasDatabaseName("ux_opening_balance_lines_doc_item");
    }
}

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> b)
    {
        b.ToTable("stock_movements", t =>
        {
            t.HasTrigger("tr_stock_movements_immutable");
            t.HasTrigger("tr_stock_movements_closed_period");
            t.HasCheckConstraint("ck_stock_movements_quantity", "[Quantity] <> 0");
            t.HasCheckConstraint("ck_stock_movements_source", "[Source] IN (1, 2, 3, 4)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.Quantity).HasColumnType("decimal(18,6)");
        b.Property(x => x.Source).HasConversion<byte>();
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.WarehouseId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_stock_movements_warehouse");
        b.HasOne<Item>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ItemId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_stock_movements_item");
        b.HasIndex(x => new { x.OrganizationId, x.WarehouseId, x.ItemId }).HasDatabaseName("ix_stock_movements_org_wh_item");
        b.HasIndex(x => new { x.Source, x.SourceId }).HasDatabaseName("ix_stock_movements_source");
    }
}

internal sealed class StockDocumentConfiguration : IEntityTypeConfiguration<StockDocument>
{
    public void Configure(EntityTypeBuilder<StockDocument> b)
    {
        b.ToTable("stock_documents", t =>
        {
            t.HasCheckConstraint("ck_stock_documents_kind", "[Kind] IN (1, 2, 3)");
            t.HasCheckConstraint("ck_stock_documents_status", "[Status] IN (1, 2, 3, 9)");
            t.HasCheckConstraint("ck_stock_documents_target",
                "([Kind] = 3 AND [TargetWarehouseId] IS NOT NULL AND [TargetWarehouseId] <> [WarehouseId])"
                + " OR ([Kind] <> 3 AND [TargetWarehouseId] IS NULL)");
            t.HasCheckConstraint("ck_stock_documents_counterparty", "[Kind] = 1 OR [CounterpartyId] IS NULL");
            t.HasCheckConstraint("ck_stock_documents_posted",
                "([Status] IN (2, 3) AND [PostedByUserId] IS NOT NULL AND [PostedAtUtc] IS NOT NULL AND [ReasonId] IS NOT NULL)"
                + " OR ([Status] NOT IN (2, 3) AND [PostedByUserId] IS NULL)");
            t.HasCheckConstraint("ck_stock_documents_reversed",
                "([Status] = 3 AND [ReversedByUserId] IS NOT NULL AND [ReversedAtUtc] IS NOT NULL AND [ReversalReason] IS NOT NULL)"
                + " OR ([Status] <> 3 AND [ReversedByUserId] IS NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.Number).HasMaxLength(30).IsRequired();
        b.Property(x => x.Kind).HasConversion<byte>();
        b.Property(x => x.Status).HasConversion<byte>();
        b.Property(x => x.Comment).HasMaxLength(StockDocument.CommentMaxLength);
        b.Property(x => x.ReversalReason).HasMaxLength(StockDocument.ReasonMaxLength);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("ak_stock_documents_org_id");
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.WarehouseId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_stock_documents_warehouse");
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.TargetWarehouseId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_stock_documents_target_warehouse");
        b.HasOne<Counterparty>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.CounterpartyId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_stock_documents_counterparty");
        b.HasOne<OperationReason>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ReasonId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_stock_documents_reason");
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.PostedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.ReversedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.OrganizationId, x.Number }).IsUnique().HasDatabaseName("ux_stock_documents_org_number");
        b.HasIndex(x => new { x.OrganizationId, x.Kind, x.DocumentDate }).HasDatabaseName("ix_stock_documents_org_kind_date");
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_lines");
    }
}

internal sealed class StockDocumentLineConfiguration : IEntityTypeConfiguration<StockDocumentLine>
{
    public void Configure(EntityTypeBuilder<StockDocumentLine> b)
    {
        b.ToTable("stock_document_lines", t => t.HasCheckConstraint("ck_stock_document_lines_quantity", "[Quantity] > 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.Quantity).HasColumnType("decimal(18,6)");
        b.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.DocumentId, x.ItemId }).IsUnique().HasDatabaseName("ux_stock_document_lines_doc_item");
    }
}

internal sealed class InventoryCountConfiguration : IEntityTypeConfiguration<InventoryCount>
{
    public void Configure(EntityTypeBuilder<InventoryCount> b)
    {
        b.ToTable("inventory_counts", t =>
        {
            t.HasCheckConstraint("ck_inventory_counts_status", "[Status] IN (1, 2, 9)");
            t.HasCheckConstraint("ck_inventory_counts_posted",
                "([Status] = 2 AND [PostedByUserId] IS NOT NULL AND [PostedAtUtc] IS NOT NULL) OR ([Status] <> 2 AND [PostedByUserId] IS NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.Number).HasMaxLength(30).IsRequired();
        b.Property(x => x.Status).HasConversion<byte>();
        b.Property(x => x.Comment).HasMaxLength(InventoryCount.CommentMaxLength);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("ak_inventory_counts_org_id");
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.WarehouseId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_inventory_counts_warehouse");
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.PostedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.OrganizationId, x.Number }).IsUnique().HasDatabaseName("ux_inventory_counts_org_number");
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_lines");
    }
}

internal sealed class InventoryLineConfiguration : IEntityTypeConfiguration<InventoryLine>
{
    public void Configure(EntityTypeBuilder<InventoryLine> b)
    {
        b.ToTable("inventory_lines", t =>
        {
            t.HasCheckConstraint("ck_inventory_lines_counted", "[CountedQuantity] IS NULL OR [CountedQuantity] >= 0");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.BookQuantity).HasColumnType("decimal(18,6)");
        b.Property(x => x.CountedQuantity).HasColumnType("decimal(18,6)");
        b.Ignore(x => x.Difference);
        b.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.DocumentId, x.ItemId }).IsUnique().HasDatabaseName("ux_inventory_lines_doc_item");
    }
}

internal sealed class PeriodClosureConfiguration : IEntityTypeConfiguration<PeriodClosure>
{
    public void Configure(EntityTypeBuilder<PeriodClosure> b)
    {
        b.ToTable("period_closures");
        b.HasKey(x => x.OrganizationId);
        b.Property(x => x.OrganizationId).ValueGeneratedNever();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Organization>().WithOne().HasForeignKey<PeriodClosure>(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.ChangedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class UserToolDataConfiguration : IEntityTypeConfiguration<KnitErp.Domain.Workspace.UserToolData>
{
    public void Configure(EntityTypeBuilder<KnitErp.Domain.Workspace.UserToolData> b)
    {
        b.ToTable("user_tool_data", t => t.HasCheckConstraint("ck_user_tool_data_size", "LEN([Json]) <= 200000"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.Kind).HasMaxLength(KnitErp.Domain.Workspace.UserToolData.KindMaxLength).IsRequired();
        b.Property(x => x.Json).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.OrganizationId, x.UserId, x.Kind }).IsUnique().HasDatabaseName("ux_user_tool_data_org_user_kind");
    }
}

internal sealed class SupportTicketConfiguration : IEntityTypeConfiguration<KnitErp.Domain.Workspace.SupportTicket>
{
    public void Configure(EntityTypeBuilder<KnitErp.Domain.Workspace.SupportTicket> b)
    {
        b.ToTable("support_tickets", t =>
        {
            t.HasCheckConstraint("ck_support_tickets_status", "[Status] IN (1, 2, 3, 9)");
            t.HasCheckConstraint("ck_support_tickets_answer", "[Status] <> 3 OR ([Answer] IS NOT NULL AND [AnsweredByUserId] IS NOT NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.Number).HasMaxLength(30).IsRequired();
        b.Property(x => x.Subject).HasMaxLength(KnitErp.Domain.Workspace.SupportTicket.SubjectMaxLength).IsRequired();
        b.Property(x => x.Text).HasMaxLength(KnitErp.Domain.Workspace.SupportTicket.TextMaxLength).IsRequired();
        b.Property(x => x.Section).HasMaxLength(KnitErp.Domain.Workspace.SupportTicket.SectionMaxLength);
        b.Property(x => x.Answer).HasMaxLength(KnitErp.Domain.Workspace.SupportTicket.TextMaxLength);
        b.Property(x => x.Status).HasConversion<byte>();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.AnsweredByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.OrganizationId, x.Number }).IsUnique().HasDatabaseName("ux_support_tickets_org_number");
        b.HasIndex(x => new { x.OrganizationId, x.AuthorUserId, x.Status }).HasDatabaseName("ix_support_tickets_org_author_status");
    }
}
