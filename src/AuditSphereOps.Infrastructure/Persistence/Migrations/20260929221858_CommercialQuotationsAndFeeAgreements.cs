using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommercialQuotationsAndFeeAgreements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "commercial_approval_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    threshold_percent = table.Column<decimal>(type: "numeric(19,6)", precision: 9, scale: 4, nullable: true),
                    required_role = table.Column<string>(type: "text", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_commercial_approval_rules", x => x.id);
                    table.CheckConstraint("ck_commercial_rule_values", "kind IN ('DISCOUNT_OVER_PERCENT','NON_STANDARD_TERMS') AND version >= 1 AND length(required_role) > 0 AND ((kind = 'DISCOUNT_OVER_PERCENT' AND threshold_percent >= 0 AND threshold_percent < 100) OR (kind = 'NON_STANDARD_TERMS' AND threshold_percent IS NULL))");
                });

            migrationBuilder.CreateTable(
                name: "commercial_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quotation_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fee_milestone_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "text", nullable: false),
                    template_version = table.Column<string>(type: "text", nullable: false),
                    profile_version = table.Column<long>(type: "bigint", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    sha256_hex = table.Column<string>(type: "text", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_commercial_documents", x => x.id);
                    table.CheckConstraint("ck_commercial_document_values", "kind IN ('QUOTATION','ENGAGEMENT_LETTER','PAYMENT_RECEIPT') AND length(template_version) > 0 AND length(sha256_hex) = 64 AND octet_length(bytes) > 0 AND length(file_name) > 0");
                });

            migrationBuilder.CreateTable(
                name: "commercial_notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fee_milestone_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient = table.Column<string>(type: "text", nullable: false),
                    subject = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    delivery_state = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_commercial_notifications", x => x.id);
                    table.CheckConstraint("ck_commercial_notification_values", "delivery_state IN ('QUEUED','SENT','FAILED') AND length(recipient) > 0 AND length(subject) > 0 AND ((delivery_state = 'SENT') = (delivered_at IS NOT NULL))");
                });

            migrationBuilder.CreateTable(
                name: "engagement_fee_agreements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quotation_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    practice_client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    currency = table.Column<string>(type: "text", nullable: false),
                    agreed_fee = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    advance_percent = table.Column<decimal>(type: "numeric(19,6)", precision: 9, scale: 4, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_engagement_fee_agreements", x => x.id);
                    table.CheckConstraint("ck_fee_agreement_values", "currency ~ '^[A-Z]{3}$' AND agreed_fee > 0 AND advance_percent > 0 AND advance_percent < 100");
                    table.ForeignKey(
                        name: "FK_engagement_fee_agreements_proposals_proposal_id",
                        column: x => x.proposal_id,
                        principalTable: "proposals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "firm_commercial_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    legal_name = table.Column<string>(type: "text", nullable: false),
                    address = table.Column<string>(type: "text", nullable: false),
                    contact_email = table.Column<string>(type: "text", nullable: false),
                    contact_phone = table.Column<string>(type: "text", nullable: false),
                    accent_color_hex = table.Column<string>(type: "text", nullable: false),
                    closing_text = table.Column<string>(type: "text", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_firm_commercial_profiles", x => x.id);
                    table.CheckConstraint("ck_firm_commercial_profile_values", "version >= 1 AND length(legal_name) > 0 AND accent_color_hex ~ '^#[0-9A-Fa-f]{6}$'");
                });

            migrationBuilder.CreateTable(
                name: "quotation_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    lines_json = table.Column<string>(type: "jsonb", nullable: false),
                    complexity_factor = table.Column<decimal>(type: "numeric(19,6)", precision: 9, scale: 4, nullable: false),
                    risk_premium_percent = table.Column<decimal>(type: "numeric(19,6)", precision: 9, scale: 4, nullable: false),
                    discount_percent = table.Column<decimal>(type: "numeric(19,6)", precision: 9, scale: 4, nullable: false),
                    non_standard_terms = table.Column<bool>(type: "boolean", nullable: false),
                    non_standard_terms_note = table.Column<string>(type: "text", nullable: true),
                    base_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    complexity_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    risk_premium_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    fee = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    input_hash = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    required_approvals_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quotation_versions", x => x.id);
                    table.CheckConstraint("ck_quotation_version_values", "revision >= 1 AND currency ~ '^[A-Z]{3}$' AND complexity_factor >= 0.5 AND complexity_factor <= 3 AND risk_premium_percent >= 0 AND risk_premium_percent <= 100 AND discount_percent >= 0 AND discount_percent <= 100 AND base_amount > 0 AND fee >= 0 AND length(input_hash) = 64 AND status IN ('DRAFT','PENDING_APPROVAL','APPROVED','SUPERSEDED') AND ((status = 'APPROVED') = (approved_at IS NOT NULL) OR status = 'SUPERSEDED')");
                    table.ForeignKey(
                        name: "FK_quotation_versions_proposals_proposal_id",
                        column: x => x.proposal_id,
                        principalTable: "proposals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fee_milestones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agreement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fee_milestones", x => x.id);
                    table.CheckConstraint("ck_fee_milestone_values", "kind IN ('ADVANCE','BALANCE') AND amount > 0 AND state IN ('PLANNED','INVOICED','PAID') AND ((state = 'PLANNED' AND invoice_id IS NULL) OR (state <> 'PLANNED' AND invoice_id IS NOT NULL)) AND ((state = 'PAID') = (receipt_id IS NOT NULL AND paid_at IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_fee_milestones_engagement_fee_agreements_agreement_id",
                        column: x => x.agreement_id,
                        principalTable: "engagement_fee_agreements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "quotation_approvals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quotation_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_key = table.Column<string>(type: "text", nullable: false),
                    required_role = table.Column<string>(type: "text", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quotation_approvals", x => x.id);
                    table.CheckConstraint("ck_quotation_approval_values", "length(rule_key) > 0 AND length(required_role) > 0 AND length(reason) > 0");
                    table.ForeignKey(
                        name: "FK_quotation_approvals_quotation_versions_quotation_version_id",
                        column: x => x.quotation_version_id,
                        principalTable: "quotation_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_commercial_approval_rules_firm_id_kind_threshold_percent_ve~",
                table: "commercial_approval_rules",
                columns: new[] { "firm_id", "kind", "threshold_percent", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_commercial_documents_firm_id_fee_milestone_id_kind",
                table: "commercial_documents",
                columns: new[] { "firm_id", "fee_milestone_id", "kind" },
                unique: true,
                filter: "kind = 'PAYMENT_RECEIPT'");

            migrationBuilder.CreateIndex(
                name: "IX_commercial_documents_firm_id_proposal_id_kind_created_at",
                table: "commercial_documents",
                columns: new[] { "firm_id", "proposal_id", "kind", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_commercial_documents_firm_id_quotation_version_id_kind",
                table: "commercial_documents",
                columns: new[] { "firm_id", "quotation_version_id", "kind" },
                unique: true,
                filter: "kind IN ('QUOTATION','ENGAGEMENT_LETTER')");

            migrationBuilder.CreateIndex(
                name: "IX_commercial_notifications_firm_id_delivery_state_created_at",
                table: "commercial_notifications",
                columns: new[] { "firm_id", "delivery_state", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_commercial_notifications_firm_id_fee_milestone_id",
                table: "commercial_notifications",
                columns: new[] { "firm_id", "fee_milestone_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_engagement_fee_agreements_firm_id_proposal_id",
                table: "engagement_fee_agreements",
                columns: new[] { "firm_id", "proposal_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_engagement_fee_agreements_proposal_id",
                table: "engagement_fee_agreements",
                column: "proposal_id");

            migrationBuilder.CreateIndex(
                name: "IX_fee_milestones_agreement_id",
                table: "fee_milestones",
                column: "agreement_id");

            migrationBuilder.CreateIndex(
                name: "IX_fee_milestones_firm_id_agreement_id_kind",
                table: "fee_milestones",
                columns: new[] { "firm_id", "agreement_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_firm_commercial_profiles_firm_id_version",
                table: "firm_commercial_profiles",
                columns: new[] { "firm_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_quotation_approvals_firm_id_quotation_version_id_rule_key",
                table: "quotation_approvals",
                columns: new[] { "firm_id", "quotation_version_id", "rule_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_quotation_approvals_quotation_version_id",
                table: "quotation_approvals",
                column: "quotation_version_id");

            migrationBuilder.CreateIndex(
                name: "IX_quotation_versions_firm_id_proposal_id_revision",
                table: "quotation_versions",
                columns: new[] { "firm_id", "proposal_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_quotation_versions_proposal_id",
                table: "quotation_versions",
                column: "proposal_id");

            migrationBuilder.Sql("""
                CREATE FUNCTION prevent_commercial_history_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'DELETE' THEN
                    RAISE EXCEPTION 'commercial history is append-only';
                  END IF;
                  IF TG_TABLE_NAME IN ('commercial_documents', 'quotation_approvals', 'firm_commercial_profiles') THEN
                    RAISE EXCEPTION 'commercial history is append-only';
                  ELSIF TG_TABLE_NAME = 'quotation_versions' THEN
                    IF NEW.id <> OLD.id OR NEW.firm_id <> OLD.firm_id OR NEW.proposal_id <> OLD.proposal_id OR NEW.revision <> OLD.revision
                       OR NEW.currency <> OLD.currency OR NEW.lines_json::text <> OLD.lines_json::text OR NEW.fee <> OLD.fee
                       OR NEW.input_hash <> OLD.input_hash OR NEW.required_approvals_json::text <> OLD.required_approvals_json::text
                       OR NEW.discount_percent <> OLD.discount_percent OR NEW.base_amount <> OLD.base_amount THEN
                      RAISE EXCEPTION 'a calculated quotation version is immutable; create a new revision';
                    END IF;
                    IF OLD.status = 'SUPERSEDED' OR (OLD.status = 'APPROVED' AND NEW.status <> 'SUPERSEDED' AND NEW.status <> 'APPROVED') THEN
                      RAISE EXCEPTION 'quotation status cannot move backwards';
                    END IF;
                  ELSIF TG_TABLE_NAME = 'commercial_approval_rules' THEN
                    IF NEW.id <> OLD.id OR NEW.firm_id <> OLD.firm_id OR NEW.kind <> OLD.kind OR NEW.required_role <> OLD.required_role
                       OR NEW.version <> OLD.version OR NEW.threshold_percent IS DISTINCT FROM OLD.threshold_percent THEN
                      RAISE EXCEPTION 'an approval rule is versioned; only its active flag may change';
                    END IF;
                  ELSIF TG_TABLE_NAME = 'engagement_fee_agreements' THEN
                    IF NEW.id <> OLD.id OR NEW.firm_id <> OLD.firm_id OR NEW.proposal_id <> OLD.proposal_id OR NEW.agreed_fee <> OLD.agreed_fee
                       OR NEW.advance_percent <> OLD.advance_percent OR NEW.currency <> OLD.currency OR NEW.quotation_version_id <> OLD.quotation_version_id
                       OR NEW.practice_client_id <> OLD.practice_client_id OR (OLD.engagement_id IS NOT NULL AND NEW.engagement_id IS DISTINCT FROM OLD.engagement_id) THEN
                      RAISE EXCEPTION 'the agreed fee is immutable';
                    END IF;
                  ELSIF TG_TABLE_NAME = 'fee_milestones' THEN
                    IF NEW.id <> OLD.id OR NEW.firm_id <> OLD.firm_id OR NEW.agreement_id <> OLD.agreement_id OR NEW.kind <> OLD.kind OR NEW.amount <> OLD.amount
                       OR (OLD.invoice_id IS NOT NULL AND NEW.invoice_id IS DISTINCT FROM OLD.invoice_id)
                       OR (OLD.receipt_id IS NOT NULL AND NEW.receipt_id IS DISTINCT FROM OLD.receipt_id) THEN
                      RAISE EXCEPTION 'a fee milestone amount, invoice and receipt are immutable once set';
                    END IF;
                    IF (OLD.state = 'PAID' AND NEW.state <> 'PAID') OR (OLD.state = 'INVOICED' AND NEW.state = 'PLANNED') THEN
                      RAISE EXCEPTION 'a fee milestone cannot move backwards';
                    END IF;
                  ELSIF TG_TABLE_NAME = 'commercial_notifications' THEN
                    IF NEW.id <> OLD.id OR NEW.firm_id <> OLD.firm_id OR NEW.recipient <> OLD.recipient OR NEW.subject <> OLD.subject
                       OR NEW.body <> OLD.body OR NEW.document_id <> OLD.document_id OR NEW.fee_milestone_id <> OLD.fee_milestone_id
                       OR (OLD.delivery_state = 'SENT' AND NEW.delivery_state <> 'SENT') THEN
                      RAISE EXCEPTION 'a queued commercial notification is immutable; only its delivery state advances';
                    END IF;
                  END IF;
                  RETURN NEW;
                END;
                $$;
                CREATE TRIGGER trg_commercial_documents_append_only BEFORE UPDATE OR DELETE ON commercial_documents FOR EACH ROW EXECUTE FUNCTION prevent_commercial_history_mutation();
                CREATE TRIGGER trg_quotation_approvals_append_only BEFORE UPDATE OR DELETE ON quotation_approvals FOR EACH ROW EXECUTE FUNCTION prevent_commercial_history_mutation();
                CREATE TRIGGER trg_firm_commercial_profiles_append_only BEFORE UPDATE OR DELETE ON firm_commercial_profiles FOR EACH ROW EXECUTE FUNCTION prevent_commercial_history_mutation();
                CREATE TRIGGER trg_quotation_versions_guard BEFORE UPDATE OR DELETE ON quotation_versions FOR EACH ROW EXECUTE FUNCTION prevent_commercial_history_mutation();
                CREATE TRIGGER trg_commercial_approval_rules_guard BEFORE UPDATE OR DELETE ON commercial_approval_rules FOR EACH ROW EXECUTE FUNCTION prevent_commercial_history_mutation();
                CREATE TRIGGER trg_fee_agreements_guard BEFORE UPDATE OR DELETE ON engagement_fee_agreements FOR EACH ROW EXECUTE FUNCTION prevent_commercial_history_mutation();
                CREATE TRIGGER trg_fee_milestones_guard BEFORE UPDATE OR DELETE ON fee_milestones FOR EACH ROW EXECUTE FUNCTION prevent_commercial_history_mutation();
                CREATE TRIGGER trg_commercial_notifications_guard BEFORE UPDATE OR DELETE ON commercial_notifications FOR EACH ROW EXECUTE FUNCTION prevent_commercial_history_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_commercial_documents_append_only ON commercial_documents;
                DROP TRIGGER IF EXISTS trg_quotation_approvals_append_only ON quotation_approvals;
                DROP TRIGGER IF EXISTS trg_firm_commercial_profiles_append_only ON firm_commercial_profiles;
                DROP TRIGGER IF EXISTS trg_quotation_versions_guard ON quotation_versions;
                DROP TRIGGER IF EXISTS trg_commercial_approval_rules_guard ON commercial_approval_rules;
                DROP TRIGGER IF EXISTS trg_fee_agreements_guard ON engagement_fee_agreements;
                DROP TRIGGER IF EXISTS trg_fee_milestones_guard ON fee_milestones;
                DROP TRIGGER IF EXISTS trg_commercial_notifications_guard ON commercial_notifications;
                DROP FUNCTION IF EXISTS prevent_commercial_history_mutation();
                """);

            migrationBuilder.DropTable(
                name: "commercial_approval_rules");

            migrationBuilder.DropTable(
                name: "commercial_documents");

            migrationBuilder.DropTable(
                name: "commercial_notifications");

            migrationBuilder.DropTable(
                name: "fee_milestones");

            migrationBuilder.DropTable(
                name: "firm_commercial_profiles");

            migrationBuilder.DropTable(
                name: "quotation_approvals");

            migrationBuilder.DropTable(
                name: "engagement_fee_agreements");

            migrationBuilder.DropTable(
                name: "quotation_versions");
        }
    }
}
