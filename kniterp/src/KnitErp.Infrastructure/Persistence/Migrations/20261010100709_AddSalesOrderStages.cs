using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnitErp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesOrderStages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "StageId",
                schema: "kniterp",
                table: "sales_orders",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sales_order_stages",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Color = table.Column<string>(type: "varchar(16)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_order_stages", x => x.Id);
                    table.UniqueConstraint("ak_sales_order_stages_org_id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("ck_sales_order_stages_color", "[Color] IN ('gray', 'blue', 'teal', 'green', 'yellow', 'orange', 'red', 'magenta', 'violet')");
                    table.ForeignKey(
                        name: "FK_sales_order_stages_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_org_stage",
                schema: "kniterp",
                table: "sales_orders",
                columns: new[] { "OrganizationId", "StageId" });

            migrationBuilder.CreateIndex(
                name: "ux_sales_order_stages_org_name_active",
                schema: "kniterp",
                table: "sales_order_stages",
                columns: new[] { "OrganizationId", "Name" },
                unique: true,
                filter: "[IsArchived] = 0");

            migrationBuilder.AddForeignKey(
                name: "fk_sales_orders_stage",
                schema: "kniterp",
                table: "sales_orders",
                columns: new[] { "OrganizationId", "StageId" },
                principalSchema: "kniterp",
                principalTable: "sales_order_stages",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);
        
            // Организациям, созданным раньше, — этапы заказов по умолчанию (D75), если своих ещё нет.
            migrationBuilder.Sql("""
                INSERT INTO [kniterp].[sales_order_stages] ([OrganizationId], [Name], [Color], [SortOrder], [IsArchived])
                SELECT o.[Id], d.[Name], d.[Color], d.[SortOrder], 0
                FROM [kniterp].[organizations] o
                CROSS JOIN (VALUES
                    (N'В обработке', 'gray', 10), (N'Ждём оплату', 'blue', 20), (N'Оплачен — в производство', 'green', 30),
                    (N'В работе', 'violet', 40), (N'Собран', 'teal', 50), (N'Отгружен', 'orange', 60)
                ) AS d([Name], [Color], [SortOrder])
                WHERE NOT EXISTS (SELECT 1 FROM [kniterp].[sales_order_stages] s WHERE s.[OrganizationId] = o.[Id]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_sales_orders_stage",
                schema: "kniterp",
                table: "sales_orders");

            migrationBuilder.DropTable(
                name: "sales_order_stages",
                schema: "kniterp");

            migrationBuilder.DropIndex(
                name: "ix_sales_orders_org_stage",
                schema: "kniterp",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "StageId",
                schema: "kniterp",
                table: "sales_orders");
        }
    }
}
