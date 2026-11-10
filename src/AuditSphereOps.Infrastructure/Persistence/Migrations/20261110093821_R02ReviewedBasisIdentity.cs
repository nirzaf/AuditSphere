using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class R02ReviewedBasisIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "reviewed_revision",
                table: "subsequent_event_reviews",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "reviewed_revision",
                table: "going_concern_assessments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "reviewed_revision",
                table: "analytical_variance_investigations",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE analytical_variance_investigations, going_concern_assessments, subsequent_event_reviews
                IN ACCESS EXCLUSIVE MODE;
                """);
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM analytical_variance_investigations WHERE reviewed_revision IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM going_concern_assessments WHERE reviewed_revision IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM subsequent_event_reviews WHERE reviewed_revision IS NOT NULL) THEN
                    RAISE EXCEPTION 'R02 downgrade would discard retained completion review-basis evidence.';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropColumn(
                name: "reviewed_revision",
                table: "subsequent_event_reviews");

            migrationBuilder.DropColumn(
                name: "reviewed_revision",
                table: "going_concern_assessments");

            migrationBuilder.DropColumn(
                name: "reviewed_revision",
                table: "analytical_variance_investigations");
        }
    }
}
