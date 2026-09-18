using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PracticeTimeBudgetWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Keep the placeholder pre-slice table and its data visible instead of
            // silently treating the old aggregate as a compatible approved budget.
            migrationBuilder.RenameTable(
                name: "budget_versions",
                newName: "legacy_budget_versions");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "completed_at",
                table: "work_tasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "activity",
                table: "time_entries",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "approved_at",
                table: "time_entries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "approved_by_user_id",
                table: "time_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "billable_classification",
                table: "time_entries",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "client_id",
                table: "time_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "correction_reason",
                table: "time_entries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "time_entries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "duration_minutes",
                table: "time_entries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "engagement_id",
                table: "time_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "narrative",
                table: "time_entries",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "narrative_visibility",
                table: "time_entries",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "rate_card_version_id",
                table: "time_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "rate_per_hour",
                table: "time_entries",
                type: "numeric(19,6)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "revision",
                table: "time_entries",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "role",
                table: "time_entries",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "start_minute",
                table: "time_entries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "submitted_at",
                table: "time_entries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "work_date",
                table: "time_entries",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.Sql("""
                DO $do$
                BEGIN
                  IF EXISTS (SELECT 1 FROM work_tasks
                             WHERE upper(status) NOT IN ('OPEN','IN_PROGRESS','COMPLETED','CANCELLED')) THEN
                    RAISE EXCEPTION 'PracticeTimeBudgetWorkflow cannot map an unknown work task status';
                  END IF;
                  IF EXISTS (SELECT 1 FROM time_entries
                             WHERE hours <= 0 OR hours * 60 <> trunc(hours * 60)) THEN
                    RAISE EXCEPTION 'PracticeTimeBudgetWorkflow cannot losslessly map legacy time hours to minutes';
                  END IF;
                  IF EXISTS (SELECT 1 FROM time_entries t
                             LEFT JOIN work_tasks w ON w.id = t.task_id AND w.firm_id = t.firm_id
                             WHERE w.id IS NULL) THEN
                    RAISE EXCEPTION 'PracticeTimeBudgetWorkflow found an orphan legacy time task';
                  END IF;
                END $do$;

                UPDATE work_tasks
                SET status = upper(status);

                UPDATE time_entries t
                SET work_date = (t.occurred_on AT TIME ZONE 'UTC')::date,
                    duration_minutes = round(t.hours * 60)::integer,
                    client_id = w.client_id,
                    engagement_id = w.engagement_id,
                    role = 'LEGACY',
                    activity = 'LEGACY',
                    billable_classification = 'NON_BILLABLE',
                    narrative_visibility = 'INTERNAL',
                    status = CASE upper(t.status)
                      WHEN 'CORRECTED' THEN 'SUPERSEDED'
                      WHEN 'DRAFT' THEN 'DRAFT'
                      WHEN 'SUBMITTED' THEN 'SUBMITTED'
                      WHEN 'APPROVED' THEN 'APPROVED'
                      ELSE 'DRAFT'
                    END,
                    revision = 1
                FROM work_tasks w
                WHERE w.id = t.task_id AND w.firm_id = t.firm_id;

                ALTER TABLE time_entries DROP COLUMN hours;
                ALTER TABLE time_entries DROP COLUMN occurred_on;
                """);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_work_tasks_firm_id_id",
                table: "work_tasks",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_time_entries_firm_id_id",
                table: "time_entries",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.CreateTable(
                name: "engagement_budgets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_engagement_budgets", x => x.id);
                    table.UniqueConstraint("AK_budgets_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_budget_state", "version >= 1 AND currency ~ '^[A-Z]{3}$' AND status IN ('DRAFT','APPROVED','SUPERSEDED')");
                    table.ForeignKey(
                        name: "FK_engagement_budgets_engagements_firm_id_engagement_id",
                        columns: x => new { x.firm_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_engagement_budgets_users_firm_id_approved_by_user_id",
                        columns: x => new { x.firm_id, x.approved_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_engagement_budgets_users_firm_id_created_by_user_id",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rate_card_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    activity = table.Column<string>(type: "text", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    rate_per_hour = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rate_card_versions", x => x.id);
                    table.UniqueConstraint("AK_rate_cards_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_rate_card_state", "version >= 1 AND length(role) > 0 AND length(activity) > 0 AND currency ~ '^[A-Z]{3}$' AND rate_per_hour >= 0 AND status IN ('DRAFT','APPROVED','SUPERSEDED')");
                    table.ForeignKey(
                        name: "FK_rate_card_versions_users_firm_id_approved_by_user_id",
                        columns: x => new { x.firm_id, x.approved_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_rate_card_versions_users_firm_id_created_by_user_id",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "budget_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_budget_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rate_card_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    activity = table.Column<string>(type: "text", nullable: false),
                    forecast_minutes = table.Column<int>(type: "integer", nullable: false),
                    rate_per_hour = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    forecast_cost = table.Column<decimal>(type: "numeric(19,6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_budget_lines", x => x.id);
                    table.UniqueConstraint("AK_budget_lines_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_budget_line_values", "length(role) > 0 AND length(activity) > 0 AND forecast_minutes > 0 AND rate_per_hour >= 0 AND forecast_cost >= 0");
                    table.ForeignKey(
                        name: "FK_budget_lines_engagement_budgets_firm_id_engagement_budget_id",
                        columns: x => new { x.firm_id, x.engagement_budget_id },
                        principalTable: "engagement_budgets",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_budget_lines_rate_card_versions_firm_id_rate_card_version_id",
                        columns: x => new { x.firm_id, x.rate_card_version_id },
                        principalTable: "rate_card_versions",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_work_tasks_firm_id_assignee_user_id",
                table: "work_tasks",
                columns: new[] { "firm_id", "assignee_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_work_tasks_firm_id_client_id_engagement_id",
                table: "work_tasks",
                columns: new[] { "firm_id", "client_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "IX_work_tasks_firm_id_status_created_at",
                table: "work_tasks",
                columns: new[] { "firm_id", "status", "created_at" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_task_state",
                table: "work_tasks",
                sql: "status IN ('OPEN','IN_PROGRESS','COMPLETED','CANCELLED') AND length(title) > 0 AND (engagement_id IS NULL OR client_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_time_entries_firm_id_approved_by_user_id",
                table: "time_entries",
                columns: new[] { "firm_id", "approved_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_time_entries_firm_id_client_id_engagement_id",
                table: "time_entries",
                columns: new[] { "firm_id", "client_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "IX_time_entries_firm_id_rate_card_version_id",
                table: "time_entries",
                columns: new[] { "firm_id", "rate_card_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_time_entries_firm_id_supersedes_id",
                table: "time_entries",
                columns: new[] { "firm_id", "supersedes_id" });

            migrationBuilder.CreateIndex(
                name: "IX_time_entries_firm_id_task_id",
                table: "time_entries",
                columns: new[] { "firm_id", "task_id" });

            migrationBuilder.CreateIndex(
                name: "IX_time_entries_firm_id_user_id_work_date_status",
                table: "time_entries",
                columns: new[] { "firm_id", "user_id", "work_date", "status" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_time_entry_content",
                table: "time_entries",
                sql: "length(role) > 0 AND length(activity) > 0 AND billable_classification IN ('BILLABLE','NON_BILLABLE','NO_CHARGE') AND narrative_visibility IN ('INTERNAL','CLIENT_VISIBLE') AND ((billable_classification = 'BILLABLE' AND rate_card_version_id IS NOT NULL AND rate_per_hour IS NOT NULL AND rate_per_hour >= 0) OR billable_classification <> 'BILLABLE')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_time_entry_state",
                table: "time_entries",
                sql: "status IN ('DRAFT','SUBMITTED','APPROVED','SUPERSEDED') AND revision >= 1 AND start_minute BETWEEN 0 AND 1439 AND duration_minutes BETWEEN 1 AND 1440 AND start_minute + duration_minutes <= 1440");

            migrationBuilder.CreateIndex(
                name: "IX_budget_lines_firm_id_engagement_budget_id_role_activity",
                table: "budget_lines",
                columns: new[] { "firm_id", "engagement_budget_id", "role", "activity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_budget_lines_firm_id_rate_card_version_id",
                table: "budget_lines",
                columns: new[] { "firm_id", "rate_card_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_engagement_budgets_firm_id_approved_by_user_id",
                table: "engagement_budgets",
                columns: new[] { "firm_id", "approved_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_engagement_budgets_firm_id_created_by_user_id",
                table: "engagement_budgets",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_engagement_budgets_firm_id_engagement_id_version",
                table: "engagement_budgets",
                columns: new[] { "firm_id", "engagement_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rate_card_versions_firm_id_approved_by_user_id",
                table: "rate_card_versions",
                columns: new[] { "firm_id", "approved_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_rate_card_versions_firm_id_created_by_user_id",
                table: "rate_card_versions",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_rate_card_versions_firm_id_role_activity_currency_version",
                table: "rate_card_versions",
                columns: new[] { "firm_id", "role", "activity", "currency", "version" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_time_entries_engagements_firm_id_client_id_engagement_id",
                table: "time_entries",
                columns: new[] { "firm_id", "client_id", "engagement_id" },
                principalTable: "engagements",
                principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_time_entries_practice_clients_firm_id_client_id",
                table: "time_entries",
                columns: new[] { "firm_id", "client_id" },
                principalTable: "practice_clients",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_time_entries_rate_card_versions_firm_id_rate_card_version_id",
                table: "time_entries",
                columns: new[] { "firm_id", "rate_card_version_id" },
                principalTable: "rate_card_versions",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_time_entries_time_entries_firm_id_supersedes_id",
                table: "time_entries",
                columns: new[] { "firm_id", "supersedes_id" },
                principalTable: "time_entries",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_time_entries_users_firm_id_approved_by_user_id",
                table: "time_entries",
                columns: new[] { "firm_id", "approved_by_user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_time_entries_users_firm_id_user_id",
                table: "time_entries",
                columns: new[] { "firm_id", "user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_time_entries_work_tasks_firm_id_task_id",
                table: "time_entries",
                columns: new[] { "firm_id", "task_id" },
                principalTable: "work_tasks",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_work_tasks_engagements_firm_id_client_id_engagement_id",
                table: "work_tasks",
                columns: new[] { "firm_id", "client_id", "engagement_id" },
                principalTable: "engagements",
                principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_work_tasks_practice_clients_firm_id_client_id",
                table: "work_tasks",
                columns: new[] { "firm_id", "client_id" },
                principalTable: "practice_clients",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_work_tasks_users_firm_id_assignee_user_id",
                table: "work_tasks",
                columns: new[] { "firm_id", "assignee_user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_time_entries_engagements_firm_id_client_id_engagement_id",
                table: "time_entries");

            migrationBuilder.DropForeignKey(
                name: "FK_time_entries_practice_clients_firm_id_client_id",
                table: "time_entries");

            migrationBuilder.DropForeignKey(
                name: "FK_time_entries_rate_card_versions_firm_id_rate_card_version_id",
                table: "time_entries");

            migrationBuilder.DropForeignKey(
                name: "FK_time_entries_time_entries_firm_id_supersedes_id",
                table: "time_entries");

            migrationBuilder.DropForeignKey(
                name: "FK_time_entries_users_firm_id_approved_by_user_id",
                table: "time_entries");

            migrationBuilder.DropForeignKey(
                name: "FK_time_entries_users_firm_id_user_id",
                table: "time_entries");

            migrationBuilder.DropForeignKey(
                name: "FK_time_entries_work_tasks_firm_id_task_id",
                table: "time_entries");

            migrationBuilder.DropForeignKey(
                name: "FK_work_tasks_engagements_firm_id_client_id_engagement_id",
                table: "work_tasks");

            migrationBuilder.DropForeignKey(
                name: "FK_work_tasks_practice_clients_firm_id_client_id",
                table: "work_tasks");

            migrationBuilder.DropForeignKey(
                name: "FK_work_tasks_users_firm_id_assignee_user_id",
                table: "work_tasks");

            migrationBuilder.DropTable(
                name: "budget_lines");

            migrationBuilder.DropTable(
                name: "engagement_budgets");

            migrationBuilder.DropTable(
                name: "rate_card_versions");

            migrationBuilder.RenameTable(
                name: "legacy_budget_versions",
                newName: "budget_versions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_work_tasks_firm_id_id",
                table: "work_tasks");

            migrationBuilder.DropIndex(
                name: "IX_work_tasks_firm_id_assignee_user_id",
                table: "work_tasks");

            migrationBuilder.DropIndex(
                name: "IX_work_tasks_firm_id_client_id_engagement_id",
                table: "work_tasks");

            migrationBuilder.DropIndex(
                name: "IX_work_tasks_firm_id_status_created_at",
                table: "work_tasks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_work_task_state",
                table: "work_tasks");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_time_entries_firm_id_id",
                table: "time_entries");

            migrationBuilder.DropIndex(
                name: "IX_time_entries_firm_id_approved_by_user_id",
                table: "time_entries");

            migrationBuilder.DropIndex(
                name: "IX_time_entries_firm_id_client_id_engagement_id",
                table: "time_entries");

            migrationBuilder.DropIndex(
                name: "IX_time_entries_firm_id_rate_card_version_id",
                table: "time_entries");

            migrationBuilder.DropIndex(
                name: "IX_time_entries_firm_id_supersedes_id",
                table: "time_entries");

            migrationBuilder.DropIndex(
                name: "IX_time_entries_firm_id_task_id",
                table: "time_entries");

            migrationBuilder.DropIndex(
                name: "IX_time_entries_firm_id_user_id_work_date_status",
                table: "time_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_time_entry_content",
                table: "time_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_time_entry_state",
                table: "time_entries");

            migrationBuilder.AddColumn<decimal>(
                name: "hours",
                table: "time_entries",
                type: "numeric(19,6)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "occurred_on",
                table: "time_entries",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.Sql("""
                UPDATE time_entries
                SET hours = duration_minutes / 60.0,
                    occurred_on = ((work_date::timestamp + start_minute * interval '1 minute') AT TIME ZONE 'UTC');
                """);

            migrationBuilder.DropColumn(
                name: "completed_at",
                table: "work_tasks");

            migrationBuilder.DropColumn(
                name: "activity",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "approved_at",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "approved_by_user_id",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "billable_classification",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "client_id",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "correction_reason",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "duration_minutes",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "engagement_id",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "narrative",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "narrative_visibility",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "rate_card_version_id",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "rate_per_hour",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "revision",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "role",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "start_minute",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "submitted_at",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "work_date",
                table: "time_entries");

        }
    }
}
