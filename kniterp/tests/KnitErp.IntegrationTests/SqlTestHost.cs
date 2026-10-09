using KnitErp.Application.Access;
using KnitErp.Application.Audit;
using KnitErp.Application.Authentication;
using KnitErp.Application.Common;
using KnitErp.Application.Organizations;
using KnitErp.Application.Structure;
using KnitErp.Domain.Access;
using KnitErp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.IntegrationTests;

/// <summary>Тест пропускается, если не задан SQL Server (переменная KNITERP_TEST_SQL). В CI он задан всегда.</summary>
public sealed class SqlFactAttribute : FactAttribute
{
    public SqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlTestHost.EnvVar)))
        {
            Skip = $"Не задан {SqlTestHost.EnvVar}: интеграционные тесты SQL Server пропущены.";
        }
    }
}

public sealed class TestClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 10, 9, 5, 0, 0, DateTimeKind.Utc);
}

public sealed class TestUser : ICurrentUser
{
    public long? UserId { get; set; }
    public long? OrganizationId { get; set; }
    public string? CorrelationId => "test";
}

/// <summary>Отдельная база на каждый тест-класс; создаётся с нуля теми же миграциями, что и рабочая, и удаляется после.</summary>
public sealed class SqlTestHost : IAsyncLifetime
{
    public const string EnvVar = "KNITERP_TEST_SQL";

    private readonly string _connectionString;

    public SqlTestHost()
    {
        var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(EnvVar) ?? "Server=.")
        {
            InitialCatalog = "kniterp_test_" + Guid.NewGuid().ToString("N")[..12],
        };
        _connectionString = builder.ConnectionString;
    }

    public TestClock Clock { get; } = new();

    public KnitErpDbContext NewDb() =>
        new(new DbContextOptionsBuilder<KnitErpDbContext>()
            .UseSqlServer(_connectionString, KnitErp.Infrastructure.DependencyInjection.ConfigureSqlServer).Options);

    /// <summary>Набор сервисов от имени пользователя. Каждый вызов — новый DbContext, как отдельный HTTP-запрос.</summary>
    public Services As(long? userId, long? organizationId)
    {
        var db = NewDb();
        var user = new TestUser { UserId = userId, OrganizationId = organizationId };
        var guard = new AccessGuard(db, user, Clock);
        var access = new UserAccessService(db, guard, user, Clock);
        return new Services(db,
            new OrganizationService(db, guard, user, Clock),
            access,
            new AuditQueryService(db, guard),
            new SignInService(db, Hasher, user, Clock),
            new StructureService(db, guard, user, Clock),
            new EmployeeService(db, guard, access, user, Clock));
    }

    private static readonly IPasswordHasher<UserAccount> Hasher = new PasswordHasher<UserAccount>();

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvVar)))
        {
            return;
        }

        await using var db = NewDb();
        await KnitErp.Infrastructure.DependencyInjection.MigrateDatabaseAsync(db);
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvVar)))
        {
            return;
        }

        await using var db = NewDb();
        SqlConnection.ClearAllPools();
        await db.Database.EnsureDeletedAsync();
    }
}

public sealed record Services(
    KnitErpDbContext Db,
    OrganizationService Organizations,
    UserAccessService Access,
    AuditQueryService Audit,
    SignInService SignIn,
    StructureService Structure,
    EmployeeService Employees) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Db.DisposeAsync();
}
