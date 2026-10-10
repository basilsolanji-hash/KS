using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnitErp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CashOrderNumber",
                schema: "kniterp",
                table: "supplier_payments",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CashOrderNumber",
                schema: "kniterp",
                table: "customer_payments",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_supplier_payments_OrganizationId_Id",
                schema: "kniterp",
                table: "supplier_payments",
                columns: new[] { "OrganizationId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_customer_payments_OrganizationId_Id",
                schema: "kniterp",
                table: "customer_payments",
                columns: new[] { "OrganizationId", "Id" });

            migrationBuilder.CreateTable(
                name: "customer_payment_allocations",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    PaymentId = table.Column<long>(type: "bigint", nullable: false),
                    OrderId = table.Column<long>(type: "bigint", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RemovedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    RemovedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_payment_allocations", x => x.Id);
                    table.CheckConstraint("ck_customer_payment_allocations_amount", "[Amount] > 0");
                    table.CheckConstraint("ck_customer_payment_allocations_removed", "([RemovedAtUtc] IS NULL AND [RemovedByUserId] IS NULL) OR ([RemovedAtUtc] IS NOT NULL AND [RemovedByUserId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_customer_payment_allocations_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_payment_allocations_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_payment_allocations_users_RemovedByUserId",
                        column: x => x.RemovedByUserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_customer_payment_allocations_order",
                        columns: x => new { x.OrganizationId, x.OrderId },
                        principalSchema: "kniterp",
                        principalTable: "sales_orders",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_customer_payment_allocations_payment",
                        columns: x => new { x.OrganizationId, x.PaymentId },
                        principalSchema: "kniterp",
                        principalTable: "customer_payments",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_payment_allocations",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    PaymentId = table.Column<long>(type: "bigint", nullable: false),
                    OrderId = table.Column<long>(type: "bigint", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RemovedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    RemovedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_payment_allocations", x => x.Id);
                    table.CheckConstraint("ck_supplier_payment_allocations_amount", "[Amount] > 0");
                    table.CheckConstraint("ck_supplier_payment_allocations_removed", "([RemovedAtUtc] IS NULL AND [RemovedByUserId] IS NULL) OR ([RemovedAtUtc] IS NOT NULL AND [RemovedByUserId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_supplier_payment_allocations_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_payment_allocations_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_payment_allocations_users_RemovedByUserId",
                        column: x => x.RemovedByUserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supplier_payment_allocations_order",
                        columns: x => new { x.OrganizationId, x.OrderId },
                        principalSchema: "kniterp",
                        principalTable: "purchase_orders",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supplier_payment_allocations_payment",
                        columns: x => new { x.OrganizationId, x.PaymentId },
                        principalSchema: "kniterp",
                        principalTable: "supplier_payments",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_supplier_payments_org_cash_order",
                schema: "kniterp",
                table: "supplier_payments",
                columns: new[] { "OrganizationId", "CashOrderNumber" },
                unique: true,
                filter: "[CashOrderNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_customer_payments_org_cash_order",
                schema: "kniterp",
                table: "customer_payments",
                columns: new[] { "OrganizationId", "CashOrderNumber" },
                unique: true,
                filter: "[CashOrderNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_customer_payment_allocations_CreatedByUserId",
                schema: "kniterp",
                table: "customer_payment_allocations",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "ix_customer_payment_allocations_org_order",
                schema: "kniterp",
                table: "customer_payment_allocations",
                columns: new[] { "OrganizationId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_payment_allocations_OrganizationId_PaymentId",
                schema: "kniterp",
                table: "customer_payment_allocations",
                columns: new[] { "OrganizationId", "PaymentId" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_payment_allocations_RemovedByUserId",
                schema: "kniterp",
                table: "customer_payment_allocations",
                column: "RemovedByUserId");

            migrationBuilder.CreateIndex(
                name: "ux_customer_payment_allocations_active",
                schema: "kniterp",
                table: "customer_payment_allocations",
                columns: new[] { "PaymentId", "OrderId" },
                unique: true,
                filter: "[RemovedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_payment_allocations_CreatedByUserId",
                schema: "kniterp",
                table: "supplier_payment_allocations",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_payment_allocations_org_order",
                schema: "kniterp",
                table: "supplier_payment_allocations",
                columns: new[] { "OrganizationId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_payment_allocations_OrganizationId_PaymentId",
                schema: "kniterp",
                table: "supplier_payment_allocations",
                columns: new[] { "OrganizationId", "PaymentId" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_payment_allocations_RemovedByUserId",
                schema: "kniterp",
                table: "supplier_payment_allocations",
                column: "RemovedByUserId");

            migrationBuilder.CreateIndex(
                name: "ux_supplier_payment_allocations_active",
                schema: "kniterp",
                table: "supplier_payment_allocations",
                columns: new[] { "PaymentId", "OrderId" },
                unique: true,
                filter: "[RemovedAtUtc] IS NULL");

            // D85: прежняя привязка оплаты к заказу становится разноской на всю сумму — оплачено по заказам не меняется.
            migrationBuilder.Sql(@"EXEC(N'INSERT INTO [kniterp].[customer_payment_allocations] ([OrganizationId], [PaymentId], [OrderId], [Amount], [CreatedByUserId], [CreatedAtUtc])
SELECT [OrganizationId], [Id], [SalesOrderId], [Amount], [CreatedByUserId], [CreatedAtUtc] FROM [kniterp].[customer_payments] WHERE [SalesOrderId] IS NOT NULL;
INSERT INTO [kniterp].[supplier_payment_allocations] ([OrganizationId], [PaymentId], [OrderId], [Amount], [CreatedByUserId], [CreatedAtUtc])
SELECT [OrganizationId], [Id], [PurchaseOrderId], [Amount], [CreatedByUserId], [CreatedAtUtc] FROM [kniterp].[supplier_payments] WHERE [PurchaseOrderId] IS NOT NULL;')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_payment_allocations",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "supplier_payment_allocations",
                schema: "kniterp");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_supplier_payments_OrganizationId_Id",
                schema: "kniterp",
                table: "supplier_payments");

            migrationBuilder.DropIndex(
                name: "ux_supplier_payments_org_cash_order",
                schema: "kniterp",
                table: "supplier_payments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_customer_payments_OrganizationId_Id",
                schema: "kniterp",
                table: "customer_payments");

            migrationBuilder.DropIndex(
                name: "ux_customer_payments_org_cash_order",
                schema: "kniterp",
                table: "customer_payments");

            migrationBuilder.DropColumn(
                name: "CashOrderNumber",
                schema: "kniterp",
                table: "supplier_payments");

            migrationBuilder.DropColumn(
                name: "CashOrderNumber",
                schema: "kniterp",
                table: "customer_payments");
        }
    }
}
