using KnitErp.Application.Common;
using KnitErp.Application.Workspace;
using KnitErp.Infrastructure.External;
using KnitErp.Infrastructure.Persistence;
using KnitErp.Infrastructure.Spreadsheets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace KnitErp.Infrastructure;

public static class DependencyInjection
{
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public static IServiceCollection AddKnitErpInfrastructure(
        this IServiceCollection services, string connectionString, string? assistantApiKey = null, string? assistantModel = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddDbContext<KnitErpDbContext>(o => o.UseSqlServer(connectionString, ConfigureSqlServer));
        services.AddScoped<IKnitErpDbContext>(sp => sp.GetRequiredService<KnitErpDbContext>());
        services.AddSingleton<IKnitErpDbContextFactory>(new KnitErpDbContextFactory(connectionString));
        services.AddSingleton<ISpreadsheetFormat, ClosedXmlSpreadsheet>();

        // Внешние сервисы панели быстрого доступа: короткие таймауты, ошибка — не ошибка системы.
        services.AddHttpClient<IWeatherProvider, OpenMeteoWeatherProvider>(c => c.Timeout = TimeSpan.FromSeconds(4));
        services.AddSingleton<IAssistantModel>(new ClaudeAssistantModel(assistantApiKey, assistantModel));
        return services;
    }

    /// <summary>Общие настройки SQL Server для приложения, тестов и <c>dotnet ef</c>.</summary>
    public static void ConfigureSqlServer(SqlServerDbContextOptionsBuilder sql) =>
        sql.MigrationsHistoryTable(MigrationsHistoryTable, KnitErpDbContext.Schema);

    /// <summary>
    /// Применяет миграции EF Core (ТЗ §5.13). Применённая миграция не редактируется — изменение схемы
    /// оформляется новой миграцией.
    /// </summary>
    public static Task MigrateDatabaseAsync(KnitErpDbContext db, CancellationToken ct = default) =>
        db.Database.MigrateAsync(ct);

    /// <summary>Неприменённые миграции. Рабочая среда с ними не запускается: схему обновляют отдельным шагом выпуска.</summary>
    public static async Task<IReadOnlyList<string>> PendingMigrationsAsync(KnitErpDbContext db, CancellationToken ct = default) =>
        (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
}

/// <summary>Новый DbContext на операцию — для инструментов, работающих поверх открытой страницы.</summary>
public sealed class KnitErpDbContextFactory(string connectionString) : IKnitErpDbContextFactory
{
    private readonly DbContextOptions<KnitErpDbContext> _options = new DbContextOptionsBuilder<KnitErpDbContext>()
        .UseSqlServer(connectionString, DependencyInjection.ConfigureSqlServer).Options;

    public IKnitErpDbContext Create() => new KnitErpDbContext(_options);
}
