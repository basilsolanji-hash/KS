using KnitErp.Domain.Access;
using KnitErp.Domain.Audit;
using KnitErp.Domain.Organizations;
using KnitErp.Domain.Catalog;
using KnitErp.Domain.Common;
using KnitErp.Domain.Structure;
using KnitErp.Domain.Warehousing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace KnitErp.Application.Common;

public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>
/// Кто выполняет запрос. OrganizationId берётся из сессии пользователя, а не из параметров запроса (ТЗ §4.6 п.3).
/// </summary>
public interface ICurrentUser
{
    long? UserId { get; }
    long? OrganizationId { get; }
    string? CorrelationId { get; }
}

public interface IKnitErpDbContext
{
    DbSet<Organization> Organizations { get; }
    DbSet<UserAccount> Users { get; }
    DbSet<OrganizationMember> OrganizationMembers { get; }
    DbSet<Role> Roles { get; }
    DbSet<RoleAssignment> RoleAssignments { get; }
    DbSet<AuditEntry> AuditEntries { get; }
    DbSet<Department> Departments { get; }
    DbSet<Position> Positions { get; }
    DbSet<Employee> Employees { get; }
    DbSet<UnitOfMeasure> Units { get; }
    DbSet<Item> Items { get; }
    DbSet<Site> Sites { get; }
    DbSet<Warehouse> Warehouses { get; }
    DbSet<Counterparty> Counterparties { get; }
    DbSet<OperationReason> OperationReasons { get; }
    DbSet<OpeningBalance> OpeningBalances { get; }
    DbSet<StockMovement> StockMovements { get; }
    DbSet<StockDocument> StockDocuments { get; }
    DbSet<DocumentCounter> DocumentCounters { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Исключительная блокировка складов до конца текущей транзакции: проверка остатка и запись расхода
    /// не перемежаются с другим проведением по тем же складам.
    /// </summary>
    Task LockWarehousesAsync(IEnumerable<long> warehouseIds, CancellationToken cancellationToken = default);
}

/// <summary>Лист таблицы для выгрузки: заголовок и строки как текст.</summary>
public sealed record SheetData(string Name, IReadOnlyList<string> Header, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>Чтение и запись таблиц Excel (.xlsx). Реализация — в Infrastructure, сценарии от библиотеки не зависят.</summary>
public interface ISpreadsheetFormat
{
    /// <summary>Строки первого листа как текст, вместе с заголовком. Плохой файл — BusinessRuleException.</summary>
    IReadOnlyList<IReadOnlyList<string>> ReadFirstSheet(Stream stream, int maxRows);

    byte[] Write(IReadOnlyList<SheetData> sheets);
}
