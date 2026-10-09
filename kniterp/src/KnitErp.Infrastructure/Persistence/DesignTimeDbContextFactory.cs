using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace KnitErp.Infrastructure.Persistence;

/// <summary>
/// Контекст для команды <c>dotnet ef</c>. Создание миграции и SQL-скрипта к базе не подключается,
/// строка нужна только поставщику SQL Server. Для <c>database update</c> задайте KNITERP_MIGRATIONS_SQL.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<KnitErpDbContext>
{
    public KnitErpDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("KNITERP_MIGRATIONS_SQL")
                               ?? "Server=localhost;Database=kniterp_design;Integrated Security=false;TrustServerCertificate=true";
        return new KnitErpDbContext(new DbContextOptionsBuilder<KnitErpDbContext>()
            .UseSqlServer(connectionString, DependencyInjection.ConfigureSqlServer)
            .Options);
    }
}
