using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnitErp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeBirthday : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "BirthDate",
                schema: "kniterp",
                table: "employees",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShareBirthday",
                schema: "kniterp",
                table: "employees",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_employees_birthday_share",
                schema: "kniterp",
                table: "employees",
                sql: "[ShareBirthday] = 0 OR [BirthDate] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_employees_birthday_share",
                schema: "kniterp",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "BirthDate",
                schema: "kniterp",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "ShareBirthday",
                schema: "kniterp",
                table: "employees");
        }
    }
}
