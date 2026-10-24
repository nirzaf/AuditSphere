using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientOpeningSuccessorProof : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_operational_opening_successor_proofs",
                columns: table => new
                {
                    opening_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prior_period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prior_period_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_dataset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_identity_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    proof_json = table.Column<string>(type: "text", nullable: false),
                    proof_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_operational_opening_successor_proofs", x => x.opening_id);
                    table.CheckConstraint("ck_native_opening_successor_proof", "actor_epoch >= 1 AND prior_period_revision >= 1\nAND request_id <> '00000000-0000-0000-0000-000000000000'::uuid\nAND source_identity_hash ~ '^[0-9a-f]{64}$'\nAND proof_hash ~ '^[0-9a-f]{64}$' AND request_hash ~ '^[0-9a-f]{64}$'\nAND octet_length(proof_json) BETWEEN 2 AND 16777216\nAND proof_hash = encode(sha256(convert_to(proof_json, 'UTF8')), 'hex')\nAND jsonb_typeof(proof_json::jsonb) = 'object'\nAND proof_json::jsonb @> jsonb_build_object(\n  'Version', 'native-opening-successor.v1', 'OpeningId', opening_id,\n  'FirmId', firm_id, 'ClientId', client_id, 'EngagementId', engagement_id,\n  'PriorPeriodId', prior_period_id, 'PriorPeriodRevision', prior_period_revision,\n  'SourceDatasetId', source_dataset_id, 'SourceIdentityHash', source_identity_hash)\nAND COALESCE((proof_json::jsonb->>'OpeningManifestHash') ~ '^[0-9a-f]{64}$', false)\nAND proof_json::jsonb ? 'OpeningManifestHash'");
                    table.ForeignKey(
                        name: "FK_client_operational_opening_successor_proofs_client_operatio~",
                        columns: x => new { x.firm_id, x.client_id, x.opening_id },
                        principalTable: "client_operational_opening_balances",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_operational_opening_successor_proofs_client_reportin~",
                        columns: x => new { x.firm_id, x.client_id, x.prior_period_id },
                        principalTable: "client_reporting_periods",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_operational_opening_successor_proofs_trial_balance_d~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.source_dataset_id },
                        principalTable: "trial_balance_datasets",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_operational_opening_successor_proofs_users_firm_id_a~",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_operational_opening_successor_proofs_firm_id_client_~",
                table: "client_operational_opening_successor_proofs",
                columns: new[] { "firm_id", "client_id", "opening_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_operational_opening_successor_proofs_firm_id_client~1",
                table: "client_operational_opening_successor_proofs",
                columns: new[] { "firm_id", "client_id", "prior_period_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_operational_opening_successor_proofs_firm_id_client~2",
                table: "client_operational_opening_successor_proofs",
                columns: new[] { "firm_id", "client_id", "engagement_id", "source_dataset_id" });

            migrationBuilder.CreateIndex(
                name: "ux_native_opening_successor_request",
                table: "client_operational_opening_successor_proofs",
                columns: new[] { "firm_id", "actor_id", "actor_epoch", "request_id" },
                unique: true);

            migrationBuilder.Sql(ClientOpeningSuccessorProofSql.Up);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ClientOpeningSuccessorProofSql.Down);
            migrationBuilder.DropTable(
                name: "client_operational_opening_successor_proofs");
        }
    }
}
