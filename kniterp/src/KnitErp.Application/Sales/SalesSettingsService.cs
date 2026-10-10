using KnitErp.Application.Access;
using KnitErp.Application.Common;
using KnitErp.Domain.Access;
using KnitErp.Domain.Audit;
using KnitErp.Domain.Catalog;
using KnitErp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.Application.Sales;

public sealed record SalesLookupDto(long Id, LookupKind Kind, string Name, bool IsArchived, int Orders, byte[] RowVersion);


/// <summary>
/// Настройки заказов покупателей (D77): проекты, каналы продаж и дополнительные поля заказа. Смотреть — «Продажи: просмотр»,
/// менять — «Продажи: заказы и оплаты». Записи не удаляются — уходят в архив, ссылки в заказах сохраняются.
/// </summary>
public sealed class SalesSettingsService(IKnitErpDbContext db, IAccessGuard guard, ICurrentUser currentUser, IClock clock)
{
    public const int MaxLookups = 500;

    public async Task<IReadOnlyList<SalesLookupDto>> LookupsAsync(LookupKind kind, bool includeArchived = false, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.SalesView, ct);
        var orders = db.SalesOrders.AsNoTracking().Where(o => o.OrganizationId == ctx.OrganizationId);
        var counts = kind == LookupKind.Project
            ? await orders.Where(o => o.ProjectId != null).GroupBy(o => o.ProjectId!.Value)
                .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct)
            : await orders.Where(o => o.ChannelId != null).GroupBy(o => o.ChannelId!.Value)
                .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var rows = await db.Lookups.AsNoTracking()
            .Where(l => l.OrganizationId == ctx.OrganizationId && l.Kind == kind && (includeArchived || !l.IsArchived))
            .OrderBy(l => l.IsArchived).ThenBy(l => l.Name).ToListAsync(ct);
        return rows.Select(l => new SalesLookupDto(l.Id, l.Kind, l.Name, l.IsArchived, counts.GetValueOrDefault(l.Id), l.RowVersion)).ToList();
    }

    public async Task<long> CreateLookupAsync(LookupKind kind, string? name, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.SalesEdit, ct);
        if (!Enum.IsDefined(kind))
        {
            throw new BusinessRuleException("catalog.kind", "Неизвестный вид справочника.");
        }

        if (await db.Lookups.CountAsync(l => l.OrganizationId == ctx.OrganizationId && l.Kind == kind && !l.IsArchived, ct) >= MaxLookups)
        {
            throw new BusinessRuleException("catalog.too_many", $"Действующих записей не больше {MaxLookups}. Уберите лишние в архив.");
        }

        var lookup = Lookup.Create(ctx.OrganizationId, kind, name);
        await EnsureLookupFreeAsync(ctx, kind, lookup.Name, null, ct);
        db.Lookups.Add(lookup);
        await db.SaveChangesAsync(ct);
        Audit(ctx, AuditActions.CatalogCreated, nameof(Lookup), lookup.Id, null, lookup.Name, Lookup.KindName(kind));
        await db.SaveChangesAsync(ct);
        return lookup.Id;
    }

    public async Task RenameLookupAsync(long id, string? name, byte[] rowVersion, CancellationToken ct = default)
    {
        var (ctx, lookup) = await LoadLookupAsync(id, rowVersion, ct);
        var before = lookup.Name;
        lookup.Rename(name);
        await EnsureLookupFreeAsync(ctx, lookup.Kind, lookup.Name, id, ct);
        Audit(ctx, AuditActions.CatalogChanged, nameof(Lookup), id, before, lookup.Name, Lookup.KindName(lookup.Kind));
        await db.SaveOrConflictAsync(ct);
    }

    public async Task SetLookupArchivedAsync(long id, bool archived, byte[] rowVersion, CancellationToken ct = default)
    {
        var (ctx, lookup) = await LoadLookupAsync(id, rowVersion, ct);
        if (!archived)
        {
            await EnsureLookupFreeAsync(ctx, lookup.Kind, lookup.Name, id, ct);
        }

        lookup.SetArchived(archived);
        Audit(ctx, archived ? AuditActions.CatalogArchived : AuditActions.CatalogRestored, nameof(Lookup), id,
            archived ? lookup.Name : "в архиве", archived ? "в архиве" : lookup.Name, Lookup.KindName(lookup.Kind));
        await db.SaveOrConflictAsync(ct);
    }

    private const string FieldReason = "Дополнительное поле заказа покупателя";

    private CustomFieldStore Fields => new(db, currentUser, clock);

    public async Task<IReadOnlyList<CustomFieldDto>> FieldsAsync(bool includeArchived = false, CancellationToken ct = default) =>
        await Fields.ListAsync(await guard.DemandAsync(Permissions.SalesView, ct), CustomFieldTarget.SalesOrder, includeArchived, ct);

    public async Task<long> CreateFieldAsync(string? name, CustomFieldType type, CancellationToken ct = default, long? catalogId = null) =>
        await Fields.CreateAsync(await guard.DemandAsync(Permissions.SalesEdit, ct), CustomFieldTarget.SalesOrder, name, type, catalogId, FieldReason, ct);

    public async Task RenameFieldAsync(long id, string? name, byte[] rowVersion, CancellationToken ct = default) =>
        await Fields.RenameAsync(await guard.DemandAsync(Permissions.SalesEdit, ct), CustomFieldTarget.SalesOrder, id, name, rowVersion, FieldReason, ct);

    /// <summary>Сдвиг на одну позицию вверх (-1) или вниз (+1) среди действующих полей.</summary>
    public async Task MoveFieldAsync(long id, int delta, CancellationToken ct = default) =>
        await Fields.MoveAsync(await guard.DemandAsync(Permissions.SalesEdit, ct), CustomFieldTarget.SalesOrder, id, delta, ct);

    public async Task SetFieldArchivedAsync(long id, bool archived, byte[] rowVersion, CancellationToken ct = default) =>
        await Fields.SetArchivedAsync(await guard.DemandAsync(Permissions.SalesEdit, ct), CustomFieldTarget.SalesOrder, id, archived, rowVersion,
            FieldReason, ct);

    private async Task<(AccessContext, Lookup)> LoadLookupAsync(long id, byte[] rowVersion, CancellationToken ct)
    {
        var ctx = await guard.DemandAsync(Permissions.SalesEdit, ct);
        var lookup = await db.Lookups.SingleOrDefaultAsync(l => l.Id == id && l.OrganizationId == ctx.OrganizationId, ct)
                     ?? throw new NotFoundException("Запись справочника");
        return (ctx, lookup.EnsureVersion(lookup.RowVersion, rowVersion));
    }

    private async Task EnsureLookupFreeAsync(AccessContext ctx, LookupKind kind, string name, long? exceptId, CancellationToken ct)
    {
        if (await db.Lookups.AnyAsync(l => l.OrganizationId == ctx.OrganizationId && l.Kind == kind && !l.IsArchived && l.Name == name
                                           && l.Id != exceptId, ct))
        {
            throw new BusinessRuleException("catalog.duplicate", $"«{name}» уже есть в справочнике.");
        }
    }

    private void Audit(AccessContext ctx, string action, string entity, long id, string? before, string? after, string? reason) =>
        db.AuditEntries.Add(AuditEntry.Create(clock.UtcNow, ctx.OrganizationId, ctx.UserId, action, entity, id.ToString(),
            before, after, reason, currentUser.CorrelationId));
}
