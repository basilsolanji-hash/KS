using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KnitErp.Application.Common;
using KnitErp.Application.Security;
using KnitErp.Domain.Audit;
using KnitErp.Domain.Warehousing;
using KnitErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.Infrastructure.Security;

/// <summary>
/// Цепочка подписей журнала аудита и регистра движений. Каждая новая запись подписывается HMAC-SHA256 от подписи
/// предыдущей записи той же организации и своих полей. Ключ подписи выводится из мастер-ключа и в базе не хранится,
/// поэтому даже администратор базы, отключивший триггеры, не сможет незаметно изменить, удалить или вставить запись.
/// Записи организации подписываются строго по очереди: на время вставки цепочка блокируется (sp_getapplock).
/// </summary>
internal static class IntegrityChain
{
    public const string AuditJournal = "Журнал аудита";
    public const string StockJournal = "Движения склада";

    private static readonly byte[] Genesis = new byte[32];

    /// <summary>
    /// Подписывает добавленные записи перед сохранением. Вызывается внутри транзакции. Порядок цепочки задаёт свой
    /// номер ChainSeq, а не Id: SQL Server не обещает выдавать Id пакетной вставки в порядке строк.
    /// </summary>
    public static async Task SealAsync(KnitErpDbContext db, CancellationToken ct)
    {
        var key = KeyRing.Current.IntegrityKeys[0];

        var audits = db.ChangeTracker.Entries<AuditEntry>().Where(e => e.State == EntityState.Added).ToList();
        foreach (var group in audits.GroupBy(e => e.Entity.OrganizationId))
        {
            await db.LockAsync($"kniterp.chain.audit.{group.Key ?? 0}", ct);
            var org = group.Key;
            var last = await db.AuditEntries.AsNoTracking()
                .Where(a => a.OrganizationId == org && a.ChainSeq != null)
                .OrderByDescending(a => a.ChainSeq).Select(a => new { a.ChainSeq, a.ChainHash }).FirstOrDefaultAsync(ct);
            var (seq, prev) = (last?.ChainSeq ?? 0, last?.ChainHash ?? Genesis);
            foreach (var entry in group)
            {
                // Время хранится с точностью до миллисекунды — подписываем ровно то, что окажется в базе.
                entry.Property(a => a.OccurredAtUtc).CurrentValue = ToMilliseconds(entry.Entity.OccurredAtUtc);
                entry.Property(a => a.ChainSeq).CurrentValue = ++seq;
                prev = Sign(key, prev, Payload(entry.Entity));
                entry.Property(a => a.ChainHash).CurrentValue = prev;
            }
        }

        var movements = db.ChangeTracker.Entries<StockMovement>().Where(e => e.State == EntityState.Added).ToList();
        foreach (var group in movements.GroupBy(e => e.Entity.OrganizationId))
        {
            await db.LockAsync($"kniterp.chain.stock.{group.Key}", ct);
            var org = group.Key;
            var last = await db.StockMovements.AsNoTracking()
                .Where(m => m.OrganizationId == org && m.ChainSeq != null)
                .OrderByDescending(m => m.ChainSeq).Select(m => new { m.ChainSeq, m.ChainHash }).FirstOrDefaultAsync(ct);
            var (seq, prev) = (last?.ChainSeq ?? 0, last?.ChainHash ?? Genesis);
            foreach (var entry in group)
            {
                entry.Property(m => m.CreatedAtUtc).CurrentValue = ToMilliseconds(entry.Entity.CreatedAtUtc);
                entry.Property(m => m.Quantity).CurrentValue = decimal.Round(entry.Entity.Quantity, 6);
                entry.Property(m => m.ChainSeq).CurrentValue = ++seq;
                prev = Sign(key, prev, Payload(entry.Entity));
                entry.Property(m => m.ChainHash).CurrentValue = prev;
            }
        }
    }

    public static bool HasChained(KnitErpDbContext db) =>
        db.ChangeTracker.Entries().Any(e => e.State == EntityState.Added && e.Entity is AuditEntry or StockMovement);

    internal static byte[] Sign(byte[] key, byte[] prev, string payload)
    {
        using var hmac = new HMACSHA256(key);
        var data = Encoding.UTF8.GetBytes(payload);
        var buffer = new byte[prev.Length + data.Length];
        prev.CopyTo(buffer, 0);
        data.CopyTo(buffer, prev.Length);
        return hmac.ComputeHash(buffer);
    }

    internal static string Payload(AuditEntry a) => Join("audit", a.ChainSeq?.ToString(CultureInfo.InvariantCulture), a.OrganizationId?.ToString(CultureInfo.InvariantCulture),
        Time(a.OccurredAtUtc), a.ActorUserId?.ToString(CultureInfo.InvariantCulture), a.Action, a.EntityType, a.EntityId, a.Before, a.After,
        a.Reason, a.CorrelationId);

    internal static string Payload(StockMovement m) => Join("stock", m.ChainSeq?.ToString(CultureInfo.InvariantCulture), m.OrganizationId.ToString(CultureInfo.InvariantCulture),
        m.WarehouseId.ToString(CultureInfo.InvariantCulture), m.ItemId.ToString(CultureInfo.InvariantCulture),
        decimal.Round(m.Quantity, 6).ToString("0.######", CultureInfo.InvariantCulture), m.OccurredOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ((byte)m.Source).ToString(CultureInfo.InvariantCulture), m.SourceId.ToString(CultureInfo.InvariantCulture), Time(m.CreatedAtUtc));

