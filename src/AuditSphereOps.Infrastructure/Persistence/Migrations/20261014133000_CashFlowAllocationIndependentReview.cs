using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class CashFlowAllocationIndependentReview : Migration
{
  /// <inheritdoc />
  protected override void Up(MigrationBuilder migrationBuilder)
  {
    migrationBuilder.AddColumn<Guid>(
      name: "prepared_by_user_id",
      table: "financial_package_cash_flow_allocations",
      type: "uuid",
      nullable: true);

    migrationBuilder.CreateIndex(
      name: "ix_fin_package_cash_alloc_preparer",
      table: "financial_package_cash_flow_allocations",
      columns: new[] { "firm_id", "prepared_by_user_id" });

    migrationBuilder.AddForeignKey(
      name: "FK_fin_package_cash_alloc_users_preparer",
      table: "financial_package_cash_flow_allocations",
      columns: new[] { "firm_id", "prepared_by_user_id" },
      principalTable: "users",
      principalColumns: new[] { "firm_id", "id" },
      onDelete: ReferentialAction.Restrict);

    migrationBuilder.CreateTable(
      name: "financial_package_cash_flow_allocation_reviews",
      columns: table => new
      {
        id = table.Column<Guid>(type: "uuid", nullable: false),
        firm_id = table.Column<Guid>(type: "uuid", nullable: false),
        client_id = table.Column<Guid>(type: "uuid", nullable: false),
        engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
        financial_package_id = table.Column<Guid>(type: "uuid", nullable: false),
        package_revision = table.Column<long>(type: "bigint", nullable: false),
        package_generation = table.Column<long>(type: "bigint", nullable: false),
        package_calculation_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
        source_manifest_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
        decision = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
        evidence_reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
        comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
        decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
        decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
      },
      constraints: table =>
      {
        table.PrimaryKey("PK_financial_package_cash_flow_allocation_reviews", x => x.id);
        table.CheckConstraint("ck_financial_package_cash_allocation_review_values",
          "package_revision >= 1 AND package_generation >= 1 " +
          "AND package_calculation_hash ~ '^[a-f0-9]{64}$' AND source_manifest_hash ~ '^[a-f0-9]{64}$' " +
          "AND decision IN ('APPROVED','CHANGES_REQUIRED','REJECTED') " +
          "AND length(trim(evidence_reference)) BETWEEN 1 AND 2000 AND length(trim(comment)) <= 4000");
        table.ForeignKey(
          name: "FK_fin_package_cash_alloc_reviews_package",
          columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.financial_package_id },
          principalTable: "financial_packages",
          principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
          onDelete: ReferentialAction.Restrict);
        table.ForeignKey(
          name: "FK_fin_package_cash_alloc_reviews_users_reviewer",
          columns: x => new { x.firm_id, x.decided_by_user_id },
          principalTable: "users",
          principalColumns: new[] { "firm_id", "id" },
          onDelete: ReferentialAction.Restrict);
      });

    migrationBuilder.CreateIndex(
      name: "ix_fin_package_cash_alloc_review_reviewer",
      table: "financial_package_cash_flow_allocation_reviews",
      columns: new[] { "firm_id", "decided_by_user_id" });
    migrationBuilder.CreateIndex(
      name: "ix_fin_package_cash_alloc_review_package",
      table: "financial_package_cash_flow_allocation_reviews",
      columns: new[] { "firm_id", "client_id", "engagement_id", "financial_package_id" });
    migrationBuilder.CreateIndex(
      name: "ix_financial_package_cash_allocation_review_history",
      table: "financial_package_cash_flow_allocation_reviews",
      columns: new[] { "firm_id", "financial_package_id", "decided_at" });
    migrationBuilder.CreateIndex(
      name: "ux_financial_package_cash_allocation_review_identity",
      table: "financial_package_cash_flow_allocation_reviews",
      columns: new[] { "firm_id", "financial_package_id", "package_revision", "source_manifest_hash", "decided_by_user_id" },
      unique: true);

    migrationBuilder.Sql(@"
      CREATE FUNCTION guard_financial_package_cash_allocation_review_history() RETURNS trigger LANGUAGE plpgsql AS $fn$
      BEGIN RAISE EXCEPTION 'Cash-flow allocation review evidence is append-only.' USING ERRCODE='23514'; END; $fn$;
      CREATE TRIGGER immutable_financial_package_cash_allocation_review
        BEFORE UPDATE OR DELETE ON financial_package_cash_flow_allocation_reviews
        FOR EACH ROW EXECUTE FUNCTION guard_financial_package_cash_allocation_review_history();");
  }

  /// <inheritdoc />
  protected override void Down(MigrationBuilder migrationBuilder)
  {
    migrationBuilder.Sql(@"
      DROP TRIGGER IF EXISTS immutable_financial_package_cash_allocation_review ON financial_package_cash_flow_allocation_reviews;
      DROP FUNCTION IF EXISTS guard_financial_package_cash_allocation_review_history();");
    migrationBuilder.DropTable(name: "financial_package_cash_flow_allocation_reviews");
    migrationBuilder.DropForeignKey(
      name: "FK_fin_package_cash_alloc_users_preparer",
      table: "financial_package_cash_flow_allocations");
    migrationBuilder.DropIndex(
      name: "ix_fin_package_cash_alloc_preparer",
      table: "financial_package_cash_flow_allocations");
    migrationBuilder.DropColumn(name: "prepared_by_user_id", table: "financial_package_cash_flow_allocations");
  }
}
