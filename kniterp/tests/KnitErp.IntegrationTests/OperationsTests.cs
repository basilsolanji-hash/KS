using ClosedXML.Excel;
using KnitErp.Application.Access;
using KnitErp.Application.Catalog;
using KnitErp.Application.Common;
using KnitErp.Application.Organizations;
using KnitErp.Application.Warehousing;
using KnitErp.Domain.Access;
using KnitErp.Domain.Audit;
using KnitErp.Domain.Catalog;
using KnitErp.Domain.Warehousing;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.IntegrationTests;

/// <summary>Сверка регистра с документами, журнал входов с адресом, числа в выгрузке Excel.</summary>
public sealed class OperationsTests(SqlTestHost host) : IClassFixture<SqlTestHost>
{
    private const string Password = "длинная парольная фраза для теста";
    private static int _innSeed = 750_000_000;

    [SqlFact]
    public async Task Reconciliation_finds_movements_that_do_not_match_documents()
    {
        var f = await SetUpAsync();
        var receipt = await PostReceiptAsync(f, 10m);
        long draft;
        await using (var s = host.As(f.Org.OwnerUserId, f.Org.OrganizationId))
        {
            var result = await s.Reconciliation.RunAsync();
            Assert.True(result.Ok, string.Join("; ", result.Problems.Select(p => p.Description)));
            Assert.Equal(1, result.MovementsChecked);

            var reason = (await s.Documents.GetOptionsAsync(StockOperationKind.WriteOff)).Reasons[0].Id;
            draft = await s.Documents.CreateAsync(StockOperationKind.WriteOff, new StockDocumentHeader(f.Yarn, null, null, reason, new DateOnly(2026, 10, 2), null));
        }

        await using (var db = host.NewDb())
        {
            // В обход программы: движение без документа, движение по черновику и удалённое движение проведённого документа.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [kniterp].[stock_movements] ([OrganizationId], [WarehouseId], [ItemId], [Quantity], [OccurredOn], [Source], [SourceId], [CreatedAtUtc])
                VALUES ({f.Org.OrganizationId}, {f.Yarn}, {f.Wool}, 3, {new DateTime(2026, 10, 2)}, 2, 999999, {host.Clock.UtcNow}),
                       ({f.Org.OrganizationId}, {f.Yarn}, {f.Wool}, -1, {new DateTime(2026, 10, 2)}, 2, {draft}, {host.Clock.UtcNow});
                DISABLE TRIGGER [kniterp].[tr_stock_movements_immutable] ON [kniterp].[stock_movements];
                DELETE FROM [kniterp].[stock_movements] WHERE [Source] = 2 AND [SourceId] = {receipt};
                ENABLE TRIGGER [kniterp].[tr_stock_movements_immutable] ON [kniterp].[stock_movements];
                """);
        }

        await using (var s = host.As(f.Org.OwnerUserId, f.Org.OrganizationId))
        {
            var problems = (await s.Reconciliation.RunAsync()).Problems;
            Assert.Contains(problems, p => p.Description.Contains("без документа-основания"));
            Assert.Contains(problems, p => p.Link == $"stock-documents/{draft}" && p.Description.Contains("движения быть не должно"));
            Assert.Contains(problems, p => p.Link == $"stock-documents/{receipt}" && p.Description.Contains("в регистре 0, по документу 10"));
        }
    }

    [SqlFact]
    public async Task Sign_in_log_shows_attempts_with_client_address()
    {
        var f = await SetUpAsync();
        string email;
        await using (var s = host.As(f.Org.OwnerUserId, f.Org.OrganizationId))
        {
            email = $"emp-{Guid.NewGuid():N}@test.local";
            var invitation = await s.Access.InviteAsync(new InviteUserCommand(email, "Сотрудник Цеха", SystemRoles.Employee, null));
            await s.SignIn.AcceptInvitationAsync(invitation.SetupToken, Password, Password);
        }

        host.ClientAddress = "203.0.113.7";
        try
        {
            await using var s = host.As(null, null);
            Assert.Equal(KnitErp.Application.Authentication.SignInStatus.Failed, (await s.SignIn.PasswordSignInAsync(email, "неверный пароль")).Status);
            Assert.Equal(KnitErp.Application.Authentication.SignInStatus.Succeeded, (await s.SignIn.PasswordSignInAsync(email, Password)).Status);
        }
        finally
        {
            host.ClientAddress = null;
        }

        await using (var s = host.As(f.Org.OwnerUserId, f.Org.OrganizationId))
        {
            var all = await s.Audit.ListSignInsAsync(null, null, failuresOnly: false);
            var failed = all.First(r => r.Action == AuditActions.SignInFailed);
            Assert.Equal(("Сотрудник Цеха", "203.0.113.7", true), (failed.UserName, failed.Address, failed.Failed));
            var success = all.First(r => r.Action == AuditActions.SignedIn);
            Assert.Equal(("пароль", "203.0.113.7"), (success.Method, success.Address));
            Assert.All(await s.Audit.ListSignInsAsync(null, null, failuresOnly: true), r => Assert.True(r.Failed));
            Assert.Empty(await s.Audit.ListSignInsAsync(host.Clock.UtcNow.AddDays(1), null, false));
        }

        // Сотрудник видит только свои входы, кладовщик без права журнала — отказ.
        await using (var db = host.NewDb())
        {
            var employee = await db.Users.SingleAsync(u => u.NormalizedEmail == email.ToUpperInvariant());
            await using var s = host.As(employee.Id, f.Org.OrganizationId);
            Assert.All(await s.Audit.ListSignInsAsync(null, null, false), r => Assert.Equal("Сотрудник Цеха", r.UserName));
        }

        await using (var s = host.As(f.Keeper, f.Org.OrganizationId))
        {
            await Assert.ThrowsAsync<AccessDeniedException>(() => s.Audit.ListSignInsAsync(null, null, false));
        }
    }

    [Fact]
    public void Excel_export_writes_numbers_as_numbers_and_codes_as_text()
    {
        var bytes = SqlTestHost.Spreadsheet.Write([new SheetData("Остатки", ["Код", "Остаток"], [["0042", "1250.5"], ["ПР-1", "-3"]], new HashSet<int> { 1 })]);
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet(1);
        Assert.Equal("0042", sheet.Cell(2, 1).GetString());
        Assert.Equal(XLDataType.Number, sheet.Cell(2, 2).DataType);
        Assert.Equal(1250.5, sheet.Cell(2, 2).GetDouble());
        Assert.Equal(-3, sheet.Cell(3, 2).GetDouble());
    }

    private async Task<long> PostReceiptAsync(Fixture f, decimal qty)
    {
        await using var s = host.As(f.Org.OwnerUserId, f.Org.OrganizationId);
        var reason = (await s.Documents.GetOptionsAsync(StockOperationKind.Receipt)).Reasons[0].Id;
        var doc = await s.Documents.CreateAsync(StockOperationKind.Receipt, new StockDocumentHeader(f.Yarn, null, null, reason, new DateOnly(2026, 10, 1), null));
        await s.Documents.SetLineAsync(doc, f.Wool, qty, (await s.Documents.GetAsync(doc)).RowVersion);
        await s.Documents.PostAsync(doc, (await s.Documents.GetAsync(doc)).RowVersion);
        return doc;
    }

    private sealed record Fixture(CreatedOrganization Org, long Yarn, long Wool, long Keeper);

    private async Task<Fixture> SetUpAsync()
    {
        var inn = NextValidInn();
        CreatedOrganization org;
        await using (var s = host.As(null, null))
        {
            org = await s.Organizations.CreateWithOwnerAsync(new CreateOrganizationCommand(
                $"Тестовая организация {inn}", $"Тест {inn}", inn, null, false, "Europe/Moscow", $"owner-{inn}@test.local", $"Владелец {inn}"));
        }

        long yarn, wool, keeper;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var units = await s.Catalog.ListUnitsAsync();
            yarn = await s.Warehouses.CreateWarehouseAsync("Склад пряжи", null);
            wool = await s.Catalog.CreateItemAsync(new ItemCommand("ПР-1", "Пряжа шерсть", ItemType.RawMaterial, units.Single(u => u.Symbol == "кг").Id, null));
            keeper = (await s.Access.InviteAsync(new InviteUserCommand($"keeper-{Guid.NewGuid():N}@test.local",
                "Кладовщик", SystemRoles.Storekeeper, null, WarehouseId: yarn))).UserId;
        }

        await using var db = host.NewDb();
        (await db.Users.SingleAsync(u => u.Id == keeper)).Activate();
        await db.SaveChangesAsync();
        return new Fixture(org, yarn, wool, keeper);
    }

    private static string NextValidInn()
    {
        int[] w = [2, 4, 10, 3, 5, 9, 4, 6, 8];
        var body = Interlocked.Increment(ref _innSeed).ToString("D9");
        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            sum += (body[i] - '0') * w[i];
        }

        return body + (sum % 11 % 10);
    }
}
