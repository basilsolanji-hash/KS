using KnitErp.Application.Access;
using KnitErp.Application.Common;
using KnitErp.Domain.Access;
using KnitErp.Domain.Audit;
using KnitErp.Domain.Catalog;
using KnitErp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.Application.Sales;

public sealed record SalesLookupDto(long Id, LookupKind Kind, string Name, bool IsArchived, int Orders, byte[] RowVersion);

public sealed record CustomFieldDto(long Id, string Name, CustomFieldType Type, int SortOrder, bool IsArchived, int Filled, byte[] RowVersion);

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

    public async Task<IReadOnlyList<CustomFieldDto>> FieldsAsync(bool includeArchived = false, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.SalesView, ct);
        var filled = await db.CustomFieldValues.AsNoTracking().Where(v => v.OrganizationId == ctx.OrganizationId)
            .GroupBy(v => v.FieldId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var rows = await db.CustomFieldDefinitions.AsNoTracking()
            .Where(f => f.OrganizationId == ctx.OrganizationId && f.Target == CustomFieldTarget.SalesOrder && (includeArchived || !f.IsArchived))
            .OrderBy(f => f.IsArchived).ThenBy(f => f.SortOrder).ThenBy(f => f.Id).ToListAsync(ct);
        return rows.Select(f => new CustomFieldDto(f.Id, f.Name, f.Type, f.SortOrder, f.IsArchived, filled.GetValueOrDefault(f.Id), f.RowVersion))
            .ToList();
    }

    public async Task<long> CreateFieldAsync(string? name, CustomFieldType type, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.SalesEdit, ct);
        var fields = db.CustomFieldDefinitions.Where(f => f.OrganizationId == ctx.OrganizationId && f.Target == CustomFieldTarget.SalesOrder);
        if (await fields.CountAsync(f => !f.IsArchived, ct) >= CustomFieldDefinition.MaxFields)
        {
            throw new BusinessRuleException("custom_field.too_many", $"Дополнительных полей не больше {CustomFieldDefinition.MaxFields}.");
        }

        var last = await fields.MaxAsync(f => (int?)f.SortOrder, ct) ?? 0;
        var field = CustomFieldDefinition.Create(ctx.OrganizationId, CustomFieldTarget.SalesOrder, name, type, last + 10);
        await EnsureFieldFreeAsync(ctx, field.Name, null, ct);
        db.CustomFieldDefinitions.Add(field);
        await db.SaveChangesAsync(ct);
        Audit(ctx, AuditActions.CatalogCreated, nameof(CustomFieldDefinition), field.Id, null,
            $"{field.Name} ({CustomFieldDefinition.TypeName(field.Type)})", "Дополнительное поле заказа покупателя");
        await db.SaveChangesAsync(ct);
        return field.Id;
    }

    public async Task RenameFieldAsync(long id, string? name, byte[] rowVersion, CancellationToken ct = default)
    {
        var (ctx, field) = await LoadFieldAsync(id, rowVersion, ct);
        var before = field.Name;
        field.Rename(name);
        await EnsureFieldFreeAsync(ctx, field.Name, id, ct);
        Audit(ctx, AuditActions.CatalogChanged, nameof(CustomFieldDefinition), id, before, field.Name, "Дополнительное поле заказа покупателя");
        await db.SaveOrConflictAsync(ct);
    }

    /// <summary>Сдвиг на одну позицию вверх (-1) или вниз (+1) среди действующих полей.</summary>
    public async Task MoveFieldAsync(long id, int delta, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.SalesEdit, ct);
        var fields = await db.CustomFieldDefinitions
            .Where(f => f.OrganizationId == ctx.OrganizationId && f.Target == CustomFieldTarget.SalesOrder && !f.IsArchived)
            .OrderBy(f => f.SortOrder).ThenBy(f => f.Id).ToListAsync(ct);
        var index = fields.FindIndex(f => f.Id == id);
        if (index < 0)
        {
            throw new NotFoundException("Дополнительное поле");
        }

        var target = Math.Clamp(index + Math.Sign(delta), 0, fields.Count - 1);
        (fields[index], fields[target]) = (fields[target], fields[index]);
        for (var i = 0; i < fields.Count; i++)
        {
            fields[i].MoveTo((i + 1) * 10);
        }

        await db.SaveOrConflictAsync(ct);
    }

    public async Task SetFieldArchivedAsync(long id, bool archived, byte[] rowVersion, CancellationToken ct = default)
    {
        var (ctx, field) = await LoadFieldAsync(id, rowVersion, ct);
        if (!archived)
        {
            await EnsureFieldFreeAsync(ctx, field.Name, id, ct);
        }

        field.SetArchived(archived);
        Audit(ctx, archived ? AuditActions.CatalogArchived : AuditActions.CatalogRestored, nameof(CustomFieldDefinition), id,
            archived ? field.Name : "в архиве", archived ? "в архиве" : field.Name, "Дополнительное поле заказа покупателя");
        await db.SaveOrConflictAsync(ct);
    }

    private async Task<(AccessContext, Lookup)> LoadLookupAsync(long id, byte[] rowVersion, CancellationToken ct)
    {
        var ctx = await guard.DemandAsync(Permissions.SalesEdit, ct);
        var lookup = await db.Lookups.SingleOrDefaultAsync(l => l.Id == id && l.OrganizationId == ctx.OrganizationId, ct)
                     ?? throw new NotFoundException("Запись справочника");
        return (ctx, lookup.EnsureVersion(lookup.RowVersion, rowVersion));
    }

    private async Task<(AccessContext, CustomFieldDefinition)> LoadFieldAsync(long id, byte[] rowVersion, CancellationToken ct)
    {
        var ctx = await guard.DemandAsync(Permissions.SalesEdit, ct);
        var field = await db.CustomFieldDefinitions.SingleOrDefaultAsync(f => f.Id == id && f.OrganizationId == ctx.OrganizationId, ct)
                    ?? throw new NotFoundException("Дополнительное поле");
        return (ctx, field.EnsureVersion(field.RowVersion, rowVersion));
    }

    private async Task EnsureLookupFreeAsync(AccessContext ctx, LookupKind kind, string name, long? exceptId, CancellationToken ct)
    {
        if (await db.Lookups.AnyAsync(l => l.OrganizationId == ctx.OrganizationId && l.Kind == kind && !l.IsArchived && l.Name == name
                                           && l.Id != exceptId, ct))
        {
            throw new BusinessRuleException("catalog.duplicate", $"«{name}» уже есть в справочнике.");
        }
    }

    private async Task EnsureFieldFreeAsync(AccessContext ctx, string name, long? exceptId, CancellationToken ct)
    {
        if (await db.CustomFieldDefinitions.AnyAsync(f => f.OrganizationId == ctx.OrganizationId && f.Target == CustomFieldTarget.SalesOrder
                                                         && !f.IsArchived && f.Name == name && f.Id != exceptId, ct))
        {
            throw new BusinessRuleException("custom_field.duplicate", $"Поле «{name}» уже есть.");
        }
    }

    private void Audit(AccessContext ctx, string action, string entity, long id, string? before, string? after, string? reason) =>
        db.AuditEntries.Add(AuditEntry.Create(clock.UtcNow, ctx.OrganizationId, ctx.UserId, action, entity, id.ToString(),
            before, after, reason, currentUser.CorrelationId));
}
