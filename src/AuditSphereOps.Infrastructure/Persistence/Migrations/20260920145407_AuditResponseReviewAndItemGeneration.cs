using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditResponseReviewAndItemGeneration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM audit_confirmation_responses) THEN
                    RAISE EXCEPTION 'AuditResponseReviewAndItemGeneration requires an explicit disposition for existing confirmation responses.' USING ERRCODE = '55000';
                  END IF;
                END $$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_item_test_values",
                table: "audit_item_tests");

            migrationBuilder.AddColumn<long>(
                name: "input_generation",
                table: "audit_item_tests",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_user_id",
                table: "audit_confirmation_responses",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_item_test_values",
                table: "audit_item_tests",
                sql: "revision > 0 AND length(trim(work_performed)) > 0 AND length(trim(evidence_references_json)) > 0 AND input_generation > 0 AND result IN ('PENDING','PASS','EXCEPTION','LIMITATION')");

            migrationBuilder.CreateIndex(
                name: "IX_audit_confirmation_responses_firm_id_created_by_user_id",
                table: "audit_confirmation_responses",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_audit_confirmation_responses_users_firm_id_created_by_user_~",
                table: "audit_confirmation_responses",
                columns: new[] { "firm_id", "created_by_user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_audit_confirmation_responses_users_firm_id_created_by_user_~",
                table: "audit_confirmation_responses");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_item_test_values",
                table: "audit_item_tests");

            migrationBuilder.DropIndex(
                name: "IX_audit_confirmation_responses_firm_id_created_by_user_id",
                table: "audit_confirmation_responses");

            migrationBuilder.DropColumn(
                name: "input_generation",
                table: "audit_item_tests");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                table: "audit_confirmation_responses");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_item_test_values",
                table: "audit_item_tests",
                sql: "revision > 0 AND length(trim(work_performed)) > 0 AND length(trim(evidence_references_json)) > 0 AND result IN ('PENDING','PASS','EXCEPTION','LIMITATION')");
        }
    }
}
