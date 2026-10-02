using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NativeAdjustmentJournalActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "adjustment_journal_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    journal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    result_journal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    review_basis = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    old_revision = table.Column<long>(type: "bigint", nullable: false),
                    new_revision = table.Column<long>(type: "bigint", nullable: false),
                    old_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    new_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    before_json = table.Column<string>(type: "text", nullable: false),
                    after_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_adjustment_journal_actions", x => x.id);
                    table.CheckConstraint("ck_adjustment_journal_action", "action IN ('UPDATE','SUBMIT','RETURN','POST','REVERSE') AND actor_epoch >= 1 AND old_revision >= 1 AND new_revision >= 1 AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND length(trim(reason)) BETWEEN 1 AND 4000 AND length(trim(evidence_reference)) BETWEEN 1 AND 2000 AND length(before_json) BETWEEN 1 AND 250000 AND length(after_json) BETWEEN 1 AND 250000");
                    table.ForeignKey(
                        name: "FK_adjustment_journal_actions_adjustment_journals_firm_id_clie~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.journal_id },
                        principalTable: "adjustment_journals",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_adjustment_journal_actions_adjustment_journals_firm_id_cli~1",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.result_journal_id },
                        principalTable: "adjustment_journals",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_adjustment_journal_actions_users_firm_id_actor_id",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_adjustment_journal_actions_firm_id_actor_id_request_id",
                table: "adjustment_journal_actions",
                columns: new[] { "firm_id", "actor_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_adjustment_journal_actions_firm_id_client_id_engagement_id_~",
                table: "adjustment_journal_actions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "journal_id" });

            migrationBuilder.CreateIndex(
                name: "IX_adjustment_journal_actions_firm_id_client_id_engagement_id~1",
                table: "adjustment_journal_actions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "result_journal_id" });

            migrationBuilder.CreateIndex(
                name: "IX_adjustment_journal_actions_firm_id_journal_id_created_at",
                table: "adjustment_journal_actions",
                columns: new[] { "firm_id", "journal_id", "created_at" });
            migrationBuilder.Sql("""
                CREATE TRIGGER adjustment_journal_actions_append_only
                  BEFORE UPDATE OR DELETE ON adjustment_journal_actions
                  FOR EACH ROW EXECUTE FUNCTION prevent_completion_evidence_mutation();
                """);
            // Returned is an explicitly editable technical state in the existing
            // service. Check both parents so moving a frozen line cannot evade the guard.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION enforce_adjustment_line_frozen() RETURNS trigger AS $$
                DECLARE parent_state text;
                BEGIN
                  IF TG_OP IN ('DELETE','UPDATE') THEN
                    SELECT status INTO parent_state FROM adjustment_journals WHERE id=OLD.journal_id FOR SHARE;
                    IF parent_state IS NULL OR parent_state NOT IN ('Draft','Returned') THEN
                      RAISE EXCEPTION 'adjustment journal % lines are frozen', OLD.journal_id;
                    END IF;
                  END IF;
                  IF TG_OP IN ('INSERT','UPDATE') THEN
                    SELECT status INTO parent_state FROM adjustment_journals WHERE id=NEW.journal_id FOR SHARE;
                    IF parent_state IS NULL OR parent_state NOT IN ('Draft','Returned') THEN
                      RAISE EXCEPTION 'adjustment journal % lines are frozen', NEW.journal_id;
                    END IF;
                  END IF;
                  IF TG_OP='DELETE' THEN RETURN OLD; END IF;
                  RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM adjustment_journal_actions) THEN
                    RAISE EXCEPTION 'Retained journal command evidence prevents migration rollback';
                  END IF;
                END $$;
                """);
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION enforce_adjustment_line_frozen() RETURNS trigger AS $$
                BEGIN
                  IF TG_OP='DELETE' THEN
                    IF EXISTS(SELECT 1 FROM adjustment_journals WHERE id=OLD.journal_id AND status <> 'Draft') THEN
                      RAISE EXCEPTION 'adjustment journal % is no longer Draft; lines are frozen', OLD.journal_id;
                    END IF;
                    RETURN OLD;
                  END IF;
                  IF EXISTS(SELECT 1 FROM adjustment_journals WHERE id=NEW.journal_id AND status <> 'Draft') THEN
                    RAISE EXCEPTION 'adjustment journal % is no longer Draft; lines are frozen', NEW.journal_id;
                  END IF;
                  RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);
            migrationBuilder.DropTable(
                name: "adjustment_journal_actions");
        }
    }
}
