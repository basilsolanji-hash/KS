using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnitErp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMoneyAccountsAndPaymentAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_legal_entity_accounts_entity_account_active",
                schema: "kniterp",
                table: "legal_entity_accounts");

            migrationBuilder.AddColumn<long>(
                name: "MoneyAccountId",
                schema: "kniterp",
                table: "supplier_payments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "Kind",
                schema: "kniterp",
                table: "legal_entity_accounts",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningBalance",
                schema: "kniterp",
                table: "legal_entity_accounts",
                type: "decimal(19,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "OpeningDate",
                schema: "kniterp",
                table: "legal_entity_accounts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MoneyAccountId",
                schema: "kniterp",
                table: "customer_payments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_legal_entity_accounts_org_id",
                schema: "kniterp",
                table: "legal_entity_accounts",
                columns: new[] { "OrganizationId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_payments_OrganizationId_MoneyAccountId",
                schema: "kniterp",
                table: "supplier_payments",
                columns: new[] { "OrganizationId", "MoneyAccountId" });

            migrationBuilder.CreateIndex(
                name: "ux_legal_entity_accounts_entity_account_active",
                schema: "kniterp",
                table: "legal_entity_accounts",
                columns: new[] { "LegalEntityId", "Account" },
                unique: true,
                filter: "[IsArchived] = 0 AND [Kind] = 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_legal_entity_accounts_kind",
                schema: "kniterp",
                table: "legal_entity_accounts",
                sql: "([Kind] = 1 AND LEN([Bic]) > 0 AND LEN([Account]) > 0) OR ([Kind] = 2 AND [IsDefault] = 0)");

            migrationBuilder.CreateIndex(
                name: "IX_customer_payments_OrganizationId_MoneyAccountId",
                schema: "kniterp",
                table: "customer_payments",
                columns: new[] { "OrganizationId", "MoneyAccountId" });

            migrationBuilder.AddForeignKey(
                name: "fk_customer_payments_money_account",
                schema: "kniterp",
                table: "customer_payments",
                columns: new[] { "OrganizationId", "MoneyAccountId" },
                principalSchema: "kniterp",
                principalTable: "legal_entity_accounts",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_supplier_payments_money_account",
                schema: "kniterp",
                table: "supplier_payments",
                columns: new[] { "OrganizationId", "MoneyAccountId" },
                principalSchema: "kniterp",
                principalTable: "legal_entity_accounts",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            // D80: прежние оплаты разносятся по счетам. Оплата покупателя — на счёт заказа, иначе на основной счёт юрлица заказа,
            // без заказа — на основной счёт основного юрлица. Оплата поставщику — на основной счёт основного юрлица.
            // Если у юрлица нет счёта, оплата остаётся «Без счёта» и видна на главном экране отдельной строкой.
            migrationBuilder.Sql(@"EXEC(N'
UPDATE p SET p.MoneyAccountId = COALESCE(o.BankAccountId, ea.Id, da.Id)
FROM kniterp.customer_payments p
LEFT JOIN kniterp.sales_orders o ON o.OrganizationId = p.OrganizationId AND o.Id = p.SalesOrderId
OUTER APPLY (SELECT TOP 1 a.Id FROM kniterp.legal_entity_accounts a
             WHERE a.OrganizationId = p.OrganizationId AND a.LegalEntityId = o.LegalEntityId AND a.IsDefault = 1 AND a.Kind = 1) ea
OUTER APPLY (SELECT TOP 1 a.Id FROM kniterp.legal_entity_accounts a
             JOIN kniterp.legal_entities e ON e.OrganizationId = a.OrganizationId AND e.Id = a.LegalEntityId AND e.IsDefault = 1
             WHERE a.OrganizationId = p.OrganizationId AND a.IsDefault = 1 AND a.Kind = 1) da
WHERE p.MoneyAccountId IS NULL;
UPDATE p SET p.MoneyAccountId = da.Id
FROM kniterp.supplier_payments p
CROSS APPLY (SELECT TOP 1 a.Id FROM kniterp.legal_entity_accounts a
             JOIN kniterp.legal_entities e ON e.OrganizationId = a.OrganizationId AND e.Id = a.LegalEntityId AND e.IsDefault = 1
             WHERE a.OrganizationId = p.OrganizationId AND a.IsDefault = 1 AND a.Kind = 1) da
WHERE p.MoneyAccountId IS NULL;
')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_customer_payments_money_account",
                schema: "kniterp",
                table: "customer_payments");

            migrationBuilder.DropForeignKey(
                name: "fk_supplier_payments_money_account",
                schema: "kniterp",
                table: "supplier_payments");

            migrationBuilder.DropIndex(
                name: "IX_supplier_payments_OrganizationId_MoneyAccountId",
                schema: "kniterp",
                table: "supplier_payments");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_legal_entity_accounts_org_id",
                schema: "kniterp",
                table: "legal_entity_accounts");

            migrationBuilder.DropIndex(
                name: "ux_legal_entity_accounts_entity_account_active",
                schema: "kniterp",
                table: "legal_entity_accounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_legal_entity_accounts_kind",
                schema: "kniterp",
                table: "legal_entity_accounts");

            migrationBuilder.DropIndex(
                name: "IX_customer_payments_OrganizationId_MoneyAccountId",
                schema: "kniterp",
                table: "customer_payments");

            migrationBuilder.DropColumn(
                name: "MoneyAccountId",
                schema: "kniterp",
                table: "supplier_payments");

            migrationBuilder.DropColumn(
                name: "Kind",
                schema: "kniterp",
                table: "legal_entity_accounts");

            migrationBuilder.DropColumn(
                name: "OpeningBalance",
                schema: "kniterp",
                table: "legal_entity_accounts");

            migrationBuilder.DropColumn(
                name: "OpeningDate",
                schema: "kniterp",
                table: "legal_entity_accounts");

            migrationBuilder.DropColumn(
                name: "MoneyAccountId",
                schema: "kniterp",
                table: "customer_payments");

            migrationBuilder.CreateIndex(
                name: "ux_legal_entity_accounts_entity_account_active",
                schema: "kniterp",
                table: "legal_entity_accounts",
                columns: new[] { "LegalEntityId", "Account" },
                unique: true,
                filter: "[IsArchived] = 0");
        }
    }
}
