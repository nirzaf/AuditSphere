using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ItemTestReviewSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_item_test_review_values",
                table: "audit_item_test_reviews");

            migrationBuilder.AddColumn<long>(
                name: "review_sequence",
                table: "audit_item_test_reviews",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_item_test_review_sequence",
                table: "audit_item_test_reviews",
                columns: new[] { "firm_id", "audit_item_test_id", "review_sequence" },
                unique: true,
                filter: "review_sequence IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_item_test_review_values",
                table: "audit_item_test_reviews",
                sql: "test_revision > 0 AND decision IN ('REVIEWED','CHANGES_REQUIRED') AND (review_sequence IS NULL OR review_sequence > 0)");

            // Nullable historical rows are retained exactly. An older writer cannot create
            // an unsequenced decision after upgrade and outrun the current review protocol.
            migrationBuilder.Sql("""
                CREATE FUNCTION require_item_test_review_sequence() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF NEW.review_sequence IS NULL THEN
                    RAISE EXCEPTION 'New item-test reviews require a serialized sequence.' USING ERRCODE = '23514';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER require_item_test_review_sequence
                BEFORE INSERT ON audit_item_test_reviews
                FOR EACH ROW EXECUTE FUNCTION require_item_test_review_sequence();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM audit_item_test_reviews WHERE review_sequence IS NOT NULL) THEN
                    RAISE EXCEPTION 'Cannot remove retained item-test review ordering.' USING ERRCODE = '55000';
                  END IF;
                END $$;
                DROP TRIGGER require_item_test_review_sequence ON audit_item_test_reviews;
                DROP FUNCTION require_item_test_review_sequence();
                """);

            migrationBuilder.DropIndex(
                name: "ux_item_test_review_sequence",
                table: "audit_item_test_reviews");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_item_test_review_values",
                table: "audit_item_test_reviews");

            migrationBuilder.DropColumn(
                name: "review_sequence",
                table: "audit_item_test_reviews");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_item_test_review_values",
                table: "audit_item_test_reviews",
                sql: "test_revision > 0 AND decision IN ('REVIEWED','CHANGES_REQUIRED')");
        }
    }
}
