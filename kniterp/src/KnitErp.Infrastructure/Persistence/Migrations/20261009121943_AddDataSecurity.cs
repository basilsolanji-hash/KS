using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnitErp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDataSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "AuthenticatorKey",
                schema: "kniterp",
                table: "users",
                type: "varchar(200)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldNullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ChainHash",
                schema: "kniterp",
                table: "stock_movements",
                type: "binary(32)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ChainSeq",
                schema: "kniterp",
                table: "stock_movements",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ChainHash",
                schema: "kniterp",
                table: "audit_log",
                type: "binary(32)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ChainSeq",
                schema: "kniterp",
                table: "audit_log",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FriendlyName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Xml = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_protection_keys", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_stock_movements_org_chain",
                schema: "kniterp",
                table: "stock_movements",
                columns: new[] { "OrganizationId", "ChainSeq" },
                unique: true,
                filter: "[ChainSeq] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_audit_log_org_chain",
                schema: "kniterp",
                table: "audit_log",
                columns: new[] { "OrganizationId", "ChainSeq" },
                unique: true,
                filter: "[ChainSeq] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_protection_keys",
                schema: "kniterp");

            migrationBuilder.DropIndex(
                name: "ux_stock_movements_org_chain",
                schema: "kniterp",
                table: "stock_movements");

            migrationBuilder.DropIndex(
                name: "ux_audit_log_org_chain",
                schema: "kniterp",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "ChainHash",
                schema: "kniterp",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "ChainSeq",
                schema: "kniterp",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "ChainHash",
                schema: "kniterp",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "ChainSeq",
                schema: "kniterp",
                table: "audit_log");

            migrationBuilder.AlterColumn<string>(
                name: "AuthenticatorKey",
                schema: "kniterp",
                table: "users",
                type: "varchar(64)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(200)",
                oldNullable: true);
        }
    }
}
