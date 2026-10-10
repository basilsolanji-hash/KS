using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnitErp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "departments",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    ParentId = table.Column<long>(type: "bigint", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_departments", x => x.Id);
                    table.UniqueConstraint("ak_departments_org_id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("ck_departments_archived", "[IsArchived] = 0 OR [ArchivedAtUtc] IS NOT NULL");
                    table.CheckConstraint("ck_departments_not_self_parent", "[ParentId] IS NULL OR [ParentId] <> [Id]");
                    table.ForeignKey(
                        name: "FK_departments_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_departments_parent",
                        columns: x => new { x.OrganizationId, x.ParentId },
                        principalSchema: "kniterp",
                        principalTable: "departments",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "positions",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_positions", x => x.Id);
                    table.UniqueConstraint("ak_positions_org_id", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_positions_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "employees",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    PersonnelNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MiddleName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DepartmentId = table.Column<long>(type: "bigint", nullable: false),
                    PositionId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    HiredOn = table.Column<DateOnly>(type: "date", nullable: false),
                    DismissedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    UserId = table.Column<long>(type: "bigint", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employees", x => x.Id);
                    table.CheckConstraint("ck_employees_dismissed", "([Status] = 9 AND [DismissedOn] IS NOT NULL AND [DismissedOn] >= [HiredOn]) OR ([Status] <> 9 AND [DismissedOn] IS NULL)");
                    table.CheckConstraint("ck_employees_status", "[Status] IN (1, 2, 9)");
                    table.ForeignKey(
                        name: "FK_employees_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_employees_users_UserId",
                        column: x => x.UserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employees_department",
                        columns: x => new { x.OrganizationId, x.DepartmentId },
                        principalSchema: "kniterp",
                        principalTable: "departments",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employees_position",
                        columns: x => new { x.OrganizationId, x.PositionId },
                        principalSchema: "kniterp",
                        principalTable: "positions",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_role_assignments_OrganizationId_DepartmentId",
                schema: "kniterp",
                table: "role_assignments",
                columns: new[] { "OrganizationId", "DepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_departments_OrganizationId_ParentId",
                schema: "kniterp",
                table: "departments",
                columns: new[] { "OrganizationId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "ux_departments_org_name_active",
                schema: "kniterp",
                table: "departments",
                columns: new[] { "OrganizationId", "Name" },
                unique: true,
                filter: "[IsArchived] = 0");

            migrationBuilder.CreateIndex(
                name: "ix_employees_org_department",
                schema: "kniterp",
                table: "employees",
                columns: new[] { "OrganizationId", "DepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_employees_OrganizationId_PositionId",
                schema: "kniterp",
                table: "employees",
                columns: new[] { "OrganizationId", "PositionId" });

            migrationBuilder.CreateIndex(
                name: "IX_employees_UserId",
                schema: "kniterp",
                table: "employees",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "ux_employees_org_number",
                schema: "kniterp",
                table: "employees",
                columns: new[] { "OrganizationId", "PersonnelNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_employees_org_user",
                schema: "kniterp",
                table: "employees",
                columns: new[] { "OrganizationId", "UserId" },
                unique: true,
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_positions_org_name_active",
                schema: "kniterp",
                table: "positions",
                columns: new[] { "OrganizationId", "Name" },
                unique: true,
                filter: "[IsArchived] = 0");

            migrationBuilder.AddForeignKey(
                name: "fk_role_assignments_department",
                schema: "kniterp",
                table: "role_assignments",
                columns: new[] { "OrganizationId", "DepartmentId" },
                principalSchema: "kniterp",
                principalTable: "departments",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_role_assignments_department",
                schema: "kniterp",
                table: "role_assignments");

            migrationBuilder.DropTable(
                name: "employees",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "departments",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "positions",
                schema: "kniterp");

            migrationBuilder.DropIndex(
                name: "IX_role_assignments_OrganizationId_DepartmentId",
                schema: "kniterp",
                table: "role_assignments");
        }
    }
}
