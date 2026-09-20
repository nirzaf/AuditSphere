using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrialBalanceProfilesAndBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_dataset_firm_engagement_raw_hash",
                table: "trial_balance_datasets");

            migrationBuilder.AddColumn<decimal>(
                name: "source_credit",
                table: "trial_balance_rows",
                type: "numeric(19,6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "source_debit",
                table: "trial_balance_rows",
                type: "numeric(19,6)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "import_batch_id",
                table: "trial_balance_datasets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "import_profile_version",
                table: "trial_balance_datasets",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "tb-signed-net.v1");

            migrationBuilder.AddColumn<string>(
                name: "source_layout",
                table: "trial_balance_datasets",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "SIGNED_NET");

            migrationBuilder.CreateTable(
                name: "trial_balance_import_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    raw_file_sha256_hex = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    normalized_dataset_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    import_profile_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_layout = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    entity_count = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trial_balance_import_batches", x => x.id);
                    table.UniqueConstraint("AK_trial_balance_import_batches_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.CheckConstraint("ck_tb_import_batch_values", "raw_file_sha256_hex ~ '^[0-9a-f]{64}$' AND normalized_dataset_digest ~ '^[0-9a-f]{64}$' AND source_layout IN ('SIGNED_NET','DEBIT_CREDIT') AND entity_count >= 2 AND status IN ('LOADING','SEALED')");
                    table.ForeignKey(
                        name: "FK_trial_balance_import_batches_engagements_firm_id_client_id_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trial_balance_import_batches_users_firm_id_created_by_user_~",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_tb_row_source_amounts",
                table: "trial_balance_rows",
                sql: "(source_debit IS NULL OR source_debit >= 0) AND (source_credit IS NULL OR source_credit >= 0)");

            migrationBuilder.CreateIndex(
                name: "IX_trial_balance_datasets_firm_id_client_id_engagement_id_impo~",
                table: "trial_balance_datasets",
                columns: new[] { "firm_id", "client_id", "engagement_id", "import_batch_id" });

            migrationBuilder.CreateIndex(
                name: "ux_dataset_firm_engagement_raw_hash_entity",
                table: "trial_balance_datasets",
                columns: new[] { "firm_id", "engagement_id", "raw_file_sha256_hex", "legal_entity_key" },
                unique: true,
                filter: "length(raw_file_sha256_hex) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tb_source_layout",
                table: "trial_balance_datasets",
                sql: "source_layout IN ('SIGNED_NET','DEBIT_CREDIT') AND (import_profile_version IS NOT NULL AND length(trim(import_profile_version)) > 0)");

            migrationBuilder.CreateIndex(
                name: "IX_trial_balance_import_batches_firm_id_created_by_user_id",
                table: "trial_balance_import_batches",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_tb_import_batch_raw_hash",
                table: "trial_balance_import_batches",
                columns: new[] { "firm_id", "engagement_id", "raw_file_sha256_hex" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_trial_balance_datasets_trial_balance_import_batches_firm_id~",
                table: "trial_balance_datasets",
                columns: new[] { "firm_id", "client_id", "engagement_id", "import_batch_id" },
                principalTable: "trial_balance_import_batches",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_trial_balance_datasets_trial_balance_import_batches_firm_id~",
                table: "trial_balance_datasets");

            migrationBuilder.DropTable(
                name: "trial_balance_import_batches");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tb_row_source_amounts",
                table: "trial_balance_rows");

            migrationBuilder.DropIndex(
                name: "IX_trial_balance_datasets_firm_id_client_id_engagement_id_impo~",
                table: "trial_balance_datasets");

            migrationBuilder.DropIndex(
                name: "ux_dataset_firm_engagement_raw_hash_entity",
                table: "trial_balance_datasets");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tb_source_layout",
                table: "trial_balance_datasets");

            migrationBuilder.DropColumn(
                name: "source_credit",
                table: "trial_balance_rows");

            migrationBuilder.DropColumn(
                name: "source_debit",
                table: "trial_balance_rows");

            migrationBuilder.DropColumn(
                name: "import_batch_id",
                table: "trial_balance_datasets");

            migrationBuilder.DropColumn(
                name: "import_profile_version",
                table: "trial_balance_datasets");

            migrationBuilder.DropColumn(
                name: "source_layout",
                table: "trial_balance_datasets");

            migrationBuilder.CreateIndex(
                name: "ux_dataset_firm_engagement_raw_hash",
                table: "trial_balance_datasets",
                columns: new[] { "firm_id", "engagement_id", "raw_file_sha256_hex" },
                unique: true,
                filter: "length(raw_file_sha256_hex) > 0");
        }
    }
}
