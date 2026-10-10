using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnitErp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMoneyOperationsAndPurchaseLegalEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LegalEntityId",
                schema: "kniterp",
                table: "purchase_orders",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "money_operations",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OperationDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    TargetAccountId = table.Column<long>(type: "bigint", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Party = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Basis = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_money_operations", x => x.Id);
                    table.CheckConstraint("ck_money_operations_amount", "[Amount] > 0");
                    table.CheckConstraint("ck_money_operations_cancel", "([Status] = 9 AND [CancelledAtUtc] IS NOT NULL AND [CancelReason] IS NOT NULL) OR ([Status] = 1 AND [CancelledAtUtc] IS NULL)");
                    table.CheckConstraint("ck_money_operations_kind", "[Kind] IN (1, 2, 3)");
                    table.CheckConstraint("ck_money_operations_status", "[Status] IN (1, 9)");
                    table.CheckConstraint("ck_money_operations_target", "([Kind] = 3 AND [TargetAccountId] IS NOT NULL AND [TargetAccountId] <> [AccountId]) OR ([Kind] <> 3 AND [TargetAccountId] IS NULL)");
                    table.ForeignKey(
                        name: "FK_money_operations_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_money_operations_account",
                        columns: x => new { x.OrganizationId, x.AccountId },
                        principalSchema: "kniterp",
                        principalTable: "legal_entity_accounts",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_money_operations_target",
                        columns: x => new { x.OrganizationId, x.TargetAccountId },
                        principalSchema: "kniterp",
                        principalTable: "legal_entity_accounts",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_org_legal_entity",
                schema: "kniterp",
                table: "purchase_orders",
                columns: new[] { "OrganizationId", "LegalEntityId" });

            migrationBuilder.CreateIndex(
                name: "ix_money_operations_org_date",
                schema: "kniterp",
                table: "money_operations",
                columns: new[] { "OrganizationId", "OperationDate" });

            migrationBuilder.CreateIndex(
                name: "IX_money_operations_OrganizationId_AccountId",
                schema: "kniterp",
                table: "money_operations",
                columns: new[] { "OrganizationId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_money_operations_OrganizationId_TargetAccountId",
                schema: "kniterp",
                table: "money_operations",
                columns: new[] { "OrganizationId", "TargetAccountId" });

            migrationBuilder.CreateIndex(
                name: "ux_money_operations_org_number",
                schema: "kniterp",
                table: "money_operations",
                columns: new[] { "OrganizationId", "Number" },
                unique: true);

            // D84: прежние заказы поставщикам — от имени основного юрлица своей организации (как заказы покупателей в D78).
            migrationBuilder.Sql(@"EXEC(N'
UPDATE o SET o.LegalEntityId = e.Id
FROM kniterp.purchase_orders o
JOIN kniterp.legal_entities e ON e.OrganizationId = o.OrganizationId AND e.IsDefault = 1
WHERE o.LegalEntityId = 0;
')");

            migrationBuilder.AddForeignKey(
                name: "fk_purchase_orders_legal_entity",
                schema: "kniterp",
                table: "purchase_orders",
                columns: new[] { "OrganizationId", "LegalEntityId" },
                principalSchema: "kniterp",
                principalTable: "legal_entities",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_purchase_orders_legal_entity",
                schema: "kniterp",
                table: "purchase_orders");

            migrationBuilder.DropTable(
                name: "money_operations",
                schema: "kniterp");

            migrationBuilder.DropIndex(
                name: "ix_purchase_orders_org_legal_entity",
                schema: "kniterp",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "LegalEntityId",
                schema: "kniterp",
                table: "purchase_orders");
        }
    }
}
