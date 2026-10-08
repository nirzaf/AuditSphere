using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MaterialityPracticalRounding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "materiality_rounding_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    materiality_calculation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_assessment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_assessment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    computed_planning_materiality = table.Column<decimal>(type: "numeric(19,6)", precision: 28, scale: 6, nullable: false),
                    computed_tolerable_error = table.Column<decimal>(type: "numeric(19,6)", precision: 28, scale: 6, nullable: false),
                    computed_sad_threshold = table.Column<decimal>(type: "numeric(19,6)", precision: 28, scale: 6, nullable: false),
                    adjusted_planning_materiality = table.Column<decimal>(type: "numeric(19,6)", precision: 28, scale: 6, nullable: false),
                    adjusted_tolerable_error = table.Column<decimal>(type: "numeric(19,6)", precision: 28, scale: 6, nullable: false),
                    adjusted_sad_threshold = table.Column<decimal>(type: "numeric(19,6)", precision: 28, scale: 6, nullable: false),
                    planning_delta_percent = table.Column<decimal>(type: "numeric(19,6)", precision: 9, scale: 4, nullable: false),
                    tolerable_delta_percent = table.Column<decimal>(type: "numeric(19,6)", precision: 9, scale: 4, nullable: false),
                    sad_delta_percent = table.Column<decimal>(type: "numeric(19,6)", precision: 9, scale: 4, nullable: false),
                    rationale = table.Column<string>(type: "text", nullable: false),
                    policy_version = table.Column<string>(type: "text", nullable: false),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_materiality_rounding_decisions", x => x.id);
                    table.CheckConstraint("ck_materiality_rounding_values", "length(rationale) > 0 AND policy_version = 'STE-MATERIALITY-2026.2' AND computed_planning_materiality > 0 AND computed_tolerable_error > 0 AND computed_sad_threshold > 0 AND adjusted_sad_threshold > 0 AND adjusted_tolerable_error > 0 AND adjusted_planning_materiality > 0 AND adjusted_sad_threshold <= adjusted_tolerable_error AND adjusted_tolerable_error <= adjusted_planning_materiality AND abs(adjusted_planning_materiality - computed_planning_materiality) * 100 <= 5 * computed_planning_materiality AND abs(adjusted_tolerable_error - computed_tolerable_error) * 100 <= 5 * computed_tolerable_error AND abs(adjusted_sad_threshold - computed_sad_threshold) * 100 <= 5 * computed_sad_threshold");
                    table.ForeignKey(
                        name: "FK_materiality_rounding_decisions_materiality_assessments_effe~",
                        column: x => x.effective_assessment_id,
                        principalTable: "materiality_assessments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_materiality_rounding_decisions_materiality_assessments_sour~",
                        column: x => x.source_assessment_id,
                        principalTable: "materiality_assessments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_materiality_rounding_decisions_materiality_calculations_mat~",
                        column: x => x.materiality_calculation_id,
                        principalTable: "materiality_calculations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_materiality_rounding_decisions_effective_assessment_id",
                table: "materiality_rounding_decisions",
                column: "effective_assessment_id");

            migrationBuilder.CreateIndex(
                name: "IX_materiality_rounding_decisions_firm_id_effective_assessment~",
                table: "materiality_rounding_decisions",
                columns: new[] { "firm_id", "effective_assessment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_materiality_rounding_decisions_firm_id_materiality_calculat~",
                table: "materiality_rounding_decisions",
                columns: new[] { "firm_id", "materiality_calculation_id", "decided_at" });

            migrationBuilder.CreateIndex(
                name: "IX_materiality_rounding_decisions_materiality_calculation_id",
                table: "materiality_rounding_decisions",
                column: "materiality_calculation_id");

            migrationBuilder.CreateIndex(
                name: "IX_materiality_rounding_decisions_source_assessment_id",
                table: "materiality_rounding_decisions",
                column: "source_assessment_id");

            // Practical rounding decisions are append-only evidence: the computed figures and the adjusted figures they
            // authorised cannot be edited or removed after the fact.
            migrationBuilder.Sql("""
                CREATE FUNCTION prevent_materiality_rounding_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'Materiality rounding decisions are immutable evidence.' USING ERRCODE = '55000';
                END $$;
                CREATE TRIGGER trg_materiality_rounding_decisions_immutable
                BEFORE UPDATE OR DELETE ON materiality_rounding_decisions
                FOR EACH ROW EXECUTE FUNCTION prevent_materiality_rounding_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_materiality_rounding_decisions_immutable ON materiality_rounding_decisions; DROP FUNCTION IF EXISTS prevent_materiality_rounding_mutation();");

            migrationBuilder.DropTable(
                name: "materiality_rounding_decisions");
        }
    }
}
