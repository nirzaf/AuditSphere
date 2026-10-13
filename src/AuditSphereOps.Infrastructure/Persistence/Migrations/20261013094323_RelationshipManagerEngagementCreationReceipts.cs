using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

public partial class RelationshipManagerEngagementCreationReceipts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION enforce_engagement_creation() RETURNS trigger AS $$
            BEGIN
            IF NOT EXISTS (
            SELECT 1 FROM engagements e
            JOIN client_safety_states c ON c.firm_id=e.firm_id AND c.id=e.practice_client_id
            JOIN users u ON u.firm_id=e.firm_id AND u.id=NEW.actor_id
            WHERE e.firm_id=NEW.firm_id AND e.id=NEW.engagement_id AND e.practice_client_id=NEW.client_id
            AND e.status='Draft' AND e.professional_work_blocked AND e.generation=NEW.engagement_generation
            AND e.service_route=NEW.input_json::jsonb->>'ServiceRoute'
            AND e.service_profile_id=NEW.input_json::jsonb->>'ServiceProfile'
            AND e.period_start=NEW.input_json::jsonb->>'PeriodStart' AND e.period_end=NEW.input_json::jsonb->>'PeriodEnd'
            AND e.created_at=NEW.created_at AND c.input_generation=NEW.client_generation
            AND u.session_epoch=NEW.actor_epoch AND NOT u.disabled AND lower(u.user_kind)<>'client'
            AND EXISTS (SELECT 1 FROM role_grants r WHERE r.firm_id=e.firm_id AND r.user_id=u.id
            AND lower(r.role) IN ('partner','manager','relationshipmanager') AND r.revoked_at IS NULL
            AND (r.expires_at IS NULL OR r.expires_at>statement_timestamp())
            AND r.engagement_id IS NULL AND (r.client_id IS NULL OR r.client_id=e.practice_client_id))
            ) THEN RAISE EXCEPTION 'Creation receipt requires exact current scoped blocked publication' USING ERRCODE='23514'; END IF;
            RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;
            """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION enforce_engagement_creation() RETURNS trigger AS $$
            BEGIN
            IF NOT EXISTS (
            SELECT 1 FROM engagements e
            JOIN client_safety_states c ON c.firm_id=e.firm_id AND c.id=e.practice_client_id
            JOIN users u ON u.firm_id=e.firm_id AND u.id=NEW.actor_id
            WHERE e.firm_id=NEW.firm_id AND e.id=NEW.engagement_id AND e.practice_client_id=NEW.client_id
            AND e.status='Draft' AND e.professional_work_blocked AND e.generation=NEW.engagement_generation
            AND e.service_route=NEW.input_json::jsonb->>'ServiceRoute'
            AND e.service_profile_id=NEW.input_json::jsonb->>'ServiceProfile'
            AND e.period_start=NEW.input_json::jsonb->>'PeriodStart' AND e.period_end=NEW.input_json::jsonb->>'PeriodEnd'
            AND e.created_at=NEW.created_at AND c.input_generation=NEW.client_generation
            AND u.session_epoch=NEW.actor_epoch AND NOT u.disabled AND lower(u.user_kind)<>'client'
            AND EXISTS (SELECT 1 FROM role_grants r WHERE r.firm_id=e.firm_id AND r.user_id=u.id
            AND lower(r.role) IN ('partner','manager') AND r.revoked_at IS NULL
            AND (r.expires_at IS NULL OR r.expires_at>statement_timestamp())
            AND r.engagement_id IS NULL AND (r.client_id IS NULL OR r.client_id=e.practice_client_id))
            ) THEN RAISE EXCEPTION 'Creation receipt requires exact current scoped blocked publication' USING ERRCODE='23514'; END IF;
            RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;
            """);
}
