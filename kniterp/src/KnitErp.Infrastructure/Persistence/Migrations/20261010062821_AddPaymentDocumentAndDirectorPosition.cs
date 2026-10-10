using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnitErp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentDocumentAndDirectorPosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DirectorPosition",
                schema: "kniterp",
                table: "organizations",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentNumber",
                schema: "kniterp",
                table: "customer_payments",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DirectorPosition",
                schema: "kniterp",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "DocumentNumber",
                schema: "kniterp",
                table: "customer_payments");
        }
    }
}
