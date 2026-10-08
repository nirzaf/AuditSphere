using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EarlyComplianceLock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "file_freeze_early_locks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    freeze_id = table.Column<Guid>(type: "uuid", nullable: false),
                    report_deliverable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    freeze_revision = table.Column<long>(type: "bigint", nullable: false),
                    release_id = table.Column<Guid>(type: "uuid", nullable: false),
                    archive_readiness_digest = table.Column<string>(type: "text", nullable: false),
                    rationale = table.Column<string>(type: "text", nullable: false),
                    locked_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    locked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_freeze_early_locks", x => x.id);
                    table.CheckConstraint("ck_file_freeze_early_lock_values", "length(rationale) > 0 AND length(archive_readiness_digest) = 64 AND freeze_revision >= 1");
                    table.ForeignKey(
                        name: "FK_file_freeze_early_locks_audit_deliverables_report_deliverab~",
                        column: x => x.report_deliverable_id,
                        principalTable: "audit_deliverables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_file_freeze_early_locks_engagement_file_freezes_freeze_id",
                        column: x => x.freeze_id,
                        principalTable: "engagement_file_freezes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_file_freeze_early_locks_firm_id_freeze_id",
                table: "file_freeze_early_locks",
                columns: new[] { "firm_id", "freeze_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_file_freeze_early_locks_freeze_id",
                table: "file_freeze_early_locks",
                column: "freeze_id");

            migrationBuilder.CreateIndex(
                name: "IX_file_freeze_early_locks_report_deliverable_id",
                table: "file_freeze_early_locks",
                column: "report_deliverable_id");

            // The early-lock record is append-only evidence of who locked the file, when, and on which reviewed digest.
            migrationBuilder.Sql("""
                CREATE FUNCTION prevent_file_freeze_early_lock_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'Early compliance locks are immutable evidence.' USING ERRCODE = '55000';
                END $$;
                CREATE TRIGGER trg_file_freeze_early_locks_immutable
                BEFORE UPDATE OR DELETE ON file_freeze_early_locks
                FOR EACH ROW EXECUTE FUNCTION prevent_file_freeze_early_lock_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_file_freeze_early_locks_immutable ON file_freeze_early_locks; DROP FUNCTION IF EXISTS prevent_file_freeze_early_lock_mutation();");

            migrationBuilder.DropTable(
                name: "file_freeze_early_locks");
        }
    }
}
