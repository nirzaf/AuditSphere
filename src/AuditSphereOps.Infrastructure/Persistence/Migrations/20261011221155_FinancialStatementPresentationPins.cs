using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinancialStatementPresentationPins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "financial_statement_presentation_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: true),
                    book_id = table.Column<Guid>(type: "uuid", nullable: true),
                    basis = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    framework = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    taxonomy_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    template_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    calculation_engine_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    period_start = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    period_end = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    package_revision = table.Column<long>(type: "bigint", nullable: false),
                    package_generation = table.Column<long>(type: "bigint", nullable: false),
                    package_calculation_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    layout_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    layout_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    comparative_package_id = table.Column<Guid>(type: "uuid", nullable: true),
                    comparative_package_revision = table.Column<long>(type: "bigint", nullable: true),
                    comparative_package_generation = table.Column<long>(type: "bigint", nullable: true),
                    comparative_calculation_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    comparative_basis = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    comparative_evidence_reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    note_source_manifest_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    builder_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    renderer_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_policy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    revision_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    snapshot_sha256_hex = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    snapshot_json = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_statement_presentation_revisions", x => x.id);
                    table.UniqueConstraint("AK_financial_statement_presentation_revisions_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.CheckConstraint("ck_financial_statement_presentation_revision_values", "package_revision >= 1 AND package_generation >= 1 AND package_calculation_hash ~ '^[0-9a-f]{64}$' AND length(trim(framework)) > 0 AND length(trim(taxonomy_version)) > 0 AND length(trim(template_version)) > 0 AND length(trim(calculation_engine_version)) > 0 AND currency ~ '^[A-Z]{3}$' AND period_start ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' AND period_end ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' AND layout_hash ~ '^[0-9a-f]{64}$' AND note_source_manifest_hash ~ '^[0-9a-f]{64}$' AND revision_hash ~ '^[0-9a-f]{64}$' AND snapshot_sha256_hex ~ '^[0-9a-f]{64}$' AND octet_length(snapshot_json) BETWEEN 1 AND 8388608 AND length(trim(builder_version)) > 0 AND length(trim(renderer_version)) > 0 AND length(trim(display_policy)) > 0 AND status = 'UNAPPROVED_DRAFT' AND ((comparative_package_id IS NULL AND comparative_package_revision IS NULL AND comparative_package_generation IS NULL AND comparative_calculation_hash IS NULL AND comparative_basis IS NULL AND comparative_evidence_reference IS NULL) OR (comparative_package_id IS NOT NULL AND comparative_package_revision IS NOT NULL AND comparative_package_revision >= 1 AND comparative_package_generation IS NOT NULL AND comparative_package_generation >= 1 AND comparative_calculation_hash IS NOT NULL AND comparative_calculation_hash ~ '^[0-9a-f]{64}$' AND comparative_basis IS NOT NULL AND length(trim(comparative_basis)) > 0 AND comparative_evidence_reference IS NOT NULL AND length(trim(comparative_evidence_reference)) > 0))");
                    table.ForeignKey(
                        name: "FK_financial_statement_presentation_revisions_client_reporting~",
                        columns: x => new { x.firm_id, x.client_id, x.book_id },
                        principalTable: "client_reporting_books",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_financial_statement_presentation_revisions_client_reportin~1",
                        columns: x => new { x.firm_id, x.client_id, x.period_id },
                        principalTable: "client_reporting_periods",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_financial_statement_presentation_revisions_financial_packag~",
                        columns: x => new { x.firm_id, x.comparative_package_id },
                        principalTable: "financial_packages",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_financial_statement_presentation_revisions_financial_packa~1",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.financial_package_id },
                        principalTable: "financial_packages",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_financial_statement_presentation_revisions_statement_layout~",
                        columns: x => new { x.firm_id, x.client_id, x.layout_version_id },
                        principalTable: "statement_layout_versions",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_financial_statement_presentation_revisions_users_firm_id_cr~",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "financial_statement_presentation_artifacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    presentation_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    format = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    file_name = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    content_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    renderer_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_statement_presentation_artifacts", x => x.id);
                    table.UniqueConstraint("AK_financial_statement_presentation_artifacts_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.CheckConstraint("ck_financial_statement_presentation_artifact_values", "format IN ('pdf','docx','xlsx') AND length(trim(file_name)) > 0 AND length(trim(content_type)) > 0 AND length(trim(renderer_version)) > 0");
                    table.ForeignKey(
                        name: "FK_financial_statement_presentation_artifacts_financial_packag~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.id },
                        principalTable: "financial_package_artifacts",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_financial_statement_presentation_artifacts_financial_statem~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.presentation_revision_id },
                        principalTable: "financial_statement_presentation_revisions",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_financial_statement_presentation_artifacts_firm_id_client_i~",
                table: "financial_statement_presentation_artifacts",
                columns: new[] { "firm_id", "client_id", "engagement_id", "presentation_revision_id" });

            migrationBuilder.CreateIndex(
                name: "ux_financial_statement_presentation_artifact_format",
                table: "financial_statement_presentation_artifacts",
                columns: new[] { "firm_id", "presentation_revision_id", "format" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_financial_statement_presentation_revision_history",
                table: "financial_statement_presentation_revisions",
                columns: new[] { "firm_id", "financial_package_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_statement_presentation_revisions_firm_id_client_~1",
                table: "financial_statement_presentation_revisions",
                columns: new[] { "firm_id", "client_id", "layout_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_statement_presentation_revisions_firm_id_client_~2",
                table: "financial_statement_presentation_revisions",
                columns: new[] { "firm_id", "client_id", "period_id" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_statement_presentation_revisions_firm_id_client_~3",
                table: "financial_statement_presentation_revisions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "financial_package_id" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_statement_presentation_revisions_firm_id_client_i~",
                table: "financial_statement_presentation_revisions",
                columns: new[] { "firm_id", "client_id", "book_id" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_statement_presentation_revisions_firm_id_comparat~",
                table: "financial_statement_presentation_revisions",
                columns: new[] { "firm_id", "comparative_package_id" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_statement_presentation_revisions_firm_id_created_~",
                table: "financial_statement_presentation_revisions",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_financial_statement_presentation_revision_identity",
                table: "financial_statement_presentation_revisions",
                columns: new[] { "firm_id", "financial_package_id", "revision_hash" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION prevent_financial_statement_presentation_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'Financial statement presentation evidence is append-only; create a new revision.'
                    USING ERRCODE = 'P0001';
                END;
                $$;

                CREATE TRIGGER trg_financial_statement_presentation_revisions_immutable
                  BEFORE UPDATE OR DELETE ON financial_statement_presentation_revisions
                  FOR EACH ROW EXECUTE FUNCTION prevent_financial_statement_presentation_mutation();
                CREATE TRIGGER trg_financial_statement_presentation_artifacts_immutable
                  BEFORE UPDATE OR DELETE ON financial_statement_presentation_artifacts
                  FOR EACH ROW EXECUTE FUNCTION prevent_financial_statement_presentation_mutation();

                CREATE OR REPLACE FUNCTION prevent_pinned_statement_package_artifact_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF OLD.artifact_version LIKE 'statement-presentation.%' THEN
                    RAISE EXCEPTION 'Pinned statement presentation package artifacts are append-only.'
                      USING ERRCODE = 'P0001';
                  END IF;
                  IF TG_OP = 'UPDATE' AND NEW.artifact_version LIKE 'statement-presentation.%' THEN
                    RAISE EXCEPTION 'Pinned statement presentation package artifacts are append-only.'
                      USING ERRCODE = 'P0001';
                  END IF;
                  IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                  RETURN NEW;
                END;
                $$;

                CREATE TRIGGER trg_pinned_statement_package_artifacts_immutable
                  BEFORE UPDATE OR DELETE ON financial_package_artifacts
                  FOR EACH ROW EXECUTE FUNCTION prevent_pinned_statement_package_artifact_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM financial_statement_presentation_revisions)
                     OR EXISTS (SELECT 1 FROM financial_statement_presentation_artifacts)
                     OR EXISTS (SELECT 1 FROM financial_package_artifacts
                       WHERE artifact_version LIKE 'statement-presentation.%') THEN
                    RAISE EXCEPTION 'Financial statement presentation downgrade would discard pinned accounting evidence.';
                  END IF;
                END $$;

                DROP TRIGGER IF EXISTS trg_pinned_statement_package_artifacts_immutable ON financial_package_artifacts;
                DROP FUNCTION IF EXISTS prevent_pinned_statement_package_artifact_mutation();
                DROP TRIGGER IF EXISTS trg_financial_statement_presentation_artifacts_immutable ON financial_statement_presentation_artifacts;
                DROP TRIGGER IF EXISTS trg_financial_statement_presentation_revisions_immutable ON financial_statement_presentation_revisions;
                DROP FUNCTION IF EXISTS prevent_financial_statement_presentation_mutation();
                """);
            migrationBuilder.DropTable(
                name: "financial_statement_presentation_artifacts");

            migrationBuilder.DropTable(
                name: "financial_statement_presentation_revisions");
        }
    }
}
