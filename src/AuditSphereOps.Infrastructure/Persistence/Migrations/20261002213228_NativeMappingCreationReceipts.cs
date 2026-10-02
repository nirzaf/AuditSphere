using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NativeMappingCreationReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_mapping_versions_firm_id_created_by_user_id",
                table: "mapping_versions");

            migrationBuilder.AddColumn<Guid>(
                name: "base_mapping_version_id",
                table: "mapping_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "creation_request_hash",
                table: "mapping_versions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "creation_request_id",
                table: "mapping_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "creation_review_revision",
                table: "mapping_versions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_mapping_versions_firm_id_client_id_engagement_id_base_mappi~",
                table: "mapping_versions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "base_mapping_version_id" });

            migrationBuilder.CreateIndex(
                name: "ux_mapping_creation_request",
                table: "mapping_versions",
                columns: new[] { "firm_id", "created_by_user_id", "creation_request_id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_mapping_creation_receipt",
                table: "mapping_versions",
                sql: "(base_mapping_version_id IS NULL AND creation_request_id IS NULL AND creation_request_hash IS NULL AND creation_review_revision IS NULL) OR (base_mapping_version_id IS NOT NULL AND base_mapping_version_id <> id AND creation_request_id IS NOT NULL AND creation_request_hash IS NOT NULL AND creation_request_hash ~ '^[a-f0-9]{64}$' AND creation_review_revision IS NOT NULL AND creation_review_revision ~ '^[a-f0-9]{64}$')");

            migrationBuilder.AddForeignKey(
                name: "FK_mapping_versions_mapping_versions_firm_id_client_id_engagem~",
                table: "mapping_versions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "base_mapping_version_id" },
                principalTable: "mapping_versions",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                CREATE FUNCTION protect_native_mapping_receipt() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'DELETE' THEN
                    IF OLD.creation_request_id IS NOT NULL THEN
                      RAISE EXCEPTION 'Native mapping creation evidence is immutable';
                    END IF;
                    RETURN OLD;
                  END IF;
                  IF OLD.creation_request_id IS NULL THEN
                    IF NEW.creation_request_id IS NOT NULL THEN
                      RAISE EXCEPTION 'A legacy mapping cannot acquire fabricated creation evidence';
                    END IF;
                  ELSIF NEW IS DISTINCT FROM OLD THEN
                    IF NOT (OLD.status = 'DRAFT' AND NEW.status = 'APPROVED'
                      AND NEW.generation = OLD.generation + 1
                      AND NEW.approved_by_user_id IS NOT NULL
                      AND NEW.approved_by_user_id <> OLD.created_by_user_id
                      AND NEW.approved_at IS NOT NULL
                      AND (to_jsonb(NEW) - ARRAY['status','generation','approved_by_user_id','approved_at'])
                        = (to_jsonb(OLD) - ARRAY['status','generation','approved_by_user_id','approved_at'])) THEN
                      RAISE EXCEPTION 'Native mapping creation evidence is immutable';
                    END IF;
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER trg_native_mapping_receipt_immutable
                  BEFORE UPDATE OR DELETE ON mapping_versions
                  FOR EACH ROW EXECUTE FUNCTION protect_native_mapping_receipt();
                CREATE FUNCTION protect_native_mapping_allocations() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE native boolean; parent_xid xid; parent_status text;
                BEGIN
                  IF TG_OP <> 'INSERT' THEN
                    SELECT creation_request_id IS NOT NULL INTO native FROM mapping_versions
                      WHERE id = OLD.mapping_version_id AND firm_id = OLD.firm_id;
                    IF native THEN RAISE EXCEPTION 'Native mapping allocations are immutable'; END IF;
                  END IF;
                  IF TG_OP <> 'DELETE' THEN
                    SELECT creation_request_id IS NOT NULL, xmin, status INTO native, parent_xid, parent_status FROM mapping_versions
                      WHERE id = NEW.mapping_version_id AND firm_id = NEW.firm_id;
                    IF native AND (TG_OP <> 'INSERT' OR parent_status <> 'DRAFT' OR parent_xid <> pg_current_xact_id()::xid) THEN
                      RAISE EXCEPTION 'Native mapping allocations must be retained in their creation transaction';
                    END IF;
                    RETURN NEW;
                  END IF;
                  RETURN OLD;
                END $$;
                CREATE TRIGGER trg_native_mapping_allocations_immutable
                  BEFORE INSERT OR UPDATE OR DELETE ON mapping_allocations
                  FOR EACH ROW EXECUTE FUNCTION protect_native_mapping_allocations();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM mapping_versions WHERE creation_request_id IS NOT NULL) THEN
                    RAISE EXCEPTION 'Cannot remove retained native mapping creation evidence';
                  END IF;
                END $$;
                DROP TRIGGER trg_native_mapping_receipt_immutable ON mapping_versions;
                DROP FUNCTION protect_native_mapping_receipt();
                DROP TRIGGER trg_native_mapping_allocations_immutable ON mapping_allocations;
                DROP FUNCTION protect_native_mapping_allocations();
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_mapping_versions_mapping_versions_firm_id_client_id_engagem~",
                table: "mapping_versions");

            migrationBuilder.DropIndex(
                name: "IX_mapping_versions_firm_id_client_id_engagement_id_base_mappi~",
                table: "mapping_versions");

            migrationBuilder.DropIndex(
                name: "ux_mapping_creation_request",
                table: "mapping_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_mapping_creation_receipt",
                table: "mapping_versions");

            migrationBuilder.DropColumn(
                name: "base_mapping_version_id",
                table: "mapping_versions");

            migrationBuilder.DropColumn(
                name: "creation_request_hash",
                table: "mapping_versions");

            migrationBuilder.DropColumn(
                name: "creation_request_id",
                table: "mapping_versions");

            migrationBuilder.DropColumn(
                name: "creation_review_revision",
                table: "mapping_versions");

            migrationBuilder.CreateIndex(
                name: "IX_mapping_versions_firm_id_created_by_user_id",
                table: "mapping_versions",
                columns: new[] { "firm_id", "created_by_user_id" });
        }
    }
}
