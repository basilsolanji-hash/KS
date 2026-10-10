using KnitErp.Domain.Catalog;
using KnitErp.Domain.Common;
using KnitErp.Domain.Organizations;
using KnitErp.Domain.Purchasing;
using KnitErp.Domain.Sales;

namespace KnitErp.UnitTests;

/// <summary>Счета и печатные формы (D68): сумма прописью, банковские реквизиты, счёт покупателю.</summary>
public sealed class InvoiceAndPrintTests
{
    private static readonly DateOnly Day = new(2026, 10, 10);

    [Theory]
    [InlineData(61_000, "RUB", "Шестьдесят одна тысяча рублей 00 копеек")]
    [InlineData(0.01, "RUB", "Ноль рублей 01 копейка")]
    [InlineData(1, "RUB", "Один рубль 00 копеек")]
    [InlineData(2.22, "RUB", "Два рубля 22 копейки")]
    [InlineData(11, "RUB", "Одиннадцать рублей 00 копеек")]
    [InlineData(112_514.5, "RUB", "Сто двенадцать тысяч пятьсот четырнадцать рублей 50 копеек")]
    [InlineData(2_001_002, "RUB", "Два миллиона одна тысяча два рубля 00 копеек")]
    [InlineData(1_000_000_000, "RUB", "Один миллиард рублей 00 копеек")]
    [InlineData(3_542.07, "KZT", "Три тысячи пятьсот сорок два тенге 07 тиынов")]
    [InlineData(21, "UZS", "Двадцать один сум 00 тийинов")]
    [InlineData(4, "BYN", "Четыре белорусских рубля 00 копеек")]
    [InlineData(-5, "RUB", "Минус пять рублей 00 копеек")]
    public void Amount_in_words(decimal amount, string currency, string expected) =>
        Assert.Equal(expected, AmountInWords.Format(amount, currency));

    [Fact]
    public void Unknown_currency_is_printed_as_number() => Assert.Equal("15.50 EUR", AmountInWords.Format(15.5m, "EUR"));

    [Fact]
    public void Bank_accounts_are_checked_against_bik()
    {
        Assert.True(RussianRequisites.IsValidBik("044525225"));
        Assert.False(RussianRequisites.IsValidBik("144525225"));
        Assert.True(RussianRequisites.IsValidCorrespondentAccount("30101810400000000225", "044525225"));
        Assert.False(RussianRequisites.IsValidCorrespondentAccount("30101810400000000226", "044525225"));
        Assert.False(RussianRequisites.IsValidCorrespondentAccount("40702810938000000001", "044525225"));
        Assert.True(RussianRequisites.IsValidSettlementAccount("40702810938000000001", "044525225"));
        Assert.False(RussianRequisites.IsValidSettlementAccount("40702810138000000000", "044525225"));
    }

    [Fact]
    public void Print_requisites_are_validated_and_logged()
    {
        var org = Organization.Create("ООО «Тест»", "Тест", "7707083893", null, false, "Europe/Moscow", DateTime.UtcNow);
        Assert.Equal("org.bank.bik_required", Assert.Throws<BusinessRuleException>(() =>
            org.UpdatePrintRequisites(new PrintRequisites(null, null, null, "40702810938000000001", null, null, null))).Code);
        Assert.Equal("org.bank.code", Assert.Throws<BusinessRuleException>(() =>
            org.UpdatePrintRequisites(new PrintRequisites(null, null, "04-4525225", null, null, null, null))).Code);
        var changes = org.UpdatePrintRequisites(new PrintRequisites(" г. Москва ", "Банк", "044525225", "4070 2810 9380 0000 0001", null, "Иванов И. И.", null));
        Assert.Equal(["LegalAddress", "BankName", "BankBic", "BankAccount", "DirectorName"], changes.Select(c => c.Field));
        Assert.Equal(("г. Москва", "40702810938000000001"), (org.LegalAddress, org.BankAccount));
        Assert.Empty(org.UpdatePrintRequisites(new PrintRequisites("г. Москва", "Банк", "044525225", "40702810938000000001", null, "Иванов И. И.", null)));

        // Другая страна: только символы и длина (IBAN, SWIFT).
        var kz = Organization.Create("ТОО «Тест»", "Тест", "980630000970", null, false, "Asia/Almaty", DateTime.UtcNow, Countries.Kazakhstan);
        kz.UpdatePrintRequisites(new PrintRequisites(null, "Halyk", "HSBKKZKX", "KZ86125KZT5004100100", null, null, null));
        Assert.Equal("HSBKKZKX", kz.BankBic);
    }

