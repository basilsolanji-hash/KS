using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnitErp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAccessCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "kniterp");

            migrationBuilder.CreateTable(
                name: "audit_log",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: true),
                    ActorUserId = table.Column<long>(type: "bigint", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Before = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    After = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "organizations",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FullName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ShortName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Inn = table.Column<string>(type: "char(10)", nullable: false),
                    Kpp = table.Column<string>(type: "char(9)", nullable: true),
                    KppVerified = table.Column<bool>(type: "bit", nullable: false),
                    ActualAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TimeZoneId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CurrencyCode = table.Column<string>(type: "char(3)", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organizations", x => x.Id);
                    table.CheckConstraint("ck_organizations_currency", "LEN([CurrencyCode]) = 3");
                    table.CheckConstraint("ck_organizations_inn", "LEN([Inn]) = 10 AND [Inn] NOT LIKE '%[^0-9]%'");
                    table.CheckConstraint("ck_organizations_kpp", "[Kpp] IS NULL OR LEN([Kpp]) = 9");
                    table.CheckConstraint("ck_organizations_kpp_verified", "[KppVerified] = 0 OR [Kpp] IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    SecurityStamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PasswordHash = table.Column<string>(type: "varchar(256)", nullable: true),
                    FailedSignInCount = table.Column<int>(type: "int", nullable: false),
                    LockoutEndUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AuthenticatorKey = table.Column<string>(type: "varchar(64)", nullable: true),
                    LastTotpStep = table.Column<long>(type: "bigint", nullable: true),
                    SetupTokenHash = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    SetupTokenExpiresAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                    table.CheckConstraint("ck_users_2fa_key", "[TwoFactorEnabled] = 0 OR [AuthenticatorKey] IS NOT NULL");
                    table.CheckConstraint("ck_users_failed_sign_in", "[FailedSignInCount] >= 0");
                    table.CheckConstraint("ck_users_setup_token", "([SetupTokenHash] IS NULL AND [SetupTokenExpiresAtUtc] IS NULL) OR ([SetupTokenHash] IS NOT NULL AND [SetupTokenExpiresAtUtc] IS NOT NULL)");
                    table.CheckConstraint("ck_users_status", "[Status] IN (1, 2, 4)");
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_roles_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "organization_members",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    JoinedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    BlockedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    BlockedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_members", x => x.Id);
                    table.CheckConstraint("ck_organization_members_blocked", "[Status] <> 2 OR [BlockedAtUtc] IS NOT NULL");
                    table.CheckConstraint("ck_organization_members_status", "[Status] IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_organization_members_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_organization_members_users_UserId",
                        column: x => x.UserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role_assignments",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    RoleId = table.Column<long>(type: "bigint", nullable: true),
                    PermissionCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsDeny = table.Column<bool>(type: "bit", nullable: false),
                    DepartmentId = table.Column<long>(type: "bigint", nullable: true),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: true),
                    ValidFromUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ValidToUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    GrantedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    GrantedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RevokedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_assignments", x => x.Id);
                    table.CheckConstraint("ck_role_assignments_deny", "[IsDeny] = 0 OR [PermissionCode] IS NOT NULL");
                    table.CheckConstraint("ck_role_assignments_deny_reason", "[IsDeny] = 0 OR [Reason] IS NOT NULL");
                    table.CheckConstraint("ck_role_assignments_period", "[ValidToUtc] IS NULL OR [ValidToUtc] > [ValidFromUtc]");
                    table.CheckConstraint("ck_role_assignments_target", "([RoleId] IS NOT NULL AND [PermissionCode] IS NULL) OR ([RoleId] IS NULL AND [PermissionCode] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_role_assignments_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_role_assignments_roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "kniterp",
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_role_assignments_users_GrantedByUserId",
                        column: x => x.GrantedByUserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_role_assignments_users_RevokedByUserId",
                        column: x => x.RevokedByUserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_role_assignments_users_UserId",
                        column: x => x.UserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                schema: "kniterp",
                columns: table => new
                {
                    RoleId = table.Column<long>(type: "bigint", nullable: false),
                    PermissionCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Level = table.Column<byte>(type: "tinyint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_permissions", x => new { x.RoleId, x.PermissionCode });
                    table.CheckConstraint("ck_role_permissions_level", "[Level] IN (1, 2, 3, 10, 11)");
                    table.ForeignKey(
                        name: "FK_role_permissions_roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "kniterp",
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_org_time",
                schema: "kniterp",
                table: "audit_log",
                columns: new[] { "OrganizationId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_organization_members_UserId",
                schema: "kniterp",
                table: "organization_members",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "ux_organization_members_org_user",
                schema: "kniterp",
                table: "organization_members",
                columns: new[] { "OrganizationId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_organizations_inn",
                schema: "kniterp",
                table: "organizations",
                column: "Inn",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_role_assignments_GrantedByUserId",
                schema: "kniterp",
                table: "role_assignments",
                column: "GrantedByUserId");

            migrationBuilder.CreateIndex(
                name: "ix_role_assignments_org_user",
                schema: "kniterp",
                table: "role_assignments",
                columns: new[] { "OrganizationId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_role_assignments_RevokedByUserId",
                schema: "kniterp",
                table: "role_assignments",
                column: "RevokedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_role_assignments_RoleId",
                schema: "kniterp",
                table: "role_assignments",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_role_assignments_UserId",
                schema: "kniterp",
                table: "role_assignments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "ux_roles_org_code",
                schema: "kniterp",
                table: "roles",
                columns: new[] { "OrganizationId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_users_normalized_email",
                schema: "kniterp",
                table: "users",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_users_setup_token",
                schema: "kniterp",
                table: "users",
                column: "SetupTokenHash",
                unique: true,
                filter: "[SetupTokenHash] IS NOT NULL");

            // Журнал аудита неизменяем на уровне SQL Server: UPDATE и DELETE откатываются (ТЗ §4.8 KA3644).
            // EXEC нужен для идемпотентного скрипта: там тело миграции внутри IF ... BEGIN, а CREATE TRIGGER
            // должен быть первой командой пакета.
            migrationBuilder.Sql("""
                EXEC(N'CREATE OR ALTER TRIGGER [kniterp].[tr_audit_log_immutable]
                ON [kniterp].[audit_log]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51000, N''Журнал аудита неизменяем: изменение и удаление записей запрещены.'', 1;
                END');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [kniterp].[tr_audit_log_immutable];");

            migrationBuilder.DropTable(
                name: "audit_log",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "organization_members",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "role_assignments",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "role_permissions",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "users",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "organizations",
                schema: "kniterp");
        }
    }
}
