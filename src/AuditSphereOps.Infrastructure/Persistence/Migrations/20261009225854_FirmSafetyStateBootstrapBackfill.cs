using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FirmSafetyStateBootstrapBackfill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO firm_safety_states (id, operating_mode, deployment_epoch, recovery_epoch, policy_generation)
                SELECT DISTINCT rg.firm_id, 'LOCAL_ONLY', 1, 0, 1
                FROM role_grants AS rg
                WHERE rg.role = 'Administrator'
                  AND rg.client_id IS NULL
                  AND rg.engagement_id IS NULL
                  AND rg.revoked_at IS NULL
                  AND (rg.expires_at IS NULL OR rg.expires_at > statement_timestamp())
                  AND NOT EXISTS (
                    SELECT 1 FROM firm_safety_states AS state WHERE state.id = rg.firm_id
                  )
                ON CONFLICT (id) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Preserve initialized safety rows if a deployment is rolled back; operations may now reference them.
        }
    }
}
