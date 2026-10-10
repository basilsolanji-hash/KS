using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnitErp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpeningBalances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_counters",
                schema: "kniterp",
                columns: table => new
                {
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LastNumber = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_counters", x => new { x.OrganizationId, x.Kind });
                    table.CheckConstraint("ck_document_counters_last", "[LastNumber] >= 0");
                    table.ForeignKey(
                        name: "FK_document_counters_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "opening_balances",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    AsOfDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ApprovedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ReturnReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_opening_balances", x => x.Id);
                    table.UniqueConstraint("ak_opening_balances_org_id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("ck_opening_balances_approved", "([Status] = 3 AND [ApprovedByUserId] IS NOT NULL AND [ApprovedAtUtc] IS NOT NULL AND [ApprovedByUserId] <> [CreatedByUserId]) OR ([Status] <> 3 AND [ApprovedByUserId] IS NULL)");
                    table.CheckConstraint("ck_opening_balances_status", "[Status] IN (1, 2, 3, 9)");
                    table.ForeignKey(
                        name: "FK_opening_balances_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_opening_balances_users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_opening_balances_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_opening_balances_warehouse",
                        columns: x => new { x.OrganizationId, x.WarehouseId },
                        principalSchema: "kniterp",
                        principalTable: "warehouses",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_movements",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    ItemId = table.Column<long>(type: "bigint", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    OccurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Source = table.Column<byte>(type: "tinyint", nullable: false),
                    SourceId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_movements", x => x.Id);
                    table.CheckConstraint("ck_stock_movements_quantity", "[Quantity] <> 0");
                    table.CheckConstraint("ck_stock_movements_source", "[Source] IN (1)");
                    table.ForeignKey(
                        name: "FK_stock_movements_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movements_item",
                        columns: x => new { x.OrganizationId, x.ItemId },
                        principalSchema: "kniterp",
                        principalTable: "items",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movements_warehouse",
                        columns: x => new { x.OrganizationId, x.WarehouseId },
                        principalSchema: "kniterp",
                        principalTable: "warehouses",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "opening_balance_lines",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<long>(type: "bigint", nullable: false),
                    ItemId = table.Column<long>(type: "bigint", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_opening_balance_lines", x => x.Id);
                    table.CheckConstraint("ck_opening_balance_lines_quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_opening_balance_lines_items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "kniterp",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_opening_balance_lines_opening_balances_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "kniterp",
                        principalTable: "opening_balances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_opening_balance_lines_ItemId",
                schema: "kniterp",
                table: "opening_balance_lines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "ux_opening_balance_lines_doc_item",
                schema: "kniterp",
                table: "opening_balance_lines",
                columns: new[] { "DocumentId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_opening_balances_ApprovedByUserId",
                schema: "kniterp",
                table: "opening_balances",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_opening_balances_CreatedByUserId",
                schema: "kniterp",
                table: "opening_balances",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_opening_balances_OrganizationId_WarehouseId",
                schema: "kniterp",
                table: "opening_balances",
                columns: new[] { "OrganizationId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "ux_opening_balances_org_number",
                schema: "kniterp",
                table: "opening_balances",
                columns: new[] { "OrganizationId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_org_wh_item",
                schema: "kniterp",
                table: "stock_movements",
                columns: new[] { "OrganizationId", "WarehouseId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_OrganizationId_ItemId",
                schema: "kniterp",
                table: "stock_movements",
                columns: new[] { "OrganizationId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_source",
                schema: "kniterp",
                table: "stock_movements",
                columns: new[] { "Source", "SourceId" });

            // Движения склада неизменяемы на уровне SQL Server: исправление — новым документом (CLAUDE.md, ТЗ §4.13).
            migrationBuilder.Sql("""
                EXEC(N'CREATE OR ALTER TRIGGER [kniterp].[tr_stock_movements_immutable]
                ON [kniterp].[stock_movements]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51001, N''Движения склада неизменяемы: исправление — новым документом.'', 1;
                END');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [kniterp].[tr_stock_movements_immutable];");

            migrationBuilder.DropTable(
                name: "document_counters",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "opening_balance_lines",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "stock_movements",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "opening_balances",
                schema: "kniterp");
        }
    }
}
