using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnitErp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStockDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_movements_source",
                schema: "kniterp",
                table: "stock_movements");

            migrationBuilder.CreateTable(
                name: "stock_documents",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    TargetWarehouseId = table.Column<long>(type: "bigint", nullable: true),
                    CounterpartyId = table.Column<long>(type: "bigint", nullable: true),
                    ReasonId = table.Column<long>(type: "bigint", nullable: true),
                    DocumentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    PostedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    PostedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ReversedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ReversedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ReversalReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_documents", x => x.Id);
                    table.UniqueConstraint("ak_stock_documents_org_id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("ck_stock_documents_counterparty", "[Kind] = 1 OR [CounterpartyId] IS NULL");
                    table.CheckConstraint("ck_stock_documents_kind", "[Kind] IN (1, 2, 3)");
                    table.CheckConstraint("ck_stock_documents_posted", "([Status] IN (2, 3) AND [PostedByUserId] IS NOT NULL AND [PostedAtUtc] IS NOT NULL AND [ReasonId] IS NOT NULL) OR ([Status] NOT IN (2, 3) AND [PostedByUserId] IS NULL)");
                    table.CheckConstraint("ck_stock_documents_reversed", "([Status] = 3 AND [ReversedByUserId] IS NOT NULL AND [ReversedAtUtc] IS NOT NULL AND [ReversalReason] IS NOT NULL) OR ([Status] <> 3 AND [ReversedByUserId] IS NULL)");
                    table.CheckConstraint("ck_stock_documents_status", "[Status] IN (1, 2, 3, 9)");
                    table.CheckConstraint("ck_stock_documents_target", "([Kind] = 3 AND [TargetWarehouseId] IS NOT NULL AND [TargetWarehouseId] <> [WarehouseId]) OR ([Kind] <> 3 AND [TargetWarehouseId] IS NULL)");
                    table.ForeignKey(
                        name: "FK_stock_documents_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_documents_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_documents_users_PostedByUserId",
                        column: x => x.PostedByUserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_documents_users_ReversedByUserId",
                        column: x => x.ReversedByUserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_documents_counterparty",
                        columns: x => new { x.OrganizationId, x.CounterpartyId },
                        principalSchema: "kniterp",
                        principalTable: "counterparties",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_documents_reason",
                        columns: x => new { x.OrganizationId, x.ReasonId },
                        principalSchema: "kniterp",
                        principalTable: "operation_reasons",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_documents_target_warehouse",
                        columns: x => new { x.OrganizationId, x.TargetWarehouseId },
                        principalSchema: "kniterp",
                        principalTable: "warehouses",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_documents_warehouse",
                        columns: x => new { x.OrganizationId, x.WarehouseId },
                        principalSchema: "kniterp",
                        principalTable: "warehouses",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_document_lines",
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
                    table.PrimaryKey("PK_stock_document_lines", x => x.Id);
                    table.CheckConstraint("ck_stock_document_lines_quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_stock_document_lines_items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "kniterp",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_document_lines_stock_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "kniterp",
                        principalTable: "stock_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_movements_source",
                schema: "kniterp",
                table: "stock_movements",
                sql: "[Source] IN (1, 2, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_stock_document_lines_ItemId",
                schema: "kniterp",
                table: "stock_document_lines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "ux_stock_document_lines_doc_item",
                schema: "kniterp",
                table: "stock_document_lines",
                columns: new[] { "DocumentId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_documents_CreatedByUserId",
                schema: "kniterp",
                table: "stock_documents",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "ix_stock_documents_org_kind_date",
                schema: "kniterp",
                table: "stock_documents",
                columns: new[] { "OrganizationId", "Kind", "DocumentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_documents_OrganizationId_CounterpartyId",
                schema: "kniterp",
                table: "stock_documents",
                columns: new[] { "OrganizationId", "CounterpartyId" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_documents_OrganizationId_ReasonId",
                schema: "kniterp",
                table: "stock_documents",
                columns: new[] { "OrganizationId", "ReasonId" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_documents_OrganizationId_TargetWarehouseId",
                schema: "kniterp",
                table: "stock_documents",
                columns: new[] { "OrganizationId", "TargetWarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_documents_OrganizationId_WarehouseId",
                schema: "kniterp",
                table: "stock_documents",
                columns: new[] { "OrganizationId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_documents_PostedByUserId",
                schema: "kniterp",
                table: "stock_documents",
                column: "PostedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_documents_ReversedByUserId",
                schema: "kniterp",
                table: "stock_documents",
                column: "ReversedByUserId");

            migrationBuilder.CreateIndex(
                name: "ux_stock_documents_org_number",
                schema: "kniterp",
                table: "stock_documents",
                columns: new[] { "OrganizationId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_document_lines",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "stock_documents",
                schema: "kniterp");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_movements_source",
                schema: "kniterp",
                table: "stock_movements");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_movements_source",
                schema: "kniterp",
                table: "stock_movements",
                sql: "[Source] IN (1)");
        }
    }
}
