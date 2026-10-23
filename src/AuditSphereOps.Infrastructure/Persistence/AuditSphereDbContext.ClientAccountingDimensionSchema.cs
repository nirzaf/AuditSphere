// Versioned client accounting dimension schema persistence; legacy flat definitions remain unchanged.
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Practice;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureClientAccountingDimensionSchema(ModelBuilder b)
  {
    var revision = b.Entity<ClientAccountingDimensionSchemaRevisionRecord>();
    revision.ToTable("client_accounting_dimension_schema_revisions", t =>
    {
      t.HasCheckConstraint("ck_client_dimension_schema_revision_values",
        "revision > 0 AND status IN ('DRAFT','SUBMITTED','APPROVED')" +
        " AND canonical_digest_version > 0 AND canonical_digest ~ '^[0-9a-f]{64}$'" +
        " AND ((revision = 1 AND base_revision_id IS NULL) OR (revision > 1 AND base_revision_id IS NOT NULL))" +
        " AND ((status = 'DRAFT' AND validated_at IS NULL AND published_by_user_id IS NULL AND published_at IS NULL)" +
        " OR (status = 'SUBMITTED' AND validated_at IS NOT NULL AND published_by_user_id IS NULL AND published_at IS NULL)" +
        " OR (status = 'APPROVED' AND validated_at IS NOT NULL AND published_by_user_id IS NOT NULL" +
        " AND published_at IS NOT NULL AND published_by_user_id <> created_by_user_id AND impact_acknowledged))");
    });
    revision.HasKey(x => x.Id);
    revision.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.Id })
      .HasName("ak_client_dim_schema_revision_scope_id");
    revision.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.SchemaId, x.Id })
      .HasName("ak_client_dim_schema_revision_lineage_id");
    revision.Property(x => x.Status).HasMaxLength(30);
    revision.Property(x => x.CanonicalDigest).HasMaxLength(64);
    // The source DTO is client-scoped and carries an optional predecessor, so one lineage per
    // client is the smallest consistent owner model; Revision 1 is the database creation fence.
    revision.HasIndex(x => new { x.FirmId, x.ClientId, x.Revision }).IsUnique()
      .HasDatabaseName("ux_client_dim_schema_owner_revision");
    revision.HasIndex(x => new { x.FirmId, x.ClientId, x.SchemaId, x.BaseRevisionId })
      .HasDatabaseName("ix_client_dim_schema_base");
    revision.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict)
      .HasConstraintName("fk_client_dim_schema_client");
    revision.HasOne<ClientAccountingDimensionSchemaRevisionRecord>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.SchemaId, x.BaseRevisionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.SchemaId, x.Id }).OnDelete(DeleteBehavior.Restrict)
      .HasConstraintName("fk_client_dim_schema_base");

    var definition = b.Entity<ClientAccountingDimensionDefinitionRevisionRecord>();
    definition.ToTable("client_accounting_dimension_definition_revisions", t =>
      t.HasCheckConstraint("ck_client_dim_definition_revision_values",
        "dimension_type IN ('BRANCH','COST_CENTRE','DEPARTMENT','PROJECT','INTERCOMPANY_COUNTERPARTY')" +
        " AND length(trim(code)) > 0 AND length(trim(code_key)) > 0 AND length(trim(name)) > 0"));
    definition.HasKey(x => new { x.SchemaRevisionId, x.Id });
    definition.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.SchemaRevisionId, x.Id })
      .HasName("ak_client_dim_definition_revision_scope_id");
    definition.Property(x => x.DimensionType).HasMaxLength(40);
    definition.Property(x => x.Code).HasMaxLength(ClientAccountingDimensionSchemaRevision.MaximumCodeLength);
    definition.Property(x => x.CodeKey).HasMaxLength(ClientAccountingDimensionSchemaRevision.MaximumCodeLength);
    definition.Property(x => x.Name).HasMaxLength(ClientAccountingDimensionSchemaRevision.MaximumNameLength);
    definition.HasIndex(x => new { x.FirmId, x.ClientId, x.SchemaRevisionId, x.DimensionType }).IsUnique()
      .HasDatabaseName("ux_client_dim_definition_revision_type");
    definition.HasIndex(x => new { x.FirmId, x.ClientId, x.SchemaRevisionId, x.DimensionType, x.CodeKey }).IsUnique()
      .HasDatabaseName("ux_client_dim_definition_revision_code");
    definition.HasOne(x => x.SchemaRevision).WithMany(x => x.Definitions)
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.SchemaRevisionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict)
      .HasConstraintName("fk_client_dim_definition_revision");

    var value = b.Entity<ClientAccountingDimensionValueRevisionRecord>();
    value.ToTable("client_accounting_dimension_value_revisions", t =>
      t.HasCheckConstraint("ck_client_dim_value_revision_values",
        "length(trim(code)) > 0 AND length(trim(code_key)) > 0 AND length(trim(name)) > 0" +
        " AND (effective_to IS NULL OR effective_to >= effective_from)"));
    value.HasKey(x => new { x.SchemaRevisionId, x.Id });
    value.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.SchemaRevisionId, x.Id })
      .HasName("ak_client_dim_value_revision_scope_id");
    value.Property(x => x.Code).HasMaxLength(ClientAccountingDimensionSchemaRevision.MaximumCodeLength);
    value.Property(x => x.CodeKey).HasMaxLength(ClientAccountingDimensionSchemaRevision.MaximumCodeLength);
    value.Property(x => x.Name).HasMaxLength(ClientAccountingDimensionSchemaRevision.MaximumNameLength);
    value.HasIndex(x => new { x.FirmId, x.ClientId, x.SchemaRevisionId, x.DefinitionId, x.CodeKey }).IsUnique()
      .HasDatabaseName("ux_client_dim_value_revision_code");
    value.HasOne(x => x.Definition).WithMany(x => x.Values)
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.SchemaRevisionId, x.DefinitionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.SchemaRevisionId, x.Id }).OnDelete(DeleteBehavior.Restrict)
      .HasConstraintName("fk_client_dim_value_definition");
  }
}

/*
Required migration enforcement, in addition to the EF constraints above:

- A revision's stable SchemaId/DefinitionId/ValueId may recur only in later revisions of the same
  owner and lineage. The owner/revision indexes reject duplicate successors; child key/indexes reject
  duplicate IDs, types, normalized definition codes, and normalized value codes in one snapshot.
- Use a client-row lock for schema creation/successor allocation and a schema-revision row lock for
  validation/publication. The database unique revision index is the final concurrent-writer fence.
- Add PostgreSQL guards that allow child edits only while the owning revision is DRAFT; allow root
  content/digest edits only while DRAFT; allow DRAFT -> SUBMITTED after validation; allow only the
  publication metadata change for SUBMITTED -> APPROVED; reject updates/deletes to APPROVED rows.
  Keep revision and child FKs restrictive. No default rows, legacy backfill, or context pins.
*/
