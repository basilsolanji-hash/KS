using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnitErp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceTools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WebsiteUrl",
                schema: "kniterp",
                table: "organizations",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "support_tickets",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AuthorUserId = table.Column<long>(type: "bigint", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Section = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Answer = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    AnsweredByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UnreadByAuthor = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_tickets", x => x.Id);
                    table.CheckConstraint("ck_support_tickets_answer", "[Status] <> 3 OR ([Answer] IS NOT NULL AND [AnsweredByUserId] IS NOT NULL)");
                    table.CheckConstraint("ck_support_tickets_status", "[Status] IN (1, 2, 3, 9)");
                    table.ForeignKey(
                        name: "FK_support_tickets_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_support_tickets_users_AnsweredByUserId",
                        column: x => x.AnsweredByUserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_support_tickets_users_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_tool_data",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_tool_data", x => x.Id);
                    table.CheckConstraint("ck_user_tool_data_size", "LEN([Json]) <= 200000");
                    table.ForeignKey(
                        name: "FK_user_tool_data_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_tool_data_users_UserId",
                        column: x => x.UserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_AnsweredByUserId",
                schema: "kniterp",
                table: "support_tickets",
                column: "AnsweredByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_AuthorUserId",
                schema: "kniterp",
                table: "support_tickets",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_org_author_status",
                schema: "kniterp",
                table: "support_tickets",
                columns: new[] { "OrganizationId", "AuthorUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "ux_support_tickets_org_number",
                schema: "kniterp",
                table: "support_tickets",
                columns: new[] { "OrganizationId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_tool_data_UserId",
                schema: "kniterp",
                table: "user_tool_data",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "ux_user_tool_data_org_user_kind",
                schema: "kniterp",
                table: "user_tool_data",
                columns: new[] { "OrganizationId", "UserId", "Kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "support_tickets",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "user_tool_data",
                schema: "kniterp");

            migrationBuilder.DropColumn(
                name: "WebsiteUrl",
                schema: "kniterp",
                table: "organizations");
        }
    }
}
