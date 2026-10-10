using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommercialPricingPolicyAndDeliveryReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_quotation_approvals_firm_id_quotation_version_id_rule_key",
                table: "quotation_approvals");

            migrationBuilder.DropCheckConstraint(
                name: "ck_commercial_notification_values",
                table: "commercial_notifications");

            // The shared commercial guard pins a sent notification to the legacy 'SENT' value, so it would reject
            // the vocabulary change below. Notifications get their own guard (created at the end of this migration)
            // that protects the same content and the new receipt states.
            migrationBuilder.Sql("DROP TRIGGER trg_commercial_notifications_guard ON commercial_notifications");

            // Legacy receipt states move to the durable delivery-receipt vocabulary before the new
            // constraint applies: a verified send becomes provider-accepted, a failure becomes unknown.
            migrationBuilder.Sql(
                "UPDATE commercial_notifications SET delivery_state = 'PROVIDER_ACCEPTED' WHERE delivery_state = 'SENT'");
            migrationBuilder.Sql(
                "UPDATE commercial_notifications SET delivery_state = 'UNKNOWN' WHERE delivery_state = 'FAILED'");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "valid_until",
                table: "quotation_versions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "firm_pricing_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    minimum_fee = table.Column<decimal>(type: "numeric(19,6)", precision: 18, scale: 2, nullable: true),
                    maximum_fee = table.Column<decimal>(type: "numeric(19,6)", precision: 18, scale: 2, nullable: true),
                    max_discount_percent = table.Column<decimal>(type: "numeric(19,6)", precision: 9, scale: 4, nullable: true),
                    validity_days = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_firm_pricing_policies", x => x.id);
                    table.CheckConstraint("ck_firm_pricing_policy_values", "version >= 1 AND currency ~ '^[A-Z]{3}$' AND validity_days >= 1 AND validity_days <= 365 AND (minimum_fee IS NULL OR minimum_fee >= 0) AND (maximum_fee IS NULL OR maximum_fee >= 0) AND (minimum_fee IS NULL OR maximum_fee IS NULL OR minimum_fee <= maximum_fee) AND (max_discount_percent IS NULL OR (max_discount_percent >= 0 AND max_discount_percent < 100)) AND status IN ('DRAFT','PENDING_APPROVAL','APPROVED') AND ((status = 'APPROVED') = (approved_by_user_id IS NOT NULL AND approved_at IS NOT NULL))");
                });

            migrationBuilder.CreateTable(
                name: "quotation_approval_revocations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quotation_approval_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    revoked_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quotation_approval_revocations", x => x.id);
                    table.CheckConstraint("ck_quotation_approval_revocation_values", "length(reason) > 0");
                    table.ForeignKey(
                        name: "FK_quotation_approval_revocations_quotation_approvals_quotatio~",
                        column: x => x.quotation_approval_id,
                        principalTable: "quotation_approvals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_quotation_approvals_firm_id_quotation_version_id_rule_key",
                table: "quotation_approvals",
                columns: new[] { "firm_id", "quotation_version_id", "rule_key" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_commercial_notification_values",
                table: "commercial_notifications",
                sql: "delivery_state IN ('QUEUED','DISPATCHED','PROVIDER_ACCEPTED','DELIVERED','REJECTED','UNKNOWN') AND kind IN ('RECEIPT','PROPOSAL','HOLDING_LETTER') AND length(recipient) > 0 AND length(subject) > 0 AND ((delivery_state IN ('PROVIDER_ACCEPTED','DELIVERED')) = (delivered_at IS NOT NULL)) AND ((kind = 'RECEIPT') = (fee_milestone_id IS NOT NULL)) AND ((kind = 'PROPOSAL') = (proposal_id IS NOT NULL)) AND ((kind = 'HOLDING_LETTER') = (deliverable_id IS NOT NULL)) AND ((kind = 'HOLDING_LETTER') = (dispatch_key IS NOT NULL))");

            migrationBuilder.CreateIndex(
                name: "IX_firm_pricing_policies_firm_id_currency_version",
                table: "firm_pricing_policies",
                columns: new[] { "firm_id", "currency", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_quotation_approval_revocations_quotation_approval_id",
                table: "quotation_approval_revocations",
                column: "quotation_approval_id",
                unique: true);

            // A pricing policy version is part of the commercial basis of an engagement: its identity and
            // approved state are append-only, exactly like the approval matrix and quotation versions.
            migrationBuilder.Sql("""
                CREATE FUNCTION prevent_pricing_policy_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'DELETE' THEN
                    RAISE EXCEPTION 'pricing policy history is append-only';
                  END IF;
                  IF NEW.id <> OLD.id OR NEW.firm_id <> OLD.firm_id OR NEW.currency <> OLD.currency OR NEW.version <> OLD.version
                     OR NEW.created_by_user_id <> OLD.created_by_user_id OR NEW.created_at <> OLD.created_at
                     OR NEW.minimum_fee IS DISTINCT FROM OLD.minimum_fee OR NEW.maximum_fee IS DISTINCT FROM OLD.maximum_fee
                     OR NEW.max_discount_percent IS DISTINCT FROM OLD.max_discount_percent OR NEW.validity_days <> OLD.validity_days THEN
                    RAISE EXCEPTION 'a pricing policy version is immutable; create a new version';
                  END IF;
                  IF OLD.status = 'APPROVED' AND NEW.status <> 'APPROVED' THEN
                    RAISE EXCEPTION 'an approved pricing policy cannot move backwards';
                  END IF;
                  RETURN NEW;
                END;
                $$;
                CREATE TRIGGER trg_firm_pricing_policies_append_only
                  BEFORE UPDATE OR DELETE ON firm_pricing_policies
                  FOR EACH ROW EXECUTE FUNCTION prevent_pricing_policy_mutation();
                """);

            // A revocation is a signed decision: it is inserted once and never edited or removed.
            migrationBuilder.Sql("""
                CREATE FUNCTION prevent_quotation_approval_revocation_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'commercial history is append-only';
                END;
                $$;
                CREATE TRIGGER trg_quotation_approval_revocations_append_only
                  BEFORE UPDATE OR DELETE ON quotation_approval_revocations
                  FOR EACH ROW EXECUTE FUNCTION prevent_quotation_approval_revocation_mutation();
                """);

            // The queued email's content and bindings are immutable; only the delivery receipt advances, and a
            // provider-verified receipt is terminal (it may only gain explicit delivery evidence).
            migrationBuilder.Sql("""
                CREATE FUNCTION guard_commercial_notification() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'DELETE' THEN
                    RAISE EXCEPTION 'commercial history is append-only';
                  END IF;
                  IF NEW.id <> OLD.id OR NEW.firm_id <> OLD.firm_id OR NEW.kind <> OLD.kind OR NEW.recipient <> OLD.recipient
                     OR NEW.subject <> OLD.subject OR NEW.body <> OLD.body
                     OR NEW.document_id IS DISTINCT FROM OLD.document_id OR NEW.fee_milestone_id IS DISTINCT FROM OLD.fee_milestone_id
                     OR NEW.proposal_id IS DISTINCT FROM OLD.proposal_id OR NEW.offer_sha256 IS DISTINCT FROM OLD.offer_sha256 THEN
                    RAISE EXCEPTION 'a queued commercial notification is immutable; only its delivery state advances';
                  END IF;
                  IF OLD.delivery_state IN ('PROVIDER_ACCEPTED', 'DELIVERED') AND NEW.delivery_state <> OLD.delivery_state
                     AND NOT (OLD.delivery_state = 'PROVIDER_ACCEPTED' AND NEW.delivery_state = 'DELIVERED') THEN
                    RAISE EXCEPTION 'a provider-verified delivery receipt is terminal';
                  END IF;
                  RETURN NEW;
                END;
                $$;
                CREATE TRIGGER trg_commercial_notifications_guard
                  BEFORE UPDATE OR DELETE ON commercial_notifications
                  FOR EACH ROW EXECUTE FUNCTION guard_commercial_notification();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the shared guard only after the receipt vocabulary is mapped back (see the end of Down).
            migrationBuilder.Sql("DROP TRIGGER trg_commercial_notifications_guard ON commercial_notifications");
            migrationBuilder.Sql("DROP FUNCTION guard_commercial_notification()");

            migrationBuilder.DropTable(
                name: "firm_pricing_policies");

            migrationBuilder.DropTable(
                name: "quotation_approval_revocations");

            migrationBuilder.DropIndex(
                name: "IX_quotation_approvals_firm_id_quotation_version_id_rule_key",
                table: "quotation_approvals");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS prevent_pricing_policy_mutation()");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS prevent_quotation_approval_revocation_mutation()");

            migrationBuilder.DropCheckConstraint(
                name: "ck_commercial_notification_values",
                table: "commercial_notifications");

            migrationBuilder.Sql(
                "UPDATE commercial_notifications SET delivery_state = CASE " +
                "WHEN delivery_state IN ('PROVIDER_ACCEPTED','DELIVERED') THEN 'SENT' " +
                "WHEN delivery_state IN ('REJECTED','UNKNOWN') THEN 'FAILED' " +
                "WHEN delivery_state = 'DISPATCHED' THEN 'QUEUED' ELSE delivery_state END");

            migrationBuilder.DropColumn(
                name: "valid_until",
                table: "quotation_versions");

            migrationBuilder.CreateIndex(
                name: "IX_quotation_approvals_firm_id_quotation_version_id_rule_key",
                table: "quotation_approvals",
                columns: new[] { "firm_id", "quotation_version_id", "rule_key" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_commercial_notification_values",
                table: "commercial_notifications",
                sql: "delivery_state IN ('QUEUED','SENT','FAILED') AND kind IN ('RECEIPT','PROPOSAL','HOLDING_LETTER') AND length(recipient) > 0 AND length(subject) > 0 AND ((delivery_state = 'SENT') = (delivered_at IS NOT NULL)) AND ((kind = 'RECEIPT') = (fee_milestone_id IS NOT NULL)) AND ((kind = 'PROPOSAL') = (proposal_id IS NOT NULL)) AND ((kind = 'HOLDING_LETTER') = (deliverable_id IS NOT NULL)) AND ((kind = 'HOLDING_LETTER') = (dispatch_key IS NOT NULL))");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_commercial_notifications_guard BEFORE UPDATE OR DELETE ON commercial_notifications " +
                "FOR EACH ROW EXECUTE FUNCTION prevent_commercial_history_mutation()");
        }
    }
}
