using KnitErp.Infrastructure.Persistence;
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
}
