using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EngagementStatutoryMilestones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "engagement_milestone_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    statutory_filing_cutoff = table.Column<DateOnly>(type: "date", nullable: false),
                    fieldwork_start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    draft_report_date = table.Column<DateOnly>(type: "date", nullable: false),
                    final_report_date = table.Column<DateOnly>(type: "date", nullable: false),
                    archive_deadline_date = table.Column<DateOnly>(type: "date", nullable: false),
                    adjustment_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    warning_override_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    warnings_json = table.Column<string>(type: "jsonb", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    scheduled_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scheduled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_engagement_milestone_plans", x => x.id);
                    table.UniqueConstraint("AK_engagement_milestone_plans_firm_id_id", x => new { x.firm_id, x.id });
                    table.UniqueConstraint("AK_engagement_milestone_plans_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.CheckConstraint("ck_engagement_milestone_plan_dates", "statutory_filing_cutoff >= final_report_date AND final_report_date >= draft_report_date AND draft_report_date >= fieldwork_start_date AND archive_deadline_date >= final_report_date AND revision >= 1");
                    table.ForeignKey(
                        name: "FK_engagement_milestone_plans_engagements_firm_id_client_id_en~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_engagement_milestone_plans_users_firm_id_scheduled_by_user_~",
                        columns: x => new { x.firm_id, x.scheduled_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_engagement_milestone_plans_firm_engagement_revision",
                table: "engagement_milestone_plans",
                columns: new[] { "firm_id", "engagement_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_engagement_milestone_plans_firm_id_scheduled_by_user_id",
                table: "engagement_milestone_plans",
                columns: new[] { "firm_id", "scheduled_by_user_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "engagement_milestone_plans");
        }
    }
}
