using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCurrencyRemeasurementWorkpaper : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_exchange_rates_set_id",
                table: "exchange_rates",
                columns: new[] { "firm_id", "rate_set_version_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_document_snapshots_scope_id",
                table: "document_snapshots",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id" });

            migrationBuilder.CreateTable(
                name: "currency_remeasurement_schedules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rate_set_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    translation_policy_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    as_of_date = table.Column<DateOnly>(type: "date", nullable: false),
                    functional_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    input_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    item_count = table.Column<int>(type: "integer", nullable: false),
                    total_foreign_exchange_adjustment = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_currency_remeasurement_schedules", x => x.id);
                    table.UniqueConstraint("ak_currency_remeasurement_schedules_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.CheckConstraint("ck_currency_remeasurement_schedule_values", "functional_currency ~ '^[A-Z]{3}$' AND input_hash ~ '^[0-9a-f]{64}$' AND item_count > 0 AND status IN ('SUBMITTED','APPROVED','STALE') AND ((status = 'APPROVED' AND approved_by_user_id IS NOT NULL AND approved_at IS NOT NULL) OR (status <> 'APPROVED' AND approved_by_user_id IS NULL AND approved_at IS NULL))");
                    table.ForeignKey(
                        name: "FK_currency_remeasurement_schedules_client_reporting_periods_f~",
                        columns: x => new { x.firm_id, x.client_id, x.period_id },
                        principalTable: "client_reporting_periods",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_currency_remeasurement_schedules_exchange_rate_set_versions~",
                        columns: x => new { x.firm_id, x.rate_set_version_id },
                        principalTable: "exchange_rate_set_versions",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_currency_remeasurement_schedules_translation_policy_version~",
                        columns: x => new { x.firm_id, x.translation_policy_version_id },
                        principalTable: "translation_policy_versions",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_currency_remeasurement_schedules_users_firm_id_approved_by_~",
                        columns: x => new { x.firm_id, x.approved_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_currency_remeasurement_schedules_users_firm_id_created_by_u~",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "currency_remeasurement_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rate_set_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    exchange_rate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stable_item_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source_evidence_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    is_monetary = table.Column<bool>(type: "boolean", nullable: false),
                    foreign_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    foreign_currency_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    prior_functional_carrying_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    rate_date = table.Column<DateOnly>(type: "date", nullable: false),
                    rate_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    applied_rate = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    remeasured_functional_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    foreign_exchange_adjustment = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    rounding_adjustment = table.Column<decimal>(type: "numeric(19,6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_currency_remeasurement_items", x => x.id);
                    table.UniqueConstraint("ak_currency_remeasurement_items_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.schedule_id, x.id });
                    table.CheckConstraint("ck_currency_remeasurement_item_values", "source_evidence_sha256 ~ '^[0-9a-f]{64}$' AND foreign_currency ~ '^[A-Z]{3}$' AND stable_item_reference <> '' AND applied_rate > 0 AND rate_type <> ''");
                    table.ForeignKey(
                        name: "FK_currency_remeasurement_items_currency_remeasurement_schedul~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.schedule_id },
                        principalTable: "currency_remeasurement_schedules",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_currency_remeasurement_items_document_snapshots_firm_id_cli~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.evidence_snapshot_id },
                        principalTable: "document_snapshots",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_currency_remeasurement_items_exchange_rates_firm_id_rate_se~",
                        columns: x => new { x.firm_id, x.rate_set_version_id, x.exchange_rate_id },
                        principalTable: "exchange_rates",
                        principalColumns: new[] { "firm_id", "rate_set_version_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_currency_remeasurement_items_firm_id_client_id_engagement_i~",
                table: "currency_remeasurement_items",
                columns: new[] { "firm_id", "client_id", "engagement_id", "evidence_snapshot_id" });

            migrationBuilder.CreateIndex(
                name: "IX_currency_remeasurement_items_firm_id_rate_set_version_id_ex~",
                table: "currency_remeasurement_items",
                columns: new[] { "firm_id", "rate_set_version_id", "exchange_rate_id" });

            migrationBuilder.CreateIndex(
                name: "ux_currency_remeasurement_item_reference",
                table: "currency_remeasurement_items",
                columns: new[] { "firm_id", "schedule_id", "stable_item_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_currency_remeasurement_schedules_firm_id_approved_by_user_id",
                table: "currency_remeasurement_schedules",
                columns: new[] { "firm_id", "approved_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_currency_remeasurement_schedules_firm_id_client_id_period_id",
                table: "currency_remeasurement_schedules",
                columns: new[] { "firm_id", "client_id", "period_id" });

            migrationBuilder.CreateIndex(
                name: "IX_currency_remeasurement_schedules_firm_id_created_by_user_id",
                table: "currency_remeasurement_schedules",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_currency_remeasurement_schedules_firm_id_rate_set_version_id",
                table: "currency_remeasurement_schedules",
                columns: new[] { "firm_id", "rate_set_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_currency_remeasurement_schedules_firm_id_translation_policy~",
                table: "currency_remeasurement_schedules",
                columns: new[] { "firm_id", "translation_policy_version_id" });

            migrationBuilder.CreateIndex(
                name: "ux_currency_remeasurement_input",
                table: "currency_remeasurement_schedules",
                columns: new[] { "firm_id", "client_id", "engagement_id", "period_id", "input_hash" },
                unique: true);

            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION guard_currency_remeasurement_schedule() RETURNS trigger AS $$
                BEGIN
                  IF TG_OP = 'DELETE' THEN
                    RAISE EXCEPTION 'Currency remeasurement schedules are retained.' USING ERRCODE = '55000';
                  END IF;
                  IF OLD.id IS DISTINCT FROM NEW.id OR OLD.firm_id IS DISTINCT FROM NEW.firm_id OR
                     OLD.client_id IS DISTINCT FROM NEW.client_id OR OLD.engagement_id IS DISTINCT FROM NEW.engagement_id OR
                     OLD.period_id IS DISTINCT FROM NEW.period_id OR OLD.rate_set_version_id IS DISTINCT FROM NEW.rate_set_version_id OR
                     OLD.translation_policy_version_id IS DISTINCT FROM NEW.translation_policy_version_id OR
                     OLD.as_of_date IS DISTINCT FROM NEW.as_of_date OR OLD.functional_currency IS DISTINCT FROM NEW.functional_currency OR
                     OLD.input_hash IS DISTINCT FROM NEW.input_hash OR OLD.item_count IS DISTINCT FROM NEW.item_count OR
                     OLD.total_foreign_exchange_adjustment IS DISTINCT FROM NEW.total_foreign_exchange_adjustment OR
                     OLD.created_by_user_id IS DISTINCT FROM NEW.created_by_user_id OR OLD.created_at IS DISTINCT FROM NEW.created_at OR
                     OLD.status <> 'SUBMITTED' OR NEW.status NOT IN ('APPROVED','STALE') OR
                     (NEW.status = 'APPROVED' AND (NEW.approved_by_user_id IS NULL OR NEW.approved_at IS NULL OR NEW.approved_by_user_id = OLD.created_by_user_id)) OR
                     (NEW.status = 'STALE' AND (NEW.approved_by_user_id IS NOT NULL OR NEW.approved_at IS NOT NULL)) THEN
                    RAISE EXCEPTION 'Only a valid review disposition may change a currency remeasurement schedule.' USING ERRCODE = '55000';
                  END IF;
                  RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_currency_remeasurement_schedule_guard
                  BEFORE UPDATE OR DELETE ON currency_remeasurement_schedules
                  FOR EACH ROW EXECUTE FUNCTION guard_currency_remeasurement_schedule();

                CREATE OR REPLACE FUNCTION guard_currency_remeasurement_item_append_only() RETURNS trigger AS $$
                BEGIN
                  RAISE EXCEPTION 'Currency remeasurement items are append-only.' USING ERRCODE = '55000';
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_currency_remeasurement_items_append_only
                  BEFORE UPDATE OR DELETE ON currency_remeasurement_items
                  FOR EACH ROW EXECUTE FUNCTION guard_currency_remeasurement_item_append_only();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS trg_currency_remeasurement_items_append_only ON currency_remeasurement_items;
                DROP FUNCTION IF EXISTS guard_currency_remeasurement_item_append_only();
                DROP TRIGGER IF EXISTS trg_currency_remeasurement_schedule_guard ON currency_remeasurement_schedules;
                DROP FUNCTION IF EXISTS guard_currency_remeasurement_schedule();
            ");
            migrationBuilder.DropTable(
                name: "currency_remeasurement_items");

            migrationBuilder.DropTable(
                name: "currency_remeasurement_schedules");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_exchange_rates_set_id",
                table: "exchange_rates");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_document_snapshots_scope_id",
                table: "document_snapshots");
        }
    }
}
