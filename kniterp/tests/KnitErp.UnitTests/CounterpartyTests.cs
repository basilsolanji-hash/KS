using KnitErp.Domain.Catalog;
using KnitErp.Domain.Common;
using KnitErp.Domain.Organizations;
using KnitErp.Domain.Warehousing;

namespace KnitErp.UnitTests;

public sealed class CounterpartyTests
{
    [Theory]
    [InlineData("500100732259", true)]
    [InlineData("500100732258", false)]
    [InlineData("50010073225", false)]
    [InlineData("50010073225a", false)]
    public void Person_inn_checks_both_control_digits(string inn, bool valid)
    {
        Assert.Equal(valid, RussianRequisites.IsValidPersonInn(inn));
    }

    [Fact]
    public void Legal_entity_with_kpp_and_individual_entrepreneur_are_accepted()
    {
        var ooo = Counterparty.Create(1, "ООО «Пряжа»", "7707083893", "773601001", isSupplier: true, isCustomer: false, null);
        Assert.Equal("773601001", ooo.Kpp);
        var ip = Counterparty.Create(1, "ИП Иванова", "500100732259", null, isSupplier: false, isCustomer: true, null);
        Assert.Equal("Покупатель", Counterparty.RolesText(ip.IsSupplier, ip.IsCustomer));
    }

    [Theory]
    [InlineData("7707083894", null, "catalog.counterparty.inn")]
    [InlineData("500100732259", "773601001", "catalog.counterparty.kpp")]
    [InlineData(null, "773601001", "catalog.counterparty.kpp")]
    public void Invalid_requisites_are_rejected(string? inn, string? kpp, string code)
    {
        var ex = Assert.Throws<BusinessRuleException>(() => Counterparty.Create(1, "Фирма", inn, kpp, true, false, null));
        Assert.Equal(code, ex.Code);
    }

    [Fact]
    public void Counterparty_must_be_supplier_or_customer()
    {
        Assert.Equal("catalog.counterparty.role",
            Assert.Throws<BusinessRuleException>(() => Counterparty.Create(1, "Фирма", null, null, false, false, null)).Code);
    }

    [Fact]
    public void Default_reasons_are_unique_within_kind()
    {
        Assert.Equal(OperationReason.Defaults.Count, OperationReason.Defaults.Select(r => (r.Kind, r.Name)).Distinct().Count());
        Assert.All(StockOperationKinds.All, k => Assert.Contains(OperationReason.Defaults, r => r.Kind == k));
    }
}
