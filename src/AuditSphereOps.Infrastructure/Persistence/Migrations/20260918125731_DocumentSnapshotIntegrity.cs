using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DocumentSnapshotIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing document rows predate scope FKs and exact-byte validation.
            // Preserve valid history, but stop rather than inventing scope or hashes.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (
                    SELECT 1
                      FROM document_references r
                      LEFT JOIN practice_clients c
                        ON c.firm_id = r.firm_id AND c.id = r.client_id
                      LEFT JOIN engagements e
                        ON e.firm_id = r.firm_id AND e.practice_client_id = r.client_id AND e.id = r.engagement_id
                     WHERE c.id IS NULL OR e.id IS NULL
                        OR length(trim(r.provider)) = 0 OR length(r.drive_id) = 0
                        OR length(r.item_id) = 0 OR length(r.path) = 0
                        OR length(r.purpose) = 0) THEN
                    RAISE EXCEPTION 'Document snapshot migration found an ambiguous document reference.';
                  END IF;
                  IF EXISTS (
                    SELECT 1
                      FROM document_snapshots s
                      LEFT JOIN document_references r
                        ON r.firm_id = s.firm_id
                       AND r.client_id = s.client_id
                       AND r.engagement_id = s.engagement_id
                       AND r.id = s.document_reference_id
                     WHERE r.id IS NULL OR length(s.drive_id) = 0 OR length(s.item_id) = 0
                        OR length(s.version_id) = 0 OR s.sha256_hex !~ '^[0-9a-f]{64}$'
                        OR s.byte_count < 0 OR length(s.captured_by) = 0) THEN
                    RAISE EXCEPTION 'Document snapshot migration found an ambiguous snapshot hash or scope.';
                  END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "version_id",
                table: "document_snapshots",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "sha256_hex",
                table: "document_snapshots",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "item_id",
                table: "document_snapshots",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "drive_id",
                table: "document_snapshots",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "captured_by",
                table: "document_snapshots",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "purpose",
                table: "document_references",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "provider",
                table: "document_references",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "path",
                table: "document_references",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "item_id",
                table: "document_references",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "drive_id",
                table: "document_references",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_document_references_firm_id_id",
                table: "document_references",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_document_references_scope_id",
                table: "document_references",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_document_snapshots_firm_id_client_id_engagement_id_document~",
                table: "document_snapshots",
                columns: new[] { "firm_id", "client_id", "engagement_id", "document_reference_id" });

            migrationBuilder.CreateIndex(
                name: "ux_document_snapshot_firm_document_version",
                table: "document_snapshots",
                columns: new[] { "firm_id", "document_reference_id", "version_id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_snapshot_values",
                table: "document_snapshots",
                sql: "length(drive_id) > 0 AND length(item_id) > 0 AND length(version_id) > 0 AND sha256_hex ~ '^[0-9a-f]{64}$' AND byte_count >= 0 AND length(captured_by) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_reference_values",
                table: "document_references",
                sql: "length(trim(provider)) > 0 AND length(drive_id) > 0 AND length(item_id) > 0 AND length(path) > 0 AND length(purpose) > 0");

            migrationBuilder.AddForeignKey(
                name: "FK_document_references_engagements_firm_id_client_id_engagemen~",
                table: "document_references",
                columns: new[] { "firm_id", "client_id", "engagement_id" },
                principalTable: "engagements",
                principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_document_references_practice_clients_firm_id_client_id",
                table: "document_references",
                columns: new[] { "firm_id", "client_id" },
                principalTable: "practice_clients",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_document_snapshots_document_references_firm_id_client_id_en~",
                table: "document_snapshots",
                columns: new[] { "firm_id", "client_id", "engagement_id", "document_reference_id" },
                principalTable: "document_references",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION prevent_document_snapshot_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'Document snapshots are append-only.' USING ERRCODE = '55000';
                END;
                $$;
                CREATE TRIGGER trg_document_snapshots_append_only
                  BEFORE UPDATE OR DELETE ON document_snapshots
                  FOR EACH ROW EXECUTE FUNCTION prevent_document_snapshot_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM document_snapshots) THEN
                    RAISE EXCEPTION 'Document snapshot downgrade would discard immutable evidence.';
                  END IF;
                END $$;
                DROP TRIGGER IF EXISTS trg_document_snapshots_append_only ON document_snapshots;
                DROP FUNCTION IF EXISTS prevent_document_snapshot_mutation();
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_document_references_engagements_firm_id_client_id_engagemen~",
                table: "document_references");

            migrationBuilder.DropForeignKey(
                name: "FK_document_references_practice_clients_firm_id_client_id",
                table: "document_references");

            migrationBuilder.DropForeignKey(
                name: "FK_document_snapshots_document_references_firm_id_client_id_en~",
                table: "document_snapshots");

            migrationBuilder.DropIndex(
                name: "IX_document_snapshots_firm_id_client_id_engagement_id_document~",
                table: "document_snapshots");

            migrationBuilder.DropIndex(
                name: "ux_document_snapshot_firm_document_version",
                table: "document_snapshots");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_snapshot_values",
                table: "document_snapshots");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_document_references_firm_id_id",
                table: "document_references");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_document_references_scope_id",
                table: "document_references");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_reference_values",
                table: "document_references");

            migrationBuilder.AlterColumn<string>(
                name: "version_id",
                table: "document_snapshots",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "sha256_hex",
                table: "document_snapshots",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "item_id",
                table: "document_snapshots",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "drive_id",
                table: "document_snapshots",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "captured_by",
                table: "document_snapshots",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "purpose",
                table: "document_references",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "provider",
                table: "document_references",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "path",
                table: "document_references",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AlterColumn<string>(
                name: "item_id",
                table: "document_references",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "drive_id",
                table: "document_references",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);
        }
    }
}
