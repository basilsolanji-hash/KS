using KnitErp.Domain.Catalog;
using KnitErp.Domain.Common;
using KnitErp.Domain.Organizations;

namespace KnitErp.UnitTests;

public sealed class CountryAndVatTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("RU", "7700000016", true)]
    [InlineData("RU", "7700000017", false)]
    [InlineData("UZ", "301234567", true)]
    [InlineData("UZ", "30123456", false)]
    [InlineData("KZ", "940240000498", true)]
    [InlineData("KZ", "940240000497", false)]
    [InlineData("BY", "190123456", true)]
    [InlineData("BY", "19012345A", false)]
    public void Organization_tax_id_follows_country_rules(string country, string id, bool valid) =>
        Assert.Equal(valid, Countries.IsValidOrganizationTaxId(country, id));

    [Fact]
    public void Uzbek_individual_counterparty_may_use_pinfl()
    {
        Assert.True(Countries.IsValidCounterpartyTaxId("UZ", "31234567890123"));
        Assert.False(Countries.IsValidOrganizationTaxId("UZ", "31234567890123"));
    }

    [Fact]
    public void Organization_takes_currency_from_country_and_kpp_only_in_russia()
    {
        var kz = Organization.Create("ТОО «Тест»", "ТОО «Тест»", "940240000498", null, false, "Asia/Almaty", Now, "KZ");
        Assert.Equal(("KZ", "KZT"), (kz.CountryCode, kz.CurrencyCode));
        Assert.Equal("org.kpp.not_applicable", Assert.Throws<BusinessRuleException>(() =>
            Organization.Create("ООО", "ООО", "301234567", "770501001", false, "Asia/Tashkent", Now, "UZ")).Code);
        Assert.Contains("СТИР", Assert.Throws<BusinessRuleException>(() =>
            Organization.Create("ООО", "ООО", "7700000016", null, false, "Asia/Tashkent", Now, "UZ")).Message);
        Assert.Equal("country.unknown", Assert.Throws<BusinessRuleException>(() =>
            Organization.Create("ООО", "ООО", "7700000016", null, false, "Europe/Moscow", Now, "DE")).Code);
    }

    [Fact]
    public void Counterparty_country_change_is_validated_and_audited()
    {
        var c = Counterparty.Create(1, "ТОО «Жибек»", "940240000498", null, true, false, null, "KZ");
        Assert.Equal("catalog.counterparty.kpp", Assert.Throws<BusinessRuleException>(() =>
            c.Update(c.Name, c.Inn, "770501001", true, false, null)).Code);
        Assert.Equal("KZ", c.CountryCode);
        var changes = c.Update("ООО «Беларусь»", "190123456", null, true, false, null, "BY");
        Assert.Contains(changes, ch => ch.Field == "Страна" && ch.Before == "Казахстан" && ch.After == "Беларусь");
    }

    [Theory]
    [InlineData("RU", 2025, 12, 31, 20)]
    [InlineData("RU", 2026, 1, 1, 22)]
    [InlineData("KZ", 2025, 6, 1, 12)]
    [InlineData("KZ", 2026, 10, 9, 16)]
    [InlineData("UZ", 2026, 10, 9, 12)]
    [InlineData("BY", 2026, 10, 9, 20)]
    public void Standard_rate_depends_on_date(string country, int y, int m, int d, int percent)
    {
        var standard = VatRate.DefaultsFor(1, country).Single(r => r.Kind == VatRateKind.Standard);
        Assert.Equal(percent, standard.PercentOn(new DateOnly(y, m, d)));
    }

    [Fact]
    public void Kazakh_medicines_rate_rises_in_2027_and_exempt_has_no_percent()
    {
        var rates = VatRate.DefaultsFor(1, "KZ");
        var medicines = rates.Single(r => r.Name == "Лекарства и медизделия");
        Assert.Equal(5m, medicines.PercentOn(new DateOnly(2026, 10, 9)));
        Assert.Equal(10m, medicines.PercentOn(new DateOnly(2027, 1, 1)));
        Assert.Null(medicines.PercentOn(new DateOnly(2025, 12, 31)));
        var exempt = rates.Single(r => r.Kind == VatRateKind.Exempt);
        Assert.Null(exempt.PercentOn(new DateOnly(2026, 10, 9)));
        Assert.Equal("vat.exempt_no_percent", Assert.Throws<BusinessRuleException>(() => exempt.AddPeriod(new DateOnly(2027, 1, 1), 5)).Code);
    }

    [Fact]
    public void Period_rules()
    {
        var rate = VatRate.Create(1, "Основная", VatRateKind.Standard);
        rate.AddPeriod(new DateOnly(2026, 1, 1), 22);
        Assert.Equal("vat.period_exists", Assert.Throws<BusinessRuleException>(() => rate.AddPeriod(new DateOnly(2026, 1, 1), 20)).Code);
        Assert.Equal("vat.percent", Assert.Throws<BusinessRuleException>(() => rate.AddPeriod(new DateOnly(2027, 1, 1), 22.555m)).Code);
        Assert.Equal("vat.zero", Assert.Throws<BusinessRuleException>(() =>
            VatRate.Create(1, "0%", VatRateKind.Zero).AddPeriod(new DateOnly(2026, 1, 1), 5)).Code);
    }
}
