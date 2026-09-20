using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkpaperDraftsAndTrialBalanceSealing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "import_state",
                table: "trial_balance_datasets",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "SEALED");

            migrationBuilder.CreateTable(
                name: "workpaper_drafts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workpaper_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    base_workpaper_revision = table.Column<long>(type: "bigint", nullable: false),
                    base_input_generation = table.Column<long>(type: "bigint", nullable: false),
                    base_policy_generation = table.Column<long>(type: "bigint", nullable: false),
                    draft_revision = table.Column<long>(type: "bigint", nullable: false),
                    work_performed = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    conclusion = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    last_save_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_saved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lifecycle = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workpaper_drafts", x => x.id);
                    table.UniqueConstraint("AK_workpaper_drafts_firm_id_id", x => new { x.firm_id, x.id });
                    table.UniqueConstraint("AK_workpaper_drafts_scope_owner", x => new { x.firm_id, x.client_id, x.engagement_id, x.workpaper_id, x.owner_user_id });
                    table.CheckConstraint("ck_workpaper_draft_values", "base_workpaper_revision > 0 AND base_input_generation > 0 AND base_policy_generation > 0 AND draft_revision > 0 AND length(work_performed) <= 100000 AND length(conclusion) <= 20000 AND lifecycle IN ('ACTIVE','CONSUMED','DISCARDED')");
                    table.ForeignKey(
                        name: "FK_workpaper_drafts_engagements_firm_id_client_id_engagement_id",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workpaper_drafts_users_firm_id_owner_user_id",
                        columns: x => new { x.firm_id, x.owner_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workpaper_drafts_workpapers_firm_id_client_id_engagement_id~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.workpaper_id },
                        principalTable: "workpapers",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_tb_import_state",
                table: "trial_balance_datasets",
                sql: "import_state IN ('LOADING', 'SEALED')");

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION prevent_sealed_trial_balance_dataset_reopen() RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF OLD.import_state = 'SEALED' AND NEW.import_state <> 'SEALED' THEN
                        RAISE EXCEPTION 'A sealed trial-balance dataset cannot be reopened.'
                            USING ERRCODE = '55000';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER trg_trial_balance_dataset_sealed
                BEFORE UPDATE OF import_state ON trial_balance_datasets
                FOR EACH ROW EXECUTE FUNCTION prevent_sealed_trial_balance_dataset_reopen();
                """);

            // The parent row is the serialization point for both writers and sealers. A
            // row-level trigger alone is not enough if a writer and the seal UPDATE race
            // under MVCC; FOR UPDATE makes the trigger observe the committed lifecycle state.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION prevent_sealed_trial_balance_row_mutation() RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    current_state text;
                BEGIN
                    IF TG_OP <> 'INSERT' THEN
                        SELECT import_state INTO current_state
                        FROM trial_balance_datasets
                        WHERE id = OLD.dataset_id
                        FOR UPDATE;
                        IF current_state = 'SEALED' THEN
                            RAISE EXCEPTION 'Trial-balance dataset is sealed and its rows are immutable.'
                                USING ERRCODE = '55000';
                        END IF;
                    END IF;

                    IF TG_OP <> 'DELETE' THEN
                        SELECT import_state INTO current_state
                        FROM trial_balance_datasets
                        WHERE id = NEW.dataset_id
                        FOR UPDATE;
                        IF current_state = 'SEALED' THEN
                            RAISE EXCEPTION 'Trial-balance dataset is sealed and its rows are immutable.'
                                USING ERRCODE = '55000';
                        END IF;
                    END IF;

                    IF TG_OP = 'DELETE' THEN
                        RETURN OLD;
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER trg_trial_balance_rows_sealed
                BEFORE INSERT OR UPDATE OR DELETE ON trial_balance_rows
                FOR EACH ROW EXECUTE FUNCTION prevent_sealed_trial_balance_row_mutation();
                """);

            migrationBuilder.CreateIndex(
                name: "IX_workpaper_drafts_firm_id_owner_user_id",
                table: "workpaper_drafts",
                columns: new[] { "firm_id", "owner_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_workpaper_drafts_owner",
                table: "workpaper_drafts",
                columns: new[] { "firm_id", "workpaper_id", "owner_user_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_trial_balance_rows_sealed ON trial_balance_rows;
                DROP FUNCTION IF EXISTS prevent_sealed_trial_balance_row_mutation();
                DROP TRIGGER IF EXISTS trg_trial_balance_dataset_sealed ON trial_balance_datasets;
                DROP FUNCTION IF EXISTS prevent_sealed_trial_balance_dataset_reopen();
                """);

            migrationBuilder.DropTable(
                name: "workpaper_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tb_import_state",
                table: "trial_balance_datasets");

            migrationBuilder.DropColumn(
                name: "import_state",
                table: "trial_balance_datasets");
        }
    }
}
