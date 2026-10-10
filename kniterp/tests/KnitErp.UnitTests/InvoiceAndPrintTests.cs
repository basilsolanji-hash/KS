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
    public void Bank_account_is_validated_by_bik_key()
    {
        Assert.Equal("org.bank.bik_required", Assert.Throws<BusinessRuleException>(() =>
            LegalEntityAccount.Create(1, 2, Countries.Russia, "Банк", null, "40702810938000000001", null, true)).Code);
        Assert.Equal("org.bank.code", Assert.Throws<BusinessRuleException>(() =>
            LegalEntityAccount.Create(1, 2, Countries.Russia, "Банк", "04-4525225", "40702810938000000001", null, true)).Code);
        Assert.Equal("org.bank.account", Assert.Throws<BusinessRuleException>(() =>
            LegalEntityAccount.Create(1, 2, Countries.Russia, "Банк", "044525225", "40702810138000000000", null, true)).Code);
        var account = LegalEntityAccount.Create(1, 2, Countries.Russia, " Банк ", "044525225", "4070 2810 9380 0000 0001", "30101810400000000225", true);
        Assert.Equal(("Банк", "40702810938000000001"), (account.BankName, account.Account));
        account.SetArchived(true);
        Assert.False(account.IsDefault);

        // Другая страна: только символы и длина (IBAN, SWIFT).
        var kz = LegalEntityAccount.Create(1, 2, Countries.Kazakhstan, "Halyk", "HSBKKZKX", "KZ86125KZT5004100100", null, true);
        Assert.Equal("HSBKKZKX", kz.Bic);
    }

    [Fact]
    public void Legal_entity_and_sole_proprietor_requisites_follow_rf_rules()
    {
        LegalEntityData Company(string inn, string? kpp = null, string? ogrn = null) =>
            new(LegalEntityKind.Company, "Общество с ограниченной ответственностью «Тест»", "ООО «Тест»", inn, kpp, ogrn, null, "Генеральный директор",
                "Иванов И. И.", null, false);
        var ooo = LegalEntity.Create(1, Countries.Russia, Company("7707083893", "773601001", "1027700132195"), isDefault: true);
        Assert.Equal(("7707083893", "773601001", "1027700132195"), (ooo.Inn, ooo.Kpp, ooo.Ogrn));
        Assert.Equal("legal_entity.inn", Assert.Throws<BusinessRuleException>(() => LegalEntity.Create(1, Countries.Russia, Company("7707083894"), false)).Code);
        Assert.Equal("legal_entity.ogrn", Assert.Throws<BusinessRuleException>(() =>
            LegalEntity.Create(1, Countries.Russia, Company("7707083893", null, "1027700132196"), false)).Code);

        // ИП: ИНН 12 цифр, без КПП, ОГРНИП 15 цифр; должность не хранится.
        var ip = new LegalEntityData(LegalEntityKind.SoleProprietor, "Индивидуальный предприниматель Петров Пётр Петрович", "ИП Петров П. П.",
            "500100732259", null, "304500116000157", null, "Директор", "Петров П. П.", null, true);
        var sole = LegalEntity.Create(1, Countries.Russia, ip, isDefault: false);
        Assert.Equal((true, null, true), (sole.IsSoleProprietor, sole.DirectorPosition, sole.VatExempt));
        Assert.Equal("legal_entity.kpp_sole", Assert.Throws<BusinessRuleException>(() =>
            LegalEntity.Create(1, Countries.Russia, ip with { Kpp = "773601001" }, false)).Code);
        Assert.Equal("legal_entity.inn", Assert.Throws<BusinessRuleException>(() =>
            LegalEntity.Create(1, Countries.Russia, ip with { Inn = "7707083893" }, false)).Code);
        Assert.Equal("legal_entity.ogrn", Assert.Throws<BusinessRuleException>(() =>
            LegalEntity.Create(1, Countries.Russia, ip with { Ogrn = "304500116000158" }, false)).Code);

        // Основное юрлицо нельзя убрать в архив; изменения возвращаются для журнала.
        Assert.Equal("legal_entity.default_archive", Assert.Throws<BusinessRuleException>(() => ooo.SetArchived(true)).Code);
        var changes = ooo.Update(Countries.Russia, Company("7707083893", "773601001", "1027700132195") with { LegalAddress = "г. Москва" });
        Assert.Equal(["LegalAddress"], changes.Select(c => c.Field));
    }

    [Theory]
    [InlineData("1027700132195", true)]
    [InlineData("1027700132196", false)]
    [InlineData("102770013219", false)]
    public void Ogrn_check_digit(string ogrn, bool valid) => Assert.Equal(valid, RussianRequisites.IsValidOgrn(ogrn));

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

    [Theory]
    [InlineData("ОТ-000015", "15")]
    [InlineData("ОТ-001200", "1200")]
    [InlineData("ОТ-000000", "ОТ-000000")]
    public void Vat_invoice_number_is_the_sequence_digits(string shipment, string expected) =>
        Assert.Equal(expected, KnitErp.Application.Printing.PrintService.VatInvoiceNumber(shipment));


    [Fact]
    public void Line_and_order_discounts_reduce_amount_and_vat()
    {
        var order = SalesOrder.Create(1, "ЗК-000001", new SalesOrderHeader(Day, 5, 7, null, null, false, null), 1, DateTime.UtcNow);
        order.SetLine(10, 10, 1000m, 22m, 10m);
        var line = order.Lines.Single();
        Assert.Equal((900m, 10_980m, 1_980m, 1_000m), (line.NetPrice, line.Amount, line.VatAmount, line.DiscountAmount));
        Assert.Equal("sales.discount", Assert.Throws<BusinessRuleException>(() => order.SetLine(10, 10, 1000m, 22m, 100.5m)).Code);
        order.SetLine(11, 2, 500m, 22m);
        order.SetDiscount(5);
        Assert.All(order.Lines, l => Assert.Equal(5m, l.DiscountPercent));
        Assert.Equal(11_590m + 1_159m, order.Total);
        Assert.Equal(500m + 50m, order.DiscountTotal);

        // Счёт копирует цену уже со скидкой.
        order.Confirm(1, DateTime.UtcNow);
        var invoice = CustomerInvoice.Create(1, "СЧ-1", Day, null, order, null, 1, DateTime.UtcNow);
        Assert.Equal(950m, invoice.Lines.Single(l => l.ItemId == 10).Price);
    }

    [Fact]
    public void Reserve_is_set_only_in_draft_or_confirmed_order()
    {
        var order = SalesOrder.Create(1, "ЗК-000001", new SalesOrderHeader(Day, 5, 7, null, null, false, null), 1, DateTime.UtcNow);
        order.SetLine(10, 1, 1m, null);
        order.SetReserve(true);
        order.Confirm(1, DateTime.UtcNow);
        order.SetReserve(false);
        order.Close();
        Assert.Equal("sales.reserve.status", Assert.Throws<BusinessRuleException>(() => order.SetReserve(true)).Code);
    }

    [Fact]
    public void Order_details_change_in_any_status_except_cancelled()
    {
        var order = SalesOrder.Create(1, "ЗК-000001", new SalesOrderHeader(Day, 5, 7, null, null, false, null), 42, DateTime.UtcNow);
        Assert.Equal(42, order.ResponsibleUserId);
        order.SetLine(10, 1, 1m, null);
        order.Confirm(1, DateTime.UtcNow);
        order.SetDetails(new SalesOrderDetails(new TimeOnly(9, 15, 59), 3, 4, "  ул. Ткацкая, 5 ", null));
        Assert.Equal((new TimeOnly(9, 15), 3L, 4L, "ул. Ткацкая, 5", (long?)null),
            (order.OrderTime!.Value, order.ProjectId!.Value, order.ChannelId!.Value, order.DeliveryAddress, order.ResponsibleUserId));
        Assert.Throws<BusinessRuleException>(() => order.SetDetails(new SalesOrderDetails(null, null, null, new string('а', 501), null)));
        order.Cancel();
        Assert.Equal("sales.order.cancelled", Assert.Throws<BusinessRuleException>(() =>
            order.SetDetails(new SalesOrderDetails(null, null, null, null, null))).Code);
    }

    [Theory]
    [InlineData(CustomFieldType.Number, " 1 200,50 ", "1200.50")]
    [InlineData(CustomFieldType.Number, "-3.5", "-3.5")]
    [InlineData(CustomFieldType.Date, "17.10.2026", "2026-10-17")]
    [InlineData(CustomFieldType.Date, "2026-10-17", "2026-10-17")]
    [InlineData(CustomFieldType.Flag, "true", "true")]
    [InlineData(CustomFieldType.Flag, "false", null)]
    [InlineData(CustomFieldType.Text, "  Синий меланж ", "Синий меланж")]
    [InlineData(CustomFieldType.Text, "   ", null)]
    public void Custom_field_values_are_stored_in_one_format(CustomFieldType type, string input, string? expected)
    {
        var field = CustomFieldDefinition.Create(1, CustomFieldTarget.SalesOrder, "Поле", type, 10);
        Assert.Equal(expected, field.Normalize(input));
    }

    [Theory]
    [InlineData(CustomFieldType.Number, "много", "custom_field.number")]
    [InlineData(CustomFieldType.Date, "32.13.2026", "custom_field.date")]
    public void Custom_field_rejects_wrong_value_with_field_name(CustomFieldType type, string input, string code)
    {
        var field = CustomFieldDefinition.Create(1, CustomFieldTarget.SalesOrder, "Время вязания", type, 10);
        var ex = Assert.Throws<BusinessRuleException>(() => field.Normalize(input));
        Assert.Equal(code, ex.Code);
        Assert.Contains("Время вязания", ex.Message);
    }
}
