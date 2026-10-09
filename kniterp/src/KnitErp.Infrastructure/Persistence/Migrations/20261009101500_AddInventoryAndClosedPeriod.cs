using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnitErp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryAndClosedPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_movements_source",
                schema: "kniterp",
                table: "stock_movements");

            migrationBuilder.CreateTable(
                name: "inventory_counts",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    CountDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    PostedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    PostedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_counts", x => x.Id);
                    table.UniqueConstraint("ak_inventory_counts_org_id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("ck_inventory_counts_posted", "([Status] = 2 AND [PostedByUserId] IS NOT NULL AND [PostedAtUtc] IS NOT NULL) OR ([Status] <> 2 AND [PostedByUserId] IS NULL)");
                    table.CheckConstraint("ck_inventory_counts_status", "[Status] IN (1, 2, 9)");
                    table.ForeignKey(
                        name: "FK_inventory_counts_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_counts_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_counts_users_PostedByUserId",
                        column: x => x.PostedByUserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_counts_warehouse",
                        columns: x => new { x.OrganizationId, x.WarehouseId },
                        principalSchema: "kniterp",
                        principalTable: "warehouses",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "period_closures",
                schema: "kniterp",
                columns: table => new
                {
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    ClosedThrough = table.Column<DateOnly>(type: "date", nullable: true),
                    ChangedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_period_closures", x => x.OrganizationId);
                    table.ForeignKey(
                        name: "FK_period_closures_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_period_closures_users_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_lines",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<long>(type: "bigint", nullable: false),
                    ItemId = table.Column<long>(type: "bigint", nullable: false),
                    BookQuantity = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    CountedQuantity = table.Column<decimal>(type: "decimal(18,6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_lines", x => x.Id);
                    table.CheckConstraint("ck_inventory_lines_counted", "[CountedQuantity] IS NULL OR [CountedQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_inventory_lines_inventory_counts_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "kniterp",
                        principalTable: "inventory_counts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_inventory_lines_items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "kniterp",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_movements_source",
                schema: "kniterp",
                table: "stock_movements",
                sql: "[Source] IN (1, 2, 3, 4)");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_counts_CreatedByUserId",
                schema: "kniterp",
                table: "inventory_counts",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_counts_OrganizationId_WarehouseId",
                schema: "kniterp",
                table: "inventory_counts",
                columns: new[] { "OrganizationId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_counts_PostedByUserId",
                schema: "kniterp",
                table: "inventory_counts",
                column: "PostedByUserId");

            migrationBuilder.CreateIndex(
                name: "ux_inventory_counts_org_number",
                schema: "kniterp",
                table: "inventory_counts",
                columns: new[] { "OrganizationId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_lines_ItemId",
                schema: "kniterp",
                table: "inventory_lines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "ux_inventory_lines_doc_item",
                schema: "kniterp",
                table: "inventory_lines",
                columns: new[] { "DocumentId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_period_closures_ChangedByUserId",
                schema: "kniterp",
                table: "period_closures",
                column: "ChangedByUserId");

            // Закрытый период на уровне SQL Server: движение с датой по границу закрытия включительно не вставляется.
            migrationBuilder.Sql("""
                EXEC(N'CREATE OR ALTER TRIGGER [kniterp].[tr_stock_movements_closed_period]
                ON [kniterp].[stock_movements]
                AFTER INSERT
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM inserted i
                               JOIN [kniterp].[period_closures] p ON p.[OrganizationId] = i.[OrganizationId]
                               WHERE p.[ClosedThrough] IS NOT NULL AND i.[OccurredOn] <= p.[ClosedThrough])
                        THROW 51002, N''Период закрыт: движение с датой в закрытом периоде запрещено.'', 1;
                END');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [kniterp].[tr_stock_movements_closed_period];");

            migrationBuilder.DropTable(
                name: "inventory_lines",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "period_closures",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "inventory_counts",
                schema: "kniterp");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_movements_source",
                schema: "kniterp",
                table: "stock_movements");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_movements_source",
                schema: "kniterp",
                table: "stock_movements",
                sql: "[Source] IN (1, 2, 3)");
        }
    }
}
