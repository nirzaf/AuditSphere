using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScheduledFileFreezeAndDocumentLocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_locks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_key = table.Column<string>(type: "text", nullable: false),
                    locked_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    locked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    released_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_locks", x => x.id);
                    table.CheckConstraint("ck_document_lock_values", "length(document_key) > 0 AND ((released_at IS NULL) = (released_by_user_id IS NULL))");
                });

            migrationBuilder.CreateTable(
                name: "engagement_file_freezes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    report_deliverable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    report_signed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    frozen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    external_read_only = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_engagement_file_freezes", x => x.id);
                    table.CheckConstraint("ck_engagement_file_freeze_values", "state IN ('SCHEDULED','FROZEN','AMENDMENT_OPEN') AND external_read_only IN ('NOT_REQUESTED','REQUESTED','OBSERVED','BLOCKED_EXTERNAL') AND due_at = report_signed_at + interval '60 days' AND revision >= 1 AND ((state = 'SCHEDULED') = (frozen_at IS NULL))");
                    table.ForeignKey(
                        name: "FK_engagement_file_freezes_audit_deliverables_report_deliverab~",
                        column: x => x.report_deliverable_id,
                        principalTable: "audit_deliverables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_engagement_file_freezes_engagements_engagement_id",
                        column: x => x.engagement_id,
                        principalTable: "engagements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "frozen_access_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    attempted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_frozen_access_attempts", x => x.id);
                    table.CheckConstraint("ck_frozen_access_attempt_values", "length(action) > 0");
                });

            migrationBuilder.CreateTable(
                name: "file_freeze_amendments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    freeze_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_freeze_amendments", x => x.id);
                    table.CheckConstraint("ck_file_freeze_amendment_values", "length(reason) > 0 AND (approved_by_user_id IS NULL OR approved_by_user_id <> requested_by_user_id) AND ((opened_at IS NULL) = (approved_by_user_id IS NULL)) AND (closed_at IS NULL OR opened_at IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_file_freeze_amendments_engagement_file_freezes_freeze_id",
                        column: x => x.freeze_id,
                        principalTable: "engagement_file_freezes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_locks_firm_id_engagement_id_document_key",
                table: "document_locks",
                columns: new[] { "firm_id", "engagement_id", "document_key" },
                unique: true,
                filter: "released_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_engagement_file_freezes_engagement_id",
                table: "engagement_file_freezes",
                column: "engagement_id");

            migrationBuilder.CreateIndex(
                name: "IX_engagement_file_freezes_firm_id_engagement_id",
                table: "engagement_file_freezes",
                columns: new[] { "firm_id", "engagement_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_engagement_file_freezes_firm_id_state_due_at",
                table: "engagement_file_freezes",
                columns: new[] { "firm_id", "state", "due_at" });

            migrationBuilder.CreateIndex(
                name: "IX_engagement_file_freezes_report_deliverable_id",
                table: "engagement_file_freezes",
                column: "report_deliverable_id");

            migrationBuilder.CreateIndex(
                name: "IX_file_freeze_amendments_firm_id_freeze_id_requested_at",
                table: "file_freeze_amendments",
                columns: new[] { "firm_id", "freeze_id", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "IX_file_freeze_amendments_freeze_id",
                table: "file_freeze_amendments",
                column: "freeze_id");

            migrationBuilder.CreateIndex(
                name: "IX_frozen_access_attempts_firm_id_engagement_id_attempted_at",
                table: "frozen_access_attempts",
                columns: new[] { "firm_id", "engagement_id", "attempted_at" });
            migrationBuilder.Sql("""
                CREATE FUNCTION prevent_freeze_evidence_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION '% rows are append-only freeze evidence', TG_TABLE_NAME;
                END;
                $$;
                CREATE TRIGGER trg_frozen_access_attempts_append_only BEFORE UPDATE OR DELETE ON frozen_access_attempts FOR EACH ROW EXECUTE FUNCTION prevent_freeze_evidence_mutation();
                CREATE FUNCTION prevent_frozen_file_unfreeze() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF OLD.state IN ('FROZEN','AMENDMENT_OPEN') AND NEW.state = 'SCHEDULED' THEN
                    RAISE EXCEPTION 'a frozen file cannot return to scheduled';
                  END IF;
                  IF OLD.state = 'FROZEN' AND NEW.state = 'AMENDMENT_OPEN' AND NOT EXISTS (
                    SELECT 1 FROM file_freeze_amendments a WHERE a.freeze_id = NEW.id AND a.approved_by_user_id IS NOT NULL AND a.closed_at IS NULL) THEN
                    RAISE EXCEPTION 'reopening a frozen file requires an approved amendment';
                  END IF;
                  IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'freeze records are retained'; END IF;
                  RETURN NEW;
                END;
                $$;
                CREATE TRIGGER trg_engagement_file_freezes_guard BEFORE UPDATE OR DELETE ON engagement_file_freezes FOR EACH ROW EXECUTE FUNCTION prevent_frozen_file_unfreeze();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_engagement_file_freezes_guard ON engagement_file_freezes;
                DROP FUNCTION IF EXISTS prevent_frozen_file_unfreeze();
                DROP TRIGGER IF EXISTS trg_frozen_access_attempts_append_only ON frozen_access_attempts;
                DROP FUNCTION IF EXISTS prevent_freeze_evidence_mutation();
                """);

            migrationBuilder.DropTable(
                name: "document_locks");

            migrationBuilder.DropTable(
                name: "file_freeze_amendments");

            migrationBuilder.DropTable(
                name: "frozen_access_attempts");

            migrationBuilder.DropTable(
                name: "engagement_file_freezes");
        }
    }
}
