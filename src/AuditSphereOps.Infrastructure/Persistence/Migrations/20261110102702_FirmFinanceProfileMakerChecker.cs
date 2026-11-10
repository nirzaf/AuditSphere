using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FirmFinanceProfileMakerChecker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "approved_draft_basis_sha256",
                table: "firm_finance_profiles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "approved_draft_revision",
                table: "firm_finance_profiles",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "created_by_epoch",
                table: "firm_finance_profiles",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_user_id",
                table: "firm_finance_profiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "draft_basis_sha256",
                table: "firm_finance_profiles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "draft_revision",
                table: "firm_finance_profiles",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_firm_finance_profiles_firm_id_created_by_user_id",
                table: "firm_finance_profiles",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_finance_profile_review_basis",
                table: "firm_finance_profiles",
                sql: "(created_by_user_id IS NULL AND created_by_epoch IS NULL AND draft_revision IS NULL AND draft_basis_sha256 IS NULL AND approved_draft_revision IS NULL AND approved_draft_basis_sha256 IS NULL) OR (created_by_user_id IS NOT NULL AND created_by_epoch IS NOT NULL AND created_by_epoch >= 0 AND draft_revision IS NOT NULL AND draft_revision >= 1 AND draft_basis_sha256 IS NOT NULL AND draft_basis_sha256 ~ '^[0-9a-f]{64}$' AND ((approved = false AND approved_draft_revision IS NULL AND approved_draft_basis_sha256 IS NULL) OR (approved = true AND approved_draft_revision IS NOT NULL AND approved_draft_revision = draft_revision AND approved_draft_basis_sha256 IS NOT NULL AND approved_draft_basis_sha256 = draft_basis_sha256 AND approved_by_user_id IS NOT NULL AND approved_by_user_id <> created_by_user_id)))");

            migrationBuilder.AddForeignKey(
                name: "FK_firm_finance_profiles_users_firm_id_created_by_user_id",
                table: "firm_finance_profiles",
                columns: new[] { "firm_id", "created_by_user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE FUNCTION prevent_firm_finance_profile_mutation() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                  IF TG_OP = 'INSERT' THEN
                    IF NEW.created_by_user_id IS NOT NULL AND (NEW.created_by_epoch IS NULL
                      OR NEW.draft_revision IS NULL OR NEW.draft_basis_sha256 IS NULL OR NEW.approved) THEN
                      RAISE EXCEPTION 'Firm finance profiles must enter through a maker-owned unapproved draft.';
                    END IF;
                    RETURN NEW;
                  END IF;
                  IF TG_OP = 'TRUNCATE' THEN
                    RAISE EXCEPTION 'Firm finance profile evidence cannot be truncated.';
                  END IF;
                  IF TG_OP = 'DELETE' THEN
                    RAISE EXCEPTION 'Firm finance profile evidence cannot be deleted.';
                  END IF;
                  IF OLD.created_by_user_id IS NULL OR OLD.approved THEN
                    RAISE EXCEPTION 'Legacy and approved firm finance profiles are immutable.';
                  END IF;
                  IF NEW.approved IS DISTINCT FROM true
                    OR NEW.approved_by_user_id IS NULL OR NEW.approved_at IS NULL
                    OR NEW.approved_draft_revision IS DISTINCT FROM OLD.draft_revision
                    OR NEW.approved_draft_basis_sha256 IS DISTINCT FROM OLD.draft_basis_sha256
                    OR NEW.id IS DISTINCT FROM OLD.id
                    OR NEW.firm_id IS DISTINCT FROM OLD.firm_id
                    OR NEW.functional_currency IS DISTINCT FROM OLD.functional_currency
                    OR NEW.profile_kind IS DISTINCT FROM OLD.profile_kind
                    OR NEW.created_at IS DISTINCT FROM OLD.created_at
                    OR NEW.created_by_user_id IS DISTINCT FROM OLD.created_by_user_id
                    OR NEW.created_by_epoch IS DISTINCT FROM OLD.created_by_epoch
                    OR NEW.draft_revision IS DISTINCT FROM OLD.draft_revision
                    OR NEW.draft_basis_sha256 IS DISTINCT FROM OLD.draft_basis_sha256 THEN
                    RAISE EXCEPTION 'A finance profile draft permits only one unchanged-basis approval transition.';
                  END IF;
                  RETURN NEW;
                END $fn$;
                CREATE TRIGGER trg_firm_finance_profiles_immutable
                  BEFORE INSERT OR UPDATE OR DELETE ON firm_finance_profiles
                  FOR EACH ROW EXECUTE FUNCTION prevent_firm_finance_profile_mutation();
                CREATE TRIGGER trg_firm_finance_profiles_no_truncate
                  BEFORE TRUNCATE ON firm_finance_profiles
                  FOR EACH STATEMENT EXECUTE FUNCTION prevent_firm_finance_profile_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE firm_finance_profiles IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM firm_finance_profiles WHERE created_by_user_id IS NOT NULL
                    OR created_by_epoch IS NOT NULL OR draft_revision IS NOT NULL OR draft_basis_sha256 IS NOT NULL
                    OR approved_draft_revision IS NOT NULL OR approved_draft_basis_sha256 IS NOT NULL) THEN
                    RAISE EXCEPTION 'Finance profile maker/checker downgrade would discard profile approval evidence.';
                  END IF;
                END $$;
                DROP TRIGGER trg_firm_finance_profiles_immutable ON firm_finance_profiles;
                DROP TRIGGER trg_firm_finance_profiles_no_truncate ON firm_finance_profiles;
                DROP FUNCTION prevent_firm_finance_profile_mutation();
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_firm_finance_profiles_users_firm_id_created_by_user_id",
                table: "firm_finance_profiles");

            migrationBuilder.DropIndex(
                name: "IX_firm_finance_profiles_firm_id_created_by_user_id",
                table: "firm_finance_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_finance_profile_review_basis",
                table: "firm_finance_profiles");

            migrationBuilder.DropColumn(
                name: "approved_draft_basis_sha256",
                table: "firm_finance_profiles");

            migrationBuilder.DropColumn(
                name: "approved_draft_revision",
                table: "firm_finance_profiles");

            migrationBuilder.DropColumn(
                name: "created_by_epoch",
                table: "firm_finance_profiles");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                table: "firm_finance_profiles");

            migrationBuilder.DropColumn(
                name: "draft_basis_sha256",
                table: "firm_finance_profiles");

            migrationBuilder.DropColumn(
                name: "draft_revision",
                table: "firm_finance_profiles");

        }
    }
}
