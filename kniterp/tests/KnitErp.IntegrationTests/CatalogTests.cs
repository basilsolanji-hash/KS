using KnitErp.Application.Access;
using KnitErp.Application.Catalog;
using KnitErp.Application.Common;
using KnitErp.Application.Organizations;
using KnitErp.Domain.Access;
using KnitErp.Domain.Audit;
using KnitErp.Domain.Catalog;
using KnitErp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.IntegrationTests;

/// <summary>Справочники: единицы, номенклатура, площадки, склады и область склада у кладовщика.</summary>
public sealed class CatalogTests(SqlTestHost host) : IClassFixture<SqlTestHost>
{
    private static int _innSeed = 500_000_000;

    [SqlFact]
    public async Task New_organization_gets_default_units_and_items_are_created_with_audit()
    {
        var org = await CreateOrganizationAsync();
        await using var s = host.As(org.OwnerUserId, org.OrganizationId);

        var units = await s.Catalog.ListUnitsAsync();
        Assert.Equal(UnitOfMeasure.Defaults.Count, units.Count);
        var kg = units.Single(u => u.Code == "166");
        Assert.Equal(("кг", (byte)3), (kg.Symbol, kg.Precision));

        var id = await s.Catalog.CreateItemAsync(new ItemCommand("пр-0001", "Пряжа шерсть 50% серая", ItemType.RawMaterial, kg.Id, null));
        var list = await s.Catalog.ListItemsAsync(new ItemFilter());
        var item = Assert.Single(list.Items);
        Assert.Equal((id, "ПР-0001", "кг", "Пряжа и сырьё"), (item.Id, item.Code, item.UnitSymbol, item.TypeName));
        Assert.True(list.CanEdit && list.CanArchive);

        await using var db = host.NewDb();
        Assert.True(await db.AuditEntries.AnyAsync(e => e.OrganizationId == org.OrganizationId && e.Action == AuditActions.CatalogCreated
                                                         && e.EntityType == nameof(Item) && e.EntityId == id.ToString()));
    }

    [SqlFact]
    public async Task Item_code_stays_unique_even_after_archive()
    {
        var org = await CreateOrganizationAsync();
        await using var s = host.As(org.OwnerUserId, org.OrganizationId);
        var pcs = (await s.Catalog.ListUnitsAsync()).Single(u => u.Code == "796").Id;
        var id = await s.Catalog.CreateItemAsync(new ItemCommand("F-1", "Пуговица", ItemType.Accessory, pcs, null));

        var version = (await s.Catalog.ListItemsAsync(new ItemFilter())).Items.Single().RowVersion;
        await s.Catalog.ArchiveItemAsync(id, version);
        Assert.Empty((await s.Catalog.ListItemsAsync(new ItemFilter())).Items);
        Assert.Single((await s.Catalog.ListItemsAsync(new ItemFilter(IncludeArchived: true))).Items);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            s.Catalog.CreateItemAsync(new ItemCommand("f-1", "Другая пуговица", ItemType.Accessory, pcs, null)));
        Assert.Equal("catalog.item.code_taken", ex.Code);
        Assert.Contains("архивной", ex.Message);

