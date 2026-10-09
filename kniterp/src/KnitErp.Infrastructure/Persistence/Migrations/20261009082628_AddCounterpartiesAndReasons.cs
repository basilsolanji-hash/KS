using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnitErp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCounterpartiesAndReasons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "counterparties",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Inn = table.Column<string>(type: "varchar(12)", nullable: true),
                    Kpp = table.Column<string>(type: "char(9)", nullable: true),
                    IsSupplier = table.Column<bool>(type: "bit", nullable: false),
                    IsCustomer = table.Column<bool>(type: "bit", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_counterparties", x => x.Id);
                    table.UniqueConstraint("ak_counterparties_org_id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("ck_counterparties_inn", "[Inn] IS NULL OR ((LEN([Inn]) = 10 OR LEN([Inn]) = 12) AND [Inn] NOT LIKE '%[^0-9]%')");
                    table.CheckConstraint("ck_counterparties_kpp", "[Kpp] IS NULL OR (LEN([Kpp]) = 9 AND LEN([Inn]) = 10)");
                    table.CheckConstraint("ck_counterparties_role", "[IsSupplier] = 1 OR [IsCustomer] = 1");
                    table.ForeignKey(
                        name: "FK_counterparties_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "operation_reasons",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    RequiresComment = table.Column<bool>(type: "bit", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_reasons", x => x.Id);
                    table.UniqueConstraint("ak_operation_reasons_org_id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("ck_operation_reasons_kind", "[Kind] IN (1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "FK_operation_reasons_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_counterparties_org_name",
                schema: "kniterp",
                table: "counterparties",
                columns: new[] { "OrganizationId", "Name" });

            migrationBuilder.CreateIndex(
                name: "ux_counterparties_org_inn_kpp_active",
                schema: "kniterp",
                table: "counterparties",
                columns: new[] { "OrganizationId", "Inn", "Kpp" },
                unique: true,
                filter: "[Inn] IS NOT NULL AND [IsArchived] = 0");

            migrationBuilder.CreateIndex(
                name: "ux_operation_reasons_org_kind_name_active",
                schema: "kniterp",
                table: "operation_reasons",
                columns: new[] { "OrganizationId", "Kind", "Name" },
                unique: true,
                filter: "[IsArchived] = 0");

            // Стандартные причины для организаций, созданных до этой миграции. Список зафиксирован здесь
            // (а не взят из OperationReason.Defaults), чтобы применённая миграция не менялась вместе с кодом.
            migrationBuilder.Sql("""
                INSERT INTO [kniterp].[operation_reasons] ([OrganizationId], [Kind], [Name], [RequiresComment], [IsArchived])
                SELECT o.[Id], d.[Kind], d.[Name], d.[RequiresComment], 0
                FROM [kniterp].[organizations] o
                CROSS JOIN (VALUES
                    (1, N'Закупка у поставщика', 0),
                    (1, N'Выпуск из производства', 0),
                    (1, N'Возврат из производства', 0),
                    (2, N'Передача в производство', 0),
                    (2, N'Брак', 1),
                    (2, N'Порча', 1),
                    (3, N'Перемещение между складами', 0),
                    (4, N'Излишек по инвентаризации', 0),
                    (4, N'Недостача по инвентаризации', 1)
                ) AS d([Kind], [Name], [RequiresComment])
                WHERE NOT EXISTS (SELECT 1 FROM [kniterp].[operation_reasons] r
                                  WHERE r.[OrganizationId] = o.[Id] AND r.[Kind] = d.[Kind] AND r.[Name] = d.[Name]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "counterparties",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "operation_reasons",
                schema: "kniterp");
        }
    }
}
