using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnitErp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TechCardRevisionAndLineGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Revision",
                schema: "kniterp",
                table: "tech_cards",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Строки техкарты меняются только у черновика (аудит 10.10.2026, п. 1): действующую или архивную карту
            // не изменить даже в обход приложения — новые нормы оформляются новой версией.
            migrationBuilder.Sql("""
                EXEC(N'CREATE OR ALTER TRIGGER [kniterp].[tr_tech_card_lines_draft_only]
                ON [kniterp].[tech_card_lines]
                AFTER INSERT, UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM (SELECT [TechCardId] FROM inserted UNION ALL SELECT [TechCardId] FROM deleted) x
                               JOIN [kniterp].[tech_cards] c ON c.[Id] = x.[TechCardId] WHERE c.[Status] <> 1)
                        THROW 51001, N''Нормы действующей или архивной техкарты не меняются — создайте новую версию.'', 1;
                END');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("EXEC(N'DROP TRIGGER IF EXISTS [kniterp].[tr_tech_card_lines_draft_only]');");

            migrationBuilder.DropColumn(
                name: "Revision",
                schema: "kniterp",
                table: "tech_cards");
        }
    }
}
