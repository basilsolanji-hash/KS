using KnitErp.Domain.Catalog;
using KnitErp.Domain.Common;

namespace KnitErp.UnitTests;

/// <summary>Правила номенклатуры (D79): штрихкоды, сведения карточки, виды цен, свои справочники.</summary>
public sealed class NomenclatureRulesTests
{
    [Theory]
    [InlineData("4600000000008", BarcodeType.Ean13)]
    [InlineData("4 600000 000008", BarcodeType.Ean13)]
    [InlineData("96385074", BarcodeType.Ean8)]
    [InlineData("14600000000005", BarcodeType.Gtin)]
    [InlineData("PRJ-0012/48", BarcodeType.Code128)]
    public void Valid_barcodes_are_accepted(string code, BarcodeType type)
    {
        var detected = ItemBarcode.Detect(code.Replace(" ", string.Empty));
        Assert.Equal(type, detected);
        Assert.Equal(code.Replace(" ", string.Empty), ItemBarcode.Create(1, 2, detected, code).Code);
    }

    [Theory]
    [InlineData("4600000000009", BarcodeType.Ean13)]
    [InlineData("96385075", BarcodeType.Ean8)]
    [InlineData("12345", BarcodeType.Ean13)]
    [InlineData("код с кириллицей", BarcodeType.Code128)]
    public void Wrong_barcodes_are_rejected(string code, BarcodeType type) =>
        Assert.Equal("catalog.barcode.invalid", Assert.Throws<BusinessRuleException>(() => ItemBarcode.Create(1, 2, type, code)).Code);

    [Fact]
    public void Item_details_are_normalized_and_checked()
    {
        var d = ItemDetailRules.Normalize(new ItemDetails(null, " A-1 ", "156", null, null, "6110 20 100 0", 0.35m, null, 10m, 120.5m));
        Assert.Equal(("A-1", "Китай", "6110201000"), (d.Article, d.OriginCountryName, d.TnVedCode));
        Assert.Equal("643", ItemDetailRules.Normalize(new ItemDetails(null, null, null, "россия", null, null, null, null, null, null)).OriginCountryCode);
        Assert.Equal("catalog.item.country", Assert.Throws<BusinessRuleException>(() =>
            ItemDetailRules.Normalize(new ItemDetails(null, null, "1560", null, null, null, null, null, null, null))).Code);
        Assert.Equal("catalog.item.tnved", Assert.Throws<BusinessRuleException>(() =>
            ItemDetailRules.Normalize(new ItemDetails(null, null, null, null, null, "61102", null, null, null, null))).Code);
        Assert.Equal("catalog.item.number", Assert.Throws<BusinessRuleException>(() =>
            ItemDetailRules.Normalize(new ItemDetails(null, null, null, null, null, null, -1m, null, null, null))).Code);
        Assert.Equal("catalog.item.number", Assert.Throws<BusinessRuleException>(() =>
            ItemDetailRules.Normalize(new ItemDetails(null, null, null, null, null, null, null, null, null, 1.23456m))).Code);
    }

    [Fact]
    public void Item_details_changes_are_reported_for_audit()
    {
        var item = Item.Create(1, "ПР-1", "Пряжа", ItemType.RawMaterial, 2, null, DateTime.UtcNow);
        var changes = item.SetDetails(item.Details with { Article = "A-1", MinStock = 5m });
        Assert.Equal(["Артикул", "Неснижаемый остаток"], changes.Select(c => c.Field));
        Assert.Empty(item.SetDetails(item.Details));
        item.Archive(DateTime.UtcNow);
        Assert.Equal("catalog.archived", Assert.Throws<BusinessRuleException>(() => item.SetDetails(item.Details)).Code);
    }

    [Fact]
    public void Default_price_type_cannot_be_archived_and_prices_are_checked()
    {
        var type = PriceType.Create(1, "Цена продажи", true, isDefault: true, 10);
        Assert.Equal("catalog.price_type.default", Assert.Throws<BusinessRuleException>(() => type.SetArchived(true)).Code);
        Assert.Equal("catalog.price", Assert.Throws<BusinessRuleException>(() => ItemPrice.Create(1, 2, 3, -1m)).Code);
        Assert.Equal("catalog.price", Assert.Throws<BusinessRuleException>(() => ItemPrice.Create(1, 2, 3, 1.00001m)).Code);
    }

    [Fact]
    public void Catalog_custom_field_needs_a_catalog_and_stores_entry_id()
    {
        Assert.Equal("custom_field.catalog", Assert.Throws<BusinessRuleException>(() =>
            CustomFieldDefinition.Create(1, CustomFieldTarget.Item, "Цвет", CustomFieldType.Catalog, 10)).Code);
        var field = CustomFieldDefinition.Create(1, CustomFieldTarget.Item, "Цвет", CustomFieldType.Catalog, 10, catalogId: 7);
        Assert.Equal("42", field.Normalize(" 42 "));
        Assert.Equal("custom_field.catalog", Assert.Throws<BusinessRuleException>(() => field.Normalize("синий")).Code);
        Assert.Equal("0012 — Синий", UserCatalogEntry.Create(1, 7, "Синий", "0012").Display);
    }

    [Theory]
    [InlineData("12,5", 12.5)]
    [InlineData("1 200", 1200.0)]
    [InlineData("", null)]
    public void Decimal_input_is_parsed(string text, double? expected) =>
        Assert.Equal(expected is { } e ? (decimal)e : null, Decimals.Parse(text, "Поле"));
}
