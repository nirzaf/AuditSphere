using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TimeEntryReturnWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_time_entry_state",
                table: "time_entries");

            migrationBuilder.AddColumn<string>(
                name: "last_return_reason",
                table: "time_entries",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "submission_round",
                table: "time_entries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "time_entry_review_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    time_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_round = table.Column<int>(type: "integer", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_time_entry_review_events", x => x.id);
                    table.UniqueConstraint("AK_time_entry_review_events_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_time_entry_review_event_values", "submission_round >= 1 AND ((action = 'LEGACY_SUBMISSION' AND actor_user_id IS NULL AND reason IS NULL) OR (action = 'RETURNED' AND actor_user_id IS NOT NULL AND reason IS NOT NULL AND length(trim(reason)) BETWEEN 1 AND 2000) OR (action IN ('SUBMITTED','APPROVED') AND actor_user_id IS NOT NULL AND reason IS NULL))");
                    table.ForeignKey(
                        name: "FK_time_entry_review_events_time_entries_firm_id_time_entry_id",
                        columns: x => new { x.firm_id, x.time_entry_id },
                        principalTable: "time_entries",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_time_entry_review_events_users_firm_id_actor_user_id",
                        columns: x => new { x.firm_id, x.actor_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("UPDATE time_entries SET submission_round = 1 WHERE status IN ('SUBMITTED','APPROVED','SUPERSEDED');");
            migrationBuilder.Sql("""
                INSERT INTO time_entry_review_events (id, firm_id, time_entry_id, submission_round, actor_user_id, action, reason, created_at)
                SELECT gen_random_uuid(), firm_id, id, submission_round, NULL, 'LEGACY_SUBMISSION', NULL, submitted_at
                FROM time_entries
                WHERE status IN ('SUBMITTED','APPROVED','SUPERSEDED') AND submitted_at IS NOT NULL;
                INSERT INTO time_entry_review_events (id, firm_id, time_entry_id, submission_round, actor_user_id, action, reason, created_at)
                SELECT gen_random_uuid(), firm_id, id, submission_round, approved_by_user_id, 'APPROVED', NULL, approved_at
                FROM time_entries
                WHERE status IN ('APPROVED','SUPERSEDED') AND approved_by_user_id IS NOT NULL AND approved_at IS NOT NULL;
                CREATE FUNCTION prevent_time_entry_review_event_mutation() RETURNS trigger AS $$
                BEGIN
                  RAISE EXCEPTION 'time entry review history is immutable' USING ERRCODE = '55000';
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_time_entry_review_events_immutable
                BEFORE UPDATE OR DELETE ON time_entry_review_events
                FOR EACH ROW EXECUTE FUNCTION prevent_time_entry_review_event_mutation();
                CREATE TRIGGER trg_time_entry_review_events_no_truncate
                BEFORE TRUNCATE ON time_entry_review_events
                FOR EACH STATEMENT EXECUTE FUNCTION prevent_time_entry_review_event_mutation();
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_time_entry_state",
                table: "time_entries",
                sql: "status IN ('DRAFT','SUBMITTED','RETURNED','APPROVED','SUPERSEDED') AND revision >= 1 AND submission_round >= 0 AND (last_return_reason IS NULL OR length(trim(last_return_reason)) BETWEEN 1 AND 2000) AND start_minute BETWEEN 0 AND 1439 AND duration_minutes BETWEEN 1 AND 1440 AND start_minute + duration_minutes <= 1440");

            migrationBuilder.CreateIndex(
                name: "IX_time_entry_review_events_firm_id_actor_user_id",
                table: "time_entry_review_events",
                columns: new[] { "firm_id", "actor_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_time_entry_review_events_firm_id_time_entry_id_submission_r~",
                table: "time_entry_review_events",
                columns: new[] { "firm_id", "time_entry_id", "submission_round", "action" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("LOCK TABLE time_entry_review_events IN ACCESS EXCLUSIVE MODE; DO $$ BEGIN IF EXISTS (SELECT 1 FROM time_entry_review_events) THEN RAISE EXCEPTION 'Time-entry review history is append-only; downgrade refused.' USING ERRCODE = '55000'; END IF; END $$;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_time_entry_review_events_no_truncate ON time_entry_review_events; DROP TRIGGER IF EXISTS trg_time_entry_review_events_immutable ON time_entry_review_events; DROP FUNCTION IF EXISTS prevent_time_entry_review_event_mutation();");
            migrationBuilder.DropTable(
                name: "time_entry_review_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_time_entry_state",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "last_return_reason",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "submission_round",
                table: "time_entries");

            migrationBuilder.AddCheckConstraint(
                name: "ck_time_entry_state",
                table: "time_entries",
                sql: "status IN ('DRAFT','SUBMITTED','APPROVED','SUPERSEDED') AND revision >= 1 AND start_minute BETWEEN 0 AND 1439 AND duration_minutes BETWEEN 1 AND 1440 AND start_minute + duration_minutes <= 1440");
        }
    }
}
