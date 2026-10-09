using KnitErp.Application.Common;
using KnitErp.Domain.Access;
using KnitErp.Domain.Audit;
using KnitErp.Domain.Organizations;
using KnitErp.Domain.Catalog;
using KnitErp.Domain.Common;
using KnitErp.Domain.Structure;
using KnitErp.Domain.Warehousing;
using KnitErp.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace KnitErp.Infrastructure.Persistence;

public sealed class KnitErpDbContext(DbContextOptions<KnitErpDbContext> options) : DbContext(options), IKnitErpDbContext, IDataProtectionKeyContext
{
    public const string Schema = "kniterp";

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();
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
    public DbSet<StockDocument> StockDocuments => Set<StockDocument>();
    public DbSet<InventoryCount> InventoryCounts => Set<InventoryCount>();
    public DbSet<PeriodClosure> PeriodClosures => Set<PeriodClosure>();
    public DbSet<KnitErp.Domain.Workspace.UserToolData> UserToolData => Set<KnitErp.Domain.Workspace.UserToolData>();
    public DbSet<KnitErp.Domain.Workspace.SupportTicket> SupportTickets => Set<KnitErp.Domain.Workspace.SupportTicket>();

    /// <summary>Ключи защиты cookie и антиподделки; XML ключа зашифрован мастер-ключом (MasterKeyXmlEncryptor).</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<DocumentCounter> DocumentCounters => Set<DocumentCounter>();

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        Database.BeginTransactionAsync(cancellationToken);

    public async Task LockWarehousesAsync(IEnumerable<long> warehouseIds, CancellationToken cancellationToken = default)
    {
        // Одинаковый порядок захвата у всех запросов — без взаимных блокировок.
        foreach (var id in warehouseIds.Distinct().Order())
        {
            await LockAsync($"kniterp.stock.warehouse.{id}", cancellationToken);
        }
    }

    public async Task LockAsync(string resource, CancellationToken cancellationToken = default)
    {
        if (Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Блокировка действует только внутри транзакции.");
        }

        // sp_getapplock при таймауте не бросает ошибку, а возвращает отрицательный код — превращаем его в ошибку.
        await Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
            IF @result < 0 THROW 51003, N'Не удалось дождаться блокировки, повторите действие.', 1;
            """, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardImmutableAudit();
        if (IntegrityChain.HasChained(this))
        {
            // Подпись цепочки требует блокировки и чтения последней подписи — это делает только асинхронное сохранение.
            throw new InvalidOperationException("Журнал аудита и движения склада сохраняются только через SaveChangesAsync.");
        }

        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <summary>
    /// Новые записи журнала аудита и движений подписываются в той же транзакции, что и сохраняются: если транзакции
    /// нет, она открывается здесь — подпись и вставка не могут разойтись.
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardImmutableAudit();
        if (!IntegrityChain.HasChained(this))
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        if (Database.CurrentTransaction is not null)
        {
            await IntegrityChain.SealAsync(this, cancellationToken);
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        await using var tx = await Database.BeginTransactionAsync(cancellationToken);
        await IntegrityChain.SealAsync(this, cancellationToken);
        var saved = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return saved;
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
