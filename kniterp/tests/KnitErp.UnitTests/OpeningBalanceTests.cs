using KnitErp.Domain.Common;
using KnitErp.Domain.Warehousing;

namespace KnitErp.UnitTests;

public sealed class OpeningBalanceTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 5, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Lifecycle_draft_submit_return_submit_approve()
    {
        var doc = New(createdBy: 7);
        Assert.Equal("stock.opening.empty", Assert.Throws<BusinessRuleException>(() => doc.Submit(Now)).Code);

        doc.SetLine(1, 10m);
        doc.SetLine(1, 12.5m);
        doc.SetLine(2, 3m);
        Assert.Equal(2, doc.Lines.Count);
        doc.Submit(Now);
        Assert.Equal("stock.opening.status", Assert.Throws<BusinessRuleException>(() => doc.SetLine(3, 1m)).Code);

        Assert.Equal("field.required", Assert.Throws<BusinessRuleException>(() => doc.ReturnToDraft(" ")).Code);
        doc.ReturnToDraft("Нет пряжи 0042");
        Assert.Equal((OpeningBalanceStatus.Draft, "Нет пряжи 0042"), (doc.Status, doc.ReturnReason));

        doc.RemoveLine(2);
        doc.Submit(Now);
        Assert.Null(doc.ReturnReason);
        doc.Approve(approverUserId: 8, Now);
        Assert.Equal(OpeningBalanceStatus.Approved, doc.Status);
        Assert.Equal("stock.opening.status", Assert.Throws<BusinessRuleException>(() => doc.Cancel()).Code);
    }

    [Fact]
    public void Author_cannot_approve_own_document()
    {
        var doc = New(createdBy: 7);
        doc.SetLine(1, 1m);
        doc.Submit(Now);
        Assert.Equal("stock.opening.self_approval", Assert.Throws<BusinessRuleException>(() => doc.Approve(7, Now)).Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Quantity_must_be_positive(int quantity)
    {
        Assert.Equal("stock.quantity.positive", Assert.Throws<BusinessRuleException>(() => New(1).SetLine(1, quantity)).Code);
    }

    [Fact]
    public void Replace_lines_rejects_duplicates_and_keeps_old_lines()
    {
        var doc = New(1);
        doc.SetLine(9, 1m);
        Assert.Equal("stock.opening.duplicate_item", Assert.Throws<BusinessRuleException>(() => doc.ReplaceLines([(1, 1m), (1, 2m)])).Code);
        Assert.Equal(9, Assert.Single(doc.Lines).ItemId);
    }

    [Fact]
    public void Document_numbers_are_sequential_and_padded()
    {
        var counter = DocumentCounter.Start(1, "НО");
        Assert.Equal("НО-000001", counter.Next());
        Assert.Equal("НО-000002", counter.Next());
    }

    [Theory]
    [InlineData("12,5", 12.5)]
    [InlineData("12.5", 12.5)]
    [InlineData("1 250", 1250)]
    [InlineData("1 250,75", 1250.75)]
    public void Quantity_from_excel_accepts_russian_formats(string text, double expected)
    {
        Assert.True(Quantities.TryParse(text, out var value));
        Assert.Equal((decimal)expected, value);
    }

    [Fact]
    public void Movement_must_not_be_zero()
    {
        Assert.Equal("stock.quantity.zero", Assert.Throws<BusinessRuleException>(() =>
            StockMovement.Create(1, 1, 1, 0m, new DateOnly(2026, 10, 1), StockSource.OpeningBalance, 1, Now)).Code);
    }

    private static OpeningBalance New(long createdBy) =>
        OpeningBalance.Create(1, "НО-000001", warehouseId: 1, new DateOnly(2026, 10, 1), null, createdBy, Now);
}
