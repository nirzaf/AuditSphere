using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EngagementLetterAcceptedOfferBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // STE 4.1.3 (STE-REM-02): the engagement letter must carry the exact accepted commercial
            // terms. The dual-key guard is extended so a letter insert is also refused when a dispatch
            // notification exists for the proposal that binds a different quotation revision, or when
            // the recorded acceptance does not cite that dispatched offer identity. An empty response
            // offer or a notification on another quotation can never satisfy the guard.
            migrationBuilder.Sql("""
              CREATE OR REPLACE FUNCTION require_engagement_letter_dual_keys() RETURNS trigger LANGUAGE plpgsql AS $$
              BEGIN
                IF NEW.kind = 'ENGAGEMENT_LETTER' AND NOT EXISTS (
                  SELECT 1 FROM proposals p
                  JOIN quotation_versions q ON q.id = NEW.quotation_version_id AND q.firm_id = p.firm_id AND q.proposal_id = p.id
                  JOIN opportunities o ON o.id = p.opportunity_id AND o.firm_id = p.firm_id
                  JOIN client_safety_states g ON g.id = p.practice_client_id AND g.firm_id = p.firm_id
                  JOIN acceptance_decisions a ON a.id = NEW.acceptance_decision_id AND a.firm_id = p.firm_id AND a.practice_client_id = p.practice_client_id
                  JOIN signature_specimens s ON s.id = NEW.signature_specimen_id AND s.firm_id = p.firm_id AND s.user_id = NEW.created_by_user_id AND s.revoked_at IS NULL
                  JOIN firm_seal_specimens f ON f.id = NEW.firm_seal_specimen_id AND f.firm_id = p.firm_id
                  WHERE p.id = NEW.proposal_id AND p.firm_id = NEW.firm_id AND p.status = 'ACCEPTED'
                    AND p.response_at = NEW.commercial_accepted_at AND q.status = 'APPROVED' AND q.fee = p.fee
                    AND a.decision = 'Accepted' AND a.engagement_id IS NULL AND a.service_route = o.service_route
                    AND a.generation = g.input_generation AND a.decided_at IS NOT NULL AND a.decided_by_user_id IS NOT NULL
                    AND NOT EXISTS (
                      SELECT 1 FROM commercial_notifications n
                      WHERE n.firm_id = NEW.firm_id AND n.proposal_id = p.id AND n.kind = 'PROPOSAL'
                        AND ((n.quotation_version_id IS NOT NULL AND n.quotation_version_id <> NEW.quotation_version_id)
                             OR (n.offer_sha256 IS NOT NULL AND n.offer_sha256 IS DISTINCT FROM p.response_offer_sha256)))
                ) THEN RAISE EXCEPTION 'Current commercial acceptance, Partner risk clearance, signature and seal are required'; END IF;
                RETURN NEW;
              END $$;
              """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
              CREATE OR REPLACE FUNCTION require_engagement_letter_dual_keys() RETURNS trigger LANGUAGE plpgsql AS $$
              BEGIN
                IF NEW.kind = 'ENGAGEMENT_LETTER' AND NOT EXISTS (
                  SELECT 1 FROM proposals p
                  JOIN quotation_versions q ON q.id = NEW.quotation_version_id AND q.firm_id = p.firm_id AND q.proposal_id = p.id
                  JOIN opportunities o ON o.id = p.opportunity_id AND o.firm_id = p.firm_id
                  JOIN client_safety_states g ON g.id = p.practice_client_id AND g.firm_id = p.firm_id
                  JOIN acceptance_decisions a ON a.id = NEW.acceptance_decision_id AND a.firm_id = p.firm_id AND a.practice_client_id = p.practice_client_id
                  JOIN signature_specimens s ON s.id = NEW.signature_specimen_id AND s.firm_id = p.firm_id AND s.user_id = NEW.created_by_user_id AND s.revoked_at IS NULL
                  JOIN firm_seal_specimens f ON f.id = NEW.firm_seal_specimen_id AND f.firm_id = p.firm_id
                  WHERE p.id = NEW.proposal_id AND p.firm_id = NEW.firm_id AND p.status = 'ACCEPTED'
                    AND p.response_at = NEW.commercial_accepted_at AND q.status = 'APPROVED' AND q.fee = p.fee
                    AND a.decision = 'Accepted' AND a.engagement_id IS NULL AND a.service_route = o.service_route
                    AND a.generation = g.input_generation AND a.decided_at IS NOT NULL AND a.decided_by_user_id IS NOT NULL
                ) THEN RAISE EXCEPTION 'Current commercial acceptance, Partner risk clearance, signature and seal are required'; END IF;
                RETURN NEW;
              END $$;
              """);
        }
    }
}
