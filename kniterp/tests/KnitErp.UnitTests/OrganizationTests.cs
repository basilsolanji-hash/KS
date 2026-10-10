using KnitErp.Domain.Common;
using KnitErp.Domain.Organizations;

namespace KnitErp.UnitTests;

public class OrganizationTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 8, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("9705239429", true)]   // ООО «Солвер»
    [InlineData("9705239428", false)]  // неверная контрольная цифра
    [InlineData("970523942", false)]
    [InlineData("97052394AB", false)]
    [InlineData(null, false)]
    public void Inn_checksum(string? inn, bool valid) =>
        Assert.Equal(valid, RussianRequisites.IsValidLegalEntityInn(inn));

    [Theory]
    [InlineData("770501001", true)]
    [InlineData("7705AB001", true)]
    [InlineData("77050100", false)]
    [InlineData("77A501001", false)]
    public void Kpp_format(string kpp, bool valid) =>
        Assert.Equal(valid, RussianRequisites.IsValidKpp(kpp));

    [Fact]
    public void Unverified_kpp_is_not_printable()
    {
        var org = Organization.Create("ООО «Солвер» полное", "ООО «Солвер»", "9705239429", "770501001", false, "Europe/Moscow", Now);
        Assert.Equal("770501001", org.Kpp);
        Assert.Null(org.PrintableKpp);

        org.ConfirmKpp("770501001");
        Assert.Equal("770501001", org.PrintableKpp);
    }

    [Fact]
    public void Invalid_inn_is_rejected() =>
        Assert.Equal("org.inn.invalid", Assert.Throws<BusinessRuleException>(() =>
            Organization.Create("Тест", "Тест", "1234567890", null, false, "Europe/Moscow", Now)).Code);

    [Fact]
    public void Requisites_update_returns_changes_for_audit()
    {
        var org = Organization.Create("Полное", "Краткое", "9705239429", null, false, "Europe/Moscow", Now);
        var changes = org.UpdateRequisites("Электросталь, ул. Ялагина, 3", "Europe/Samara");
        Assert.Equal(new[] { "ActualAddress", "TimeZoneId" }, changes.Select(c => c.Field));
        Assert.Empty(org.UpdateRequisites("Электросталь, ул. Ялагина, 3", "Europe/Samara"));
    }

    [Fact]
    public void Unknown_time_zone_is_rejected()
    {
        var org = Organization.Create("Полное", "Краткое", "9705239429", null, false, "Europe/Moscow", Now);
        Assert.Throws<BusinessRuleException>(() => org.UpdateRequisites(null, "Mars/Olympus"));
    }
}
