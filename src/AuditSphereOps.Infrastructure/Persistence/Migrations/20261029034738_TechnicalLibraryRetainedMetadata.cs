using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TechnicalLibraryRetainedMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION protect_published_library_versions() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'DELETE' AND OLD.status <> 'DRAFT' THEN RAISE EXCEPTION 'published library versions are retained'; END IF;
                  IF TG_OP = 'UPDATE' AND OLD.status <> 'DRAFT' AND (
                    (to_jsonb(NEW) - 'status') IS DISTINCT FROM (to_jsonb(OLD) - 'status')
                    OR NOT (NEW.status = OLD.status OR (OLD.status = 'PUBLISHED' AND NEW.status = 'SUPERSEDED'))
                  ) THEN
                    RAISE EXCEPTION 'a published library version is immutable';
                  END IF;
                  RETURN COALESCE(NEW, OLD);
                END;
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM technical_library_versions WHERE status <> 'DRAFT') THEN
                    RAISE EXCEPTION 'cannot downgrade retained library version protection';
                  END IF;
                END;
                $$;
                CREATE OR REPLACE FUNCTION protect_published_library_versions() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'DELETE' AND OLD.status <> 'DRAFT' THEN RAISE EXCEPTION 'published library versions are retained'; END IF;
                  IF TG_OP = 'UPDATE' AND OLD.status <> 'DRAFT' AND (NEW.body IS DISTINCT FROM OLD.body OR NEW.content_sha256 IS DISTINCT FROM OLD.content_sha256
                     OR NEW.source_reference IS DISTINCT FROM OLD.source_reference OR (OLD.status = 'SUPERSEDED' AND NEW.status <> 'SUPERSEDED')) THEN
                    RAISE EXCEPTION 'a published library version is immutable';
                  END IF;
                  RETURN COALESCE(NEW, OLD);
                END;
                $$;
                """);
        }
    }
}
