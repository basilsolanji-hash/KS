using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnitErp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCashFlowAndExpenseReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CashFlowItemId",
                schema: "kniterp",
                table: "money_operations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CashOrderNumber",
                schema: "kniterp",
                table: "money_operations",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "EmployeeId",
                schema: "kniterp",
                table: "money_operations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetCashOrderNumber",
                schema: "kniterp",
                table: "money_operations",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_employees_OrganizationId_Id",
                schema: "kniterp",
                table: "employees",
                columns: new[] { "OrganizationId", "Id" });

            migrationBuilder.CreateTable(
                name: "cash_flow_items",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Direction = table.Column<byte>(type: "tinyint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SystemCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_flow_items", x => x.Id);
                    table.UniqueConstraint("AK_cash_flow_items_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("ck_cash_flow_items_direction", "[Direction] IN (1, 2)");
                    table.CheckConstraint("ck_cash_flow_items_system_active", "[SystemCode] IS NULL OR [IsArchived] = 0");
                    table.ForeignKey(
                        name: "FK_cash_flow_items_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "expense_reports",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EmployeeId = table.Column<long>(type: "bigint", nullable: false),
                    LegalEntityId = table.Column<long>(type: "bigint", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ApprovedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CancelledByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LinesRevision = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expense_reports", x => x.Id);
                    table.CheckConstraint("ck_expense_reports_approved", "([Status] = 1 AND [ApprovedAtUtc] IS NULL) OR ([Status] = 2 AND [ApprovedAtUtc] IS NOT NULL) OR [Status] = 9");
                    table.CheckConstraint("ck_expense_reports_cancel", "([Status] = 9 AND [CancelledAtUtc] IS NOT NULL AND [CancelReason] IS NOT NULL) OR ([Status] <> 9 AND [CancelledAtUtc] IS NULL)");
                    table.CheckConstraint("ck_expense_reports_status", "[Status] IN (1, 2, 9)");
                    table.ForeignKey(
                        name: "FK_expense_reports_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "kniterp",
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_expense_reports_users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_expense_reports_users_CancelledByUserId",
                        column: x => x.CancelledByUserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_expense_reports_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "kniterp",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_expense_reports_employee",
                        columns: x => new { x.OrganizationId, x.EmployeeId },
                        principalSchema: "kniterp",
                        principalTable: "employees",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_expense_reports_legal_entity",
                        columns: x => new { x.OrganizationId, x.LegalEntityId },
                        principalSchema: "kniterp",
                        principalTable: "legal_entities",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "expense_report_lines",
                schema: "kniterp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExpenseReportId = table.Column<long>(type: "bigint", nullable: false),
                    DocumentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Document = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    CashFlowItemId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expense_report_lines", x => x.Id);
                    table.CheckConstraint("ck_expense_report_lines_amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_expense_report_lines_cash_flow_items_CashFlowItemId",
                        column: x => x.CashFlowItemId,
                        principalSchema: "kniterp",
                        principalTable: "cash_flow_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_expense_report_lines_expense_reports_ExpenseReportId",
                        column: x => x.ExpenseReportId,
                        principalSchema: "kniterp",
                        principalTable: "expense_reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_money_operations_OrganizationId_CashFlowItemId",
                schema: "kniterp",
                table: "money_operations",
                columns: new[] { "OrganizationId", "CashFlowItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_money_operations_OrganizationId_EmployeeId",
                schema: "kniterp",
                table: "money_operations",
                columns: new[] { "OrganizationId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "ux_money_operations_org_cash_order",
                schema: "kniterp",
                table: "money_operations",
                columns: new[] { "OrganizationId", "CashOrderNumber" },
                unique: true,
                filter: "[CashOrderNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_money_operations_org_target_cash_order",
                schema: "kniterp",
                table: "money_operations",
                columns: new[] { "OrganizationId", "TargetCashOrderNumber" },
                unique: true,
                filter: "[TargetCashOrderNumber] IS NOT NULL");

            // D86: стандартные статьи ДДС каждой существующей организации — списком в самой миграции (а не из CashFlowItem.Defaults),
            // чтобы применённая миграция не менялась вместе с кодом. Прежние операции получают статьи «Прочие поступления» и «Прочие выплаты».
            migrationBuilder.Sql(@"EXEC(N'INSERT INTO [kniterp].[cash_flow_items] ([OrganizationId], [Direction], [Name], [SystemCode], [IsArchived])
SELECT o.[Id], v.[Direction], v.[Name], v.[SystemCode], 0 FROM [kniterp].[organizations] o
CROSS JOIN (VALUES
(1, N''Поступления от покупателей'', N''customer_payments''),
(1, N''Возврат подотчётных сумм'', N''accountable_return''),
(1, N''Получение займов и кредитов'', NULL),
(1, N''Взносы учредителей'', NULL),
(1, N''Проценты банка'', NULL),
(1, N''Прочие поступления'', N''other_income''),
(2, N''Оплата поставщикам'', N''supplier_payments''),
(2, N''Выдача под отчёт'', N''accountable_issue''),
(2, N''Заработная плата'', NULL),
(2, N''Налоги и страховые взносы'', NULL),
(2, N''Аренда'', NULL),
(2, N''Банковские комиссии'', NULL),
(2, N''Возврат займов и кредитов'', NULL),
(2, N''Прочие выплаты'', N''other_expense'')
) v([Direction], [Name], [SystemCode]);
UPDATE m SET m.[CashFlowItemId] = i.[Id] FROM [kniterp].[money_operations] m
JOIN [kniterp].[cash_flow_items] i ON i.[OrganizationId] = m.[OrganizationId]
 AND i.[SystemCode] = CASE m.[Kind] WHEN 1 THEN N''other_income'' ELSE N''other_expense'' END
WHERE m.[Kind] IN (1, 2) AND m.[CashFlowItemId] IS NULL;')");

            // Строки авансового отчёта меняются только у черновика: утверждённый или отменённый отчёт не изменить в обход приложения.
            migrationBuilder.Sql("""
                EXEC(N'CREATE OR ALTER TRIGGER [kniterp].[tr_expense_report_lines_draft_only]
                ON [kniterp].[expense_report_lines]
                AFTER INSERT, UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM (SELECT [ExpenseReportId] FROM inserted UNION ALL SELECT [ExpenseReportId] FROM deleted) x
                               JOIN [kniterp].[expense_reports] r ON r.[Id] = x.[ExpenseReportId] WHERE r.[Status] <> 1)
                        THROW 51003, N''Строки утверждённого авансового отчёта не меняются — отмените отчёт и составьте новый.'', 1;
                END');
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_money_operations_item_required",
                schema: "kniterp",
                table: "money_operations",
                sql: "[Kind] = 3 OR [CashFlowItemId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_money_operations_transfer_item",
                schema: "kniterp",
                table: "money_operations",
                sql: "[Kind] <> 3 OR ([CashFlowItemId] IS NULL AND [EmployeeId] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_money_operations_transfer_orders",
                schema: "kniterp",
                table: "money_operations",
                sql: "[Kind] = 3 OR ([CashOrderNumber] IS NULL AND [TargetCashOrderNumber] IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ux_cash_flow_items_org_direction_name",
                schema: "kniterp",
                table: "cash_flow_items",
                columns: new[] { "OrganizationId", "Direction", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_cash_flow_items_org_system",
                schema: "kniterp",
                table: "cash_flow_items",
                columns: new[] { "OrganizationId", "SystemCode" },
                unique: true,
                filter: "[SystemCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_expense_report_lines_CashFlowItemId",
                schema: "kniterp",
                table: "expense_report_lines",
                column: "CashFlowItemId");

            migrationBuilder.CreateIndex(
                name: "IX_expense_report_lines_ExpenseReportId",
                schema: "kniterp",
                table: "expense_report_lines",
                column: "ExpenseReportId");

            migrationBuilder.CreateIndex(
                name: "IX_expense_reports_ApprovedByUserId",
                schema: "kniterp",
                table: "expense_reports",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_expense_reports_CancelledByUserId",
                schema: "kniterp",
                table: "expense_reports",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_expense_reports_CreatedByUserId",
                schema: "kniterp",
                table: "expense_reports",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "ix_expense_reports_org_employee_date",
                schema: "kniterp",
                table: "expense_reports",
                columns: new[] { "OrganizationId", "EmployeeId", "ReportDate" });

            migrationBuilder.CreateIndex(
                name: "IX_expense_reports_OrganizationId_LegalEntityId",
                schema: "kniterp",
                table: "expense_reports",
                columns: new[] { "OrganizationId", "LegalEntityId" });

            migrationBuilder.CreateIndex(
                name: "ux_expense_reports_org_number",
                schema: "kniterp",
                table: "expense_reports",
                columns: new[] { "OrganizationId", "Number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_money_operations_cash_flow_item",
                schema: "kniterp",
                table: "money_operations",
                columns: new[] { "OrganizationId", "CashFlowItemId" },
                principalSchema: "kniterp",
                principalTable: "cash_flow_items",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_money_operations_employee",
                schema: "kniterp",
                table: "money_operations",
                columns: new[] { "OrganizationId", "EmployeeId" },
                principalSchema: "kniterp",
                principalTable: "employees",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("EXEC(N'DROP TRIGGER IF EXISTS [kniterp].[tr_expense_report_lines_draft_only]');");

            migrationBuilder.DropForeignKey(
                name: "fk_money_operations_cash_flow_item",
                schema: "kniterp",
                table: "money_operations");

            migrationBuilder.DropForeignKey(
                name: "fk_money_operations_employee",
                schema: "kniterp",
                table: "money_operations");

            migrationBuilder.DropTable(
                name: "expense_report_lines",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "cash_flow_items",
                schema: "kniterp");

            migrationBuilder.DropTable(
                name: "expense_reports",
                schema: "kniterp");

            migrationBuilder.DropIndex(
                name: "IX_money_operations_OrganizationId_CashFlowItemId",
                schema: "kniterp",
                table: "money_operations");

            migrationBuilder.DropIndex(
                name: "IX_money_operations_OrganizationId_EmployeeId",
                schema: "kniterp",
                table: "money_operations");

            migrationBuilder.DropIndex(
                name: "ux_money_operations_org_cash_order",
                schema: "kniterp",
                table: "money_operations");

            migrationBuilder.DropIndex(
                name: "ux_money_operations_org_target_cash_order",
                schema: "kniterp",
                table: "money_operations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_money_operations_item_required",
                schema: "kniterp",
                table: "money_operations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_money_operations_transfer_item",
                schema: "kniterp",
                table: "money_operations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_money_operations_transfer_orders",
                schema: "kniterp",
                table: "money_operations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_employees_OrganizationId_Id",
                schema: "kniterp",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "CashFlowItemId",
                schema: "kniterp",
                table: "money_operations");

            migrationBuilder.DropColumn(
                name: "CashOrderNumber",
                schema: "kniterp",
                table: "money_operations");

            migrationBuilder.DropColumn(
                name: "EmployeeId",
                schema: "kniterp",
                table: "money_operations");

            migrationBuilder.DropColumn(
                name: "TargetCashOrderNumber",
                schema: "kniterp",
                table: "money_operations");
        }
    }
}
