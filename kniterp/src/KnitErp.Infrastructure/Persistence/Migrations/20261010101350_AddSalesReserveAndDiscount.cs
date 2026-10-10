using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnitErp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesReserveAndDiscount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Reserve",
                schema: "kniterp",
                table: "sales_orders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPercent",
                schema: "kniterp",
                table: "sales_order_lines",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "ck_sales_order_lines_discount",
                schema: "kniterp",
                table: "sales_order_lines",
                sql: "[DiscountPercent] BETWEEN 0 AND 100");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_sales_order_lines_discount",
                schema: "kniterp",
                table: "sales_order_lines");

            migrationBuilder.DropColumn(
                name: "Reserve",
                schema: "kniterp",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "DiscountPercent",
                schema: "kniterp",
                table: "sales_order_lines");
        }
    }
}
