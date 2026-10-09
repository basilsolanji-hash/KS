using KnitErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KnitErp.Web.Security;

/// <summary>
/// /health для мониторинга: база доступна и схема актуальна. Подробности (строка подключения, имена миграций)
/// наружу не отдаются — только состояние; причина пишется в лог.
/// </summary>
public sealed class DatabaseHealthCheck(KnitErpDbContext db, ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await db.Database.CanConnectAsync(cancellationToken))
            {
                return HealthCheckResult.Unhealthy("База данных недоступна.");
            }

            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).Count();
            return pending == 0 ? HealthCheckResult.Healthy() : HealthCheckResult.Degraded("Есть неприменённые миграции.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Проверка базы для /health не прошла");
            return HealthCheckResult.Unhealthy("База данных недоступна.");
        }
    }
}
