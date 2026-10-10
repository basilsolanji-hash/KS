using KnitErp.Domain.Common;
using KnitErp.Domain.Production;

namespace KnitErp.UnitTests;

public sealed class TechCardTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Requirement_scales_norm_by_batch_adds_waste_and_rounds_up()
    {
        // Нормы на партию 10 свитеров: 3,5 кг пряжи с отходом 4%, 50 пуговиц без отхода.
        var card = TechCard.Create(1, itemId: 100, version: 1, outputQuantity: 10, comment: null, userId: 7, Now);
        card.SetLine(200, 3.5m, 4m);
        card.SetLine(300, 50m, 0m);

        var need = card.Requirement(25, id => id == 200 ? 3 : 0).ToDictionary(n => n.ItemId);

        Assert.Equal((8.75m, 9.1m), (need[200].Net, need[200].Gross));
        Assert.Equal((125m, 125m), (need[300].Net, need[300].Gross));
        // 3 свитера: 1,05 кг × 1,04 = 1,092 кг; пуговицы — 15.
        var three = card.Requirement(3, id => id == 200 ? 3 : 0).ToDictionary(n => n.ItemId);
        Assert.Equal(1.092m, three[200].Gross);
        // Округление вверх до точности единицы: 0,0001 кг сверх граммов — это ещё 1 грамм.
        var one = TechCard.Create(1, 100, 1, 1, null, 7, Now);
        one.SetLine(200, 0.3501m, 0);
        Assert.Equal(0.351m, one.Requirement(1, _ => 3).Single().Gross);
    }

    [Fact]
    public void Lines_are_validated_and_only_draft_changes()
    {
        var card = TechCard.Create(1, 100, 1, 1, null, 7, Now);
        Assert.Equal("techcard.self", Assert.Throws<BusinessRuleException>(() => card.SetLine(100, 1, 0)).Code);
        Assert.Equal("techcard.quantity", Assert.Throws<BusinessRuleException>(() => card.SetLine(200, 0, 0)).Code);
        Assert.Equal("techcard.waste", Assert.Throws<BusinessRuleException>(() => card.SetLine(200, 1, 100)).Code);
        Assert.Equal("techcard.empty", Assert.Throws<BusinessRuleException>(() => card.Activate(7, Now)).Code);

        card.SetLine(200, 1, 0);
        card.SetLine(200, 2, 5);
        Assert.Equal((2m, 5m), (card.Lines.Single().Quantity, card.Lines.Single().WastePercent));
        card.Activate(7, Now);
        Assert.Equal(TechCardStatus.Active, card.Status);
        Assert.Equal("techcard.not_draft", Assert.Throws<BusinessRuleException>(() => card.SetLine(300, 1, 0)).Code);

        var next = card.CopyAsVersion(2, 8, Now);
        Assert.Equal((2, TechCardStatus.Draft, 1), (next.Version, next.Status, next.Lines.Count));
        next.SetLine(300, 1, 0);
        Assert.Single(card.Lines);
    }
}
