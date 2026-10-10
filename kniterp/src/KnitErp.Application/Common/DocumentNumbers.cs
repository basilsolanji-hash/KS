using KnitErp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.Application.Common;

public static class DocumentNumbers
{
    /// <summary>
    /// Следующий номер документа организации. Вызывается внутри транзакции документа: если документ не сохранится,
    /// номер не израсходуется. Счётчик заблокирован до конца транзакции, поэтому одновременные запросы получают
    /// номера по очереди; повтор по конфликту версии — запасной путь.
    /// </summary>
    public static async Task<string> NextAsync(IKnitErpDbContext db, long organizationId, string kind, CancellationToken ct)
    {
        await db.LockAsync($"kniterp.counter.{organizationId}.{kind}", ct);
        for (var attempt = 1; ; attempt++)
        {
            var counter = await db.DocumentCounters.SingleOrDefaultAsync(c => c.OrganizationId == organizationId && c.Kind == kind, ct);
            if (counter is null)
            {
                counter = DocumentCounter.Start(organizationId, kind);
                db.DocumentCounters.Add(counter);
            }

            var number = counter.Next();
            try
            {
                await db.SaveChangesAsync(ct);
                return number;
            }
            catch (DbUpdateException ex) when (attempt < 5)
            {
                // Другой запрос успел выдать номер (или создать счётчик) — перечитываем и пробуем снова.
                foreach (var entry in ex.Entries)
                {
                    if (entry.State == EntityState.Added)
                    {
                        entry.State = EntityState.Detached;
                    }
                    else
                    {
                        await entry.ReloadAsync(ct);
                    }
                }
            }
        }
    }
}
