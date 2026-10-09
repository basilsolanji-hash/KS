using KnitErp.Application.Access;
using KnitErp.Application.Common;
using KnitErp.Domain.Access;
using KnitErp.Domain.Audit;
using KnitErp.Domain.Catalog;
using KnitErp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.Application.Catalog;

public enum ImportAction
{
    Create,
    Update,
    Unchanged,
    Error,
}

/// <summary>Строка файла: исходные значения (для повторной проверки при применении) и итог проверки.</summary>
public sealed record ItemImportRow(
    int RowNumber, string Code, string Name, string TypeText, string UnitText, string Description,
    ImportAction Action, IReadOnlyList<string> Errors);

/// <summary>Протокол проверки и контрольные итоги. Применить можно только файл без ошибок.</summary>
public sealed record ItemImportPlan(IReadOnlyList<ItemImportRow> Rows)
{
    public int Total => Rows.Count;
    public int ToCreate => Rows.Count(r => r.Action == ImportAction.Create);
    public int ToUpdate => Rows.Count(r => r.Action == ImportAction.Update);
    public int Unchanged => Rows.Count(r => r.Action == ImportAction.Unchanged);
    public int ErrorRows => Rows.Count(r => r.Action == ImportAction.Error);
    public bool CanApply => ErrorRows == 0 && ToCreate + ToUpdate > 0;
}

public sealed record ItemImportResult(int Created, int Updated);

