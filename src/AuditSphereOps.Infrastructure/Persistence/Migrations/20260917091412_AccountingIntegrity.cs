using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountingIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_practice_clients_firm_id_id",
                table: "practice_clients",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_engagements_firm_id_id",
                table: "engagements",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_trial_balance_datasets_firm_id_client_id",
                table: "trial_balance_datasets",
                columns: new[] { "firm_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "IX_trial_balance_datasets_firm_id_engagement_id",
                table: "trial_balance_datasets",
                columns: new[] { "firm_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "IX_adjustment_lines_journal_id",
                table: "adjustment_lines",
                column: "journal_id");

            migrationBuilder.CreateIndex(
                name: "IX_adjustment_journals_base_dataset_id",
                table: "adjustment_journals",
                column: "base_dataset_id");

            migrationBuilder.AddForeignKey(
                name: "FK_adjustment_journals_engagements_firm_id_engagement_id",
                table: "adjustment_journals",
                columns: new[] { "firm_id", "engagement_id" },
                principalTable: "engagements",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_adjustment_journals_trial_balance_datasets_base_dataset_id",
                table: "adjustment_journals",
                column: "base_dataset_id",
                principalTable: "trial_balance_datasets",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_adjustment_lines_adjustment_journals_journal_id",
                table: "adjustment_lines",
                column: "journal_id",
                principalTable: "adjustment_journals",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_trial_balance_datasets_engagements_firm_id_engagement_id",
                table: "trial_balance_datasets",
                columns: new[] { "firm_id", "engagement_id" },
                principalTable: "engagements",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_trial_balance_datasets_practice_clients_firm_id_client_id",
                table: "trial_balance_datasets",
                columns: new[] { "firm_id", "client_id" },
                principalTable: "practice_clients",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_trial_balance_rows_trial_balance_datasets_dataset_id",
                table: "trial_balance_rows",
                column: "dataset_id",
                principalTable: "trial_balance_datasets",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // ND-03 immutability: raw TB rows are append-only — correction means a new
            // dataset, never an edit. (AT-07: the original is preserved.)
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION enforce_tb_row_append_only() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'trial_balance_rows is append-only; % is not permitted (import a new dataset instead)', TG_OP;
                END;
                $$ LANGUAGE plpgsql;

                DROP TRIGGER IF EXISTS trg_tb_rows_append_only ON trial_balance_rows;
                CREATE TRIGGER trg_tb_rows_append_only
                    BEFORE UPDATE OR DELETE ON trial_balance_rows
                    FOR EACH ROW EXECUTE FUNCTION enforce_tb_row_append_only();
                """);

            // ND-03 immutability: journal lines freeze once the journal leaves Draft.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION enforce_adjustment_line_frozen() RETURNS trigger AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        IF EXISTS (SELECT 1 FROM adjustment_journals j WHERE j.id = OLD.journal_id AND j.status <> 'Draft') THEN
                            RAISE EXCEPTION 'adjustment journal % is no longer Draft; lines are frozen', OLD.journal_id;
                        END IF;
                        RETURN OLD;
                    END IF;
                    IF EXISTS (SELECT 1 FROM adjustment_journals j WHERE j.id = NEW.journal_id AND j.status <> 'Draft') THEN
                        RAISE EXCEPTION 'adjustment journal % is no longer Draft; lines are frozen', NEW.journal_id;
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                DROP TRIGGER IF EXISTS trg_adjustment_lines_frozen ON adjustment_lines;
                CREATE TRIGGER trg_adjustment_lines_frozen
                    BEFORE INSERT OR UPDATE OR DELETE ON adjustment_lines
                    FOR EACH ROW EXECUTE FUNCTION enforce_adjustment_line_frozen();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_adjustment_lines_frozen ON adjustment_lines;
                DROP FUNCTION IF EXISTS enforce_adjustment_line_frozen();
                DROP TRIGGER IF EXISTS trg_tb_rows_append_only ON trial_balance_rows;
                DROP FUNCTION IF EXISTS enforce_tb_row_append_only();
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_adjustment_journals_engagements_firm_id_engagement_id",
                table: "adjustment_journals");

            migrationBuilder.DropForeignKey(
                name: "FK_adjustment_journals_trial_balance_datasets_base_dataset_id",
                table: "adjustment_journals");

            migrationBuilder.DropForeignKey(
                name: "FK_adjustment_lines_adjustment_journals_journal_id",
                table: "adjustment_lines");

            migrationBuilder.DropForeignKey(
                name: "FK_trial_balance_datasets_engagements_firm_id_engagement_id",
                table: "trial_balance_datasets");

            migrationBuilder.DropForeignKey(
                name: "FK_trial_balance_datasets_practice_clients_firm_id_client_id",
                table: "trial_balance_datasets");

            migrationBuilder.DropForeignKey(
                name: "FK_trial_balance_rows_trial_balance_datasets_dataset_id",
                table: "trial_balance_rows");

            migrationBuilder.DropIndex(
                name: "IX_trial_balance_datasets_firm_id_client_id",
                table: "trial_balance_datasets");

            migrationBuilder.DropIndex(
                name: "IX_trial_balance_datasets_firm_id_engagement_id",
                table: "trial_balance_datasets");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_practice_clients_firm_id_id",
                table: "practice_clients");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_engagements_firm_id_id",
                table: "engagements");

            migrationBuilder.DropIndex(
                name: "IX_adjustment_lines_journal_id",
                table: "adjustment_lines");

            migrationBuilder.DropIndex(
                name: "IX_adjustment_journals_base_dataset_id",
                table: "adjustment_journals");
        }
    }
}
