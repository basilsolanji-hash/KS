using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnitErp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReceivedVatInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "received_vat_invoices",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    SupplierNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SupplierId = table.Column<long>(type: "bigint", nullable: false),
                    ReceiptDocumentId = table.Column<long>(type: "bigint", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
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
                    table.PrimaryKey("PK_received_vat_invoices", x => x.Id);
                    table.CheckConstraint("ck_received_vat_invoices_amount", "[Amount] > 0 AND [VatAmount] >= 0 AND [VatAmount] < [Amount]");
                    table.CheckConstraint("ck_received_vat_invoices_cancelled", "([Status] = 9 AND [CancelledByUserId] IS NOT NULL AND [CancelReason] IS NOT NULL) OR ([Status] = 2 AND [CancelledByUserId] IS NULL)");
                    table.CheckConstraint("ck_received_vat_invoices_status", "[Status] IN (2, 9)");
                    table.ForeignKey(
                        name: "FK_received_vat_invoices_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_received_vat_invoices_users_CancelledByUserId",
                        column: x => x.CancelledByUserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_received_vat_invoices_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_received_vat_invoices_receipt",
                        columns: x => new { x.OrganizationId, x.ReceiptDocumentId },
                        principalSchema: "kniterp",
                        principalTable: "stock_documents",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_received_vat_invoices_supplier",
                        columns: x => new { x.OrganizationId, x.SupplierId },
                        principalSchema: "kniterp",
                        principalTable: "counterparties",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_received_vat_invoices_CancelledByUserId",
                schema: "kniterp",
                table: "received_vat_invoices",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_received_vat_invoices_CreatedByUserId",
                schema: "kniterp",
                table: "received_vat_invoices",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "ix_received_vat_invoices_org_date",
                schema: "kniterp",
                table: "received_vat_invoices",
                columns: new[] { "OrganizationId", "InvoiceDate" });

            migrationBuilder.CreateIndex(
                name: "ux_received_vat_invoices_receipt_registered",
                schema: "kniterp",
                table: "received_vat_invoices",
                columns: new[] { "OrganizationId", "ReceiptDocumentId" },
                unique: true,
                filter: "[Status] = 2");

            migrationBuilder.CreateIndex(
                name: "ux_received_vat_invoices_supplier_number",
                schema: "kniterp",
                table: "received_vat_invoices",
                columns: new[] { "OrganizationId", "SupplierId", "SupplierNumber", "InvoiceDate" },
                unique: true,
                filter: "[Status] = 2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "received_vat_invoices",
                schema: "kniterp");
        }
    }
}
