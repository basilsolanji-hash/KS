using System.Globalization;
using KnitErp.Application.Access;
using KnitErp.Application.Audit;
using KnitErp.Application.Common;
using KnitErp.Application.Warehousing;
using KnitErp.Web.Components.Shared;

namespace KnitErp.Web.Exports;

/// <summary>
/// Выгрузка отчётов и журналов в Excel теми же фильтрами, что на экране. Данные дают сервисы — они же проверяют права
/// и область складов; здесь только раскладка по столбцам. Количества — числами, время — по часовому поясу организации.
/// </summary>
public sealed class ReportExports(
    StockService stock, StockReportService reports, AuditQueryService audit, UserAccessService access, ISpreadsheetFormat spreadsheet)
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public async Task<byte[]> StockAsync(StockFilter filter, CancellationToken ct)
    {
        var rows = await stock.BalancesAsync(filter, ct);
        return spreadsheet.Write([new SheetData("Остатки", ["Склад", "Код", "Наименование", "Тип", "Остаток", "Ед."],
            rows.Select(r => Row(r.WarehouseName, r.Code, r.Name, r.TypeName, Number(r.Quantity), r.UnitSymbol)).ToList(), Numeric(4))]);
    }

    public async Task<byte[]> MovementsAsync(MovementFilter filter, CancellationToken ct)
    {
        var report = await reports.MovementsAsync(filter, ct);
        return spreadsheet.Write([new SheetData("Движения", ["Дата", "Документ", "Склад", "Код", "Наименование", "Приход", "Расход", "Ед."],
            report.Rows.Select(r => Row(r.Date.ToString("dd.MM.yyyy"), r.Document, r.WarehouseName, r.Code, r.Name,
                r.Incoming == 0 ? "" : Number(r.Incoming), r.Outgoing == 0 ? "" : Number(r.Outgoing), r.UnitSymbol)).ToList(), Numeric(5, 6))]);
    }

    public async Task<byte[]> TurnoverAsync(TurnoverFilter filter, CancellationToken ct)
    {
        var rows = await reports.TurnoverAsync(filter, ct);
        var title = $"Обороты {filter.From:dd.MM.yyyy}–{filter.To:dd.MM.yyyy}";
        return spreadsheet.Write([new SheetData("Обороты", ["Склад", "Код", "Наименование", "На начало", "Приход", "Расход", "На конец", "Ед.", title],
            rows.Select(r => Row(r.WarehouseName, r.Code, r.Name, Number(r.Opening), Number(r.Incoming), Number(r.Outgoing), Number(r.Closing),
                r.UnitSymbol, "")).ToList(), Numeric(3, 4, 5, 6))]);
    }

    public async Task<byte[]> AuditAsync(CancellationToken ct)
    {
        var rows = await audit.ListAsync(1000, ct);
        var tz = (await access.GetCurrentAccessAsync(ct)).TimeZoneId;
        return spreadsheet.Write([new SheetData("Журнал аудита", ["Время", "Кто", "Действие", "Объект", "Было", "Стало", "Причина"],
            rows.Select(r => Row(Local(r.OccurredAtUtc, tz), r.ActorName ?? "система", AuditLabels.Action(r.Action), r.ObjectName,
                r.Before ?? "", r.After ?? "", r.Reason ?? "")).ToList())]);
    }

    public async Task<byte[]> SignInsAsync(DateOnly? from, DateOnly? to, bool failuresOnly, CancellationToken ct)
    {
        var tz = (await access.GetCurrentAccessAsync(ct)).TimeZoneId;
        var rows = await audit.ListSignInsAsync(ToUtc(from, tz), ToUtc(to?.AddDays(1), tz), failuresOnly, 5000, ct);
        return spreadsheet.Write([new SheetData("Журнал входов", ["Время", "Пользователь", "Событие", "Способ", "Подробности", "Адрес"],
            rows.Select(r => Row(Local(r.OccurredAtUtc, tz), r.UserName ?? "—", AuditLabels.Action(r.Action), r.Method ?? "", r.Detail ?? "",
                r.Address ?? "")).ToList())]);
    }

    /// <summary>Начало местных суток организации в UTC — граница фильтра по дате.</summary>
    public static DateTime? ToUtc(DateOnly? localDate, string timeZoneId)
    {
        if (localDate is not { } d)
        {
            return null;
        }

        var local = d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var tz) ? TimeZoneInfo.ConvertTimeToUtc(local, tz) : local;
    }

    private static IReadOnlyList<string> Row(params string[] cells) => cells;

    private static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static HashSet<int> Numeric(params int[] columns) => [.. columns];

    private static string Local(DateTime utc, string tz) => TimeZones.ToLocal(utc, tz).ToString("dd.MM.yyyy HH:mm:ss");
}

/// <summary>Ссылка на выгрузку с текущими фильтрами страницы; пустые фильтры не передаются.</summary>
public static class ExportLink
{
    public static string Build(string path, params (string Name, string? Value)[] query)
    {
        var parts = query.Where(q => !string.IsNullOrWhiteSpace(q.Value))
            .Select(q => $"{q.Name}={Uri.EscapeDataString(q.Value!.Trim())}").ToList();
        return parts.Count == 0 ? path : $"{path}?{string.Join("&", parts)}";
    }

    public static string? Date(DateTime? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
