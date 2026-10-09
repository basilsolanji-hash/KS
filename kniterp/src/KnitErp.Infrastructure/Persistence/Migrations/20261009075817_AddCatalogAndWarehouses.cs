using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnitErp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogAndWarehouses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sites",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sites", x => x.Id);
                    table.UniqueConstraint("ak_sites_org_id", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_sites_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "units",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Precision = table.Column<byte>(type: "tinyint", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_units", x => x.Id);
                    table.UniqueConstraint("ak_units_org_id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("ck_units_precision", "[Precision] BETWEEN 0 AND 6");
                    table.ForeignKey(
                        name: "FK_units_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "warehouses",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    SiteId = table.Column<long>(type: "bigint", nullable: true),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_warehouses", x => x.Id);
                    table.UniqueConstraint("ak_warehouses_org_id", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_warehouses_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_warehouses_site",
                        columns: x => new { x.OrganizationId, x.SiteId },
                        principalSchema: "kniterp",
                        principalTable: "sites",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "items",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Type = table.Column<byte>(type: "tinyint", nullable: false),
                    UnitId = table.Column<long>(type: "bigint", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_items", x => x.Id);
                    table.UniqueConstraint("ak_items_org_id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("ck_items_archived", "[IsArchived] = 0 OR [ArchivedAtUtc] IS NOT NULL");
                    table.CheckConstraint("ck_items_type", "[Type] IN (1, 2, 3, 4, 5, 6, 9)");
                    table.ForeignKey(
                        name: "FK_items_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_items_unit",
                        columns: x => new { x.OrganizationId, x.UnitId },
                        principalSchema: "kniterp",
                        principalTable: "units",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_role_assignments_OrganizationId_WarehouseId",
                schema: "kniterp",
                table: "role_assignments",
                columns: new[] { "OrganizationId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "ix_items_org_type_name",
                schema: "kniterp",
                table: "items",
                columns: new[] { "OrganizationId", "Type", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_items_OrganizationId_UnitId",
                schema: "kniterp",
                table: "items",
                columns: new[] { "OrganizationId", "UnitId" });

            migrationBuilder.CreateIndex(
                name: "ux_items_org_code",
                schema: "kniterp",
                table: "items",
                columns: new[] { "OrganizationId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_sites_org_name_active",
                schema: "kniterp",
                table: "sites",
                columns: new[] { "OrganizationId", "Name" },
                unique: true,
                filter: "[IsArchived] = 0");

            migrationBuilder.CreateIndex(
                name: "ux_units_org_code",
                schema: "kniterp",
                table: "units",
                columns: new[] { "OrganizationId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_warehouses_OrganizationId_SiteId",
                schema: "kniterp",
                table: "warehouses",
                columns: new[] { "OrganizationId", "SiteId" });

            migrationBuilder.CreateIndex(
                name: "ux_warehouses_org_name_active",
                schema: "kniterp",
                table: "warehouses",
                columns: new[] { "OrganizationId", "Name" },
                unique: true,
                filter: "[IsArchived] = 0");

            migrationBuilder.AddForeignKey(
                name: "fk_role_assignments_warehouse",
                schema: "kniterp",
                table: "role_assignments",
                columns: new[] { "OrganizationId", "WarehouseId" },
                principalSchema: "kniterp",
                principalTable: "warehouses",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            // Стандартные единицы для организаций, созданных до этой миграции. Список зафиксирован здесь
            // (а не взят из UnitOfMeasure.Defaults), чтобы применённая миграция не менялась вместе с кодом.
            migrationBuilder.Sql("""
                INSERT INTO [kniterp].[units] ([OrganizationId], [Code], [Name], [Symbol], [Precision], [IsArchived])
                SELECT o.[Id], d.[Code], d.[Name], d.[Symbol], d.[Precision], 0
                FROM [kniterp].[organizations] o
                CROSS JOIN (VALUES
                    (N'796', N'Штука', N'шт', 0),
                    (N'166', N'Килограмм', N'кг', 3),
                    (N'163', N'Грамм', N'г', 1),
                    (N'006', N'Метр', N'м', 2),
                    (N'055', N'Квадратный метр', N'м²', 3),
                    (N'715', N'Пара', N'пар', 0),
                    (N'778', N'Упаковка', N'упак', 0)
                ) AS d([Code], [Name], [Symbol], [Precision])
                WHERE NOT EXISTS (SELECT 1 FROM [kniterp].[units] u WHERE u.[OrganizationId] = o.[Id] AND u.[Code] = d.[Code]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_role_assignments_warehouse",
                schema: "kniterp",
                table: "role_assignments");

            migrationBuilder.DropTable(
                name: "items",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "warehouses",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "units",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "sites",
                schema: "kniterp");

            migrationBuilder.DropIndex(
                name: "IX_role_assignments_OrganizationId_WarehouseId",
                schema: "kniterp",
                table: "role_assignments");
        }
    }
}
