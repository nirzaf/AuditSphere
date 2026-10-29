using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PhysicalEvidenceMovementFirmScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_physical_evidence_movements_physical_evidence_items_physica~",
                table: "physical_evidence_movements");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_physical_evidence_items_firm_id_id",
                table: "physical_evidence_items",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.AddForeignKey(
                name: "fk_physical_evidence_movement_item_scope",
                table: "physical_evidence_movements",
                columns: new[] { "firm_id", "physical_evidence_item_id" },
                principalTable: "physical_evidence_items",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_physical_evidence_movement_item_scope",
                table: "physical_evidence_movements");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_physical_evidence_items_firm_id_id",
                table: "physical_evidence_items");

            migrationBuilder.AddForeignKey(
                name: "FK_physical_evidence_movements_physical_evidence_items_physica~",
                table: "physical_evidence_movements",
                column: "physical_evidence_item_id",
                principalTable: "physical_evidence_items",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
