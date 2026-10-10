using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnitErp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTechCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tech_cards",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    ItemId = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    OutputQuantity = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ActivatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ActivatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tech_cards", x => x.Id);
                    table.UniqueConstraint("ak_tech_cards_org_id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("ck_tech_cards_activated", "([Status] = 1 AND [ActivatedByUserId] IS NULL) OR ([Status] <> 1 AND [ActivatedByUserId] IS NOT NULL AND [ActivatedAtUtc] IS NOT NULL) OR ([Status] = 9 AND [ActivatedByUserId] IS NULL)");
                    table.CheckConstraint("ck_tech_cards_output", "[OutputQuantity] > 0");
                    table.CheckConstraint("ck_tech_cards_status", "[Status] IN (1, 2, 9)");
                    table.CheckConstraint("ck_tech_cards_version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_tech_cards_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tech_cards_users_ActivatedByUserId",
                        column: x => x.ActivatedByUserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tech_cards_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tech_cards_item",
                        columns: x => new { x.OrganizationId, x.ItemId },
                        principalSchema: "kniterp",
                        principalTable: "items",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tech_card_lines",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TechCardId = table.Column<long>(type: "bigint", nullable: false),
                    ItemId = table.Column<long>(type: "bigint", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    WastePercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tech_card_lines", x => x.Id);
                    table.CheckConstraint("ck_tech_card_lines_quantity", "[Quantity] > 0");
                    table.CheckConstraint("ck_tech_card_lines_waste", "[WastePercent] >= 0 AND [WastePercent] < 100");
                    table.ForeignKey(
                        name: "FK_tech_card_lines_items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "kniterp",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tech_card_lines_tech_cards_TechCardId",
                        column: x => x.TechCardId,
                        principalSchema: "kniterp",
                        principalTable: "tech_cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tech_card_lines_ItemId",
                schema: "kniterp",
                table: "tech_card_lines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "ux_tech_card_lines_card_item",
                schema: "kniterp",
                table: "tech_card_lines",
                columns: new[] { "TechCardId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tech_cards_ActivatedByUserId",
                schema: "kniterp",
                table: "tech_cards",
                column: "ActivatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_tech_cards_CreatedByUserId",
                schema: "kniterp",
                table: "tech_cards",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "ux_tech_cards_item_active",
                schema: "kniterp",
                table: "tech_cards",
                columns: new[] { "OrganizationId", "ItemId" },
                unique: true,
                filter: "[Status] = 2");

            migrationBuilder.CreateIndex(
                name: "ux_tech_cards_item_version",
                schema: "kniterp",
                table: "tech_cards",
                columns: new[] { "OrganizationId", "ItemId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tech_card_lines",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "tech_cards",
                schema: "kniterp");
        }
    }
}
