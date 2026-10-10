using KnitErp.Domain.Catalog;
using KnitErp.Domain.Common;
using KnitErp.Domain.Warehousing;

namespace KnitErp.UnitTests;

public sealed class CatalogTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 5, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Item_code_is_normalized_and_validated()
    {
        Assert.Equal("ПР-0001", Item.Create(1, " пр-0001 ", "Пряжа шерсть 50%", ItemType.RawMaterial, 1, null, Now).Code);
        Assert.Equal("catalog.item.code_invalid",
            Assert.Throws<BusinessRuleException>(() => Item.Create(1, "ПР 0001", "Пряжа", ItemType.RawMaterial, 1, null, Now)).Code);
        Assert.Equal("catalog.item.type_invalid",
            Assert.Throws<BusinessRuleException>(() => Item.Create(1, "X1", "Пряжа", (ItemType)7, 1, null, Now)).Code);
    }

    [Fact]
    public void Archived_item_is_not_edited_until_restored()
    {
        var item = Item.Create(1, "F-1", "Пуговица", ItemType.Accessory, 1, null, Now);
        item.Archive(Now);
        Assert.Equal("catalog.archived", Assert.Throws<BusinessRuleException>(() => item.Update("F-1", "Пуговица 2", ItemType.Accessory, 1, null)).Code);
        item.Restore();
        var changes = item.Update("F-1", "Пуговица 15 мм", ItemType.Accessory, 2, "  ");
        Assert.Equal(["Наименование", "UnitId"], changes.Select(c => c.Field));
        Assert.Null(item.Description);
    }

    [Fact]
    public void Unit_precision_is_limited_and_used_unit_is_not_archived()
    {
        Assert.Equal("catalog.unit.precision",
            Assert.Throws<BusinessRuleException>(() => UnitOfMeasure.Create(1, "999", "Бобина", "боб", 7)).Code);
        var unit = UnitOfMeasure.Create(1, "999", "Бобина", "боб", 0);
        Assert.Equal("catalog.unit.in_use", Assert.Throws<BusinessRuleException>(() => unit.Archive(2)).Code);
        unit.Archive(0);
        Assert.True(unit.IsArchived);
    }

    [Fact]
    public void Default_units_have_unique_codes_and_fit_quantity_precision()
    {
        Assert.Equal(UnitOfMeasure.Defaults.Count, UnitOfMeasure.Defaults.Select(u => u.Code).Distinct().Count());
        Assert.All(UnitOfMeasure.Defaults, u => Assert.InRange(u.Precision, 0, UnitOfMeasure.MaxPrecision));
    }

    [Fact]
    public void Site_with_active_warehouses_is_not_archived()
    {
        var site = Site.Create(1, "Электросталь", null);
        Assert.Equal("warehouse.site.has_warehouses", Assert.Throws<BusinessRuleException>(() => site.Archive(1)).Code);
        site.Archive(0);
        Assert.Equal("catalog.archived", Assert.Throws<BusinessRuleException>(() => site.Update("Новое", null)).Code);
    }

    [Fact]
    public void Every_item_type_has_a_russian_name()
    {
        Assert.All(ItemTypes.All, t => Assert.NotEqual(t.ToString(), ItemTypes.Name(t)));
    }
}
