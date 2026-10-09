using KnitErp.Application.Common;
using KnitErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KnitErp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddKnitErpInfrastructure(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddDbContext<KnitErpDbContext>(o => o.UseSqlServer(connectionString));
        services.AddScoped<IKnitErpDbContext>(sp => sp.GetRequiredService<KnitErpDbContext>());
        return services;
    }

    /// <summary>
    /// Создаёт схему для разработки и тестов. Для рабочих сред будет заменено миграциями EF Core (ТЗ §5.13).
    /// </summary>
    public static async Task EnsureDatabaseAsync(KnitErpDbContext db, CancellationToken ct = default)
    {
        if (await db.Database.EnsureCreatedAsync(ct))
        {
            await db.Database.ExecuteSqlRawAsync(DatabaseObjects.AuditImmutableTrigger, ct);
        }
    }
}
