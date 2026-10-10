using KnitErp.Domain.Common;
using KnitErp.Domain.Organizations;

namespace KnitErp.Domain.Warehousing;

/// <summary>Площадка — место, где стоит производство или склад (фактический адрес). Не удаляется — архивируется.</summary>
public sealed class Site
{
    public const int NameMaxLength = 150;
    public const int AddressMaxLength = 500;

    private Site()
    {
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Address { get; private set; }
    public bool IsArchived { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static Site Create(long organizationId, string name, string? address) => new()
    {
        OrganizationId = organizationId,
        Name = DomainText.Require(name, NameMaxLength, "Название площадки"),
        Address = DomainText.Optional(address, AddressMaxLength, "Адрес"),
    };

    public IReadOnlyList<FieldChange> Update(string name, string? address)
    {
        EnsureActive();
        var before = (Name, Address);
        Name = DomainText.Require(name, NameMaxLength, "Название площадки");
        Address = DomainText.Optional(address, AddressMaxLength, "Адрес");
        var changes = new List<FieldChange>();
        if (before.Name != Name)
        {
            changes.Add(new FieldChange("Название", before.Name, Name));
        }

        if (before.Address != Address)
        {
            changes.Add(new FieldChange("Адрес", before.Address, Address));
        }

        return changes;
    }

    public void Archive(int activeWarehouses)
    {
        EnsureActive();
        if (activeWarehouses > 0)
        {
            throw new BusinessRuleException("warehouse.site.has_warehouses", $"На площадке есть действующие склады ({activeWarehouses}).");
        }

        IsArchived = true;
    }

    private void EnsureActive()
    {
        if (IsArchived)
        {
            throw new BusinessRuleException("catalog.archived", "Площадка в архиве.");
        }
    }
}

/// <summary>
/// Склад (место хранения). По складу ограничивается область данных кладовщика: назначение роли со складом
/// даёт доступ только к нему. Остатки и документы появятся в следующих срезах; архив склада с остатком будет запрещён.
/// </summary>
public sealed class Warehouse
{
    public const int NameMaxLength = 150;

    private Warehouse()
    {
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public long? SiteId { get; private set; }
    public bool IsArchived { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static Warehouse Create(long organizationId, string name, long? siteId) => new()
    {
        OrganizationId = organizationId,
        Name = DomainText.Require(name, NameMaxLength, "Название склада"),
        SiteId = siteId,
    };

    public IReadOnlyList<FieldChange> Update(string name, long? siteId)
    {
        EnsureActive();
        var before = (Name, SiteId);
        Name = DomainText.Require(name, NameMaxLength, "Название склада");
        SiteId = siteId;
        var changes = new List<FieldChange>();
        if (before.Name != Name)
        {
            changes.Add(new FieldChange("Название", before.Name, Name));
        }

        if (before.SiteId != SiteId)
        {
            changes.Add(new FieldChange(nameof(SiteId), before.SiteId?.ToString(), SiteId?.ToString()));
        }

        return changes;
    }

    public void Archive()
    {
        EnsureActive();
        IsArchived = true;
    }

    private void EnsureActive()
    {
        if (IsArchived)
        {
            throw new BusinessRuleException("catalog.archived", "Склад в архиве.");
        }
    }
}
