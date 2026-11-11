using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpeningBalanceReviewedBasis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "review_comment",
                table: "opening_balance_verifications",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "reviewed_basis_revision",
                table: "opening_balance_verifications",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Serialize the retention check with writers; never erase newly retained review evidence.
            migrationBuilder.Sql("""
                LOCK TABLE opening_balance_verifications IN ACCESS EXCLUSIVE MODE;
                DO $guard$
                BEGIN
                  IF EXISTS (SELECT 1 FROM opening_balance_verifications
                    WHERE reviewed_basis_revision IS NOT NULL OR review_comment IS NOT NULL) THEN
                    RAISE EXCEPTION 'Cannot downgrade retained opening-balance review evidence' USING ERRCODE = '55000';
                  END IF;
                END $guard$;
                """);
            migrationBuilder.DropColumn(
                name: "review_comment",
                table: "opening_balance_verifications");

            migrationBuilder.DropColumn(
                name: "reviewed_basis_revision",
                table: "opening_balance_verifications");
        }
    }
}
