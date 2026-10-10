using KnitErp.Domain.Production;
using KnitErp.Domain.Sales;

namespace KnitErp.UnitTests;

/// <summary>Себестоимость для прибыльности (D69): закупки, техкарты, полуфабрикаты, циклы.</summary>
public sealed class UnitCostTests
{
    private static TechCard Card(long product, decimal batch, params (long Item, decimal Qty, decimal Waste)[] lines)
    {
        var card = TechCard.Create(1, product, 1, batch, null, 1, DateTime.UtcNow);
        foreach (var l in lines)
        {
            card.SetLine(l.Item, l.Qty, l.Waste);
        }

        return card;
    }

    [Fact]
    public void Purchases_win_then_tech_cards_roll_up_through_semi_finished()
    {
        // 1 — пряжа (закупка 80 кг на 16 000 → 200), 2 — пуговицы (закупка 1 000 шт на 500 → 0,5),
        // 3 — полотно (полуфабрикат): 1,2 кг пряжи на 2 м без отхода → 120 за метр,
        // 4 — свитер: 1,5 м полотна с отходом 10% и 6 пуговиц на 1 шт → 120 × 1,65 + 3 = 201.
        var purchases = new Dictionary<long, (decimal, decimal)> { [1] = (80m, 16_000m), [2] = (1000m, 500m) };
        var cards = new Dictionary<long, TechCard>
        {
            [3] = Card(3, 2, (1, 1.2m, 0)),
            [4] = Card(4, 1, (3, 1.5m, 10m), (2, 6, 0)),
            [1] = Card(1, 1, (2, 1, 0)), // у закупаемой пряжи техкарта не используется
        };
        var costs = UnitCosts.Calculate(purchases, cards, [4, 3, 1, 5]);
        Assert.Equal((201m, CostSource.TechCard), costs[4]);
        Assert.Equal((120m, CostSource.TechCard), costs[3]);
        Assert.Equal((200m, CostSource.Purchases), costs[1]);
        Assert.False(costs.ContainsKey(5));
    }

    [Fact]
    public void Unknown_material_or_cycle_gives_unknown_cost()
    {
        var cards = new Dictionary<long, TechCard>
        {
            [10] = Card(10, 1, (11, 1, 0)), // материал 11 без цены
            [20] = Card(20, 1, (21, 1, 0)),
            [21] = Card(21, 1, (20, 1, 0)), // цикл 20 ↔ 21
        };
        var costs = UnitCosts.Calculate(new Dictionary<long, (decimal, decimal)>(), cards, [10, 20, 21]);
        Assert.Empty(costs);
    }

    [Fact]
    public void Fully_returned_purchase_has_no_price()
    {
        var costs = UnitCosts.Calculate(new Dictionary<long, (decimal, decimal)> { [1] = (0m, 0m) }, new Dictionary<long, TechCard>(), [1]);
        Assert.Empty(costs);
    }
}
