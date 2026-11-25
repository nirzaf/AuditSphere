using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AuditSphereDbContext))]
[Migration("20261125005102_AcceptedOpportunityClientConversionGuard")]
public sealed class AcceptedOpportunityClientConversionGuard : Migration
{
  protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(AcceptedOpportunityFunction);

  protected override void Down(MigrationBuilder migrationBuilder)
  {
    migrationBuilder.Sql("""
      DO $$ BEGIN
        IF EXISTS (
          SELECT 1 FROM client_conversions cc
          JOIN proposals p ON p.firm_id=cc.firm_id AND p.id=cc.proposal_id
          JOIN opportunities o ON o.firm_id=p.firm_id AND o.id=p.opportunity_id
          JOIN acceptance_decisions a ON a.firm_id=cc.firm_id AND a.practice_client_id=cc.client_id
            AND a.opportunity_id=o.id AND a.decision='Accepted' AND a.conditions IS NULL
          WHERE cc.firm_id=p.firm_id
        ) THEN
          RAISE EXCEPTION 'Retained acceptance-first conversion receipts prevent guard rollback' USING ERRCODE='55000';
        END IF;
      END $$;
      """);
    migrationBuilder.Sql(LegacyPendingFunction);
  }

  private const string AcceptedOpportunityFunction = """
    CREATE OR REPLACE FUNCTION enforce_client_conversion() RETURNS trigger AS $$
    DECLARE review jsonb := NEW.preview_json::jsonb;
    BEGIN
     IF NOT EXISTS (
      SELECT 1 FROM proposals p
      JOIN opportunities o ON o.id=p.opportunity_id AND o.firm_id=NEW.firm_id
      JOIN practice_clients c ON c.id=NEW.client_id AND c.firm_id=NEW.firm_id
      JOIN users actor ON actor.id=NEW.actor_id AND actor.firm_id=NEW.firm_id
      WHERE p.id=NEW.proposal_id AND p.firm_id=NEW.firm_id AND p.practice_client_id=c.id
       AND o.practice_client_id=c.id AND p.status='ACCEPTED' AND o.stage='WON'
       AND p.revision=(review->>'ProposalRevision')::bigint
       AND (review->>'ProposalId')::uuid=p.id AND (review->>'RequestId')::uuid=NEW.request_id
       AND review->>'RequestHash'=NEW.request_hash AND review->>'ReviewBasis'=NEW.review_basis
       AND o.service_route=review->>'ServiceRoute'
       AND lower(trim(c.legal_name))=lower(trim(review->'Fields'->>'LegalName'))
       AND ((review->>'ExistingClientId') IS NULL OR (review->>'ExistingClientId')::uuid=c.id)
       AND actor.session_epoch=NEW.actor_epoch AND NOT actor.disabled AND lower(actor.user_kind)<>'client'
       AND EXISTS(SELECT 1 FROM role_grants r WHERE r.firm_id=NEW.firm_id AND r.user_id=NEW.actor_id
        AND lower(r.role) IN ('administrator','partner','manager','relationshipmanager')
        AND r.client_id IS NULL AND r.engagement_id IS NULL AND r.revoked_at IS NULL
        AND (r.expires_at IS NULL OR r.expires_at>statement_timestamp()))
       AND EXISTS(SELECT 1 FROM acceptance_decisions a
        JOIN client_safety_states s ON s.firm_id=a.firm_id AND s.id=a.practice_client_id
        WHERE a.firm_id=NEW.firm_id AND a.practice_client_id=c.id AND a.engagement_id IS NULL
         AND a.opportunity_id=o.id AND a.service_route=o.service_route AND a.decision='Accepted'
         AND a.conditions IS NULL AND a.generation=s.input_generation
         AND a.opportunity_basis_sha256 ~ '^[0-9a-f]{64}$'
         AND a.decided_by_user_id IS NOT NULL AND a.decided_at IS NOT NULL)
       AND EXISTS(SELECT 1 FROM client_portal_intents i WHERE i.firm_id=NEW.firm_id AND i.practice_client_id=c.id)
     ) THEN RAISE EXCEPTION 'Conversion requires exact current opportunity acceptance and authorized committed commercial handover' USING ERRCODE='23514'; END IF;
     RETURN NEW;
    END;
    $$ LANGUAGE plpgsql;
    """;

  private const string LegacyPendingFunction = """
    CREATE OR REPLACE FUNCTION enforce_client_conversion() RETURNS trigger AS $$
    DECLARE review jsonb := NEW.preview_json::jsonb;
    BEGIN
     IF NOT EXISTS (
      SELECT 1 FROM proposals p
      JOIN opportunities o ON o.id=p.opportunity_id AND o.firm_id=NEW.firm_id
      JOIN practice_clients c ON c.id=NEW.client_id AND c.firm_id=NEW.firm_id
      JOIN users actor ON actor.id=NEW.actor_id AND actor.firm_id=NEW.firm_id
      WHERE p.id=NEW.proposal_id AND p.firm_id=NEW.firm_id AND p.practice_client_id=c.id
       AND o.practice_client_id=c.id AND p.status='ACCEPTED' AND o.stage='WON'
       AND p.revision=(review->>'ProposalRevision')::bigint
       AND (review->>'ProposalId')::uuid=p.id AND (review->>'RequestId')::uuid=NEW.request_id
       AND review->>'RequestHash'=NEW.request_hash AND review->>'ReviewBasis'=NEW.review_basis
       AND o.service_route=review->>'ServiceRoute'
       AND lower(trim(c.legal_name))=lower(trim(review->'Fields'->>'LegalName'))
       AND ((review->>'ExistingClientId') IS NULL OR (review->>'ExistingClientId')::uuid=c.id)
       AND actor.session_epoch=NEW.actor_epoch AND NOT actor.disabled AND lower(actor.user_kind)<>'client'
       AND EXISTS(SELECT 1 FROM role_grants r WHERE r.firm_id=NEW.firm_id AND r.user_id=NEW.actor_id
        AND lower(r.role) IN ('administrator','partner','manager','relationshipmanager')
        AND r.client_id IS NULL AND r.engagement_id IS NULL AND r.revoked_at IS NULL
        AND (r.expires_at IS NULL OR r.expires_at>statement_timestamp()))
       AND EXISTS(SELECT 1 FROM acceptance_decisions a WHERE a.firm_id=NEW.firm_id
        AND a.practice_client_id=c.id AND a.engagement_id IS NULL AND a.service_route=o.service_route AND a.decision='Pending')
       AND EXISTS(SELECT 1 FROM client_portal_intents i WHERE i.firm_id=NEW.firm_id AND i.practice_client_id=c.id)
     ) THEN RAISE EXCEPTION 'Conversion requires exact authorized committed commercial handover' USING ERRCODE='23514'; END IF;
     RETURN NEW;
    END;
    $$ LANGUAGE plpgsql;
    """;
}
