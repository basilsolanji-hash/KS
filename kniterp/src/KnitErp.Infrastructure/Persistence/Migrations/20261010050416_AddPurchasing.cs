using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnitErp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchasing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_documents_counterparty",
                schema: "kniterp",
                table: "stock_documents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_documents_kind",
                schema: "kniterp",
                table: "stock_documents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_operation_reasons_kind",
                schema: "kniterp",
                table: "operation_reasons");

            migrationBuilder.AddColumn<long>(
                name: "PurchaseOrderId",
                schema: "kniterp",
                table: "stock_documents",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "purchase_orders",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OrderDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SupplierId = table.Column<long>(type: "bigint", nullable: false),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    ExpectedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SupplierInvoice = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PricesIncludeVat = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ConfirmedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_orders", x => x.Id);
                    table.UniqueConstraint("ak_purchase_orders_org_id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("ck_purchase_orders_confirmed", "([Status] = 1 AND [ConfirmedByUserId] IS NULL) OR ([Status] IN (2, 3) AND [ConfirmedByUserId] IS NOT NULL AND [ConfirmedAtUtc] IS NOT NULL) OR [Status] = 9");
                    table.CheckConstraint("ck_purchase_orders_status", "[Status] IN (1, 2, 3, 9)");
                    table.ForeignKey(
                        name: "FK_purchase_orders_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_orders_users_ConfirmedByUserId",
                        column: x => x.ConfirmedByUserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_orders_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_orders_supplier",
                        columns: x => new { x.OrganizationId, x.SupplierId },
                        principalSchema: "kniterp",
                        principalTable: "counterparties",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_orders_warehouse",
                        columns: x => new { x.OrganizationId, x.WarehouseId },
                        principalSchema: "kniterp",
                        principalTable: "warehouses",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_lines",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<long>(type: "bigint", nullable: false),
                    ItemId = table.Column<long>(type: "bigint", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    VatPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_order_lines", x => x.Id);
                    table.CheckConstraint("ck_purchase_order_lines_price", "[Price] >= 0 AND [Amount] >= 0 AND [VatAmount] >= 0");
                    table.CheckConstraint("ck_purchase_order_lines_quantity", "[Quantity] > 0");
                    table.CheckConstraint("ck_purchase_order_lines_vat", "[VatPercent] IS NULL OR [VatPercent] BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_purchase_order_lines_items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "kniterp",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_order_lines_purchase_orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "kniterp",
                        principalTable: "purchase_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "supplier_payments",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PaymentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SupplierId = table.Column<long>(type: "bigint", nullable: false),
                    PurchaseOrderId = table.Column<long>(type: "bigint", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    CancelledByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_payments", x => x.Id);
                    table.CheckConstraint("ck_supplier_payments_amount", "[Amount] > 0");
                    table.CheckConstraint("ck_supplier_payments_cancelled", "([Status] = 9 AND [CancelledByUserId] IS NOT NULL AND [CancelReason] IS NOT NULL) OR ([Status] = 2 AND [CancelledByUserId] IS NULL)");
                    table.CheckConstraint("ck_supplier_payments_status", "[Status] IN (2, 9)");
                    table.ForeignKey(
                        name: "FK_supplier_payments_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_payments_users_CancelledByUserId",
                        column: x => x.CancelledByUserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_payments_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supplier_payments_order",
                        columns: x => new { x.OrganizationId, x.PurchaseOrderId },
                        principalSchema: "kniterp",
                        principalTable: "purchase_orders",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supplier_payments_supplier",
                        columns: x => new { x.OrganizationId, x.SupplierId },
                        principalSchema: "kniterp",
                        principalTable: "counterparties",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stock_documents_OrganizationId_PurchaseOrderId",
                schema: "kniterp",
                table: "stock_documents",
                columns: new[] { "OrganizationId", "PurchaseOrderId" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_documents_counterparty",
                schema: "kniterp",
                table: "stock_documents",
                sql: "([Kind] = 1) OR ([Kind] = 5 AND [CounterpartyId] IS NOT NULL) OR ([Kind] NOT IN (1, 5) AND [CounterpartyId] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_documents_kind",
                schema: "kniterp",
                table: "stock_documents",
                sql: "[Kind] IN (1, 2, 3, 5)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_documents_purchase_order",
                schema: "kniterp",
                table: "stock_documents",
                sql: "[PurchaseOrderId] IS NULL OR [Kind] IN (1, 5)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_operation_reasons_kind",
                schema: "kniterp",
                table: "operation_reasons",
                sql: "[Kind] IN (1, 2, 3, 4, 5)");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_lines_ItemId",
                schema: "kniterp",
                table: "purchase_order_lines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "ux_purchase_order_lines_order_item",
                schema: "kniterp",
                table: "purchase_order_lines",
                columns: new[] { "OrderId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_ConfirmedByUserId",
                schema: "kniterp",
                table: "purchase_orders",
                column: "ConfirmedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_CreatedByUserId",
                schema: "kniterp",
                table: "purchase_orders",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_org_supplier_date",
                schema: "kniterp",
                table: "purchase_orders",
                columns: new[] { "OrganizationId", "SupplierId", "OrderDate" });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_OrganizationId_WarehouseId",
                schema: "kniterp",
                table: "purchase_orders",
                columns: new[] { "OrganizationId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "ux_purchase_orders_org_number",
                schema: "kniterp",
                table: "purchase_orders",
                columns: new[] { "OrganizationId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_payments_CancelledByUserId",
                schema: "kniterp",
                table: "supplier_payments",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_payments_CreatedByUserId",
                schema: "kniterp",
                table: "supplier_payments",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_payments_org_supplier_date",
                schema: "kniterp",
                table: "supplier_payments",
                columns: new[] { "OrganizationId", "SupplierId", "PaymentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_payments_OrganizationId_PurchaseOrderId",
                schema: "kniterp",
                table: "supplier_payments",
                columns: new[] { "OrganizationId", "PurchaseOrderId" });

            migrationBuilder.CreateIndex(
                name: "ux_supplier_payments_org_number",
                schema: "kniterp",
                table: "supplier_payments",
                columns: new[] { "OrganizationId", "Number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_documents_purchase_order",
                schema: "kniterp",
                table: "stock_documents",
                columns: new[] { "OrganizationId", "PurchaseOrderId" },
                principalSchema: "kniterp",
                principalTable: "purchase_orders",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            // Организациям, созданным раньше, — причина возврата поставщику и права закупок системным ролям (D64).
            // Значения зафиксированы здесь, а не взяты из кода, чтобы применённая миграция не менялась вместе с ним.
            migrationBuilder.Sql("""
                INSERT INTO [kniterp].[operation_reasons] ([OrganizationId], [Kind], [Name], [RequiresComment], [IsArchived])
                SELECT o.[Id], 5, N'Возврат поставщику', 0, 0
                FROM [kniterp].[organizations] o
                WHERE NOT EXISTS (SELECT 1 FROM [kniterp].[operation_reasons] r WHERE r.[OrganizationId] = o.[Id] AND r.[Kind] = 5);
                """);
            migrationBuilder.Sql("""
                INSERT INTO [kniterp].[role_permissions] ([RoleId], [PermissionCode], [Level])
                SELECT r.[Id], p.[Code], 3
                FROM [kniterp].[roles] r
                JOIN (VALUES
                    ('owner', 'purchase.document.view'), ('senior_storekeeper', 'purchase.document.view'),
                    ('accountant', 'purchase.document.view'), ('auditor', 'purchase.document.view'),
                    ('admin', 'purchase.document.view'),
                    ('owner', 'purchase.document.edit'), ('admin', 'purchase.document.edit'), ('accountant', 'purchase.document.edit')
                ) AS p([Role], [Code]) ON p.[Role] = r.[Code]
                WHERE r.[IsSystem] = 1
                  AND NOT EXISTS (SELECT 1 FROM [kniterp].[role_permissions] x WHERE x.[RoleId] = r.[Id] AND x.[PermissionCode] = p.[Code]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_stock_documents_purchase_order",
                schema: "kniterp",
                table: "stock_documents");

            migrationBuilder.DropTable(
                name: "purchase_order_lines",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "supplier_payments",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "purchase_orders",
                schema: "kniterp");

            migrationBuilder.DropIndex(
                name: "IX_stock_documents_OrganizationId_PurchaseOrderId",
                schema: "kniterp",
                table: "stock_documents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_documents_counterparty",
                schema: "kniterp",
                table: "stock_documents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_documents_kind",
                schema: "kniterp",
                table: "stock_documents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_documents_purchase_order",
                schema: "kniterp",
                table: "stock_documents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_operation_reasons_kind",
                schema: "kniterp",
                table: "operation_reasons");

            migrationBuilder.DropColumn(
                name: "PurchaseOrderId",
                schema: "kniterp",
                table: "stock_documents");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_documents_counterparty",
                schema: "kniterp",
                table: "stock_documents",
                sql: "[Kind] = 1 OR [CounterpartyId] IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_documents_kind",
                schema: "kniterp",
                table: "stock_documents",
                sql: "[Kind] IN (1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_operation_reasons_kind",
                schema: "kniterp",
                table: "operation_reasons",
                sql: "[Kind] IN (1, 2, 3, 4)");
        }
    }
}
