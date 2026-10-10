using KnitErp.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.IntegrationTests;

public sealed class MigrationTests(SqlTestHost host) : IClassFixture<SqlTestHost>
{
    /// <summary>
    /// Изменили сущность или конфигурацию и забыли миграцию — тест падает без базы.
    /// Исправление: <c>dotnet ef migrations add &lt;Имя&gt; -p src/KnitErp.Infrastructure -o Persistence/Migrations</c>.
    /// </summary>
    [Fact]
    public void Model_has_no_changes_missing_from_migrations()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        Assert.False(db.Database.HasPendingModelChanges(), "Модель изменена, а миграция не создана.");
    }

    [SqlFact]
    public async Task Fresh_database_is_fully_migrated_into_kniterp_schema()
    {
        await using var db = host.NewDb();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.NotEmpty(await db.Database.GetAppliedMigrationsAsync());

        var historyInSchema = await db.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name = {KnitErpDbContext.Schema} AND t.name = {KnitErp.Infrastructure.DependencyInjection.MigrationsHistoryTable}")
            .SingleAsync();
        Assert.Equal(1, historyInSchema);

        var trigger = await db.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE name = 'tr_audit_log_immutable'")
            .SingleAsync();
        Assert.Equal(1, trigger);
    }

    /// <summary>
    /// Обновление существующей базы: организация, созданная до справочников, получает стандартные единицы и причины операций.
    /// Так обновится и база, которая уже работает (или у пользователя локально).
    /// </summary>
    [SqlFact]
    public async Task Upgrade_seeds_default_units_and_reasons_for_existing_organizations()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(SqlTestHost.EnvVar))
        {
            InitialCatalog = "kniterp_upgrade_" + Guid.NewGuid().ToString("N")[..12],
        }.ConnectionString;
        await using var db = new KnitErpDbContext(new DbContextOptionsBuilder<KnitErpDbContext>()
            .UseSqlServer(connection, KnitErp.Infrastructure.DependencyInjection.ConfigureSqlServer).Options);
        try
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261009072129_AddStructure");
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO [kniterp].[organizations] ([FullName], [ShortName], [Inn], [KppVerified], [TimeZoneId], [CurrencyCode], [IsArchived], [CreatedAtUtc])
                VALUES (N'Старая организация', N'Старая', '7707083893', 0, 'Europe/Moscow', 'RUB', 0, SYSUTCDATETIME());
                """);

            await migrator.MigrateAsync();

            var codes = await db.Units.Select(u => u.Code).OrderBy(c => c).ToListAsync();
            Assert.Equal(KnitErp.Domain.Catalog.UnitOfMeasure.Defaults.Select(u => u.Code).OrderBy(c => c), codes);
            Assert.Equal(KnitErp.Domain.Warehousing.OperationReason.Defaults.Count, await db.OperationReasons.CountAsync());
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await db.Database.EnsureDeletedAsync();
        }
    }

    /// <summary>
    /// D85: при обновлении прежняя привязка оплаты к заказу становится разноской на всю сумму — оплачено по заказам не меняется.
    /// Проверка: данные создаются, миграция разносок откатывается и применяется снова.
    /// </summary>
    [SqlFact]
    public async Task Upgrade_turns_payment_order_links_into_allocations()
    {
        long order, payment, purchase, supplierPayment;
        long organizationId;
        await using (var s0 = host.As(null, null))
        {
            var org = await s0.Organizations.CreateWithOwnerAsync(new KnitErp.Application.Organizations.CreateOrganizationCommand(
                "Тестовая организация миграции", "Тест", "7707083893", null, false, "Europe/Moscow", $"owner-{Guid.NewGuid():N}@test.local", "Владелец", "RU"));
            organizationId = org.OrganizationId;
            await using var s = host.As(org.OwnerUserId, org.OrganizationId);
            var units = await s.Catalog.ListUnitsAsync();
            var store = await s.Warehouses.CreateWarehouseAsync("Склад", null);
            var item = await s.Catalog.CreateItemAsync(new KnitErp.Application.Catalog.ItemCommand("СВ-1", "Свитер", KnitErp.Domain.Catalog.ItemType.Finished,
                units.Single(u => u.Symbol == "шт").Id, null));
            var customer = await s.Counterparties.CreateAsync(new KnitErp.Application.Catalog.CounterpartyCommand("ООО «Магазин»", null, null, false, true, null));
            var supplier = await s.Counterparties.CreateAsync(new KnitErp.Application.Catalog.CounterpartyCommand("ООО «Пряжа»", null, null, true, false, null));
            var day = new DateOnly(2026, 10, 9);
            order = await s.Sales.CreateOrderAsync(new KnitErp.Domain.Sales.SalesOrderHeader(day, customer, store, null, null, true, null));
            await s.Sales.SetOrderLineAsync(order, item, 2, 500m, 22m, (await s.Sales.GetOrderAsync(order)).RowVersion);
            await s.Sales.ConfirmOrderAsync(order, (await s.Sales.GetOrderAsync(order)).RowVersion);
            payment = await s.Sales.CreatePaymentAsync(day, customer, order, 700m, null);
            purchase = await s.Purchases.CreateOrderAsync(new KnitErp.Domain.Purchasing.PurchaseOrderHeader(day, supplier, store, null, null, true, null));
            await s.Purchases.SetOrderLineAsync(purchase, item, 1, 300m, null, (await s.Purchases.GetOrderAsync(purchase)).RowVersion);
            await s.Purchases.ConfirmOrderAsync(purchase, (await s.Purchases.GetOrderAsync(purchase)).RowVersion);
            supplierPayment = await s.Purchases.CreatePaymentAsync(day, supplier, purchase, 300m, null);
        }

        await using (var db = host.NewDb())
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261010201433_AddMoneyOperationsAndPurchaseLegalEntity");
            await migrator.MigrateAsync();
        }

        await using var db2 = host.NewDb();
        var allocation = await db2.CustomerPaymentAllocations.SingleAsync(a => a.OrganizationId == organizationId);
        Assert.Equal((payment, order, 700m, true), (allocation.PaymentId, allocation.OrderId, allocation.Amount, allocation.IsActive));
        var supplierAllocation = await db2.SupplierPaymentAllocations.SingleAsync(a => a.OrganizationId == organizationId);
        Assert.Equal((supplierPayment, purchase, 300m), (supplierAllocation.PaymentId, supplierAllocation.OrderId, supplierAllocation.Amount));
    }
}
