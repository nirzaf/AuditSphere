using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientSalesInvoiceDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_sales_invoice_drafts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    previous_revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    previous_revision = table.Column<long>(type: "bigint", nullable: true),
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    intent_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    draft_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chart_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    net_amount = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    gross_amount = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    snapshot_json = table.Column<string>(type: "character varying(500000)", maxLength: 500000, nullable: false),
                    snapshot_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_sales_invoice_drafts", x => x.id);
                    table.UniqueConstraint("AK_client_sales_invoice_drafts_firm_id_client_id_invoice_id_id~", x => new { x.firm_id, x.client_id, x.invoice_id, x.id, x.revision });
                    table.CheckConstraint("ck_client_sales_invoice_draft", "revision>0 AND command_id<>'00000000-0000-0000-0000-000000000000'::uuid AND invoice_id<>'00000000-0000-0000-0000-000000000000'::uuid AND length(trim(draft_reference))>0 AND intent_hash ~ '^[a-f0-9]{64}$' AND snapshot_hash ~ '^[a-f0-9]{64}$' AND currency ~ '^[A-Z]{3}$' AND net_amount>=0 AND gross_amount=net_amount AND length(snapshot_json)>0 AND ((revision=1 AND previous_revision_id IS NULL AND previous_revision IS NULL) OR (revision>1 AND previous_revision_id IS NOT NULL AND previous_revision=revision-1))");
                    table.ForeignKey(
                        name: "FK_client_sales_invoice_drafts_client_bookkeeping_counterparti~",
                        columns: x => new { x.firm_id, x.client_id, x.customer_id },
                        principalTable: "client_bookkeeping_counterparties",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_sales_invoice_drafts_client_chart_versions_firm_id_c~",
                        columns: x => new { x.firm_id, x.client_id, x.chart_version_id },
                        principalTable: "client_chart_versions",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_sales_invoice_drafts_client_reporting_periods_firm_i~",
                        columns: x => new { x.firm_id, x.client_id, x.period_id },
                        principalTable: "client_reporting_periods",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_sales_invoice_drafts_client_sales_invoice_drafts_fir~",
                        columns: x => new { x.firm_id, x.client_id, x.invoice_id, x.previous_revision_id, x.previous_revision },
                        principalTable: "client_sales_invoice_drafts",
                        principalColumns: new[] { "firm_id", "client_id", "invoice_id", "id", "revision" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_sales_invoice_drafts_users_firm_id_created_by_user_id",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_sales_invoice_drafts_firm_id_client_id_chart_version~",
                table: "client_sales_invoice_drafts",
                columns: new[] { "firm_id", "client_id", "chart_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_sales_invoice_drafts_firm_id_client_id_created_by_us~",
                table: "client_sales_invoice_drafts",
                columns: new[] { "firm_id", "client_id", "created_by_user_id", "command_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_sales_invoice_drafts_firm_id_client_id_customer_id",
                table: "client_sales_invoice_drafts",
                columns: new[] { "firm_id", "client_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_sales_invoice_drafts_firm_id_client_id_draft_referen~",
                table: "client_sales_invoice_drafts",
                columns: new[] { "firm_id", "client_id", "draft_reference" },
                unique: true,
                filter: "revision = 1");

            migrationBuilder.CreateIndex(
                name: "IX_client_sales_invoice_drafts_firm_id_client_id_invoice_id_pr~",
                table: "client_sales_invoice_drafts",
                columns: new[] { "firm_id", "client_id", "invoice_id", "previous_revision_id", "previous_revision" });

            migrationBuilder.CreateIndex(
                name: "IX_client_sales_invoice_drafts_firm_id_client_id_invoice_id_re~",
                table: "client_sales_invoice_drafts",
                columns: new[] { "firm_id", "client_id", "invoice_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_sales_invoice_drafts_firm_id_client_id_period_id",
                table: "client_sales_invoice_drafts",
                columns: new[] { "firm_id", "client_id", "period_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_sales_invoice_drafts_firm_id_created_by_user_id",
                table: "client_sales_invoice_drafts",
                columns: new[] { "firm_id", "created_by_user_id" });
            migrationBuilder.Sql("""
                CREATE FUNCTION guard_client_sales_draft_history() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                  RAISE EXCEPTION 'Client sales drafts retain immutable revisions.' USING ERRCODE='23514';
                END;
                $fn$;
                CREATE FUNCTION guard_client_sales_draft_insert() RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE parent client_sales_invoice_drafts%ROWTYPE; body jsonb;
                BEGIN
                  body:=NEW.snapshot_json::jsonb;
                  IF encode(sha256(convert_to(NEW.snapshot_json,'UTF8')),'hex')<>NEW.snapshot_hash
                     OR body->>'Version' IS DISTINCT FROM 'client-sales-draft-v1'
                     OR (body->'Seller'->>'ClientId')::uuid IS DISTINCT FROM NEW.client_id
                     OR (body->'Customer'->>'ClientId')::uuid IS DISTINCT FROM NEW.client_id
                     OR (body->'Customer'->>'Id')::uuid IS DISTINCT FROM NEW.customer_id
                     OR (body->>'ChartVersionId')::uuid IS DISTINCT FROM NEW.chart_version_id
                     OR body->>'Currency' IS DISTINCT FROM NEW.currency
                     OR body->>'DraftReference' IS DISTINCT FROM NEW.draft_reference
                     OR body->>'SourceReference' IS DISTINCT FROM NEW.source_reference
                     OR body->>'TaxTreatment' IS DISTINCT FROM 'NONE'
                     OR (body->>'Net')::numeric IS DISTINCT FROM NEW.net_amount
                     OR (body->>'Gross')::numeric IS DISTINCT FROM NEW.gross_amount THEN
                    RAISE EXCEPTION 'Client sales draft snapshot/header identity mismatch.' USING ERRCODE='23514';
                  END IF;
                  IF NEW.previous_revision_id IS NOT NULL THEN
                    SELECT * INTO parent FROM client_sales_invoice_drafts WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id
                      AND invoice_id=NEW.invoice_id AND id=NEW.previous_revision_id AND revision=NEW.previous_revision FOR UPDATE;
                    IF parent.id IS NULL OR parent.created_by_user_id<>NEW.created_by_user_id OR parent.draft_reference<>NEW.draft_reference THEN
                      RAISE EXCEPTION 'Client sales draft revision requires its original maker and reference.' USING ERRCODE='23514';
                    END IF;
                  END IF;
                  RETURN NEW;
                END;
                $fn$;
                CREATE TRIGGER immutable_client_sales_draft BEFORE UPDATE OR DELETE ON client_sales_invoice_drafts
                  FOR EACH ROW EXECUTE FUNCTION guard_client_sales_draft_history();
                CREATE TRIGGER scoped_client_sales_draft_insert BEFORE INSERT ON client_sales_invoice_drafts
                  FOR EACH ROW EXECUTE FUNCTION guard_client_sales_draft_insert();
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER scoped_client_sales_draft_insert ON client_sales_invoice_drafts;
                DROP TRIGGER immutable_client_sales_draft ON client_sales_invoice_drafts;
                DROP FUNCTION guard_client_sales_draft_insert();
                DROP FUNCTION guard_client_sales_draft_history();
                """);
            migrationBuilder.DropTable(
                name: "client_sales_invoice_drafts");
        }
    }
}
