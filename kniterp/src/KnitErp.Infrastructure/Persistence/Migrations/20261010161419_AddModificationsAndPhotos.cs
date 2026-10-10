using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnitErp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModificationsAndPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ParentItemId",
                schema: "kniterp",
                table: "items",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VariantKey",
                schema: "kniterp",
                table: "items",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "characteristics",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_characteristics", x => x.Id);
                    table.UniqueConstraint("ak_characteristics_org_id", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_characteristics_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "item_photos",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    ItemId = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "varchar(20)", nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    SizeBytes = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_photos", x => x.Id);
                    table.CheckConstraint("ck_item_photos_size", "[SizeBytes] BETWEEN 1 AND 2097152 AND DATALENGTH([Content]) = [SizeBytes]");
                    table.CheckConstraint("ck_item_photos_type", "[ContentType] IN ('image/jpeg', 'image/png', 'image/webp')");
                    table.ForeignKey(
                        name: "fk_item_photos_item",
                        columns: x => new { x.OrganizationId, x.ItemId },
                        principalSchema: "kniterp",
                        principalTable: "items",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "item_characteristic_values",
                schema: "kniterp",
                columns: table => new
                {
                    ItemId = table.Column<long>(type: "bigint", nullable: false),
                    CharacteristicId = table.Column<long>(type: "bigint", nullable: false),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_characteristic_values", x => new { x.ItemId, x.CharacteristicId });
                    table.ForeignKey(
                        name: "fk_item_characteristic_values_characteristic",
                        columns: x => new { x.OrganizationId, x.CharacteristicId },
                        principalSchema: "kniterp",
                        principalTable: "characteristics",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_item_characteristic_values_item",
                        columns: x => new { x.OrganizationId, x.ItemId },
                        principalSchema: "kniterp",
                        principalTable: "items",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_items_parent_variant",
                schema: "kniterp",
                table: "items",
                columns: new[] { "OrganizationId", "ParentItemId", "VariantKey" },
                unique: true,
                filter: "[ParentItemId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_items_parent",
                schema: "kniterp",
                table: "items",
                sql: "[ParentItemId] IS NULL OR [ParentItemId] <> [Id]");

            migrationBuilder.AddCheckConstraint(
                name: "ck_items_variant",
                schema: "kniterp",
                table: "items",
                sql: "([ParentItemId] IS NULL AND [VariantKey] IS NULL) OR ([ParentItemId] IS NOT NULL AND [VariantKey] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ux_characteristics_org_name_active",
                schema: "kniterp",
                table: "characteristics",
                columns: new[] { "OrganizationId", "Name" },
                unique: true,
                filter: "[IsArchived] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_item_characteristic_values_OrganizationId_ItemId",
                schema: "kniterp",
                table: "item_characteristic_values",
                columns: new[] { "OrganizationId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "ix_item_characteristic_values_value",
                schema: "kniterp",
                table: "item_characteristic_values",
                columns: new[] { "OrganizationId", "CharacteristicId", "Value" });

            migrationBuilder.CreateIndex(
                name: "ix_item_photos_item_order",
                schema: "kniterp",
                table: "item_photos",
                columns: new[] { "ItemId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_item_photos_OrganizationId_ItemId",
                schema: "kniterp",
                table: "item_photos",
                columns: new[] { "OrganizationId", "ItemId" });

            migrationBuilder.AddForeignKey(
                name: "fk_items_parent",
                schema: "kniterp",
                table: "items",
                columns: new[] { "OrganizationId", "ParentItemId" },
                principalSchema: "kniterp",
                principalTable: "items",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_items_parent",
                schema: "kniterp",
                table: "items");

            migrationBuilder.DropTable(
                name: "item_characteristic_values",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "item_photos",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "characteristics",
                schema: "kniterp");

            migrationBuilder.DropIndex(
                name: "ux_items_parent_variant",
                schema: "kniterp",
                table: "items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_items_parent",
                schema: "kniterp",
                table: "items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_items_variant",
                schema: "kniterp",
                table: "items");

            migrationBuilder.DropColumn(
                name: "ParentItemId",
                schema: "kniterp",
                table: "items");

            migrationBuilder.DropColumn(
                name: "VariantKey",
                schema: "kniterp",
                table: "items");
        }
    }
}
