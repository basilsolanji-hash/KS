using KnitErp.Application.Common;
using KnitErp.Domain.Access;
using KnitErp.Domain.Audit;
using KnitErp.Domain.Organizations;
using KnitErp.Domain.Catalog;
using KnitErp.Domain.Common;
using KnitErp.Domain.Structure;
using KnitErp.Domain.Warehousing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace KnitErp.Infrastructure.Persistence;

public sealed class KnitErpDbContext(DbContextOptions<KnitErpDbContext> options) : DbContext(options), IKnitErpDbContext
{
    public const string Schema = "kniterp";

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<UnitOfMeasure> Units => Set<UnitOfMeasure>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Counterparty> Counterparties => Set<Counterparty>();
    public DbSet<OperationReason> OperationReasons => Set<OperationReason>();
    public DbSet<OpeningBalance> OpeningBalances => Set<OpeningBalance>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<DocumentCounter> DocumentCounters => Set<DocumentCounter>();

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        Database.BeginTransactionAsync(cancellationToken);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardImmutableAudit();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardImmutableAudit();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KnitErpDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Время — UTC в datetime2(3) (ТЗ §5.13, §5.15).
        configurationBuilder.Properties<DateTime>().HaveColumnType("datetime2(3)");
        configurationBuilder.Properties<DateTime?>().HaveColumnType("datetime2(3)");
    }

    /// <summary>
    /// Журнал аудита (ТЗ §4.8 KA3644) и движения склада только добавляются. Второй рубеж — триггеры в базе.
    /// </summary>
    private void GuardImmutableAudit()
    {
        foreach (var entry in ChangeTracker.Entries<AuditEntry>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException("Журнал аудита неизменяем: изменение и удаление записей запрещены.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<StockMovement>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException("Движения склада неизменяемы: исправление — новым документом.");
            }
        }
    }
}
