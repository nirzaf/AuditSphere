using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommercialProposalCreateRequestFingerprint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "create_request_hash",
                table: "proposals",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_proposal_create_request_hash",
                table: "proposals",
                sql: "create_request_hash IS NULL OR create_request_hash ~ '^[a-f0-9]{64}$'");

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION prevent_proposal_create_request_evidence_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'DELETE' THEN
                    IF OLD.create_request_hash IS NOT NULL THEN
                      RAISE EXCEPTION 'Proposal creation request evidence is retained';
                    END IF;
                    RETURN OLD;
                  END IF;
                  IF NEW.create_request_hash IS DISTINCT FROM OLD.create_request_hash THEN
                    RAISE EXCEPTION 'Proposal creation request evidence is immutable';
                  END IF;
                  RETURN NEW;
                END;
                $$;
                CREATE TRIGGER trg_proposal_create_request_evidence_immutable
                BEFORE UPDATE OR DELETE ON proposals
                FOR EACH ROW EXECUTE FUNCTION prevent_proposal_create_request_evidence_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM proposals WHERE create_request_hash IS NOT NULL) THEN
                    RAISE EXCEPTION 'Retained proposal creation request evidence prohibits destructive rollback';
                  END IF;
                END $$;
                DROP TRIGGER IF EXISTS trg_proposal_create_request_evidence_immutable ON proposals;
                DROP FUNCTION IF EXISTS prevent_proposal_create_request_evidence_mutation();
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_proposal_create_request_hash",
                table: "proposals");

            migrationBuilder.DropColumn(
                name: "create_request_hash",
                table: "proposals");
        }
    }
}
