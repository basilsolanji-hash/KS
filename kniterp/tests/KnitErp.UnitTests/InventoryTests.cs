using KnitErp.Domain.Common;
using KnitErp.Domain.Warehousing;

namespace KnitErp.UnitTests;

public sealed class InventoryTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 5, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Day = new(2026, 10, 31);

    [Fact]
    public void Post_needs_all_counted_and_writes_differences_against_book_at_posting()
    {
        var doc = New();
        doc.FillFromBook(new Dictionary<long, decimal> { [1] = 10m, [2] = 5m });
        doc.SetCounted(1, 12m, 0);
        Assert.Equal("stock.inventory.not_counted", Assert.Throws<BusinessRuleException>(() =>
            doc.Post(new Dictionary<long, decimal>(), 7, Now)).Code);

        doc.SetCounted(2, 5m, 0);
        doc.SetCounted(3, 1m, bookIfNew: 0);

        // Пока шёл пересчёт, по позиции 1 пришло ещё 2: учёт на момент проведения — 12, разницы нет.
        var diff = doc.Post(new Dictionary<long, decimal> { [1] = 12m, [2] = 5m }, 7, Now);
        Assert.Equal([(3L, 1m)], diff);
        Assert.Equal(InventoryStatus.Posted, doc.Status);
        Assert.Equal("stock.inventory.status", Assert.Throws<BusinessRuleException>(() => doc.SetCounted(1, 1m, 0)).Code);
    }

    [Fact]
    public void Shortage_requires_comment_and_counts_cannot_be_negative()
    {
        var doc = New();
        doc.FillFromBook(new Dictionary<long, decimal> { [1] = 10m });
        Assert.Equal("stock.quantity.negative", Assert.Throws<BusinessRuleException>(() => doc.SetCounted(1, -1m, 0)).Code);
        doc.SetCounted(1, 0m, 0);
        Assert.Equal(-10m, doc.Lines.Single().Difference);
        Assert.Equal("stock.inventory.comment_required", Assert.Throws<BusinessRuleException>(() =>
            doc.Post(new Dictionary<long, decimal> { [1] = 10m }, 7, Now)).Code);
        doc.UpdateHeader(Day, "Моль, 10 кг выброшено");
        Assert.Equal([(1L, -10m)], doc.Post(new Dictionary<long, decimal> { [1] = 10m }, 7, Now));
    }

    [Fact]
    public void Refill_keeps_counted_and_zeroes_book_of_items_gone_from_stock()
    {
        var doc = New();
        doc.FillFromBook(new Dictionary<long, decimal> { [1] = 10m, [2] = 3m });
        doc.SetCounted(1, 9m, 0);
        Assert.Equal(1, doc.FillFromBook(new Dictionary<long, decimal> { [1] = 11m, [4] = 2m }));
        Assert.Equal((11m, (decimal?)9m), (doc.Lines.Single(l => l.ItemId == 1).BookQuantity, doc.Lines.Single(l => l.ItemId == 1).CountedQuantity));
        Assert.Equal(0m, doc.Lines.Single(l => l.ItemId == 2).BookQuantity);
    }

    [Fact]
    public void Period_closes_forward_and_reopens_back_with_reason()
    {
        var p = PeriodClosure.Start(1, 7, Now);
        Assert.False(p.IsClosed(Day));
        p.CloseThrough(new DateOnly(2026, 9, 30), 7, Now);
        Assert.True(p.IsClosed(new DateOnly(2026, 9, 30)));
        Assert.False(p.IsClosed(new DateOnly(2026, 10, 1)));
        Assert.Equal("period.close.backwards", Assert.Throws<BusinessRuleException>(() => p.CloseThrough(new DateOnly(2026, 8, 31), 7, Now)).Code);
        Assert.Equal("field.required", Assert.Throws<BusinessRuleException>(() => p.Reopen(new DateOnly(2026, 8, 31), " ", 7, Now)).Code);
        Assert.Equal("period.reopen.forward", Assert.Throws<BusinessRuleException>(() => p.Reopen(new DateOnly(2026, 10, 31), "x", 7, Now)).Code);
        p.Reopen(null, "Нашли непроведённую накладную", 7, Now);
        Assert.Null(p.ClosedThrough);
    }

    private static InventoryCount New() => InventoryCount.Create(1, "ИН-000001", 1, Day, null, 7, Now);
}
