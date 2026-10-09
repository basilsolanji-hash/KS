using ClosedXML.Excel;
using KnitErp.Application.Common;
using KnitErp.Domain.Common;

namespace KnitErp.Infrastructure.Spreadsheets;

/// <summary>Excel (.xlsx) через ClosedXML. Формулы не вычисляются: берётся сохранённое в файле значение.</summary>
public sealed class ClosedXmlSpreadsheet : ISpreadsheetFormat
{
    public IReadOnlyList<IReadOnlyList<string>> ReadFirstSheet(Stream stream, int maxRows)
    {
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new BusinessRuleException("import.file_invalid", "Файл не читается как таблица Excel (.xlsx). Сохраните его в формате .xlsx.");
        }

        using (workbook)
        {
            var sheet = workbook.Worksheets.FirstOrDefault()
                        ?? throw new BusinessRuleException("import.file_empty", "В файле нет листов.");
            var used = sheet.RangeUsed();
            if (used is null)
            {
                return [];
            }

            var lastRow = used.LastRow().RowNumber();
            if (lastRow > maxRows + 1)
            {
                throw new BusinessRuleException("import.too_many_rows", $"В файле больше {maxRows} строк. Разбейте его на части.");
            }

            var lastColumn = used.LastColumn().ColumnNumber();
            var rows = new List<IReadOnlyList<string>>(lastRow);
            for (var r = 1; r <= lastRow; r++)
            {
                var row = sheet.Row(r);
                var cells = new string[lastColumn];
                for (var c = 1; c <= lastColumn; c++)
                {
                    cells[c - 1] = row.Cell(c).GetFormattedString().Trim();
                }

                rows.Add(cells);
            }

            return rows;
        }
    }

    public byte[] Write(IReadOnlyList<SheetData> sheets)
    {
        using var workbook = new XLWorkbook();
        foreach (var data in sheets)
        {
            var sheet = workbook.Worksheets.Add(data.Name);
            var numeric = data.NumericColumns ?? new HashSet<int>();
            for (var c = 0; c < data.Header.Count; c++)
            {
                // Текстовый формат на весь столбец: и новые строки, которые впишет пользователь, не станут числами.
                sheet.Column(c + 1).Style.NumberFormat.Format = numeric.Contains(c) ? "#,##0.######" : "@";
                var cell = sheet.Cell(1, c + 1);
                cell.Value = data.Header[c];
                cell.Style.Font.Bold = true;
            }

            for (var r = 0; r < data.Rows.Count; r++)
            {
                for (var c = 0; c < data.Rows[r].Count; c++)
                {
                    var cell = sheet.Cell(r + 2, c + 1);
                    var text = data.Rows[r][c];
                    if (numeric.Contains(c) && decimal.TryParse(text, System.Globalization.NumberStyles.Number,
                            System.Globalization.CultureInfo.InvariantCulture, out var number))
                    {
                        cell.SetValue(number);
                        cell.Style.NumberFormat.Format = "#,##0.######";
                        continue;
                    }

                    // Остальное как текст: коды вида «0042» не должны превращаться в число 42.
                    cell.Style.NumberFormat.Format = "@";
                    cell.SetValue(text);
                }
            }

            sheet.SheetView.FreezeRows(1);
            sheet.Columns().AdjustToContents(1, Math.Min(data.Rows.Count + 1, 200));
        }

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }
}