/// <summary>
/// Шаблонный импорт и выгрузка номенклатуры в Excel (MVP 1.0: «шаблонный импорт и экспорт основных данных,
/// протокол ошибок, контрольные итоги»). Импорт — всё или ничего: одна ошибка в файле, и не применяется ни одна строка.
/// </summary>
public sealed class ItemExchangeService(
    IKnitErpDbContext db, IAccessGuard guard, ISpreadsheetFormat spreadsheet, ICurrentUser currentUser, IClock clock)
{
    public const int MaxRows = 5000;
    public static readonly IReadOnlyList<string> Columns = ["Код", "Наименование", "Тип", "Единица", "Описание"];

    /// <summary>Пустой шаблон с примером и листом допустимых значений.</summary>
    public async Task<byte[]> TemplateAsync(CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.CatalogImport, ct);
        var units = await ActiveUnitsAsync(ctx, ct);
        return spreadsheet.Write(
        [
            new SheetData("Номенклатура", Columns,
            [
                ["ПР-0001", "Пряжа шерсть 50% серая", ItemTypes.Name(ItemType.RawMaterial), "кг", "Пример — удалите строку"],
                ["Ф-015", "Пуговица 15 мм", ItemTypes.Name(ItemType.Accessory), "шт", ""],
            ]),
            new SheetData("Допустимые значения", ["Тип", "Единица (обозначение)", "Единица (наименование)"],
                Enumerable.Range(0, Math.Max(ItemTypes.All.Count, units.Count)).Select(i => (IReadOnlyList<string>)
                [
                    i < ItemTypes.All.Count ? ItemTypes.Name(ItemTypes.All[i]) : "",
                    i < units.Count ? units[i].Symbol : "",
                    i < units.Count ? units[i].Name : "",
                ]).ToList()),
        ]);
    }

    /// <summary>Действующая номенклатура в формате шаблона: выгрузку можно поправить и загрузить обратно.</summary>
    public async Task<byte[]> ExportAsync(CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.CatalogView, ct);
        var rows = await db.Items.AsNoTracking()
            .Where(i => i.OrganizationId == ctx.OrganizationId && !i.IsArchived)
            .OrderBy(i => i.Type).ThenBy(i => i.Name)
            .Join(db.Units.AsNoTracking(), i => i.UnitId, u => u.Id, (i, u) => new { i.Code, i.Name, i.Type, u.Symbol, i.Description })
            .ToListAsync(ct);
        return spreadsheet.Write(
        [
            new SheetData("Номенклатура", Columns,
                rows.Select(r => (IReadOnlyList<string>)[r.Code, r.Name, ItemTypes.Name(r.Type), r.Symbol, r.Description ?? ""]).ToList()),
        ]);
    }

    /// <summary>Чтение и полная проверка файла. В базе ничего не меняется.</summary>
    public async Task<ItemImportPlan> PreviewAsync(Stream file, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.CatalogImport, ct);
        var sheet = spreadsheet.ReadFirstSheet(file, MaxRows);
        if (sheet.Count == 0)
        {
            throw new BusinessRuleException("import.file_empty", "Файл пустой.");
        }

        var header = sheet[0];
        for (var c = 0; c < Columns.Count; c++)
        {
            var actual = c < header.Count ? header[c] : "";
            if (!string.Equals(actual, Columns[c], StringComparison.OrdinalIgnoreCase))
            {
                throw new BusinessRuleException("import.header",
                    $"Столбец {c + 1} должен называться «{Columns[c]}», а в файле «{actual}». Скачайте шаблон и заполните его.");
            }
        }

        var raw = sheet.Skip(1)
            .Select((cells, i) => (RowNumber: i + 2, Cells: cells))
            .Where(r => r.Cells.Any(v => v.Length > 0))
            .Select(r => new ItemImportRow(r.RowNumber, Cell(r.Cells, 0), Cell(r.Cells, 1), Cell(r.Cells, 2), Cell(r.Cells, 3), Cell(r.Cells, 4),
                ImportAction.Error, []))
            .ToList();
        if (raw.Count == 0)
        {
            throw new BusinessRuleException("import.file_empty", "В файле нет строк с данными.");
        }

        return await PlanAsync(ctx, raw, ct);
    }

    /// <summary>
    /// Применение. Строки проверяются заново на текущих данных базы (между проверкой и применением справочник мог измениться);
    /// при любой ошибке не применяется ничего.
    /// </summary>
    public async Task<ItemImportResult> ApplyAsync(IReadOnlyList<ItemImportRow> rows, CancellationToken ct = default)
    {
        var ctx = await guard.DemandAsync(Permissions.CatalogImport, ct);
        if (rows.Count is 0 or > MaxRows)
        {
            throw new BusinessRuleException("import.file_empty", "Нет строк для применения.");
        }

        await using var tx = await db.BeginTransactionAsync(ct);
        var plan = await PlanAsync(ctx, rows, ct);
        if (plan.ErrorRows > 0)
        {
            throw new BusinessRuleException("import.has_errors",
                $"В файле есть ошибки ({plan.ErrorRows} строк) — ничего не загружено. Исправьте файл и проверьте его снова.");
        }

        var units = await ActiveUnitsAsync(ctx, ct);
        var existing = await db.Items.Where(i => i.OrganizationId == ctx.OrganizationId).ToDictionaryAsync(i => i.Code, ct);
        var created = new List<Item>();
        var updated = 0;
        foreach (var row in plan.Rows.Where(r => r.Action is ImportAction.Create or ImportAction.Update))
        {
            var type = ParseType(row.TypeText)!.Value;
            var unit = FindUnit(units, row.UnitText)!;
            var description = row.Description.Length == 0 ? null : row.Description;
            if (row.Action == ImportAction.Create)
            {
                var item = Item.Create(ctx.OrganizationId, row.Code, row.Name, type, unit.Id, description, clock.UtcNow);
                db.Items.Add(item);
                created.Add(item);
            }
            else
            {
                var item = existing[Item.NormalizeCode(row.Code)];
                foreach (var change in item.Update(row.Code, row.Name, type, unit.Id, description))
                {
                    Audit(ctx, AuditActions.CatalogChanged, nameof(Item), item.Id.ToString(), change.Before, change.After, $"Импорт: {change.Field}");
                }

                updated++;
            }
        }

        await db.SaveChangesAsync(ct);
        foreach (var item in created)
        {
            Audit(ctx, AuditActions.CatalogCreated, nameof(Item), item.Id.ToString(), null, $"{item.Code} {item.Name}", "Импорт");
        }

        Audit(ctx, AuditActions.CatalogImported, "ItemImport", null, null,
            $"строк {plan.Total}: создано {created.Count}, обновлено {updated}, без изменений {plan.Unchanged}", null);
        await db.SaveOrConflictAsync(ct);
        await tx.CommitAsync(ct);
        return new ItemImportResult(created.Count, updated);
    }

    private async Task<ItemImportPlan> PlanAsync(AccessContext ctx, IReadOnlyList<ItemImportRow> raw, CancellationToken ct)
    {
        var units = await ActiveUnitsAsync(ctx, ct);
        var codes = raw.Select(r => Item.NormalizeCode(r.Code)).Where(c => c.Length > 0).Distinct().ToList();
        var existing = await db.Items.AsNoTracking()
            .Where(i => i.OrganizationId == ctx.OrganizationId && codes.Contains(i.Code))
            .ToDictionaryAsync(i => i.Code, ct);
        var duplicates = raw.GroupBy(r => Item.NormalizeCode(r.Code)).Where(g => g.Key.Length > 0 && g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.Select(r => r.RowNumber).ToList());

        var rows = new List<ItemImportRow>(raw.Count);
        foreach (var r in raw)
        {
            var errors = new List<string>();
            var code = Item.NormalizeCode(r.Code);
            if (duplicates.TryGetValue(code, out var same))
            {
                errors.Add($"Код {code} повторяется в строках {string.Join(", ", same)}.");
            }

            var type = ParseType(r.TypeText);
            if (type is null)
            {
                errors.Add(r.TypeText.Length == 0 ? "Не указан тип." : $"Неизвестный тип «{r.TypeText}». Допустимые — на листе «Допустимые значения» шаблона.");
            }

            var unit = FindUnit(units, r.UnitText);
            if (unit is null)
            {
                errors.Add(r.UnitText.Length == 0 ? "Не указана единица." : $"Неизвестная единица «{r.UnitText}».");
            }

            var action = ImportAction.Error;
            if (type is not null && unit is not null)
            {
                var description = r.Description.Length == 0 ? null : r.Description;
                try
                {
                    // Те же правила, что при ручном вводе: проверка через доменную сущность.
                    var candidate = Item.Create(ctx.OrganizationId, r.Code, r.Name, type.Value, unit.Id, description, clock.UtcNow);
                    if (existing.TryGetValue(candidate.Code, out var current))
                    {
                        if (current.IsArchived)
                        {
                            errors.Add($"Код {candidate.Code} принадлежит архивной позиции «{current.Name}». Верните её из архива или смените код.");
                        }
                        else
                        {
                            action = current.Name == candidate.Name && current.Type == candidate.Type && current.UnitId == candidate.UnitId
                                     && current.Description == candidate.Description
                                ? ImportAction.Unchanged
                                : ImportAction.Update;
                        }
                    }
                    else
                    {
                        action = ImportAction.Create;
                    }
                }
                catch (BusinessRuleException ex)
                {
                    errors.Add(ex.Message);
                }
            }

            rows.Add(r with { Code = code.Length > 0 ? code : r.Code, Action = errors.Count > 0 ? ImportAction.Error : action, Errors = errors });
        }

        return new ItemImportPlan(rows);
    }

    private static ItemType? ParseType(string text) =>
        ItemTypes.All.Cast<ItemType?>().FirstOrDefault(t => string.Equals(ItemTypes.Name(t!.Value), text.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Единица по обозначению («кг»), наименованию («Килограмм») или коду ОКЕИ («166»).</summary>
    private static UnitOfMeasure? FindUnit(IReadOnlyList<UnitOfMeasure> units, string text)
    {
        var t = text.Trim();
        return t.Length == 0
            ? null
            : units.FirstOrDefault(u => string.Equals(u.Symbol, t, StringComparison.OrdinalIgnoreCase))
              ?? units.FirstOrDefault(u => string.Equals(u.Name, t, StringComparison.OrdinalIgnoreCase))
              ?? units.FirstOrDefault(u => u.Code == t);
    }

    private async Task<List<UnitOfMeasure>> ActiveUnitsAsync(AccessContext ctx, CancellationToken ct) =>
        await db.Units.AsNoTracking().Where(u => u.OrganizationId == ctx.OrganizationId && !u.IsArchived).OrderBy(u => u.Name).ToListAsync(ct);

    private static string Cell(IReadOnlyList<string> cells, int index) => index < cells.Count ? cells[index].Trim() : string.Empty;

    private void Audit(AccessContext ctx, string action, string entityType, string? id, string? before, string? after, string? reason) =>
        db.AuditEntries.Add(AuditEntry.Create(clock.UtcNow, ctx.OrganizationId, ctx.UserId, action, entityType, id,
            before, after, reason, currentUser.CorrelationId));
}
