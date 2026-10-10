using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnitErp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesOrderDetailsAndCustomFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ChannelId",
                schema: "kniterp",
                table: "sales_orders",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryAddress",
                schema: "kniterp",
                table: "sales_orders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "OrderTime",
                schema: "kniterp",
                table: "sales_orders",
                type: "time(0)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ProjectId",
                schema: "kniterp",
                table: "sales_orders",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ResponsibleUserId",
                schema: "kniterp",
                table: "sales_orders",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "custom_field_definitions",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Target = table.Column<byte>(type: "tinyint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Type = table.Column<byte>(type: "tinyint", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_field_definitions", x => x.Id);
                    table.UniqueConstraint("ak_custom_field_definitions_org_id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("ck_custom_field_definitions_target", "[Target] IN (1)");
                    table.CheckConstraint("ck_custom_field_definitions_type", "[Type] IN (1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "FK_custom_field_definitions_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lookups",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lookups", x => x.Id);
                    table.UniqueConstraint("ak_lookups_org_id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("ck_lookups_kind", "[Kind] IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_lookups_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "custom_field_values",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    FieldId = table.Column<long>(type: "bigint", nullable: false),
                    TargetId = table.Column<long>(type: "bigint", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_field_values", x => x.Id);
                    table.ForeignKey(
                        name: "fk_custom_field_values_field",
                        columns: x => new { x.OrganizationId, x.FieldId },
                        principalSchema: "kniterp",
                        principalTable: "custom_field_definitions",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_org_channel",
                schema: "kniterp",
                table: "sales_orders",
                columns: new[] { "OrganizationId", "ChannelId" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_org_project",
                schema: "kniterp",
                table: "sales_orders",
                columns: new[] { "OrganizationId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_orders_ResponsibleUserId",
                schema: "kniterp",
                table: "sales_orders",
                column: "ResponsibleUserId");

            migrationBuilder.CreateIndex(
                name: "ux_custom_field_definitions_org_target_name_active",
                schema: "kniterp",
                table: "custom_field_definitions",
                columns: new[] { "OrganizationId", "Target", "Name" },
                unique: true,
                filter: "[IsArchived] = 0");

            migrationBuilder.CreateIndex(
                name: "ix_custom_field_values_org_target",
                schema: "kniterp",
                table: "custom_field_values",
                columns: new[] { "OrganizationId", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_custom_field_values_OrganizationId_FieldId",
                schema: "kniterp",
                table: "custom_field_values",
                columns: new[] { "OrganizationId", "FieldId" });

            migrationBuilder.CreateIndex(
                name: "ux_custom_field_values_field_target",
                schema: "kniterp",
                table: "custom_field_values",
                columns: new[] { "FieldId", "TargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_lookups_org_kind_name_active",
                schema: "kniterp",
                table: "lookups",
                columns: new[] { "OrganizationId", "Kind", "Name" },
                unique: true,
                filter: "[IsArchived] = 0");

            migrationBuilder.AddForeignKey(
                name: "fk_sales_orders_channel",
                schema: "kniterp",
                table: "sales_orders",
                columns: new[] { "OrganizationId", "ChannelId" },
                principalSchema: "kniterp",
                principalTable: "lookups",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_sales_orders_project",
                schema: "kniterp",
                table: "sales_orders",
                columns: new[] { "OrganizationId", "ProjectId" },
                principalSchema: "kniterp",
                principalTable: "lookups",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_sales_orders_responsible",
                schema: "kniterp",
                table: "sales_orders",
                column: "ResponsibleUserId",
                principalSchema: "kniterp",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Ответственный у уже созданных заказов — их автор (как у новых, D77). EXEC — столбец новый, иначе идемпотентный скрипт не скомпилируется.
            migrationBuilder.Sql("""
                EXEC(N'UPDATE [kniterp].[sales_orders] SET [ResponsibleUserId] = [CreatedByUserId] WHERE [ResponsibleUserId] IS NULL;');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_sales_orders_channel",
                schema: "kniterp",
                table: "sales_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_orders_project",
                schema: "kniterp",
                table: "sales_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_orders_responsible",
                schema: "kniterp",
                table: "sales_orders");

            migrationBuilder.DropTable(
                name: "custom_field_values",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "lookups",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "custom_field_definitions",
                schema: "kniterp");

            migrationBuilder.DropIndex(
                name: "ix_sales_orders_org_channel",
                schema: "kniterp",
                table: "sales_orders");

            migrationBuilder.DropIndex(
                name: "ix_sales_orders_org_project",
                schema: "kniterp",
                table: "sales_orders");

            migrationBuilder.DropIndex(
                name: "IX_sales_orders_ResponsibleUserId",
                schema: "kniterp",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "ChannelId",
                schema: "kniterp",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "DeliveryAddress",
                schema: "kniterp",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "OrderTime",
                schema: "kniterp",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                schema: "kniterp",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "ResponsibleUserId",
                schema: "kniterp",
                table: "sales_orders");
        }
    }
}
