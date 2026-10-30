using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReviewNoteParentScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_procedure_review_note_firm_id",
                table: "procedure_review_notes",
                columns: new[] { "firm_id", "id" });

            // Reuse the existing full-scope result key. Historical result identities and
            // note revision/excerpt anchors remain unchanged.
            migrationBuilder.AddForeignKey(
                name: "fk_procedure_review_note_result_scope",
                table: "procedure_review_notes",
                columns: new[] { "firm_id", "client_id", "engagement_id", "result_id" },
                principalTable: "audit_procedure_results",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_procedure_review_event_note_scope",
                table: "procedure_review_note_events",
                columns: new[] { "firm_id", "note_id" },
                principalTable: "procedure_review_notes",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_procedure_review_event_note_scope",
                table: "procedure_review_note_events");
            migrationBuilder.DropForeignKey(
                name: "fk_procedure_review_note_result_scope",
                table: "procedure_review_notes");
            migrationBuilder.DropUniqueConstraint(
                name: "ak_procedure_review_note_firm_id",
                table: "procedure_review_notes");
        }
    }
}
