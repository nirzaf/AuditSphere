using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceAcceptanceAndValidationIssues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "source_acceptance_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    trial_balance_dataset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    import_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_identity_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    accepted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_acceptance_decisions", x => x.id);
                    table.CheckConstraint("ck_source_acceptance_values", "source_kind IN ('TB','GL') AND decision IN ('ACCEPTED') AND length(trim(evidence_reference)) > 0 AND ((source_kind = 'TB' AND trial_balance_dataset_id IS NOT NULL AND import_batch_id IS NULL) OR (source_kind = 'GL' AND import_batch_id IS NOT NULL AND trial_balance_dataset_id IS NULL))");
                    table.ForeignKey(
                        name: "FK_source_acceptance_decisions_source_import_batches_firm_id_c~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.import_batch_id },
                        principalTable: "source_import_batches",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_source_acceptance_decisions_trial_balance_datasets_firm_id_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.trial_balance_dataset_id },
                        principalTable: "trial_balance_datasets",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trial_balance_validation_issues",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dataset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    row_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trial_balance_validation_issues", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_source_acceptance_decisions_firm_id_client_id_engagement_i~1",
                table: "source_acceptance_decisions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "trial_balance_dataset_id" });

            migrationBuilder.CreateIndex(
                name: "IX_source_acceptance_decisions_firm_id_client_id_engagement_id~",
                table: "source_acceptance_decisions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "import_batch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_source_acceptance_pointer",
                table: "source_acceptance_decisions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "source_kind", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_source_acceptance_gl_batch",
                table: "source_acceptance_decisions",
                columns: new[] { "firm_id", "import_batch_id" },
                unique: true,
                filter: "import_batch_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_source_acceptance_tb_dataset",
                table: "source_acceptance_decisions",
                columns: new[] { "firm_id", "trial_balance_dataset_id" },
                unique: true,
                filter: "trial_balance_dataset_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tb_validation_issues",
                table: "trial_balance_validation_issues",
                columns: new[] { "firm_id", "dataset_id", "severity" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "source_acceptance_decisions");

            migrationBuilder.DropTable(
                name: "trial_balance_validation_issues");
        }
    }
}
