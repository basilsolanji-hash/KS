using System.IO.Compression;
using ClosedXML.Excel;
using KnitErp.Domain.Common;
using KnitErp.Infrastructure.Spreadsheets;

namespace KnitErp.IntegrationTests;

/// <summary>Аудит 10.10.2026, п. 7: маленький, но «широкий» или раздувающийся при распаковке файл отклоняется до обработки.</summary>
public sealed class SpreadsheetLimitsTests
{
    private static MemoryStream Workbook(Action<IXLWorksheet> fill)
    {
        using var wb = new XLWorkbook();
        fill(wb.AddWorksheet("Лист1"));
        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void Wide_sheet_with_two_cells_is_rejected()
    {
        // Как в аудите: две заполненные ячейки, но 16 384 столбца в используемой области.
        using var file = Workbook(ws => { ws.Cell(1, 1).Value = "Код"; ws.Cell(2, 16_384).Value = "x"; });
        Assert.True(file.Length < 20_000);
        var ex = Assert.Throws<BusinessRuleException>(() => new ClosedXmlSpreadsheet().ReadFirstSheet(file, 5000));
        Assert.Equal("import.too_many_columns", ex.Code);
    }

    [Fact]
    public void Archive_that_unpacks_too_big_is_rejected_before_parsing()
    {
        using var file = Workbook(ws => ws.Cell(1, 1).Value = "Код");
        using (var zip = new ZipArchive(file, ZipArchiveMode.Update, leaveOpen: true))
        {
            using var s = zip.CreateEntry("xl/media/padding.bin", CompressionLevel.SmallestSize).Open();
            var zeros = new byte[1024 * 1024];
            for (var i = 0; i < 52; i++)
            {
                s.Write(zeros);
            }
        }

        file.Position = 0;
        Assert.True(file.Length < 1_000_000);
        var ex = Assert.Throws<BusinessRuleException>(() => new ClosedXmlSpreadsheet().ReadFirstSheet(file, 5000));
        Assert.Equal("import.file_too_large_unpacked", ex.Code);
    }

    [Fact]
    public void Normal_template_is_read()
    {
        using var file = Workbook(ws => { ws.Cell(1, 1).Value = "Код"; ws.Cell(1, 2).Value = "Наименование"; ws.Cell(2, 1).Value = "ПР-1"; ws.Cell(2, 2).Value = "Пряжа"; });
        var rows = new ClosedXmlSpreadsheet().ReadFirstSheet(file, 5000);
        Assert.Equal(["ПР-1", "Пряжа"], rows[1]);
    }
}
