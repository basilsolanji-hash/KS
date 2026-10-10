using KnitErp.Domain.Common;
using KnitErp.Domain.Warehousing;

namespace KnitErp.UnitTests;

public sealed class StockDocumentTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 5, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Day = new(2026, 10, 9);

    [Fact]
    public void Post_requires_lines_and_reason_then_document_is_frozen()
    {
        var doc = New(StockOperationKind.Receipt, new StockDocumentHeader(1, null, 5, null, Day, null));
        Assert.Equal("stock.document.empty", Assert.Throws<BusinessRuleException>(() => doc.Post(7, false, Now)).Code);
        doc.SetLine(10, 2m);
        Assert.Equal("stock.document.reason_required", Assert.Throws<BusinessRuleException>(() => doc.Post(7, false, Now)).Code);

        doc.UpdateHeader(new StockDocumentHeader(1, null, 5, 3, Day, null));
        doc.Post(7, false, Now);
        Assert.Equal((StockDocumentStatus.Posted, 7L), (doc.Status, doc.PostedByUserId));
        Assert.Equal("stock.document.status", Assert.Throws<BusinessRuleException>(() => doc.SetLine(11, 1m)).Code);
        Assert.Equal("stock.document.status", Assert.Throws<BusinessRuleException>(() => doc.Cancel()).Code);
    }

    [Fact]
    public void Reason_with_required_comment_needs_comment()
    {
        var doc = New(StockOperationKind.WriteOff, new StockDocumentHeader(1, null, null, 3, Day, " "));
        doc.SetLine(10, 1m);
        Assert.Equal("stock.document.comment_required", Assert.Throws<BusinessRuleException>(() => doc.Post(7, true, Now)).Code);
        doc.UpdateHeader(new StockDocumentHeader(1, null, null, 3, Day, "Моль, партия 12"));
        doc.Post(7, true, Now);
    }

    [Fact]
    public void Header_combinations_are_checked_by_kind()
    {
        Assert.Equal("stock.document.target_required", Assert.Throws<BusinessRuleException>(() =>
            New(StockOperationKind.Transfer, new StockDocumentHeader(1, null, null, null, Day, null))).Code);
        Assert.Equal("stock.document.same_warehouse", Assert.Throws<BusinessRuleException>(() =>
            New(StockOperationKind.Transfer, new StockDocumentHeader(1, 1, null, null, Day, null))).Code);
        Assert.Equal("stock.document.target_not_allowed", Assert.Throws<BusinessRuleException>(() =>
            New(StockOperationKind.WriteOff, new StockDocumentHeader(1, 2, null, null, Day, null))).Code);
        Assert.Equal("stock.document.counterparty_not_allowed", Assert.Throws<BusinessRuleException>(() =>
            New(StockOperationKind.Transfer, new StockDocumentHeader(1, 2, 5, null, Day, null))).Code);
        Assert.Equal("stock.document.kind", Assert.Throws<BusinessRuleException>(() =>
            New(StockOperationKind.Inventory, new StockDocumentHeader(1, null, null, null, Day, null))).Code);
    }

    [Fact]
    public void Movements_follow_kind_and_reversal_needs_reason()
    {
        var transfer = New(StockOperationKind.Transfer, new StockDocumentHeader(1, 2, null, 3, Day, null));
        transfer.SetLine(10, 4.5m);
        Assert.Equal([(1L, 10L, -4.5m), (2L, 10L, 4.5m)], transfer.MovementDeltas());

        var writeOff = New(StockOperationKind.WriteOff, new StockDocumentHeader(1, null, null, 3, Day, null));
        writeOff.SetLine(10, 2m);
        Assert.Equal([(1L, 10L, -2m)], writeOff.MovementDeltas());

        Assert.Equal("stock.document.status", Assert.Throws<BusinessRuleException>(() => writeOff.Reverse(7, "Ошибка", Now)).Code);
        writeOff.Post(7, false, Now);
        Assert.Equal("field.required", Assert.Throws<BusinessRuleException>(() => writeOff.Reverse(8, "", Now)).Code);
        writeOff.Reverse(8, "Списали не ту пряжу", Now);
        Assert.Equal((StockDocumentStatus.Reversed, "Списали не ту пряжу"), (writeOff.Status, writeOff.ReversalReason));
        Assert.Equal("stock.document.status", Assert.Throws<BusinessRuleException>(() => writeOff.Reverse(8, "Ещё раз", Now)).Code);
    }

    [Fact]
    public void Lines_are_one_per_item_and_positive()
    {
        var doc = New(StockOperationKind.Receipt, new StockDocumentHeader(1, null, null, null, Day, null));
        doc.SetLine(10, 1m);
        doc.SetLine(10, 3m);
        Assert.Single(doc.Lines);
        Assert.Equal("stock.quantity.positive", Assert.Throws<BusinessRuleException>(() => doc.SetLine(11, 0m)).Code);
        Assert.Equal("stock.document.duplicate_item", Assert.Throws<BusinessRuleException>(() =>
            doc.ReplaceLines([(10, 1m), (10, 2m)])).Code);
        Assert.Equal("ПМ", StockDocument.NumberPrefix(StockOperationKind.Transfer));
    }

    private static StockDocument New(StockOperationKind kind, StockDocumentHeader header) =>
        StockDocument.Create(1, "ПТ-000001", kind, header, 7, Now);
}
