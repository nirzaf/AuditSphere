using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NativeTrialBalanceSourceHandoff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "native_trial_balance_sources",
                columns: table => new
                {
                    dataset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_identity_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    manifest_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    manifest_json = table.Column<string>(type: "text", nullable: false),
                    generated_rows_json = table.Column<string>(type: "text", nullable: false),
                    generated_rows_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    row_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_native_trial_balance_sources", x => x.dataset_id);
                    table.CheckConstraint("ck_native_tb_source_values", "actor_epoch >= 1 AND row_count BETWEEN 1 AND 250000 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_hash ~ '^[0-9a-f]{64}$' AND source_identity_hash ~ '^[0-9a-f]{64}$' AND manifest_hash ~ '^[0-9a-f]{64}$' AND generated_rows_hash ~ '^[0-9a-f]{64}$' AND jsonb_typeof(manifest_json::jsonb) = 'object' AND jsonb_typeof(generated_rows_json::jsonb) = 'array' AND jsonb_array_length(generated_rows_json::jsonb) = row_count AND octet_length(manifest_json) BETWEEN 2 AND 16777216 AND octet_length(generated_rows_json) BETWEEN 2 AND 33554432 AND manifest_hash = encode(sha256(convert_to(manifest_json, 'UTF8')), 'hex') AND generated_rows_hash = encode(sha256(convert_to(generated_rows_json, 'UTF8')), 'hex')");
                    table.ForeignKey(
                        name: "FK_native_trial_balance_sources_trial_balance_datasets_firm_id~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.dataset_id },
                        principalTable: "trial_balance_datasets",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_native_trial_balance_sources_users_firm_id_actor_id",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_tb_native_origin",
                table: "trial_balance_datasets",
                sql: "source_kind <> 'Native' OR (period_id IS NOT NULL AND book_id IS NULL AND import_batch_id IS NULL AND raw_file_sha256_hex = '' AND import_profile_version = 'native-period-ledger.v1' AND source_layout = 'DEBIT_CREDIT' AND revision >= 1 AND currency ~ '^[A-Z]{3}$' AND length(trim(legal_entity_key)) > 0 AND normalized_dataset_digest ~ '^[0-9a-f]{64}$' AND sha256_hex ~ '^[0-9a-f]{64}$')");

            migrationBuilder.CreateIndex(
                name: "IX_native_trial_balance_sources_firm_id_client_id_engagement_i~",
                table: "native_trial_balance_sources",
                columns: new[] { "firm_id", "client_id", "engagement_id", "dataset_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_native_tb_actor_request",
                table: "native_trial_balance_sources",
                columns: new[] { "firm_id", "actor_id", "actor_epoch", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_native_tb_source_identity",
                table: "native_trial_balance_sources",
                columns: new[] { "firm_id", "client_id", "engagement_id", "source_identity_hash" },
                unique: true);
            migrationBuilder.Sql(NativeTrialBalanceSourceIntegritySql.Install);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(NativeTrialBalanceSourceIntegritySql.Remove);
            migrationBuilder.DropTable(
                name: "native_trial_balance_sources");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tb_native_origin",
                table: "trial_balance_datasets");
        }
    }
}
