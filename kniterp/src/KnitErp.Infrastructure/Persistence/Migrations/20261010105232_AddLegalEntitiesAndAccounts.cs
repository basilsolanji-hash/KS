using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnitErp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLegalEntitiesAndAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BankAccountId",
                schema: "kniterp",
                table: "sales_orders",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LegalEntityId",
                schema: "kniterp",
                table: "sales_orders",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "BankAccountId",
                schema: "kniterp",
                table: "customer_invoices",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LegalEntityId",
                schema: "kniterp",
                table: "customer_invoices",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "legal_entities",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ShortName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Inn = table.Column<string>(type: "varchar(14)", nullable: false),
                    Kpp = table.Column<string>(type: "char(9)", nullable: true),
                    Ogrn = table.Column<string>(type: "varchar(20)", nullable: true),
                    LegalAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DirectorPosition = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DirectorName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    AccountantName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    VatExempt = table.Column<bool>(type: "bit", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_legal_entities", x => x.Id);
                    table.UniqueConstraint("ak_legal_entities_org_id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("ck_legal_entities_default", "[IsDefault] = 0 OR [IsArchived] = 0");
                    table.CheckConstraint("ck_legal_entities_inn", "LEN([Inn]) BETWEEN 9 AND 14 AND [Inn] NOT LIKE '%[^0-9]%'");
                    table.CheckConstraint("ck_legal_entities_kind", "[Kind] IN (1, 2)");
                    table.CheckConstraint("ck_legal_entities_kpp", "[Kpp] IS NULL OR (LEN([Kpp]) = 9 AND [Kind] = 1)");
                    table.ForeignKey(
                        name: "FK_legal_entities_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "legal_entity_accounts",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    LegalEntityId = table.Column<long>(type: "bigint", nullable: false),
                    BankName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Bic = table.Column<string>(type: "varchar(11)", nullable: false),
                    Account = table.Column<string>(type: "varchar(34)", nullable: false),
                    CorrAccount = table.Column<string>(type: "varchar(34)", nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_legal_entity_accounts", x => x.Id);
                    table.UniqueConstraint("ak_legal_entity_accounts_org_entity_id", x => new { x.OrganizationId, x.LegalEntityId, x.Id });
                    table.CheckConstraint("ck_legal_entity_accounts_default", "[IsDefault] = 0 OR [IsArchived] = 0");
                    table.ForeignKey(
                        name: "fk_legal_entity_accounts_entity",
                        columns: x => new { x.OrganizationId, x.LegalEntityId },
                        principalSchema: "kniterp",
                        principalTable: "legal_entities",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });


            // Реквизиты организации — в основное юрлицо (D78), банк — в его основной счёт; заказы и счета — к основному юрлицу.
            // EXEC — таблицы и столбцы новые: так работает и идемпотентный скрипт. КПП переносится только сверенный (D15).
            migrationBuilder.Sql("""
                EXEC(N'INSERT INTO [kniterp].[legal_entities] ([OrganizationId], [Kind], [Name], [ShortName], [Inn], [Kpp], [Ogrn], [LegalAddress],
                    [DirectorPosition], [DirectorName], [AccountantName], [VatExempt], [IsDefault], [IsArchived])
                SELECT o.[Id], 1, o.[FullName], o.[ShortName], o.[Inn], CASE WHEN o.[KppVerified] = 1 THEN o.[Kpp] END, NULL,
                    COALESCE(o.[LegalAddress], o.[ActualAddress]), o.[DirectorPosition], o.[DirectorName], o.[AccountantName], 0, 1, 0
                FROM [kniterp].[organizations] o
                WHERE NOT EXISTS (SELECT 1 FROM [kniterp].[legal_entities] e WHERE e.[OrganizationId] = o.[Id]);

                INSERT INTO [kniterp].[legal_entity_accounts] ([OrganizationId], [LegalEntityId], [BankName], [Bic], [Account], [CorrAccount], [IsDefault], [IsArchived])
                SELECT o.[Id], e.[Id], COALESCE(o.[BankName], N''Банк''), o.[BankBic], o.[BankAccount], o.[BankCorrAccount], 1, 0
                FROM [kniterp].[organizations] o
                JOIN [kniterp].[legal_entities] e ON e.[OrganizationId] = o.[Id] AND e.[IsDefault] = 1
                WHERE o.[BankAccount] IS NOT NULL AND o.[BankBic] IS NOT NULL
                    AND NOT EXISTS (SELECT 1 FROM [kniterp].[legal_entity_accounts] a WHERE a.[LegalEntityId] = e.[Id]);

                UPDATE s SET s.[LegalEntityId] = e.[Id]
                FROM [kniterp].[sales_orders] s
                JOIN [kniterp].[legal_entities] e ON e.[OrganizationId] = s.[OrganizationId] AND e.[IsDefault] = 1
                WHERE s.[LegalEntityId] IS NULL;

                UPDATE i SET i.[LegalEntityId] = e.[Id], i.[BankAccountId] = a.[Id]
                FROM [kniterp].[customer_invoices] i
                JOIN [kniterp].[legal_entities] e ON e.[OrganizationId] = i.[OrganizationId] AND e.[IsDefault] = 1
                LEFT JOIN [kniterp].[legal_entity_accounts] a ON a.[LegalEntityId] = e.[Id] AND a.[IsDefault] = 1
                WHERE i.[LegalEntityId] IS NULL;');
                """);

            migrationBuilder.AlterColumn<long>(
                name: "LegalEntityId",
                schema: "kniterp",
                table: "sales_orders",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "LegalEntityId",
                schema: "kniterp",
                table: "customer_invoices",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

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
                name: "DirectorPosition",
                schema: "kniterp",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "LegalAddress",
                schema: "kniterp",
                table: "organizations");

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_org_legal_entity",
                schema: "kniterp",
                table: "sales_orders",
                columns: new[] { "OrganizationId", "LegalEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_orders_OrganizationId_LegalEntityId_BankAccountId",
                schema: "kniterp",
                table: "sales_orders",
                columns: new[] { "OrganizationId", "LegalEntityId", "BankAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoices_OrganizationId_LegalEntityId_BankAccountId",
                schema: "kniterp",
                table: "customer_invoices",
                columns: new[] { "OrganizationId", "LegalEntityId", "BankAccountId" });

            migrationBuilder.CreateIndex(
                name: "ux_legal_entities_org_default",
                schema: "kniterp",
                table: "legal_entities",
                column: "OrganizationId",
                unique: true,
                filter: "[IsDefault] = 1");

            migrationBuilder.CreateIndex(
                name: "ux_legal_entities_org_inn_kpp_active",
                schema: "kniterp",
                table: "legal_entities",
                columns: new[] { "OrganizationId", "Inn", "Kpp" },
                unique: true,
                filter: "[IsArchived] = 0");

            migrationBuilder.CreateIndex(
                name: "ux_legal_entity_accounts_entity_account_active",
                schema: "kniterp",
                table: "legal_entity_accounts",
                columns: new[] { "LegalEntityId", "Account" },
                unique: true,
                filter: "[IsArchived] = 0");

            migrationBuilder.CreateIndex(
                name: "ux_legal_entity_accounts_entity_default",
                schema: "kniterp",
                table: "legal_entity_accounts",
                column: "LegalEntityId",
                unique: true,
                filter: "[IsDefault] = 1");

            migrationBuilder.AddForeignKey(
                name: "fk_customer_invoices_bank_account",
                schema: "kniterp",
                table: "customer_invoices",
                columns: new[] { "OrganizationId", "LegalEntityId", "BankAccountId" },
                principalSchema: "kniterp",
                principalTable: "legal_entity_accounts",
                principalColumns: new[] { "OrganizationId", "LegalEntityId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_customer_invoices_legal_entity",
                schema: "kniterp",
                table: "customer_invoices",
                columns: new[] { "OrganizationId", "LegalEntityId" },
                principalSchema: "kniterp",
                principalTable: "legal_entities",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_sales_orders_bank_account",
                schema: "kniterp",
                table: "sales_orders",
                columns: new[] { "OrganizationId", "LegalEntityId", "BankAccountId" },
                principalSchema: "kniterp",
                principalTable: "legal_entity_accounts",
                principalColumns: new[] { "OrganizationId", "LegalEntityId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_sales_orders_legal_entity",
                schema: "kniterp",
                table: "sales_orders",
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
                name: "fk_customer_invoices_bank_account",
                schema: "kniterp",
                table: "customer_invoices");

            migrationBuilder.DropForeignKey(
                name: "fk_customer_invoices_legal_entity",
                schema: "kniterp",
                table: "customer_invoices");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_orders_bank_account",
                schema: "kniterp",
                table: "sales_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_orders_legal_entity",
                schema: "kniterp",
                table: "sales_orders");

            migrationBuilder.DropTable(
                name: "legal_entity_accounts",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "legal_entities",
                schema: "kniterp");

            migrationBuilder.DropIndex(
                name: "ix_sales_orders_org_legal_entity",
                schema: "kniterp",
                table: "sales_orders");

            migrationBuilder.DropIndex(
                name: "IX_sales_orders_OrganizationId_LegalEntityId_BankAccountId",
                schema: "kniterp",
                table: "sales_orders");

            migrationBuilder.DropIndex(
                name: "IX_customer_invoices_OrganizationId_LegalEntityId_BankAccountId",
                schema: "kniterp",
                table: "customer_invoices");

            migrationBuilder.DropColumn(
                name: "BankAccountId",
                schema: "kniterp",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "LegalEntityId",
                schema: "kniterp",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "BankAccountId",
                schema: "kniterp",
                table: "customer_invoices");

            migrationBuilder.DropColumn(
                name: "LegalEntityId",
                schema: "kniterp",
                table: "customer_invoices");

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
                name: "DirectorPosition",
                schema: "kniterp",
                table: "organizations",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalAddress",
                schema: "kniterp",
                table: "organizations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }
    }
}
