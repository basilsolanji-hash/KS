using KnitErp.Application.Access;
using KnitErp.Application.Catalog;
using KnitErp.Application.Common;
using KnitErp.Application.Organizations;
using KnitErp.Application.Structure;
using KnitErp.Application.Warehousing;
using KnitErp.Domain.Access;
using KnitErp.Domain.Audit;
using KnitErp.Domain.Catalog;
using KnitErp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.IntegrationTests;

/// <summary>Подготовка пилота: загрузка контрагентов и сотрудников из Excel, «Готовность к запуску».</summary>
public sealed class PilotPreparationTests(SqlTestHost host) : IClassFixture<SqlTestHost>
{
    private static int _innSeed = 760_000_000;

    [SqlFact]
    public async Task Counterparty_import_creates_updates_and_reports_errors_all_or_nothing()
    {
        var org = await CreateOrganizationAsync();
        var existingInn = NextValidInn();
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            await s.Counterparties.CreateAsync(new CounterpartyCommand("ООО «Старое имя»", existingInn, null, true, false, null));
            var archived = await s.Counterparties.CreateAsync(new CounterpartyCommand("ИП Архивный", null, null, true, false, null));
            var row = (await s.Counterparties.ListAsync(new CounterpartyFilter())).Counterparties.Single(c => c.Id == archived);
            await s.Counterparties.ArchiveAsync(archived, row.RowVersion);
        }

