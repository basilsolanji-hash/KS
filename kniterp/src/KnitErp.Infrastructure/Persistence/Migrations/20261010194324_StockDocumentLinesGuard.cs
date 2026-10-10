using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnitErp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StockDocumentLinesGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LinesRevision",
                schema: "kniterp",
                table: "stock_documents",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Строки складского документа меняются только у черновика (аудит 10.10.2026, п. 1): проведённый или сторнированный
            // документ не изменить даже устаревшей формой или в обход приложения.
            migrationBuilder.Sql("""
                EXEC(N'CREATE OR ALTER TRIGGER [kniterp].[tr_stock_document_lines_draft_only]
                ON [kniterp].[stock_document_lines]
                AFTER INSERT, UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM (SELECT [DocumentId] FROM inserted UNION ALL SELECT [DocumentId] FROM deleted) x
                               JOIN [kniterp].[stock_documents] d ON d.[Id] = x.[DocumentId] WHERE d.[Status] <> 1)
                        THROW 51002, N''Строки проведённого документа не меняются — оформите сторно или новый документ.'', 1;
                END');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("EXEC(N'DROP TRIGGER IF EXISTS [kniterp].[tr_stock_document_lines_draft_only]');");

            migrationBuilder.DropColumn(
                name: "LinesRevision",
                schema: "kniterp",
                table: "stock_documents");
        }
    }
}
