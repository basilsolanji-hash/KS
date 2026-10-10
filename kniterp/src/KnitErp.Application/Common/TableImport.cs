using System.Globalization;
using KnitErp.Application.Catalog;
using KnitErp.Domain.Common;

namespace KnitErp.Application.Common;

/// <summary>Строка файла: значения ячеек по столбцам шаблона (для повторной проверки при применении) и итог проверки.</summary>
public sealed record ImportRow(int RowNumber, IReadOnlyList<string> Cells, ImportAction Action, IReadOnlyList<string> Errors)
{
    public string Cell(int index) => index < Cells.Count ? Cells[index] : string.Empty;
}

/// <summary>Протокол проверки и контрольные итоги. Применить можно только файл без ошибок.</summary>
public sealed record ImportPlan(IReadOnlyList<string> Columns, IReadOnlyList<ImportRow> Rows)
{
    public int Total => Rows.Count;
    public int ToCreate => Rows.Count(r => r.Action == ImportAction.Create);
    public int ToUpdate => Rows.Count(r => r.Action == ImportAction.Update);
    public int Unchanged => Rows.Count(r => r.Action == ImportAction.Unchanged);
    public int ErrorRows => Rows.Count(r => r.Action == ImportAction.Error);
    public bool CanApply => ErrorRows == 0 && ToCreate + ToUpdate > 0;
}

public sealed record ImportResult(int Created, int Updated);

/// <summary>
/// Общая часть шаблонных загрузок из Excel (контрагенты, сотрудники): чтение листа, проверка заголовка, разбор значений.
/// Правила загрузки те же, что у номенклатуры: сначала проверка целиком, затем всё или ничего.
/// </summary>
public static class TableImport
{
    public const int MaxRows = 5000;

    /// <summary>
    /// Строки с данными первого листа. Заголовок должен совпадать с шаблоном; последние столбцы после
    /// <paramref name="requiredColumns"/> необязательны — так файлы по старому шаблону остаются годными.
    /// </summary>
    public static IReadOnlyList<ImportRow> Read(ISpreadsheetFormat spreadsheet, Stream file, IReadOnlyList<string> columns, int? requiredColumns = null)
    {
        var sheet = spreadsheet.ReadFirstSheet(file, MaxRows);
        if (sheet.Count == 0)
        {
            throw new BusinessRuleException("import.file_empty", "Файл пустой.");
        }

        var header = sheet[0];
        for (var c = 0; c < columns.Count; c++)
        {
            var actual = c < header.Count ? header[c].Trim() : "";
            if (actual.Length == 0 && c >= (requiredColumns ?? columns.Count))
            {
                continue;
            }

            if (!string.Equals(actual, columns[c], StringComparison.OrdinalIgnoreCase))
            {
                throw new BusinessRuleException("import.header",
                    $"Столбец {c + 1} должен называться «{columns[c]}», а в файле «{actual}». Скачайте шаблон и заполните его.");
            }
        }

        var rows = sheet.Skip(1)
            .Select((cells, i) => (RowNumber: i + 2, Cells: cells))
            .Where(r => r.Cells.Any(v => v.Trim().Length > 0))
            .Select(r => new ImportRow(r.RowNumber,
                Enumerable.Range(0, columns.Count).Select(c => c < r.Cells.Count ? r.Cells[c].Trim() : string.Empty).ToList(),
                ImportAction.Error, []))
            .ToList();
        return rows.Count == 0 ? throw new BusinessRuleException("import.file_empty", "В файле нет строк с данными.") : rows;
    }

    /// <summary>Строки, переданные на применение, — те же ограничения, что у файла.</summary>
    public static void EnsureApplicable(IReadOnlyList<ImportRow> rows)
    {
        if (rows.Count is 0 or > MaxRows)
        {
            throw new BusinessRuleException("import.file_empty", "Нет строк для применения.");
        }
    }

    public static BusinessRuleException HasErrors(ImportPlan plan) =>
        new("import.has_errors",
            $"В файле есть ошибки ({plan.ErrorRows} строк) — ничего не загружено. Исправьте файл и проверьте его снова.");

    /// <summary>Повторы ключа в файле: ключ → номера строк.</summary>
    public static Dictionary<string, List<int>> Duplicates(IEnumerable<(string Key, int RowNumber)> keys) =>
        keys.Where(k => k.Key.Length > 0).GroupBy(k => k.Key)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.Select(k => k.RowNumber).ToList());

    /// <summary>«да», «+», «1», «x» — истина; пусто, «нет», «-», «0» — ложь; иное — null (ошибка строки).</summary>
    public static bool? ParseYesNo(string text) => text.Trim().ToLowerInvariant() switch
    {
        "да" or "+" or "1" or "x" or "х" or "yes" or "истина" or "true" => true,
        "" or "нет" or "-" or "0" or "no" or "ложь" or "false" => false,
        _ => null,
    };

    /// <summary>Дата из ячейки: «09.10.2026», «2026-10-09», американский формат Excel или число дней Excel.</summary>
    public static DateOnly? ParseDate(string text)
    {
        var t = text.Trim();
        if (t.Length == 0)
        {
            return null;
        }

        // Ячейка даты с форматом времени («09.10.2026 0:00:00») — берём только дату.
        var space = t.IndexOf(' ');
        if (space > 0)
        {
            t = t[..space];
        }

        string[] formats = ["dd.MM.yyyy", "d.M.yyyy", "dd.MM.yy", "yyyy-MM-dd", "M/d/yyyy", "MM/dd/yyyy", "M/d/yy"];
        if (DateOnly.TryParseExact(t, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is > 1 and < 2958466
            ? DateOnly.FromDateTime(DateTime.FromOADate(serial))
            : null;
    }

    public static string? Optional(string text) => text.Length == 0 ? null : text;
}
