using Microsoft.EntityFrameworkCore;

namespace KnitErp.Application.Common;

public static class Persistence
{
    /// <summary>Версия строки из формы должна совпасть с текущей — иначе данные изменил кто-то другой.</summary>
    public static T EnsureVersion<T>(this T entity, byte[] actual, byte[] expected) =>
        actual.AsSpan().SequenceEqual(expected) ? entity : throw new ConcurrencyConflictException();

    /// <summary>Сохранение, где одновременная правка возвращается пользователю как конфликт, а не теряется.</summary>
    public static async Task SaveOrConflictAsync(this IKnitErpDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException();
        }
    }
}