        var archived = (await s.Catalog.ListItemsAsync(new ItemFilter(IncludeArchived: true))).Items.Single();
        await s.Catalog.RestoreItemAsync(id, archived.RowVersion);
        Assert.Single((await s.Catalog.ListItemsAsync(new ItemFilter())).Items);
    }

    [SqlFact]
    public async Task Unit_in_use_is_not_archived_and_stale_item_edit_conflicts()
    {
        var org = await CreateOrganizationAsync();
        await using var s = host.As(org.OwnerUserId, org.OrganizationId);
        var m = (await s.Catalog.ListUnitsAsync()).Single(u => u.Code == "006");
        var id = await s.Catalog.CreateItemAsync(new ItemCommand("L-1", "Лента", ItemType.Material, m.Id, null));

        Assert.Equal("catalog.unit.in_use",
            (await Assert.ThrowsAsync<BusinessRuleException>(() => s.Catalog.ArchiveUnitAsync(m.Id, m.RowVersion))).Code);

        var version = (await s.Catalog.ListItemsAsync(new ItemFilter())).Items.Single().RowVersion;
        await s.Catalog.UpdateItemAsync(id, new ItemCommand("L-1", "Лента атласная", ItemType.Material, m.Id, null), version);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            s.Catalog.UpdateItemAsync(id, new ItemCommand("L-1", "Лента репсовая", ItemType.Material, m.Id, null), version));
    }

    [SqlFact]
    public async Task Rights_follow_p0_matrix()
    {
        var org = await CreateOrganizationAsync();
        long pcs;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            pcs = (await s.Catalog.ListUnitsAsync()).Single(u => u.Code == "796").Id;
        }

        var keeper = await InviteActiveAsync(org, SystemRoles.Storekeeper);
        var senior = await InviteActiveAsync(org, SystemRoles.SeniorStorekeeper);
        var employee = await InviteActiveAsync(org, SystemRoles.Employee);

        await using (var s = host.As(keeper, org.OrganizationId))
        {
            var list = await s.Catalog.ListItemsAsync(new ItemFilter());
            Assert.False(list.CanEdit);
            await Assert.ThrowsAsync<AccessDeniedException>(() =>
                s.Catalog.CreateItemAsync(new ItemCommand("K-1", "Кладовщик", ItemType.Other, pcs, null)));
        }

        long id;
        await using (var s = host.As(senior, org.OrganizationId))
        {
            id = await s.Catalog.CreateItemAsync(new ItemCommand("S-1", "Старший", ItemType.Other, pcs, null));
            var row = (await s.Catalog.ListItemsAsync(new ItemFilter())).Items.Single();
            var ex = await Assert.ThrowsAsync<AccessDeniedException>(() => s.Catalog.ArchiveItemAsync(id, row.RowVersion));
            Assert.Equal(Permissions.CatalogArchive, ex.PermissionCode);
            Assert.False((await s.Warehouses.GetAsync()).CanArchive);
        }

        await using (var s = host.As(employee, org.OrganizationId))
        {
            Assert.Single((await s.Catalog.ListItemsAsync(new ItemFilter())).Items);
        }
    }

    [SqlFact]
    public async Task Sites_and_warehouses_with_storekeeper_scope()
    {
        var org = await CreateOrganizationAsync();
        long site, main, yarn;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            site = await s.Warehouses.CreateSiteAsync("Электросталь", "г. Электросталь, ул. Тестовая, 1");
            main = await s.Warehouses.CreateWarehouseAsync("Склад готовой продукции", site);
            yarn = await s.Warehouses.CreateWarehouseAsync("Склад пряжи", site);
            Assert.Equal("warehouse.duplicate", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
                s.Warehouses.CreateWarehouseAsync("Склад пряжи", null))).Code);

            var data = await s.Warehouses.GetAsync();
            Assert.Equal(2, data.Sites.Single().WarehouseCount);
            Assert.All(data.Warehouses, w => Assert.Equal("Электросталь", w.SiteName));
            Assert.Equal("warehouse.site.has_warehouses", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
                s.Warehouses.ArchiveSiteAsync(site, data.Sites.Single().RowVersion))).Code);
        }

        long keeper;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var preview = await s.Access.PreviewRoleAsync(null, SystemRoles.Storekeeper, warehouseId: yarn);
            Assert.Equal("склад «Склад пряжи»", preview.ScopeText);
            keeper = (await s.Access.InviteAsync(new InviteUserCommand($"keeper-{Guid.NewGuid():N}@test.local", "Кладовщик пряжи",
                SystemRoles.Storekeeper, null, WarehouseId: yarn))).UserId;
            var row = (await s.Access.ListUsersAsync()).Single(u => u.UserId == keeper);
            Assert.Equal(("склад «Склад пряжи»", (long?)yarn), (row.ScopeText, row.WarehouseId));
        }

        await using (var db = host.NewDb())
        {
            (await db.Users.SingleAsync(u => u.Id == keeper)).Activate();
            await db.SaveChangesAsync();
        }

        await using (var s = host.As(keeper, org.OrganizationId))
        {
            Assert.True((await s.Access.GetCurrentAccessAsync()).Has(Permissions.WarehouseDocumentCreate));
        }

        await using (var db = host.NewDb())
        {
            var guard = new AccessGuard(db, new TestUser { UserId = keeper, OrganizationId = org.OrganizationId }, host.Clock);
            var rights = await guard.LoadAsync(keeper, org.OrganizationId);
            var scope = rights.ScopeOf(Permissions.WarehouseDocumentCreate);
            Assert.True(scope.CoversWarehouse(yarn));
            Assert.False(scope.CoversWarehouse(main));
        }
    }

    [SqlFact]
    public async Task Foreign_unit_or_warehouse_is_not_found()
    {
        var a = await CreateOrganizationAsync();
        var b = await CreateOrganizationAsync();
        long foreignUnit, foreignWarehouse;
        await using (var s = host.As(b.OwnerUserId, b.OrganizationId))
        {
            foreignUnit = (await s.Catalog.ListUnitsAsync()).First().Id;
            foreignWarehouse = await s.Warehouses.CreateWarehouseAsync("Чужой склад", null);
        }

        await using (var s = host.As(a.OwnerUserId, a.OrganizationId))
        {
            await Assert.ThrowsAsync<NotFoundException>(() =>
                s.Catalog.CreateItemAsync(new ItemCommand("X-1", "Чужая единица", ItemType.Other, foreignUnit, null)));
            await Assert.ThrowsAsync<NotFoundException>(() =>
                s.Access.InviteAsync(new InviteUserCommand("x@test.local", "Икс", SystemRoles.Storekeeper, null, WarehouseId: foreignWarehouse)));
        }
    }

    private async Task<CreatedOrganization> CreateOrganizationAsync()
    {
        var inn = NextValidInn();
        await using var s = host.As(null, null);
        return await s.Organizations.CreateWithOwnerAsync(new CreateOrganizationCommand(
            $"Тестовая организация {inn}", $"Тест {inn}", inn, null, false, "Europe/Moscow",
            $"owner-{inn}@test.local", $"Владелец {inn}"));
    }

    private async Task<long> InviteActiveAsync(CreatedOrganization org, string roleCode)
    {
        long id;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            id = (await s.Access.InviteAsync(new InviteUserCommand($"{roleCode}-{Guid.NewGuid():N}@test.local",
                SystemRoles.NameOf(roleCode), roleCode, SystemRoles.IsAdministrative(roleCode) ? "Тест" : null))).UserId;
        }

        await using var db = host.NewDb();
        (await db.Users.SingleAsync(u => u.Id == id)).Activate();
        await db.SaveChangesAsync();
        return id;
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
