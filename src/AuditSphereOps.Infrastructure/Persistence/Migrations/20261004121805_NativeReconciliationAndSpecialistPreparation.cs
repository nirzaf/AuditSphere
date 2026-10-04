using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NativeReconciliationAndSpecialistPreparation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_specialist_accounting_schedules_firm_id_client_id_engagemen~",
                table: "specialist_accounting_schedules");

            migrationBuilder.AddColumn<long>(
                name: "revision",
                table: "specialist_accounting_schedules",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "supersedes_schedule_id",
                table: "specialist_accounting_schedules",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                WITH ordered AS (
                  SELECT id, row_number() OVER (PARTITION BY firm_id, client_id, engagement_id, period_id, area ORDER BY created_at, id) AS revision,
                         lag(id) OVER (PARTITION BY firm_id, client_id, engagement_id, period_id, area ORDER BY created_at, id) AS prior_id
                  FROM accounting_reconciliations
                )
                UPDATE accounting_reconciliations target
                SET revision = ordered.revision, supersedes_reconciliation_id = ordered.prior_id
                FROM ordered WHERE target.id = ordered.id;

                WITH ordered AS (
                  SELECT id, row_number() OVER (PARTITION BY firm_id, client_id, engagement_id, period_id, area ORDER BY created_at, id) AS revision,
                         lag(id) OVER (PARTITION BY firm_id, client_id, engagement_id, period_id, area ORDER BY created_at, id) AS prior_id
                  FROM specialist_accounting_schedules
                )
                UPDATE specialist_accounting_schedules target
                SET revision = ordered.revision, supersedes_schedule_id = ordered.prior_id
                FROM ordered WHERE target.id = ordered.id;
                """);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_specialist_schedules_scope_id",
                table: "specialist_accounting_schedules",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id" });

            migrationBuilder.CreateTable(
                name: "accounting_reconciliation_preparations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    review_basis = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reconciliation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    input_json = table.Column<string>(type: "character varying(30000)", maxLength: 30000, nullable: false),
                    reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_reconciliation_preparations", x => x.id);
                    table.CheckConstraint("ck_accounting_reconciliation_preparations_values", "actor_epoch >= 1 AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND length(input_json) BETWEEN 1 AND 30000 AND length(trim(reason)) BETWEEN 1 AND 4000 AND length(trim(evidence_reference)) BETWEEN 1 AND 2000");
                    table.ForeignKey(
                        name: "FK_accounting_reconciliation_preparations_accounting_reconcili~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.reconciliation_id },
                        principalTable: "accounting_reconciliations",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounting_reconciliation_preparations_engagements_firm_id_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounting_reconciliation_preparations_users_firm_id_actor_~",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "specialist_schedule_preparations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    review_basis = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    input_json = table.Column<string>(type: "character varying(30000)", maxLength: 30000, nullable: false),
                    reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_specialist_schedule_preparations", x => x.id);
                    table.CheckConstraint("ck_specialist_schedule_preparations_values", "actor_epoch >= 1 AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND length(input_json) BETWEEN 1 AND 30000 AND length(trim(reason)) BETWEEN 1 AND 4000");
                    table.ForeignKey(
                        name: "FK_specialist_schedule_preparations_engagements_firm_id_client~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_specialist_schedule_preparations_specialist_accounting_sche~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.schedule_id },
                        principalTable: "specialist_accounting_schedules",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_specialist_schedule_preparations_users_firm_id_actor_id",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_specialist_accounting_schedules_firm_id_client_id_engagemen~",
                table: "specialist_accounting_schedules",
                columns: new[] { "firm_id", "client_id", "engagement_id", "supersedes_schedule_id" });

            migrationBuilder.CreateIndex(
                name: "ux_specialist_schedule_revision",
                table: "specialist_accounting_schedules",
                columns: new[] { "firm_id", "engagement_id", "period_id", "area", "revision" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE FUNCTION reject_accounting_creation_receipt_mutation() RETURNS trigger AS $$
                BEGIN
                  RAISE EXCEPTION 'accounting creation receipts are immutable';
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER accounting_reconciliation_preparation_immutable
                  BEFORE UPDATE OR DELETE ON accounting_reconciliation_preparations
                  FOR EACH ROW EXECUTE FUNCTION reject_accounting_creation_receipt_mutation();
                CREATE TRIGGER specialist_schedule_preparation_immutable
                  BEFORE UPDATE OR DELETE ON specialist_schedule_preparations
                  FOR EACH ROW EXECUTE FUNCTION reject_accounting_creation_receipt_mutation();
                """);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_reconciliations_firm_id_client_id_engagement_id_~",
                table: "accounting_reconciliations",
                columns: new[] { "firm_id", "client_id", "engagement_id", "supersedes_reconciliation_id" });

            migrationBuilder.CreateIndex(
                name: "ux_accounting_reconciliation_revision",
                table: "accounting_reconciliations",
                columns: new[] { "firm_id", "engagement_id", "period_id", "area", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_reconciliation_preparations_firm_id_client_id_en~",
                table: "accounting_reconciliation_preparations",
                columns: new[] { "firm_id", "client_id", "engagement_id", "reconciliation_id" });

            migrationBuilder.CreateIndex(
                name: "ux_accounting_reconciliation_preparations_actor_request",
                table: "accounting_reconciliation_preparations",
                columns: new[] { "firm_id", "actor_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_accounting_reconciliation_preparations_result",
                table: "accounting_reconciliation_preparations",
                columns: new[] { "firm_id", "engagement_id", "reconciliation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_specialist_schedule_preparations_firm_id_client_id_engageme~",
                table: "specialist_schedule_preparations",
                columns: new[] { "firm_id", "client_id", "engagement_id", "schedule_id" });

            migrationBuilder.CreateIndex(
                name: "ux_specialist_schedule_preparations_actor_request",
                table: "specialist_schedule_preparations",
                columns: new[] { "firm_id", "actor_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_specialist_schedule_preparations_result",
                table: "specialist_schedule_preparations",
                columns: new[] { "firm_id", "engagement_id", "schedule_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_reconciliations_accounting_reconciliations_firm_~",
                table: "accounting_reconciliations",
                columns: new[] { "firm_id", "client_id", "engagement_id", "supersedes_reconciliation_id" },
                principalTable: "accounting_reconciliations",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_specialist_accounting_schedules_specialist_accounting_sched~",
                table: "specialist_accounting_schedules",
                columns: new[] { "firm_id", "client_id", "engagement_id", "supersedes_schedule_id" },
                principalTable: "specialist_accounting_schedules",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM accounting_reconciliation_preparations) OR
                     EXISTS (SELECT 1 FROM specialist_schedule_preparations) OR
                     EXISTS (SELECT 1 FROM specialist_accounting_schedules WHERE revision > 1) THEN
                    RAISE EXCEPTION 'cannot remove retained reconciliation or specialist preparation evidence';
                  END IF;
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS accounting_reconciliation_preparation_immutable ON accounting_reconciliation_preparations;
                DROP TRIGGER IF EXISTS specialist_schedule_preparation_immutable ON specialist_schedule_preparations;
                DROP FUNCTION IF EXISTS reject_accounting_creation_receipt_mutation();
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_accounting_reconciliations_accounting_reconciliations_firm_~",
                table: "accounting_reconciliations");

            migrationBuilder.DropForeignKey(
                name: "FK_specialist_accounting_schedules_specialist_accounting_sched~",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropTable(
                name: "accounting_reconciliation_preparations");

            migrationBuilder.DropTable(
                name: "specialist_schedule_preparations");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_specialist_schedules_scope_id",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropIndex(
                name: "IX_specialist_accounting_schedules_firm_id_client_id_engagemen~",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropIndex(
                name: "ux_specialist_schedule_revision",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropIndex(
                name: "IX_accounting_reconciliations_firm_id_client_id_engagement_id_~",
                table: "accounting_reconciliations");

            migrationBuilder.DropIndex(
                name: "ux_accounting_reconciliation_revision",
                table: "accounting_reconciliations");

            migrationBuilder.DropColumn(
                name: "revision",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropColumn(
                name: "supersedes_schedule_id",
                table: "specialist_accounting_schedules");

            migrationBuilder.CreateIndex(
                name: "IX_specialist_accounting_schedules_firm_id_client_id_engagemen~",
                table: "specialist_accounting_schedules",
                columns: new[] { "firm_id", "client_id", "engagement_id" });
        }
    }
}