        var newInn = NextValidInn();
        ImportPlan plan;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            plan = await s.CounterpartyExchange.PreviewAsync(Workbook(CounterpartyExchangeService.Columns,
                ["ООО «Новое имя»", existingInn, "", "да", "", ""],
                ["ООО «Пряжа»", newInn, "", "+", "нет", "поставщик шерсти"],
                ["ИП Архивный", "", "", "да", "нет", ""],
                ["ООО «Повтор»", newInn, "", "да", "нет", ""],
                ["Без роли", "", "", "нет", "нет", ""],
                ["Плохой ИНН", "1234567890", "", "да", "нет", ""],
                ["Непонятно", "", "", "может", "нет", ""]));
        }

        Assert.Equal(7, plan.Total);
        Assert.Equal(ImportAction.Update, plan.Rows[0].Action);
        Assert.Contains("в архиве", string.Join(" ", plan.Rows[2].Errors));
        Assert.Contains("повторяется в строках 3, 5", string.Join(" ", plan.Rows[1].Errors));
        Assert.Contains(plan.Rows[4].Errors, e => e.Contains("поставщик это или покупатель"));
        Assert.Contains(plan.Rows[5].Errors, e => e.Contains("ИНН указан неверно"));
        Assert.Contains(plan.Rows[6].Errors, e => e.Contains("«да» или «нет»"));
        Assert.False(plan.CanApply);

        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            Assert.Equal("import.has_errors", (await Assert.ThrowsAsync<BusinessRuleException>(() => s.CounterpartyExchange.ApplyAsync(plan.Rows))).Code);
        }

        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            plan = await s.CounterpartyExchange.PreviewAsync(Workbook(CounterpartyExchangeService.Columns,
                ["ООО «Новое имя»", existingInn, "", "да", "", ""],
                ["ООО «Пряжа»", newInn, "", "+", "нет", "поставщик шерсти"],
                ["ИП Сидоров", "", "", "да", "да", ""]));
            Assert.True(plan.CanApply);
            var result = await s.CounterpartyExchange.ApplyAsync(plan.Rows);
            Assert.Equal((2, 1), (result.Created, result.Updated));
        }

        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var list = (await s.Counterparties.ListAsync(new CounterpartyFilter())).Counterparties;
            Assert.Contains(list, c => c.Inn == existingInn && c.Name == "ООО «Новое имя»");
            Assert.Contains(list, c => c.Name == "ИП Сидоров" && c.IsSupplier && c.IsCustomer);

            // Выгрузка загружается обратно без изменений.
            var again = await s.CounterpartyExchange.PreviewAsync(new MemoryStream(await s.CounterpartyExchange.ExportAsync()));
            Assert.All(again.Rows, r => Assert.Equal(ImportAction.Unchanged, r.Action));
            Assert.False(again.CanApply);
            Assert.NotEmpty(await s.CounterpartyExchange.TemplateAsync());

            var audit = await s.Db.AuditEntries.Where(a => a.OrganizationId == org.OrganizationId && a.Action == AuditActions.CatalogImported).ToListAsync();
            Assert.Single(audit);
            Assert.Contains("создано 2, обновлено 1", audit[0].After);
        }
    }

    [SqlFact]
    public async Task Employee_import_hires_by_number_and_updates_existing()
    {
        var org = await CreateOrganizationAsync();
        long knitting, sewing, position, headId;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            knitting = await s.Structure.CreateDepartmentAsync("Вязальный цех", null);
            sewing = await s.Structure.CreateDepartmentAsync("Швейный цех", null);
            position = await s.Structure.CreatePositionAsync("Вязальщица");
            await s.Structure.CreatePositionAsync("Швея");
            var dismissed = await s.Employees.HireAsync(new EmployeeCommand("0099", "Уволенная", "Анна", null, knitting, position, new DateOnly(2020, 1, 1)));
            var row = (await s.Employees.ListAsync(new EmployeeFilter())).Employees.Single(e => e.Id == dismissed);
            await s.Employees.DismissAsync(dismissed, new DateOnly(2025, 1, 1), "по собственному", row.RowVersion);
            headId = (await s.Access.InviteAsync(new InviteUserCommand($"head-{Guid.NewGuid():N}@test.local", "Начальник вязального",
                SystemRoles.DepartmentHead, null, DepartmentId: knitting))).UserId;
        }

        await using (var db = host.NewDb())
        {
            (await db.Users.SingleAsync(u => u.Id == headId)).Activate();
            await db.SaveChangesAsync();
        }

        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var plan = await s.EmployeeExchange.PreviewAsync(Workbook(EmployeeExchangeService.Columns,
                ["0001", "Иванова", "Мария", "Петровна", "вязальный цех", "Вязальщица", "09.10.2026"],
                ["0002", "Петрова", "Ольга", "", "Швейный цех", "Швея", "2026-10-01"],
                ["0099", "Уволенная", "Анна", "", "Вязальный цех", "Вязальщица", "01.01.2020"],
                ["0003", "Без", "Даты", "", "Вязальный цех", "Вязальщица", ""],
                ["0004", "Нет", "Цеха", "", "Цех мечты", "Вязальщица", "01.10.2026"]));
            Assert.Equal(ImportAction.Create, plan.Rows[0].Action);
            Assert.Equal(ImportAction.Create, plan.Rows[1].Action);
            Assert.Contains(plan.Rows[2].Errors, e => e.Contains("уволенного"));
            Assert.Contains(plan.Rows[3].Errors, e => e.Contains("дата приёма"));
            Assert.Contains(plan.Rows[4].Errors, e => e.Contains("«Цех мечты» нет среди действующих"));

            var good = await s.EmployeeExchange.PreviewAsync(Workbook(EmployeeExchangeService.Columns,
                ["0001", "Иванова", "Мария", "Петровна", "Вязальный цех", "Вязальщица", "09.10.2026"],
                ["0002", "Петрова", "Ольга", "", "Швейный цех", "Швея", "46296"]));
            var result = await s.EmployeeExchange.ApplyAsync(good.Rows);
            Assert.Equal((2, 0), (result.Created, result.Updated));
        }

        // Руководитель цеха сотрудников не меняет (P0), а выгрузка показывает только его цех.
        await using (var s = host.As(headId, org.OrganizationId))
        {
            await Assert.ThrowsAsync<AccessDeniedException>(() => s.EmployeeExchange.PreviewAsync(Workbook(EmployeeExchangeService.Columns,
                ["0001", "Иванова", "Мария", "", "Вязальный цех", "Вязальщица", ""])));
            var sheet = SqlTestHost.Spreadsheet.ReadFirstSheet(new MemoryStream(await s.EmployeeExchange.ExportAsync()), 100);
            Assert.Equal(2, sheet.Count);
            Assert.Equal("0001", sheet[1][0]);
        }

        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var plan = await s.EmployeeExchange.PreviewAsync(Workbook(EmployeeExchangeService.Columns,
                ["0001", "Иванова", "Мария", "Петровна", "Вязальный цех", "Вязальщица", ""],
                ["0002", "Петрова", "Ольга", "Ивановна", "Швейный цех", "Швея", ""]));
            Assert.Equal(ImportAction.Unchanged, plan.Rows[0].Action);
            Assert.Equal(ImportAction.Update, plan.Rows[1].Action);
            var result = await s.EmployeeExchange.ApplyAsync(plan.Rows);
            Assert.Equal((0, 1), (result.Created, result.Updated));
        }

        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var employees = (await s.Employees.ListAsync(new EmployeeFilter())).Employees;
            var ivanova = employees.Single(e => e.PersonnelNumber == "0001");
            Assert.Equal(new DateOnly(2026, 10, 9), ivanova.HiredOn);
            Assert.Equal("Петровна", ivanova.MiddleName);
            Assert.Equal(new DateOnly(2026, 10, 1), employees.Single(e => e.PersonnelNumber == "0002").HiredOn);
            Assert.Equal("Петрова Ольга Ивановна", employees.Single(e => e.PersonnelNumber == "0002").FullName);
            Assert.Equal(sewing, employees.Single(e => e.PersonnelNumber == "0002").DepartmentId);
        }
    }

    [SqlFact]
    public async Task Launch_readiness_follows_the_data_and_is_for_owner_and_admin()
    {
        var org = await CreateOrganizationAsync();
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var dto = await s.Readiness.GetAsync();
            Assert.False(dto.Ready);
            Assert.False(Item(dto, "requisites").Done);
            Assert.Contains("КПП не сверен", Item(dto, "requisites").Detail);
            Assert.False(Item(dto, "warehouses").Done);
            Assert.False(Item(dto, "opening").Done);
            Assert.Null(Item(dto, "backup").Done);
            Assert.True(Item(dto, "waiting").Done);
        }

        long keeper;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var units = await s.Catalog.ListUnitsAsync();
            var yarn = await s.Warehouses.CreateWarehouseAsync("Склад пряжи", null);
            var wool = await s.Catalog.CreateItemAsync(new ItemCommand("ПР-1", "Пряжа", ItemType.RawMaterial, units.Single(u => u.Symbol == "кг").Id, null));
            await s.Counterparties.CreateAsync(new CounterpartyCommand("ООО «Пряжа»", null, null, true, false, null));
            keeper = (await s.Access.InviteAsync(new InviteUserCommand($"senior-{Guid.NewGuid():N}@test.local", "Старший кладовщик",
                SystemRoles.SeniorStorekeeper, null, WarehouseId: yarn))).UserId;

            var dto = await s.Readiness.GetAsync();
            Assert.True(Item(dto, "warehouses").Done);
            Assert.True(Item(dto, "items").Done);
            Assert.True(Item(dto, "suppliers").Done);
            Assert.Contains("Склад пряжи", Item(dto, "opening").Detail);
            // Приглашённый, но не активированный пользователь ещё не работает.
            Assert.Contains("Старший кладовщик", Item(dto, "users").Detail);
            _ = wool;
        }

        await using (var db = host.NewDb())
        {
            (await db.Users.SingleAsync(u => u.Id == keeper)).Activate();
            await db.SaveChangesAsync();
        }

        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var users = Item(await s.Readiness.GetAsync(), "users");
            Assert.DoesNotContain("Старший кладовщик", users.Detail);
            Assert.Contains("Кладовщик", users.Detail);
        }

        await using (var s = host.As(keeper, org.OrganizationId))
        {
            await Assert.ThrowsAsync<AccessDeniedException>(() => s.Readiness.GetAsync());
        }
    }

    private static ReadinessItem Item(LaunchReadinessDto dto, string id) => dto.Items.Single(i => i.Id == id);

    private static MemoryStream Workbook(IReadOnlyList<string> columns, params IReadOnlyList<string>[] rows) =>
        new(SqlTestHost.Spreadsheet.Write([new SheetData("Лист", columns, rows)]));

    private async Task<CreatedOrganization> CreateOrganizationAsync()
    {
        var inn = NextValidInn();
        await using var s = host.As(null, null);
        return await s.Organizations.CreateWithOwnerAsync(new CreateOrganizationCommand(
            $"Тестовая организация {inn}", $"Тест {inn}", inn, "770501001", false, "Europe/Moscow", $"owner-{inn}@test.local", $"Владелец {inn}"));
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
