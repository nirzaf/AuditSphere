using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NativeGeneralLedgerSourceHandoff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "origin_kind",
                table: "source_import_batches",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "EXTERNAL");

            migrationBuilder.AlterColumn<bool>(
                name: "is_year_end",
                table: "general_ledger_transactions",
                type: "boolean",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<bool>(
                name: "is_manual",
                table: "general_ledger_transactions",
                type: "boolean",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<bool>(
                name: "is_year_end",
                table: "general_ledger_lines",
                type: "boolean",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<bool>(
                name: "is_manual",
                table: "general_ledger_lines",
                type: "boolean",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.CreateTable(
                name: "native_general_ledger_sources",
                columns: table => new
                {
                    import_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trial_balance_dataset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    native_trial_balance_source_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    native_trial_balance_manifest_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_identity_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    manifest_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    manifest_json = table.Column<string>(type: "text", nullable: false),
                    generated_rows_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    generated_rows_json = table.Column<string>(type: "text", nullable: false),
                    transaction_count = table.Column<int>(type: "integer", nullable: false),
                    line_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_native_general_ledger_sources", x => x.import_batch_id);
                    table.CheckConstraint("ck_native_gl_source_values", "actor_epoch >= 1 AND transaction_count BETWEEN 1 AND 250000 AND line_count BETWEEN 1 AND 250000 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND native_trial_balance_source_hash ~ '^[0-9a-f]{64}$' AND native_trial_balance_manifest_hash ~ '^[0-9a-f]{64}$' AND request_hash ~ '^[0-9a-f]{64}$' AND source_identity_hash ~ '^[0-9a-f]{64}$' AND manifest_hash ~ '^[0-9a-f]{64}$' AND generated_rows_hash ~ '^[0-9a-f]{64}$' AND jsonb_typeof(manifest_json::jsonb) = 'object' AND jsonb_typeof(generated_rows_json::jsonb) = 'array' AND jsonb_array_length(generated_rows_json::jsonb) = line_count AND octet_length(manifest_json) BETWEEN 2 AND 16777216 AND octet_length(generated_rows_json) BETWEEN 2 AND 33554432 AND manifest_hash = encode(sha256(convert_to(manifest_json, 'UTF8')), 'hex') AND generated_rows_hash = encode(sha256(convert_to(generated_rows_json, 'UTF8')), 'hex')");
                    table.ForeignKey(
                        name: "FK_native_general_ledger_sources_source_import_batches_firm_id~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.import_batch_id },
                        principalTable: "source_import_batches",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_native_general_ledger_sources_trial_balance_datasets_firm_i~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.trial_balance_dataset_id },
                        principalTable: "trial_balance_datasets",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_native_general_ledger_sources_users_firm_id_actor_id",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_source_import_origin",
                table: "source_import_batches",
                sql: "origin_kind IN ('EXTERNAL', 'NATIVE_LEDGER') AND (origin_kind <> 'NATIVE_LEDGER' OR (source_kind = 'GL' AND book_id IS NULL AND raw_file_sha256_hex = '' AND profile_version = 'native-posted-gl.v1' AND parser_version = 'native-gl-export.v1' AND expected_chunk_count = 0 AND accepted_chunk_count = 0))");

            migrationBuilder.CreateIndex(
                name: "IX_native_general_ledger_sources_firm_id_client_id_engagement_~",
                table: "native_general_ledger_sources",
                columns: new[] { "firm_id", "client_id", "engagement_id", "import_batch_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_native_general_ledger_sources_firm_id_client_id_engagement~1",
                table: "native_general_ledger_sources",
                columns: new[] { "firm_id", "client_id", "engagement_id", "trial_balance_dataset_id" });

            migrationBuilder.CreateIndex(
                name: "ux_native_gl_actor_request",
                table: "native_general_ledger_sources",
                columns: new[] { "firm_id", "actor_id", "actor_epoch", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_native_gl_source_identity",
                table: "native_general_ledger_sources",
                columns: new[] { "firm_id", "client_id", "engagement_id", "source_identity_hash" },
                unique: true);

            migrationBuilder.Sql(NativeGeneralLedgerSourceIntegritySql.Install);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Refuse destruction once this schema holds retained native provenance.
            migrationBuilder.Sql(NativeGeneralLedgerSourceIntegritySql.Remove);

            migrationBuilder.DropTable(
                name: "native_general_ledger_sources");

            migrationBuilder.DropCheckConstraint(
                name: "ck_source_import_origin",
                table: "source_import_batches");

            migrationBuilder.DropColumn(
                name: "origin_kind",
                table: "source_import_batches");

            migrationBuilder.AlterColumn<bool>(
                name: "is_year_end",
                table: "general_ledger_transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "is_manual",
                table: "general_ledger_transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "is_year_end",
                table: "general_ledger_lines",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "is_manual",
                table: "general_ledger_lines",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true);
        }
    }
}