    /// <summary>Поля с длиной впереди: «ab»+«c» и «a»+«bc» дают разные строки; null отличается от пустой строки.</summary>
    private static string Join(params string?[] fields)
    {
        var sb = new StringBuilder();
        foreach (var f in fields)
        {
            sb.Append(f is null ? "-" : f.Length.ToString(CultureInfo.InvariantCulture) + ":" + f).Append('|');
        }

        return sb.ToString();
    }

    private static string Time(DateTime utc) => ToMilliseconds(utc).ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);

    private static DateTime ToMilliseconds(DateTime t) => new(t.Ticks - t.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
}

/// <summary>
/// Проверка цепочек организации: записи идут по номеру ChainSeq, каждая подпись пересчитывается. Пропуск номера —
/// удалённая запись, несовпадение подписи — изменённая, запись без номера после начала цепочки — вставленная в обход.
/// </summary>
public sealed class IntegrityVerifier(IKnitErpDbContextFactory factory) : IIntegrityVerifier
{
    private const int Chunk = 2000;
    private const int MaxProblems = 100;

    private sealed record Row(long Id, long? Seq, byte[]? Hash, string Payload);

    public async Task<IReadOnlyList<JournalCheck>> VerifyAsync(long organizationId, CancellationToken ct = default)
    {
        await using var db = factory.Create();
        var audit = await CheckAsync(IntegrityChain.AuditJournal,
            async after => (await db.AuditEntries.AsNoTracking().Where(a => a.OrganizationId == organizationId && a.ChainSeq > after)
                    .OrderBy(a => a.ChainSeq).Take(Chunk).ToListAsync(ct))
                .Select(a => new Row(a.Id, a.ChainSeq, a.ChainHash, IntegrityChain.Payload(a))).ToList(),
            async () =>
            {
                var first = await db.AuditEntries.AsNoTracking().Where(a => a.OrganizationId == organizationId && a.ChainSeq != null)
                    .MinAsync(a => (long?)a.Id, ct);
                var unsigned = await db.AuditEntries.AsNoTracking().Where(a => a.OrganizationId == organizationId && a.ChainSeq == null)
                    .Select(a => a.Id).ToListAsync(ct);
                return (first, unsigned);
            });
        var stock = await CheckAsync(IntegrityChain.StockJournal,
            async after => (await db.StockMovements.AsNoTracking().Where(m => m.OrganizationId == organizationId && m.ChainSeq > after)
                    .OrderBy(m => m.ChainSeq).Take(Chunk).ToListAsync(ct))
                .Select(m => new Row(m.Id, m.ChainSeq, m.ChainHash, IntegrityChain.Payload(m))).ToList(),
            async () =>
            {
                var first = await db.StockMovements.AsNoTracking().Where(m => m.OrganizationId == organizationId && m.ChainSeq != null)
                    .MinAsync(m => (long?)m.Id, ct);
                var unsigned = await db.StockMovements.AsNoTracking().Where(m => m.OrganizationId == organizationId && m.ChainSeq == null)
                    .Select(m => m.Id).ToListAsync(ct);
                return (first, unsigned);
            });
        return [audit, stock];
    }

    private static async Task<JournalCheck> CheckAsync(
        string journal, Func<long, Task<List<Row>>> read, Func<Task<(long? FirstChainedId, List<long> Unsigned)>> unsignedRows)
    {
        var keys = KeyRing.Current.IntegrityKeys;
        var problems = new List<IntegrityProblem>();
        byte[] prev = new byte[32];
        long expected = 1, after = 0;
        var checkedCount = 0;
        while (true)
        {
            var rows = await read(after);
            if (rows.Count == 0)
            {
                break;
            }

            foreach (var row in rows)
            {
                var seq = row.Seq!.Value;
                after = seq;
                checkedCount++;
                if (seq != expected)
                {
                    Add(problems, new IntegrityProblem(journal, row.Id,
                        seq - expected == 1 ? $"Удалена запись цепочки № {expected}." : $"Удалены записи цепочки № {expected}–{seq - 1}."));
                }
                else if (row.Hash is null || !keys.Any(k => CryptographicOperations.FixedTimeEquals(IntegrityChain.Sign(k, prev, row.Payload), row.Hash)))
                {
                    Add(problems, new IntegrityProblem(journal, row.Id, "Подпись не сходится — запись изменена."));
                }

                prev = row.Hash ?? new byte[32];
                expected = seq + 1;
            }
        }

        var (firstChained, unsigned) = await unsignedRows();
        var legacy = 0;
        foreach (var id in unsigned)
        {
            if (firstChained is { } first && id > first)
            {
                Add(problems, new IntegrityProblem(journal, id, "Запись без подписи — добавлена в обход приложения."));
            }
            else
            {
                legacy++;
            }
        }

        var fingerprint = checkedCount == 0 ? null : $"№ {expected - 1} · {Convert.ToHexString(prev)[..16]}";
        return new JournalCheck(journal, checkedCount, legacy, fingerprint, problems);
    }

    private static void Add(List<IntegrityProblem> list, IntegrityProblem problem)
    {
        if (list.Count < MaxProblems)
        {
            list.Add(problem);
        }
    }
}
