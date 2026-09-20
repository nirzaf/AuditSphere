using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GeneralLedgerStreamingImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_source_import_batch_values",
                table: "source_import_batches");

            migrationBuilder.AddColumn<int>(
                name: "accepted_chunk_count",
                table: "source_import_batches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "accepted_line_count",
                table: "source_import_batches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "accepted_transaction_count",
                table: "source_import_batches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "expected_chunk_count",
                table: "source_import_batches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "expected_line_count",
                table: "source_import_batches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "expected_transaction_count",
                table: "source_import_batches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "general_ledger_import_chunks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    import_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chunk_number = table.Column<int>(type: "integer", nullable: false),
                    chunk_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    transaction_count = table.Column<int>(type: "integer", nullable: false),
                    line_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_general_ledger_import_chunks", x => x.id);
                    table.CheckConstraint("ck_gl_import_chunk_values", "chunk_number >= 0 AND chunk_digest ~ '^[0-9a-f]{64}$' AND transaction_count > 0 AND line_count > 0");
                    table.ForeignKey(
                        name: "FK_general_ledger_import_chunks_engagements_firm_id_client_id_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_general_ledger_import_chunks_source_import_batches_firm_id_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.import_batch_id },
                        principalTable: "source_import_batches",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_source_import_batch_values",
                table: "source_import_batches",
                sql: "length(trim(source_kind)) > 0 AND length(trim(profile_version)) > 0 AND length(trim(parser_version)) > 0 AND currency ~ '^[A-Z]{3}$' AND expected_chunk_count >= 0 AND expected_transaction_count >= 0 AND expected_line_count >= 0 AND accepted_chunk_count >= 0 AND accepted_transaction_count >= 0 AND accepted_line_count >= 0 AND accepted_chunk_count <= expected_chunk_count AND accepted_transaction_count <= expected_transaction_count AND accepted_line_count <= expected_line_count AND status IN ('LOADING','SEALED','REJECTED')");

            migrationBuilder.CreateIndex(
                name: "IX_general_ledger_import_chunks_firm_id_client_id_engagement_i~",
                table: "general_ledger_import_chunks",
                columns: new[] { "firm_id", "client_id", "engagement_id", "import_batch_id" });

            migrationBuilder.CreateIndex(
                name: "ux_gl_import_chunk_digest",
                table: "general_ledger_import_chunks",
                columns: new[] { "firm_id", "import_batch_id", "chunk_digest" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_gl_import_chunk_number",
                table: "general_ledger_import_chunks",
                columns: new[] { "firm_id", "import_batch_id", "chunk_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "general_ledger_import_chunks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_source_import_batch_values",
                table: "source_import_batches");

            migrationBuilder.DropColumn(
                name: "accepted_chunk_count",
                table: "source_import_batches");

            migrationBuilder.DropColumn(
                name: "accepted_line_count",
                table: "source_import_batches");

            migrationBuilder.DropColumn(
                name: "accepted_transaction_count",
                table: "source_import_batches");

            migrationBuilder.DropColumn(
                name: "expected_chunk_count",
                table: "source_import_batches");

            migrationBuilder.DropColumn(
                name: "expected_line_count",
                table: "source_import_batches");

            migrationBuilder.DropColumn(
                name: "expected_transaction_count",
                table: "source_import_batches");

            migrationBuilder.AddCheckConstraint(
                name: "ck_source_import_batch_values",
                table: "source_import_batches",
                sql: "length(trim(source_kind)) > 0 AND length(trim(profile_version)) > 0 AND length(trim(parser_version)) > 0 AND currency ~ '^[A-Z]{3}$' AND status IN ('LOADING','SEALED','REJECTED')");
        }
    }
}
