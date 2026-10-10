using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnitErp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoicesAndPrintRequisites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccountantName",
                schema: "kniterp",
                table: "organizations",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankAccount",
                schema: "kniterp",
                table: "organizations",
                type: "varchar(34)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankBic",
                schema: "kniterp",
                table: "organizations",
                type: "varchar(11)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankCorrAccount",
                schema: "kniterp",
                table: "organizations",
                type: "varchar(34)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankName",
                schema: "kniterp",
                table: "organizations",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DirectorName",
                schema: "kniterp",
                table: "organizations",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalAddress",
                schema: "kniterp",
                table: "organizations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address",
                schema: "kniterp",
                table: "counterparties",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "customer_invoices",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SalesOrderId = table.Column<long>(type: "bigint", nullable: false),
                    CustomerId = table.Column<long>(type: "bigint", nullable: false),
                    PricesIncludeVat = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    CancelledByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_invoices", x => x.Id);
                    table.CheckConstraint("ck_customer_invoices_cancelled", "([Status] = 9 AND [CancelledByUserId] IS NOT NULL AND [CancelReason] IS NOT NULL) OR ([Status] = 2 AND [CancelledByUserId] IS NULL)");
                    table.CheckConstraint("ck_customer_invoices_due", "[DueDate] IS NULL OR [DueDate] >= [InvoiceDate]");
                    table.CheckConstraint("ck_customer_invoices_status", "[Status] IN (2, 9)");
                    table.ForeignKey(
                        name: "FK_customer_invoices_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_invoices_users_CancelledByUserId",
                        column: x => x.CancelledByUserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_invoices_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_customer_invoices_customer",
                        columns: x => new { x.OrganizationId, x.CustomerId },
                        principalSchema: "kniterp",
                        principalTable: "counterparties",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_customer_invoices_order",
                        columns: x => new { x.OrganizationId, x.SalesOrderId },
                        principalSchema: "kniterp",
                        principalTable: "sales_orders",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customer_invoice_lines",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvoiceId = table.Column<long>(type: "bigint", nullable: false),
                    ItemId = table.Column<long>(type: "bigint", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    VatPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_invoice_lines", x => x.Id);
                    table.CheckConstraint("ck_customer_invoice_lines_price", "[Price] >= 0 AND [Amount] >= 0 AND [VatAmount] >= 0");
                    table.CheckConstraint("ck_customer_invoice_lines_quantity", "[Quantity] > 0");
                    table.CheckConstraint("ck_customer_invoice_lines_vat", "[VatPercent] IS NULL OR [VatPercent] BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_customer_invoice_lines_customer_invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "kniterp",
                        principalTable: "customer_invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_customer_invoice_lines_items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "kniterp",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_lines_ItemId",
                schema: "kniterp",
                table: "customer_invoice_lines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "ux_customer_invoice_lines_invoice_item",
                schema: "kniterp",
                table: "customer_invoice_lines",
                columns: new[] { "InvoiceId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoices_CancelledByUserId",
                schema: "kniterp",
                table: "customer_invoices",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoices_CreatedByUserId",
                schema: "kniterp",
                table: "customer_invoices",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "ix_customer_invoices_org_customer_date",
                schema: "kniterp",
                table: "customer_invoices",
                columns: new[] { "OrganizationId", "CustomerId", "InvoiceDate" });

            migrationBuilder.CreateIndex(
                name: "ux_customer_invoices_order_issued",
                schema: "kniterp",
                table: "customer_invoices",
                columns: new[] { "OrganizationId", "SalesOrderId" },
                unique: true,
                filter: "[Status] = 2");

            migrationBuilder.CreateIndex(
                name: "ux_customer_invoices_org_number",
                schema: "kniterp",
                table: "customer_invoices",
                columns: new[] { "OrganizationId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_invoice_lines",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "customer_invoices",
                schema: "kniterp");

            migrationBuilder.DropColumn(
                name: "AccountantName",
                schema: "kniterp",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "BankAccount",
                schema: "kniterp",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "BankBic",
                schema: "kniterp",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "BankCorrAccount",
                schema: "kniterp",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "BankName",
                schema: "kniterp",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "DirectorName",
                schema: "kniterp",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "LegalAddress",
                schema: "kniterp",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "Address",
                schema: "kniterp",
                table: "counterparties");
        }
    }
}
