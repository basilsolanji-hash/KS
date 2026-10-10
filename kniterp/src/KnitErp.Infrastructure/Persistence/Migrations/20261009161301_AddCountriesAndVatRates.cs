using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnitErp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCountriesAndVatRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_organizations_inn",
                schema: "kniterp",
                table: "organizations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_organizations_inn",
                schema: "kniterp",
                table: "organizations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_organizations_kpp",
                schema: "kniterp",
                table: "organizations");

            migrationBuilder.DropIndex(
                name: "ux_counterparties_org_inn_kpp_active",
                schema: "kniterp",
                table: "counterparties");

            migrationBuilder.DropCheckConstraint(
                name: "ck_counterparties_inn",
                schema: "kniterp",
                table: "counterparties");

            migrationBuilder.DropCheckConstraint(
                name: "ck_counterparties_kpp",
                schema: "kniterp",
                table: "counterparties");

            migrationBuilder.AlterColumn<string>(
                name: "Inn",
                schema: "kniterp",
                table: "organizations",
                type: "varchar(12)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "char(10)");

            migrationBuilder.AddColumn<string>(
                name: "CountryCode",
                schema: "kniterp",
                table: "organizations",
                type: "char(2)",
                nullable: false,
                defaultValue: "RU");

            migrationBuilder.AddColumn<long>(
                name: "VatRateId",
                schema: "kniterp",
                table: "items",
                type: "bigint",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Inn",
                schema: "kniterp",
                table: "counterparties",
                type: "varchar(14)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(12)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CountryCode",
                schema: "kniterp",
                table: "counterparties",
                type: "char(2)",
                nullable: false,
                defaultValue: "RU");

            migrationBuilder.CreateTable(
                name: "vat_rates",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vat_rates", x => x.Id);
                    table.UniqueConstraint("ak_vat_rates_org_id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("ck_vat_rates_kind", "[Kind] IN (1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "FK_vat_rates_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vat_rate_periods",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VatRateId = table.Column<long>(type: "bigint", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Percent = table.Column<decimal>(type: "decimal(5,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vat_rate_periods", x => x.Id);
                    table.CheckConstraint("ck_vat_rate_periods_percent", "[Percent] BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_vat_rate_periods_vat_rates_VatRateId",
                        column: x => x.VatRateId,
                        principalSchema: "kniterp",
                        principalTable: "vat_rates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_organizations_country_inn",
                schema: "kniterp",
                table: "organizations",
                columns: new[] { "CountryCode", "Inn" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_organizations_country",
                schema: "kniterp",
                table: "organizations",
                sql: "[CountryCode] IN ('RU', 'UZ', 'KZ', 'BY')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_organizations_inn",
                schema: "kniterp",
                table: "organizations",
                sql: "LEN([Inn]) BETWEEN 9 AND 12 AND [Inn] NOT LIKE '%[^0-9]%'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_organizations_kpp",
                schema: "kniterp",
                table: "organizations",
                sql: "[Kpp] IS NULL OR (LEN([Kpp]) = 9 AND [CountryCode] = 'RU')");

            migrationBuilder.CreateIndex(
                name: "IX_items_OrganizationId_VatRateId",
                schema: "kniterp",
                table: "items",
                columns: new[] { "OrganizationId", "VatRateId" });

            migrationBuilder.CreateIndex(
                name: "ux_counterparties_org_country_inn_kpp_active",
                schema: "kniterp",
                table: "counterparties",
                columns: new[] { "OrganizationId", "CountryCode", "Inn", "Kpp" },
                unique: true,
                filter: "[Inn] IS NOT NULL AND [IsArchived] = 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_counterparties_country",
                schema: "kniterp",
                table: "counterparties",
                sql: "[CountryCode] IN ('RU', 'UZ', 'KZ', 'BY')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_counterparties_inn",
                schema: "kniterp",
                table: "counterparties",
                sql: "[Inn] IS NULL OR (LEN([Inn]) BETWEEN 9 AND 14 AND [Inn] NOT LIKE '%[^0-9]%')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_counterparties_kpp",
                schema: "kniterp",
                table: "counterparties",
                sql: "[Kpp] IS NULL OR (LEN([Kpp]) = 9 AND LEN([Inn]) = 10 AND [CountryCode] = 'RU')");

            migrationBuilder.CreateIndex(
                name: "ux_vat_rate_periods_rate_from",
                schema: "kniterp",
                table: "vat_rate_periods",
                columns: new[] { "VatRateId", "ValidFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_vat_rates_org_name_active",
                schema: "kniterp",
                table: "vat_rates",
                columns: new[] { "OrganizationId", "Name" },
                unique: true,
                filter: "[IsArchived] = 0");

            migrationBuilder.AddForeignKey(
                name: "fk_items_vat_rate",
                schema: "kniterp",
                table: "items",
                columns: new[] { "OrganizationId", "VatRateId" },
                principalSchema: "kniterp",
                principalTable: "vat_rates",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            // Ставки НДС для организаций, созданных до этой миграции (все российские). Список зафиксирован здесь,
            // а не взят из VatRate.DefaultsFor, чтобы применённая миграция не менялась вместе с кодом.
            migrationBuilder.Sql("""
                INSERT INTO [kniterp].[vat_rates] ([OrganizationId], [Name], [Kind], [IsArchived])
                SELECT o.[Id], d.[Name], d.[Kind], 0
                FROM [kniterp].[organizations] o
                CROSS JOIN (VALUES (N'Основная', 1), (N'Пониженная 10%', 2), (N'0%', 3), (N'Без НДС', 4)) d([Name], [Kind])
                WHERE NOT EXISTS (SELECT 1 FROM [kniterp].[vat_rates] r WHERE r.[OrganizationId] = o.[Id]);

                INSERT INTO [kniterp].[vat_rate_periods] ([VatRateId], [ValidFrom], [Percent])
                SELECT r.[Id], p.[ValidFrom], p.[Percent]
                FROM [kniterp].[vat_rates] r
                JOIN (VALUES (N'Основная', CAST('2019-01-01' AS date), 20.00), (N'Основная', CAST('2026-01-01' AS date), 22.00),
                             (N'Пониженная 10%', CAST('2004-01-01' AS date), 10.00), (N'0%', CAST('2000-01-01' AS date), 0.00))
                     p([Name], [ValidFrom], [Percent]) ON p.[Name] = r.[Name]
                WHERE NOT EXISTS (SELECT 1 FROM [kniterp].[vat_rate_periods] x WHERE x.[VatRateId] = r.[Id]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_items_vat_rate",
                schema: "kniterp",
                table: "items");

            migrationBuilder.DropTable(
                name: "vat_rate_periods",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "vat_rates",
                schema: "kniterp");

            migrationBuilder.DropIndex(
                name: "ux_organizations_country_inn",
                schema: "kniterp",
                table: "organizations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_organizations_country",
                schema: "kniterp",
                table: "organizations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_organizations_inn",
                schema: "kniterp",
                table: "organizations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_organizations_kpp",
                schema: "kniterp",
                table: "organizations");

            migrationBuilder.DropIndex(
                name: "IX_items_OrganizationId_VatRateId",
                schema: "kniterp",
                table: "items");

            migrationBuilder.DropIndex(
                name: "ux_counterparties_org_country_inn_kpp_active",
                schema: "kniterp",
                table: "counterparties");

            migrationBuilder.DropCheckConstraint(
                name: "ck_counterparties_country",
                schema: "kniterp",
                table: "counterparties");

            migrationBuilder.DropCheckConstraint(
                name: "ck_counterparties_inn",
                schema: "kniterp",
                table: "counterparties");

            migrationBuilder.DropCheckConstraint(
                name: "ck_counterparties_kpp",
                schema: "kniterp",
                table: "counterparties");

            migrationBuilder.DropColumn(
                name: "CountryCode",
                schema: "kniterp",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "VatRateId",
                schema: "kniterp",
                table: "items");

            migrationBuilder.DropColumn(
                name: "CountryCode",
                schema: "kniterp",
                table: "counterparties");

            migrationBuilder.AlterColumn<string>(
                name: "Inn",
                schema: "kniterp",
                table: "organizations",
                type: "char(10)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(12)");

            migrationBuilder.AlterColumn<string>(
                name: "Inn",
                schema: "kniterp",
                table: "counterparties",
                type: "varchar(12)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(14)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_organizations_inn",
                schema: "kniterp",
                table: "organizations",
                column: "Inn",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_organizations_inn",
                schema: "kniterp",
                table: "organizations",
                sql: "LEN([Inn]) = 10 AND [Inn] NOT LIKE '%[^0-9]%'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_organizations_kpp",
                schema: "kniterp",
                table: "organizations",
                sql: "[Kpp] IS NULL OR LEN([Kpp]) = 9");

            migrationBuilder.CreateIndex(
                name: "ux_counterparties_org_inn_kpp_active",
                schema: "kniterp",
                table: "counterparties",
                columns: new[] { "OrganizationId", "Inn", "Kpp" },
                unique: true,
                filter: "[Inn] IS NOT NULL AND [IsArchived] = 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_counterparties_inn",
                schema: "kniterp",
                table: "counterparties",
                sql: "[Inn] IS NULL OR ((LEN([Inn]) = 10 OR LEN([Inn]) = 12) AND [Inn] NOT LIKE '%[^0-9]%')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_counterparties_kpp",
                schema: "kniterp",
                table: "counterparties",
                sql: "[Kpp] IS NULL OR (LEN([Kpp]) = 9 AND LEN([Inn]) = 10)");
        }
    }
}
