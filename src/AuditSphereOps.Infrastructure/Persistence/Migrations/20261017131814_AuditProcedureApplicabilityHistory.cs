using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditProcedureApplicabilityHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_procedure_values",
                table: "audit_procedures");

            migrationBuilder.AddColumn<Guid>(
                name: "current_applicability_decision_id",
                table: "audit_procedures",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "current_applicability_revision",
                table: "audit_procedures",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "audit_procedure_applicability_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audit_procedure_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    definition_revision = table.Column<int>(type: "integer", nullable: false),
                    input_generation = table.Column<long>(type: "bigint", nullable: false),
                    action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    rationale = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    proposal_decision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    previous_decision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_procedure_applicability_decisions", x => x.id);
                    table.UniqueConstraint("AK_audit_procedure_applicability_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.audit_procedure_id, x.id });
                    table.CheckConstraint("ck_audit_procedure_applicability_values", "revision > 0 AND definition_revision >= 0 AND input_generation > 0 AND actor_epoch > 0 AND action IN ('APPLICABLE','PROPOSE_NOT_APPLICABLE','APPROVE_NOT_APPLICABLE','RETURN_NOT_APPLICABLE') AND length(request_digest) = 64 AND (action NOT IN ('PROPOSE_NOT_APPLICABLE','RETURN_NOT_APPLICABLE') OR length(trim(coalesce(rationale,''))) BETWEEN 1 AND 1000) AND (action NOT IN ('APPROVE_NOT_APPLICABLE','RETURN_NOT_APPLICABLE') OR proposal_decision_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_audit_procedure_applicability_decisions_audit_procedures_fi~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.audit_procedure_id },
                        principalTable: "audit_procedures",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_procedure_applicability_decisions_engagements_firm_id~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_procedure_applicability_decisions_users_firm_id_actor~",
                        columns: x => new { x.firm_id, x.actor_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_procedure_applicability_previous",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.audit_procedure_id, x.previous_decision_id },
                        principalTable: "audit_procedure_applicability_decisions",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "audit_procedure_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_procedure_applicability_proposal",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.audit_procedure_id, x.proposal_decision_id },
                        principalTable: "audit_procedure_applicability_decisions",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "audit_procedure_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_procedures_firm_id_client_id_engagement_id_id_current~",
                table: "audit_procedures",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id", "current_applicability_decision_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_procedure_values",
                table: "audit_procedures",
                sql: "length(trim(title)) > 0 AND status IN ('PLANNED','IN_PROGRESS','SUBMITTED','IN_REVIEW','CHANGES_REQUIRED','REVIEWED') AND applicability_status IN ('PENDING','APPLICABLE','NA_PENDING_REVIEW','NA_APPROVED') AND current_definition_revision >= 0 AND current_applicability_revision >= 0 AND ((current_applicability_revision = 0 AND current_applicability_decision_id IS NULL) OR (current_applicability_revision > 0 AND current_applicability_decision_id IS NOT NULL))");

            migrationBuilder.CreateIndex(
                name: "IX_audit_procedure_applicability_decisions_firm_id_actor_user_~",
                table: "audit_procedure_applicability_decisions",
                columns: new[] { "firm_id", "actor_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_procedure_applicability_decisions_firm_id_client_id_~1",
                table: "audit_procedure_applicability_decisions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "audit_procedure_id", "proposal_decision_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_procedure_applicability_decisions_firm_id_client_id_e~",
                table: "audit_procedure_applicability_decisions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "audit_procedure_id", "previous_decision_id" });

            migrationBuilder.CreateIndex(
                name: "ux_audit_procedure_applicability_command",
                table: "audit_procedure_applicability_decisions",
                columns: new[] { "firm_id", "audit_procedure_id", "command_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_audit_procedure_applicability_revision",
                table: "audit_procedure_applicability_decisions",
                columns: new[] { "firm_id", "audit_procedure_id", "revision" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_audit_procedure_current_applicability_decision",
                table: "audit_procedures",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id", "current_applicability_decision_id" },
                principalTable: "audit_procedure_applicability_decisions",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "audit_procedure_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION prevent_audit_procedure_applicability_mutation() RETURNS trigger AS $$
                BEGIN
                  RAISE EXCEPTION 'Audit procedure applicability decisions are append-only.';
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_audit_procedure_applicability_immutable
                  BEFORE UPDATE OR DELETE ON audit_procedure_applicability_decisions
                  FOR EACH ROW EXECUTE FUNCTION prevent_audit_procedure_applicability_mutation();
                CREATE TRIGGER trg_audit_procedure_applicability_no_truncate
                  BEFORE TRUNCATE ON audit_procedure_applicability_decisions
                  FOR EACH STATEMENT EXECUTE FUNCTION prevent_audit_procedure_applicability_mutation();

                CREATE OR REPLACE FUNCTION validate_audit_procedure_applicability() RETURNS trigger AS $$
                DECLARE
                  decision_row audit_procedure_applicability_decisions%ROWTYPE;
                  proposal_row audit_procedure_applicability_decisions%ROWTYPE;
                  current_input_generation bigint;
                BEGIN
                  IF NEW.current_applicability_revision = OLD.current_applicability_revision
                     AND NEW.current_applicability_decision_id IS NOT DISTINCT FROM OLD.current_applicability_decision_id
                     AND NEW.applicability_status = OLD.applicability_status
                     AND NEW.applicability_rationale IS NOT DISTINCT FROM OLD.applicability_rationale
                     AND NEW.applicability_decided_by_user_id IS NOT DISTINCT FROM OLD.applicability_decided_by_user_id
                     AND NEW.applicability_decided_at IS NOT DISTINCT FROM OLD.applicability_decided_at THEN
                    RETURN NEW;
                  END IF;
                  IF NEW.current_applicability_revision <> OLD.current_applicability_revision + 1 THEN
                    RAISE EXCEPTION 'Applicability revisions must advance exactly once.';
                  END IF;
                  SELECT * INTO decision_row FROM audit_procedure_applicability_decisions d
                    WHERE d.firm_id=NEW.firm_id AND d.client_id=NEW.client_id AND d.engagement_id=NEW.engagement_id
                      AND d.audit_procedure_id=NEW.id AND d.id=NEW.current_applicability_decision_id;
                  IF NOT FOUND OR decision_row.revision <> NEW.current_applicability_revision
                     OR decision_row.previous_decision_id IS DISTINCT FROM OLD.current_applicability_decision_id
                     OR decision_row.definition_revision <> NEW.current_definition_revision
                     OR decision_row.actor_user_id IS DISTINCT FROM NEW.applicability_decided_by_user_id
                     OR decision_row.occurred_at IS DISTINCT FROM NEW.applicability_decided_at THEN
                    RAISE EXCEPTION 'Applicability cache must reference its exact immutable decision.';
                  END IF;
                  SELECT cs.input_generation INTO current_input_generation FROM client_safety_states cs
                    WHERE cs.firm_id=NEW.firm_id AND cs.id=NEW.client_id;
                  IF current_input_generation IS DISTINCT FROM decision_row.input_generation THEN
                    RAISE EXCEPTION 'Applicability decision is not based on current client inputs.';
                  END IF;
                  IF decision_row.action = 'APPLICABLE' THEN
                    IF NEW.applicability_status <> 'APPLICABLE'
                       OR NEW.applicability_rationale IS DISTINCT FROM decision_row.rationale THEN
                      RAISE EXCEPTION 'Applicable cache does not match its decision.';
                    END IF;
                  ELSIF decision_row.action = 'PROPOSE_NOT_APPLICABLE' THEN
                    IF NEW.applicability_status <> 'NA_PENDING_REVIEW'
                       OR NEW.applicability_rationale IS DISTINCT FROM decision_row.rationale THEN
                      RAISE EXCEPTION 'Pending N/A cache does not match its proposal.';
                    END IF;
                  ELSIF decision_row.action IN ('APPROVE_NOT_APPLICABLE','RETURN_NOT_APPLICABLE') THEN
                    SELECT * INTO proposal_row FROM audit_procedure_applicability_decisions p
                      WHERE p.firm_id=NEW.firm_id AND p.client_id=NEW.client_id AND p.engagement_id=NEW.engagement_id
                        AND p.audit_procedure_id=NEW.id AND p.id=decision_row.proposal_decision_id;
                    IF NOT FOUND OR decision_row.proposal_decision_id IS DISTINCT FROM OLD.current_applicability_decision_id
                       OR proposal_row.action <> 'PROPOSE_NOT_APPLICABLE'
                       OR proposal_row.actor_user_id = decision_row.actor_user_id
                       OR proposal_row.definition_revision <> decision_row.definition_revision
                       OR proposal_row.input_generation <> decision_row.input_generation
                       OR NEW.applicability_rationale IS DISTINCT FROM proposal_row.rationale THEN
                      RAISE EXCEPTION 'Partner disposition must cite the exact independent current proposal.';
                    END IF;
                    IF decision_row.action = 'APPROVE_NOT_APPLICABLE' AND NEW.applicability_status <> 'NA_APPROVED' THEN
                      RAISE EXCEPTION 'Approved N/A cache does not match its Partner decision.';
                    ELSIF decision_row.action = 'RETURN_NOT_APPLICABLE' AND NEW.applicability_status <> 'PENDING' THEN
                      RAISE EXCEPTION 'Returned N/A proposal must remain pending.';
                    END IF;
                  ELSE
                    RAISE EXCEPTION 'Unsupported applicability decision action.';
                  END IF;
                  RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_audit_procedure_applicability_current
                  BEFORE UPDATE OF current_applicability_revision, current_applicability_decision_id, applicability_status,
                    applicability_rationale, applicability_decided_by_user_id, applicability_decided_at
                  ON audit_procedures FOR EACH ROW EXECUTE FUNCTION validate_audit_procedure_applicability();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM audit_procedure_applicability_decisions) THEN
                    RAISE EXCEPTION 'Refusing to remove retained audit procedure applicability decisions.';
                  END IF;
                END;
                $$;
                DROP TRIGGER IF EXISTS trg_audit_procedure_applicability_current ON audit_procedures;
                DROP FUNCTION IF EXISTS validate_audit_procedure_applicability();
                DROP TRIGGER IF EXISTS trg_audit_procedure_applicability_no_truncate ON audit_procedure_applicability_decisions;
                DROP TRIGGER IF EXISTS trg_audit_procedure_applicability_immutable ON audit_procedure_applicability_decisions;
                DROP FUNCTION IF EXISTS prevent_audit_procedure_applicability_mutation();
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_audit_procedure_current_applicability_decision",
                table: "audit_procedures");

            migrationBuilder.DropTable(
                name: "audit_procedure_applicability_decisions");

            migrationBuilder.DropIndex(
                name: "IX_audit_procedures_firm_id_client_id_engagement_id_id_current~",
                table: "audit_procedures");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_procedure_values",
                table: "audit_procedures");

            migrationBuilder.DropColumn(
                name: "current_applicability_decision_id",
                table: "audit_procedures");

            migrationBuilder.DropColumn(
                name: "current_applicability_revision",
                table: "audit_procedures");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_procedure_values",
                table: "audit_procedures",
                sql: "length(trim(title)) > 0 AND status IN ('PLANNED','IN_PROGRESS','SUBMITTED','IN_REVIEW','CHANGES_REQUIRED','REVIEWED') AND applicability_status IN ('PENDING','APPLICABLE','NA_PENDING_REVIEW','NA_APPROVED') AND current_definition_revision >= 0");
        }
    }
}
