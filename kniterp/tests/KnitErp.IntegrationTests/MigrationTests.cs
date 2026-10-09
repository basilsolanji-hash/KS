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
    /// Обновление существующей базы: организация, созданная до справочников, получает стандартные единицы.
    /// Так обновится и база, которая уже работает (или у пользователя локально).
    /// </summary>
    [SqlFact]
    public async Task Upgrade_seeds_default_units_for_existing_organizations()
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
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await db.Database.EnsureDeletedAsync();
        }
    }
}