    [Fact]
    public void Counterparty_address_changes_separately()
    {
        var c = Counterparty.Create(1, "ООО «Магазин»", null, null, false, true, null);
        Assert.Equal("Адрес", c.SetAddress(" г. Тверь ")!.Field);
        Assert.Null(c.SetAddress("г. Тверь"));
        c.Update("ООО «Магазин»", null, null, false, true, "комментарий");
        Assert.Equal("г. Тверь", c.Address);
        Assert.NotNull(c.SetAddress(""));
        Assert.Null(c.Address);
    }

    [Fact]
    public void Invoice_copies_confirmed_order_and_cancels_with_reason()
    {
        var order = SalesOrder.Create(1, "ЗК-000001", new SalesOrderHeader(Day, 5, 7, null, null, false, null), 1, DateTime.UtcNow);
        order.SetLine(10, 25, 2000m, 22m);
        Assert.Equal("sales.invoice.order_status", Assert.Throws<BusinessRuleException>(() =>
            CustomerInvoice.Create(1, "СЧ-000001", Day, null, order, null, 1, DateTime.UtcNow)).Code);
        order.Confirm(1, DateTime.UtcNow);
        Assert.Equal("sales.invoice.due_date", Assert.Throws<BusinessRuleException>(() =>
            CustomerInvoice.Create(1, "СЧ-000001", Day, Day.AddDays(-1), order, null, 1, DateTime.UtcNow)).Code);

        var invoice = CustomerInvoice.Create(1, "СЧ-000001", Day, Day.AddDays(5), order, null, 1, DateTime.UtcNow);
        Assert.Equal((61_000m, 11_000m, 5L, false), (invoice.Total, invoice.VatTotal, invoice.CustomerId, invoice.PricesIncludeVat));
        Assert.Equal("field.required", Assert.Throws<BusinessRuleException>(() => invoice.Cancel(1, " ", DateTime.UtcNow)).Code);
        invoice.Cancel(1, "другой срок", DateTime.UtcNow);
        Assert.Equal(CustomerInvoiceStatus.Cancelled, invoice.Status);
        Assert.Equal("sales.invoice.cancelled", Assert.Throws<BusinessRuleException>(() => invoice.Cancel(1, "ещё раз", DateTime.UtcNow)).Code);
    }

    [Theory]
    [InlineData("77", 0, 0, "purchase.vat_invoice.amount")]
    [InlineData("77", 100, 100, "purchase.vat_invoice.amount")]
    [InlineData("77", 100.001, 0, "purchase.vat_invoice.amount")]
    [InlineData("77", 100, -1, "purchase.vat_invoice.amount")]
    [InlineData("", 100, 18, "field.required")]
    public void Received_vat_invoice_is_validated(string number, decimal amount, decimal vat, string code) =>
        Assert.Equal(code, Assert.Throws<BusinessRuleException>(() =>
            ReceivedVatInvoice.Register(1, 2, 3, number, Day, amount, vat, null, 1, DateTime.UtcNow)).Code);

    [Fact]
    public void Received_vat_invoice_cancels_once_with_reason()
    {
        var invoice = ReceivedVatInvoice.Register(1, 2, 3, " 77 ", Day, 122m, 22m, null, 1, DateTime.UtcNow);
        Assert.Equal(("77", 100m, ReceivedVatInvoiceStatus.Registered), (invoice.SupplierNumber, invoice.AmountWithoutVat, invoice.Status));
        invoice.Cancel(1, "ошибка", DateTime.UtcNow);
        Assert.Equal("purchase.vat_invoice.cancelled", Assert.Throws<BusinessRuleException>(() => invoice.Cancel(1, "ещё", DateTime.UtcNow)).Code);
    }
}
