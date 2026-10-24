using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientOpeningSuccessorCarry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_native_opening_successor_proof",
                table: "client_operational_opening_successor_proofs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_native_opening_successor_proof",
                table: "client_operational_opening_successor_proofs",
                sql: "actor_epoch >= 1 AND prior_period_revision >= 1\nAND request_id <> '00000000-0000-0000-0000-000000000000'::uuid\nAND source_identity_hash ~ '^[0-9a-f]{64}$'\nAND proof_hash ~ '^[0-9a-f]{64}$' AND request_hash ~ '^[0-9a-f]{64}$'\nAND octet_length(proof_json) BETWEEN 2 AND 16777216\nAND proof_hash = encode(sha256(convert_to(proof_json, 'UTF8')), 'hex')\nAND jsonb_typeof(proof_json::jsonb) = 'object'\nAND jsonb_typeof(proof_json::jsonb->'Version')='string'\nAND COALESCE(proof_json::jsonb->>'Version' IN ('native-opening-successor.v1', 'native-opening-successor.v2'), false)\nAND CASE WHEN proof_json::jsonb->>'Version'='native-opening-successor.v1'\n  THEN NOT (proof_json::jsonb->'Facts' ? 'CarriedPopulation')\n  ELSE jsonb_typeof(proof_json::jsonb->'Facts'->'CarriedPopulation')='object' END\nAND proof_json::jsonb @> jsonb_build_object(\n  'OpeningId', opening_id,\n  'FirmId', firm_id, 'ClientId', client_id, 'EngagementId', engagement_id,\n  'PriorPeriodId', prior_period_id, 'PriorPeriodRevision', prior_period_revision,\n  'SourceDatasetId', source_dataset_id, 'SourceIdentityHash', source_identity_hash)\nAND COALESCE((proof_json::jsonb->>'OpeningManifestHash') ~ '^[0-9a-f]{64}$', false)\nAND proof_json::jsonb ? 'OpeningManifestHash'");
            migrationBuilder.Sql(ClientOpeningSuccessorCarrySql.Up);
            migrationBuilder.Sql(ClientOpeningSuccessorCarryAllocationSql.Up);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ClientOpeningSuccessorCarrySql.Down);
            migrationBuilder.Sql(ClientOpeningSuccessorCarryAllocationSql.Down);
            migrationBuilder.DropCheckConstraint(
                name: "ck_native_opening_successor_proof",
                table: "client_operational_opening_successor_proofs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_native_opening_successor_proof",
                table: "client_operational_opening_successor_proofs",
                sql: "actor_epoch >= 1 AND prior_period_revision >= 1\nAND request_id <> '00000000-0000-0000-0000-000000000000'::uuid\nAND source_identity_hash ~ '^[0-9a-f]{64}$'\nAND proof_hash ~ '^[0-9a-f]{64}$' AND request_hash ~ '^[0-9a-f]{64}$'\nAND octet_length(proof_json) BETWEEN 2 AND 16777216\nAND proof_hash = encode(sha256(convert_to(proof_json, 'UTF8')), 'hex')\nAND jsonb_typeof(proof_json::jsonb) = 'object'\nAND proof_json::jsonb @> jsonb_build_object(\n  'Version', 'native-opening-successor.v1', 'OpeningId', opening_id,\n  'FirmId', firm_id, 'ClientId', client_id, 'EngagementId', engagement_id,\n  'PriorPeriodId', prior_period_id, 'PriorPeriodRevision', prior_period_revision,\n  'SourceDatasetId', source_dataset_id, 'SourceIdentityHash', source_identity_hash)\nAND COALESCE((proof_json::jsonb->>'OpeningManifestHash') ~ '^[0-9a-f]{64}$', false)\nAND proof_json::jsonb ? 'OpeningManifestHash'");
        }
    }
}
