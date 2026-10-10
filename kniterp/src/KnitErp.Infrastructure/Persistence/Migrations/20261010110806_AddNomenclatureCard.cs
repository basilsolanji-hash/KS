using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnitErp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNomenclatureCard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_custom_field_definitions_target",
                schema: "kniterp",
                table: "custom_field_definitions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_custom_field_definitions_type",
                schema: "kniterp",
                table: "custom_field_definitions");

            migrationBuilder.AddColumn<string>(
                name: "Article",
                schema: "kniterp",
                table: "items",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomsDeclaration",
                schema: "kniterp",
                table: "items",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "GroupId",
                schema: "kniterp",
                table: "items",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinStock",
                schema: "kniterp",
                table: "items",
                type: "decimal(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginCountryCode",
                schema: "kniterp",
                table: "items",
                type: "char(3)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginCountryName",
                schema: "kniterp",
                table: "items",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PurchasePrice",
                schema: "kniterp",
                table: "items",
                type: "decimal(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TnVedCode",
                schema: "kniterp",
                table: "items",
                type: "varchar(10)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VolumeM3",
                schema: "kniterp",
                table: "items",
                type: "decimal(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WeightKg",
                schema: "kniterp",
                table: "items",
                type: "decimal(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CatalogId",
                schema: "kniterp",
                table: "custom_field_definitions",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "item_barcodes",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    ItemId = table.Column<long>(type: "bigint", nullable: false),
                    Type = table.Column<byte>(type: "tinyint", nullable: false),
                    Code = table.Column<string>(type: "varchar(64)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_barcodes", x => x.Id);
                    table.CheckConstraint("ck_item_barcodes_type", "[Type] IN (1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "fk_item_barcodes_item",
                        columns: x => new { x.OrganizationId, x.ItemId },
                        principalSchema: "kniterp",
                        principalTable: "items",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "item_groups",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    ParentId = table.Column<long>(type: "bigint", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_groups", x => x.Id);
                    table.UniqueConstraint("ak_item_groups_org_id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("ck_item_groups_parent", "[ParentId] IS NULL OR [ParentId] <> [Id]");
                    table.ForeignKey(
                        name: "FK_item_groups_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_item_groups_parent",
                        columns: x => new { x.OrganizationId, x.ParentId },
                        principalSchema: "kniterp",
                        principalTable: "item_groups",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "price_types",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IncludesVat = table.Column<bool>(type: "bit", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_types", x => x.Id);
                    table.UniqueConstraint("ak_price_types_org_id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("ck_price_types_default", "[IsDefault] = 0 OR [IsArchived] = 0");
                    table.ForeignKey(
                        name: "FK_price_types_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_catalogs",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_catalogs", x => x.Id);
                    table.UniqueConstraint("ak_user_catalogs_org_id", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_user_catalogs_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "item_prices",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    ItemId = table.Column<long>(type: "bigint", nullable: false),
                    PriceTypeId = table.Column<long>(type: "bigint", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(19,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_prices", x => x.Id);
                    table.CheckConstraint("ck_item_prices_price", "[Price] >= 0");
                    table.ForeignKey(
                        name: "fk_item_prices_item",
                        columns: x => new { x.OrganizationId, x.ItemId },
                        principalSchema: "kniterp",
                        principalTable: "items",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_item_prices_type",
                        columns: x => new { x.OrganizationId, x.PriceTypeId },
                        principalSchema: "kniterp",
                        principalTable: "price_types",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_catalog_entries",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    CatalogId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_catalog_entries", x => x.Id);
                    table.ForeignKey(
                        name: "fk_user_catalog_entries_catalog",
                        columns: x => new { x.OrganizationId, x.CatalogId },
                        principalSchema: "kniterp",
                        principalTable: "user_catalogs",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_items_org_article",
                schema: "kniterp",
                table: "items",
                columns: new[] { "OrganizationId", "Article" });

            migrationBuilder.CreateIndex(
                name: "ix_items_org_group",
                schema: "kniterp",
                table: "items",
                columns: new[] { "OrganizationId", "GroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_custom_field_definitions_OrganizationId_CatalogId",
                schema: "kniterp",
                table: "custom_field_definitions",
                columns: new[] { "OrganizationId", "CatalogId" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_custom_field_definitions_catalog",
                schema: "kniterp",
                table: "custom_field_definitions",
                sql: "([Type] = 5 AND [CatalogId] IS NOT NULL) OR ([Type] <> 5 AND [CatalogId] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_custom_field_definitions_target",
                schema: "kniterp",
                table: "custom_field_definitions",
                sql: "[Target] IN (1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_custom_field_definitions_type",
                schema: "kniterp",
                table: "custom_field_definitions",
                sql: "[Type] IN (1, 2, 3, 4, 5)");

            migrationBuilder.CreateIndex(
                name: "ix_item_barcodes_item",
                schema: "kniterp",
                table: "item_barcodes",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_item_barcodes_OrganizationId_ItemId",
                schema: "kniterp",
                table: "item_barcodes",
                columns: new[] { "OrganizationId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "ux_item_barcodes_org_code",
                schema: "kniterp",
                table: "item_barcodes",
                columns: new[] { "OrganizationId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_item_groups_org_parent_name_active",
                schema: "kniterp",
                table: "item_groups",
                columns: new[] { "OrganizationId", "ParentId", "Name" },
                unique: true,
                filter: "[IsArchived] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_item_prices_OrganizationId_ItemId",
                schema: "kniterp",
                table: "item_prices",
                columns: new[] { "OrganizationId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_item_prices_OrganizationId_PriceTypeId",
                schema: "kniterp",
                table: "item_prices",
                columns: new[] { "OrganizationId", "PriceTypeId" });

            migrationBuilder.CreateIndex(
                name: "ux_item_prices_item_type",
                schema: "kniterp",
                table: "item_prices",
                columns: new[] { "ItemId", "PriceTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_price_types_org_default",
                schema: "kniterp",
                table: "price_types",
                column: "OrganizationId",
                unique: true,
                filter: "[IsDefault] = 1");

            migrationBuilder.CreateIndex(
                name: "ux_price_types_org_name_active",
                schema: "kniterp",
                table: "price_types",
                columns: new[] { "OrganizationId", "Name" },
                unique: true,
                filter: "[IsArchived] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_user_catalog_entries_OrganizationId_CatalogId",
                schema: "kniterp",
                table: "user_catalog_entries",
                columns: new[] { "OrganizationId", "CatalogId" });

            migrationBuilder.CreateIndex(
                name: "ux_user_catalog_entries_catalog_name_active",
                schema: "kniterp",
                table: "user_catalog_entries",
                columns: new[] { "CatalogId", "Name" },
                unique: true,
                filter: "[IsArchived] = 0");

            migrationBuilder.CreateIndex(
                name: "ux_user_catalogs_org_name_active",
                schema: "kniterp",
                table: "user_catalogs",
                columns: new[] { "OrganizationId", "Name" },
                unique: true,
                filter: "[IsArchived] = 0");

            migrationBuilder.AddForeignKey(
                name: "fk_custom_field_definitions_catalog",
                schema: "kniterp",
                table: "custom_field_definitions",
                columns: new[] { "OrganizationId", "CatalogId" },
                principalSchema: "kniterp",
                principalTable: "user_catalogs",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_items_group",
                schema: "kniterp",
                table: "items",
                columns: new[] { "OrganizationId", "GroupId" },
                principalSchema: "kniterp",
                principalTable: "item_groups",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            // Основной вид цены «Цена продажи» (с НДС) организациям, созданным раньше (D79).
            migrationBuilder.Sql("""
                EXEC(N'INSERT INTO [kniterp].[price_types] ([OrganizationId], [Name], [IncludesVat], [IsDefault], [SortOrder], [IsArchived])
                SELECT o.[Id], N''Цена продажи'', 1, 1, 10, 0 FROM [kniterp].[organizations] o
                WHERE NOT EXISTS (SELECT 1 FROM [kniterp].[price_types] t WHERE t.[OrganizationId] = o.[Id]);');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_custom_field_definitions_catalog",
                schema: "kniterp",
                table: "custom_field_definitions");

            migrationBuilder.DropForeignKey(
                name: "fk_items_group",
                schema: "kniterp",
                table: "items");

            migrationBuilder.DropTable(
                name: "item_barcodes",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "item_groups",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "item_prices",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "user_catalog_entries",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "price_types",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "user_catalogs",
                schema: "kniterp");

            migrationBuilder.DropIndex(
                name: "ix_items_org_article",
                schema: "kniterp",
                table: "items");

            migrationBuilder.DropIndex(
                name: "ix_items_org_group",
                schema: "kniterp",
                table: "items");

            migrationBuilder.DropIndex(
                name: "IX_custom_field_definitions_OrganizationId_CatalogId",
                schema: "kniterp",
                table: "custom_field_definitions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_custom_field_definitions_catalog",
                schema: "kniterp",
                table: "custom_field_definitions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_custom_field_definitions_target",
                schema: "kniterp",
                table: "custom_field_definitions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_custom_field_definitions_type",
                schema: "kniterp",
                table: "custom_field_definitions");

            migrationBuilder.DropColumn(
                name: "Article",
                schema: "kniterp",
                table: "items");

            migrationBuilder.DropColumn(
                name: "CustomsDeclaration",
                schema: "kniterp",
                table: "items");

            migrationBuilder.DropColumn(
                name: "GroupId",
                schema: "kniterp",
                table: "items");

            migrationBuilder.DropColumn(
                name: "MinStock",
                schema: "kniterp",
                table: "items");

            migrationBuilder.DropColumn(
                name: "OriginCountryCode",
                schema: "kniterp",
                table: "items");

            migrationBuilder.DropColumn(
                name: "OriginCountryName",
                schema: "kniterp",
                table: "items");

            migrationBuilder.DropColumn(
                name: "PurchasePrice",
                schema: "kniterp",
                table: "items");

            migrationBuilder.DropColumn(
                name: "TnVedCode",
                schema: "kniterp",
                table: "items");

            migrationBuilder.DropColumn(
                name: "VolumeM3",
                schema: "kniterp",
                table: "items");

            migrationBuilder.DropColumn(
                name: "WeightKg",
                schema: "kniterp",
                table: "items");

            migrationBuilder.DropColumn(
                name: "CatalogId",
                schema: "kniterp",
                table: "custom_field_definitions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_custom_field_definitions_target",
                schema: "kniterp",
                table: "custom_field_definitions",
                sql: "[Target] IN (1)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_custom_field_definitions_type",
                schema: "kniterp",
                table: "custom_field_definitions",
                sql: "[Type] IN (1, 2, 3, 4)");
        }
    }
}
