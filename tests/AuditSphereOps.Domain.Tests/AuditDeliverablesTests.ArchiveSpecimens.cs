using System.Text.Json;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using System.Text;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class AuditDeliverablesTests
{
  private sealed record ArchiveReceiptProbeScope(Guid FirmId, Guid ClientId, Guid EngagementId, Guid OperationId);

  private static async Task AssertArchiveReceiptParentScopeAsync(PgTestSchema pg, ArchiveReceiptProbeScope own,
    ArchiveReceiptProbeScope foreign, IReadOnlyDictionary<string, Guid> parentIds)
  {
      var accepted = new List<string>();
      await using (var connection = new NpgsqlConnection(pg.ConnectionString))
      {
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        foreach (var table in new[] { "archive_copy_file_receipts", "archive_copy_permission_observations" })
        {
          async Task<int> InsertAsync(Guid firm, Guid client, Guid engagement, Guid operation)
          {
            await using var command = new NpgsqlCommand($"""
              INSERT INTO {table}
              SELECT (jsonb_populate_record(NULL::{table}, to_jsonb(r) || jsonb_build_object(
                'id', @id, 'firm_id', @firm, 'client_id', @client, 'engagement_id', @engagement,
                'operation_id', @operation, 'attempt_token', r.attempt_token + 1))).*
              FROM {table} r WHERE r.id = (SELECT id FROM {table} WHERE operation_id = @source ORDER BY id LIMIT 1)
              """, connection, tx);
            command.Parameters.AddWithValue("id", Guid.CreateVersion7());
            command.Parameters.AddWithValue("firm", firm);
            command.Parameters.AddWithValue("client", client);
            command.Parameters.AddWithValue("engagement", engagement);
            command.Parameters.AddWithValue("operation", operation);
            command.Parameters.AddWithValue("source", own.OperationId);
            return await command.ExecuteNonQueryAsync();
          }
          await new NpgsqlCommand("SAVEPOINT archive_operation_probe", connection, tx).ExecuteNonQueryAsync();
          // Rollback-only structural clone: attempt+1 avoids the existing unique attempt/source indexes.
          // It is not a real second attempt or a new native receipt; native attempt1 was checked above.
          Assert.Equal(1, await InsertAsync(own.FirmId, own.ClientId, own.EngagementId, own.OperationId));
          await new NpgsqlCommand("ROLLBACK TO SAVEPOINT archive_operation_probe", connection, tx).ExecuteNonQueryAsync();
          // A same-scope PENDING LOCAL parent isolates scope from the other generic parents' kind/state.
          Assert.Equal(1, await InsertAsync(own.FirmId, own.ClientId, own.EngagementId, parentIds["same-scope operation"]));
          await new NpgsqlCommand("ROLLBACK TO SAVEPOINT archive_operation_probe", connection, tx).ExecuteNonQueryAsync();
          foreach (var (label, firm, client, engagement, operation) in new[]
          {
            ("foreign operation", own.FirmId, own.ClientId, own.EngagementId, foreign.OperationId),
            ("same-firm different-client operation", own.FirmId, own.ClientId, own.EngagementId, parentIds["same-firm different-client operation"]),
            ("same-client sibling-engagement operation", own.FirmId, own.ClientId, own.EngagementId, parentIds["same-client sibling-engagement operation"]),
            ("client-only operation", own.FirmId, own.ClientId, own.EngagementId, parentIds["client-only operation"]),
            ("firm-wide operation", own.FirmId, own.ClientId, own.EngagementId, parentIds["firm-wide operation"]),
            ("wrong child firm", foreign.FirmId, own.ClientId, own.EngagementId, own.OperationId),
            ("wrong child client", own.FirmId, foreign.ClientId, own.EngagementId, own.OperationId),
            ("wrong child engagement", own.FirmId, own.ClientId, foreign.EngagementId, own.OperationId)
          })
          {
            try
            {
              Assert.Equal(1, await InsertAsync(firm, client, engagement, operation));
              accepted.Add(table + ":" + label);
            }
            catch (PostgresException error)
            {
              Assert.Equal("23503", error.SqlState);
              if (label.Contains("operation", StringComparison.Ordinal))
                Assert.Equal(table == "archive_copy_file_receipts"
                  ? "fk_archive_copy_file_operation_scope" : "fk_archive_copy_permission_operation_scope", error.ConstraintName);
            }
            await new NpgsqlCommand("ROLLBACK TO SAVEPOINT archive_operation_probe", connection, tx).ExecuteNonQueryAsync();
          }
        }
        await tx.RollbackAsync();
      }
      Assert.True(accepted.Count == 0, "Cross-scope archive-copy operation references accepted: " + string.Join(", ", accepted));

  }
  private static async Task AssertHistoricalArchiveReceiptStructureAsync()
  {
    // Explicitly synthetic historical structural specimens, not current approval or provider evidence.
    // The current-head caller retains the real application copies and full financial/source oracles.
    const string baseline = "20261029104522_SamplingRunSelectionScope";
    const string current = "20261105173658_ArchiveProfileIdentity";
    await using var pg = await PgTestSchema.CreateAsync(current);
    var (own, foreign, parents) = await SeedHistoricalArchiveReceiptStructureAsync(pg);
    var before = await SnapshotHistoricalArchiveReceiptStructureAsync(pg, compatible: false);
    var compatibleBefore = await SnapshotHistoricalArchiveReceiptStructureAsync(pg, compatible: true);
    await AssertArchiveReceiptParentScopeAsync(pg, own, foreign, parents);
    Assert.Equal(before, await SnapshotHistoricalArchiveReceiptStructureAsync(pg, compatible: false));
      await using var migrationDb = new AuditSphereDbContext(pg.Options);
      var migrator = migrationDb.GetService<IMigrator>();
      var applied = (await migrationDb.Database.GetAppliedMigrationsAsync()).ToArray();
      Assert.Equal(current, applied[^1]);
      var baselineIndex = Array.IndexOf(applied, baseline);
      Assert.InRange(baselineIndex, 0, applied.Length - 2);
      Assert.Equal("20261029124914_ArchiveCopyOperationScope", applied[baselineIndex + 1]);
      Assert.True(migrationDb.Model.FindEntityType(typeof(DurableOperation))!.FindProperty(nameof(DurableOperation.ClientId))!.IsNullable);
      Assert.True(migrationDb.Model.FindEntityType(typeof(DurableOperation))!.FindProperty(nameof(DurableOperation.EngagementId))!.IsNullable);
      async Task<string> CatalogAsync(bool newObjects)
      {
        await using var connection = new NpgsqlConnection(pg.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
          SELECT jsonb_build_object(
            'indexes', (SELECT jsonb_agg(jsonb_build_array(indexname,indexdef) ORDER BY indexname)
              FROM pg_indexes WHERE schemaname=current_schema()
                AND tablename IN ('durable_operations','archive_copy_file_receipts','archive_copy_permission_observations')
                AND (indexname='ux_durable_operation_scope_id')=@new),
            'constraints', (SELECT jsonb_agg(jsonb_build_array(conname,pg_get_constraintdef(oid),convalidated,condeferrable) ORDER BY conname)
              FROM pg_constraint WHERE connamespace=current_schema()::regnamespace
                AND conrelid IN ('durable_operations'::regclass,'archive_copy_file_receipts'::regclass,'archive_copy_permission_observations'::regclass)
                AND (conname IN ('fk_archive_copy_file_operation_scope','fk_archive_copy_permission_operation_scope'))=@new))::text
          """, connection);
        command.Parameters.AddWithValue("new", newObjects);
        return (string)(await command.ExecuteScalarAsync())!;
      }
      async Task AssertNewScopeAsync(bool present)
      {
        using var catalog = JsonDocument.Parse(await CatalogAsync(true));
        var constraints = catalog.RootElement.GetProperty("constraints");
        if (!present)
        {
          Assert.Equal(JsonValueKind.Null, constraints.ValueKind);
          Assert.Equal(JsonValueKind.Null, catalog.RootElement.GetProperty("indexes").ValueKind);
          return;
        }
        Assert.Equal(2, constraints.GetArrayLength());
        foreach (var row in constraints.EnumerateArray())
        {
          Assert.True(row[2].GetBoolean());
          Assert.False(row[3].GetBoolean());
          Assert.Contains("FOREIGN KEY (firm_id, client_id, engagement_id, operation_id)", row[1].GetString());
          Assert.Contains("REFERENCES durable_operations(firm_id, client_id, engagement_id, id) ON DELETE RESTRICT", row[1].GetString());
        }
        var index = Assert.Single(catalog.RootElement.GetProperty("indexes").EnumerateArray());
        Assert.Equal("ux_durable_operation_scope_id", index[0].GetString());
        Assert.Contains("CREATE UNIQUE INDEX", index[1].GetString());
        Assert.Contains("(firm_id, client_id, engagement_id, id)", index[1].GetString());
        Assert.DoesNotContain(" WHERE ", index[1].GetString());
        await using var connection = new NpgsqlConnection(pg.ConnectionString);
        await connection.OpenAsync();
        await using var nullable = new NpgsqlCommand("""
          SELECT count(*) FROM information_schema.columns
          WHERE table_schema=current_schema() AND table_name='durable_operations'
            AND column_name IN ('client_id','engagement_id') AND is_nullable='YES'
          """, connection);
        Assert.Equal(2L, (long)(await nullable.ExecuteScalarAsync())!);
      }
      await AssertNewScopeAsync(true);

      // Later additive migrations can be reverted, but the retained payload prevents removing its guard migration.
      const string retainedPayloadGuard = "20261105163636_ArchiveAudienceAndReviewedManifestIntegrity";
      var guardIndex = Array.IndexOf(applied, retainedPayloadGuard);
      Assert.InRange(guardIndex, baselineIndex + 1, applied.Length - 1);
      var expectedAppliedAfterRefusal = applied[..(guardIndex + 1)];
      var refusedDowngrade = await Assert.ThrowsAnyAsync<Exception>(() => migrator.MigrateAsync(baseline));
      var audienceFailure = Assert.IsType<PostgresException>(refusedDowngrade.GetBaseException());
      Assert.Equal("55000", audienceFailure.SqlState);
      Assert.Equal("Cannot remove archive-manifest integrity guards while retained manifests exist.", audienceFailure.MessageText);
      Assert.Equal(expectedAppliedAfterRefusal, (await migrationDb.Database.GetAppliedMigrationsAsync()).ToArray());
      await AssertNewScopeAsync(true);

    Assert.Equal(compatibleBefore, await SnapshotHistoricalArchiveReceiptStructureAsync(pg, compatible: true));
    await migrator.MigrateAsync(current);
    Assert.Equal(applied, (await migrationDb.Database.GetAppliedMigrationsAsync()).ToArray());
    Assert.Equal(before, await SnapshotHistoricalArchiveReceiptStructureAsync(pg, compatible: false));
    await AssertNewScopeAsync(true);

    const string exportGuard = "20261107095608_ArchiveStructuredExportAppendOnly";
    const string exportPredecessor = "20261107062516_RiskBandAssessmentScope";
    await migrator.MigrateAsync(exportGuard);
    var exportHistory = (await migrationDb.Database.GetAppliedMigrationsAsync()).ToArray();
    Assert.Equal(exportGuard, exportHistory[^1]);
    Assert.Equal(exportPredecessor, exportHistory[^2]);
    Assert.Equal(before, await SnapshotHistoricalArchiveReceiptStructureAsync(pg, compatible: false));
    var refusedExport = await Assert.ThrowsAnyAsync<Exception>(() => migrator.MigrateAsync(exportPredecessor));
    var exportFailure = Assert.IsType<PostgresException>(refusedExport.GetBaseException());
    Assert.Equal("55000", exportFailure.SqlState);
    Assert.Equal("Cannot remove structured-export integrity guards while retained structured exports exist.", exportFailure.MessageText);
    Assert.Equal(exportHistory, (await migrationDb.Database.GetAppliedMigrationsAsync()).ToArray());
    Assert.Equal(before, await SnapshotHistoricalArchiveReceiptStructureAsync(pg, compatible: false));
    await AssertNewScopeAsync(true);
  }

  // Fixed columns transcribed from the pinned ArchiveProfileIdentity model, never runtime discovery.
  private static readonly (string Table, string Columns)[] HistoricalArchiveReceiptColumns =
  [
    ("firm_safety_states", "id,deployment_epoch,operating_mode,policy_generation,recovery_epoch"),
    ("practice_clients", "id,billing_account_id,commercial_name,created_at,entity_type,firm_id,jurisdiction,legal_name,registration_number,restricted_profile,status,tax_registration_number"),
    ("engagements", "id,created_at,firm_id,generation,period_end,period_start,practice_client_id,professional_work_blocked,service_profile_id,service_route,status"),
    ("users", "id,created_at,created_by_user_id,disabled,display_name,email,firm_id,last_sign_in_at,session_epoch,subject,tenant_id,user_kind"),
    ("m365_connection_revisions", "id,activated_at,approved_by_user_id,cloud_profile,consent_state,created_at,created_by_user_id,firm_id,login_client_id_reference,revision,runtime_credential_reference,state,tenant_id,verified_at"),
    ("client_share_point_sites", "id,client_id,connection_revision_id,created_at,creation_dispatched_at,creation_operation_id,desired_digest,drive_id,firm_id,last_membership_sync_at,last_operation_id,member_object_ids_json,membership_state,ownership_marker,reason,requested_by_user_id,requested_url,root_item_id,site_id,staff_group_id,state,tenant_id,title,verified_at"),
    ("approvals", "id,client_id,decided_at,decided_by_user_id,decision,engagement_id,firm_id,input_generation,manifest_digest,policy_generation,target_id,target_kind,target_revision"),
    ("release_candidates", "id,approval_id,client_id,composition_id,composition_manifest_digest,created_at,engagement_id,firm_id,input_generation,manifest_digest,policy_generation,presentation_revision_id,revision,status,target_id,target_kind,target_revision"),
    ("release_checkpoints", "id,authorized_release_key,candidate_revision,client_id,created_at,engagement_id,firm_id,manifest_digest,read_back_digest,release_candidate_id,stored_reference,verified_at,verified_status,verifier"),
    ("releases", "id,authorized_release_key,checkpoint_id,client_id,engagement_id,firm_id,manifest_digest,package_id,package_revision,pinned_artifact_id,pinned_artifact_sha256_hex,pinned_artifact_version,pinned_composition_id,pinned_composition_manifest_digest,release_candidate_id,released_at,released_by_user_id"),
    ("durable_operations", "id,attempt_count,attempt_token,authority_mode,cancellation_disposition,claimed_epoch,client_id,completed_at,correlation_id,created_at,engagement_id,error_code,execution_group,execution_mode,expected_revision,firm_id,idempotency_key,is_reconciliation,lease_expires_at,lease_owner,next_attempt_at,operation_kind,originator_id,payload_json,request_bytes,request_digest,result_digest,result_identity,schema_version,status,target_id"),
    ("archives", "id,client_id,created_at,engagement_id,firm_id,observed_protection_at,observed_protection_state,profile_id,profile_version,status"),
    ("archive_manifests", "id,archive_id,build_request_key,built_at,client_id,commercial_bundle_id,completeness_exception,completeness_status,engagement_id,entry_count,firm_id,manifest_digest,predecessor_manifest_id,review_request_key,reviewed_at,reviewed_by_user_id,status,superseded_by_manifest_id,version"),
    ("archive_manifest_entries", "id,archive_manifest_id,byte_count,client_id,content_hash,engagement_id,entry_kind,firm_id,metadata_json,ordinal,relative_name,required,source_id,source_kind"),
    ("archive_structured_exports", "id,archive_id,archive_manifest_id,byte_count,client_id,content_hash,created_at,engagement_id,firm_id,payload_json,schema,version"),
    ("archive_copy_target_revisions", "id,authorized_staff_sha256,client_id,client_share_point_site_id,configuration_sha256,connection_revision_id,created_at,created_by_user_id,drive_id,expected_permissions_json,expected_permissions_sha256,firm_id,library_root_item_id,revision,selected_resource_draft_id,selected_resource_draft_revision,selected_resource_evidence_sha256,selected_resource_root_folder_id,selected_resource_site_url,selected_resource_verification_id,site_id,staff_group_id,target_code,tenant_id"),
    ("archive_copy_file_receipts", "id,archive_id,archive_manifest_id,archive_manifest_sha256,archive_manifest_version,attempt_token,checkpoint_id,client_id,destination_drive_id,destination_item_id,destination_library_root_item_id,destination_site_id,destination_tenant_id,destination_version,engagement_id,firm_id,has_exact_target_binding,manifest_entry_id,operation_id,read_back_at,read_back_byte_count,read_back_sha256,relative_path,release_id,source_byte_count,source_id,source_key_sha256,source_kind,source_sha256,source_version,target_configuration_sha256,target_revision,target_revision_id,verification_outcome"),
    ("archive_copy_permission_observations", "id,archive_id,archive_manifest_id,archive_manifest_sha256,archive_manifest_version,attempt_token,authorized_staff_sha256,checkpoint_id,client_id,comparison_outcome,engagement_id,expected_permissions_json,expected_permissions_sha256,firm_id,has_exact_target_binding,has_unique_permission_scope,observed_at,observed_drive_id,observed_library_root_item_id,observed_permissions_json,observed_permissions_sha256,observed_site_id,observed_staff_sha256,observed_tenant_id,operation_id,release_id,staff_group_id,target_configuration_sha256,target_revision,target_revision_id"),
  ];

  private static async Task<string[]> SnapshotHistoricalArchiveReceiptStructureAsync(PgTestSchema pg, bool compatible)
  {
    await using var connection = new NpgsqlConnection(pg.ConnectionString);
    await connection.OpenAsync();
    var rows = new List<string>();
    foreach (var (table, fixedColumns) in HistoricalArchiveReceiptColumns)
    {
      // Only these explicitly known additive columns are absent after the Audience-directed Down.
      var excluded = compatible ? table switch
      {
        "archive_manifests" => new[] { "build_request_key", "review_request_key", "commercial_bundle_id" },
        "release_candidates" => new[] { "composition_id", "presentation_revision_id", "composition_manifest_digest" },
        _ => Array.Empty<string>()
      } : Array.Empty<string>();
      var columns = string.Join(",", fixedColumns.Split(',').Where(x => !excluded.Contains(x)));
      await using var command = new NpgsqlCommand(
        $"SELECT COALESCE(jsonb_agg(to_jsonb(r) ORDER BY r.id), '[]'::jsonb)::text FROM (SELECT {columns} FROM {table}) r", connection);
      rows.Add((string)(await command.ExecuteScalarAsync())!);
    }
    Assert.Equal(18, rows.Count);
    return rows.ToArray();
  }

  private static async Task<(ArchiveReceiptProbeScope Own, ArchiveReceiptProbeScope Foreign,
    IReadOnlyDictionary<string, Guid> Parents)> SeedHistoricalArchiveReceiptStructureAsync(PgTestSchema pg)
  {
    await using (var boundary = new AuditSphereDbContext(pg.Options))
      Assert.Equal("20261105173658_ArchiveProfileIdentity", (await boundary.Database.GetAppliedMigrationsAsync()).Last());
    var ids = new[] { "fOwn", "fForeign", "cOwn", "cSecond", "cForeign", "eOwn", "eSibling", "eSecond", "eForeign",
      "u", "connection", "site", "target", "approval", "candidate", "checkpoint", "release", "archive", "manifest",
      "entry", "export", "fileReceipt", "permissionReceipt", "legacyTarget", "opOwn", "opForeign", "opSame",
      "opSecond", "opSibling", "opClient", "opFirm" }.ToDictionary(x => x, _ => Guid.NewGuid());
    var timestamp = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    const string exportJson = "{\"syntheticHistoricalArchiveReceipt\":true}";
    static string Digest(string value) => Hashing.Sha256Hex(Encoding.UTF8.GetBytes(value));
    await using var connection = new NpgsqlConnection(pg.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    async Task ExecuteAsync(string sql)
    {
      await using var command = new NpgsqlCommand(sql, connection, transaction);
      foreach (var (key, value) in ids) command.Parameters.AddWithValue(key, value);
      command.Parameters.AddWithValue("t", timestamp);
      command.Parameters.AddWithValue("json", "{}");
      command.Parameters.AddWithValue("jsonHash", Digest("{}"));
      command.Parameters.AddWithValue("exportJson", exportJson);
      command.Parameters.AddWithValue("exportHash", Digest(exportJson));
      command.Parameters.AddWithValue("exportBytes", (long)Encoding.UTF8.GetByteCount(exportJson));
      command.Parameters.AddWithValue("manifestHash", Digest("Synthetic historical manifest; no professional approval claim"));
      command.Parameters.AddWithValue("configHash", Digest("Synthetic historical target; no provider verification claim"));
      command.Parameters.AddWithValue("staffHash", Digest("[]"));
      await command.ExecuteNonQueryAsync();
    }
    await ExecuteAsync("""
      INSERT INTO firm_safety_states(id,deployment_epoch,operating_mode,policy_generation,recovery_epoch)
      VALUES(@fOwn,1,'LOCAL_ONLY',1,0),(@fForeign,1,'LOCAL_ONLY',1,0);
      INSERT INTO practice_clients(id,created_at,firm_id,legal_name,status)
      VALUES(@cOwn,@t,@fOwn,'Synthetic historical own client','Draft'),
        (@cSecond,@t,@fOwn,'Synthetic historical second client','Draft'),
        (@cForeign,@t,@fForeign,'Synthetic historical foreign client','Draft');
      INSERT INTO users(id,created_at,disabled,display_name,email,firm_id,session_epoch,subject,tenant_id,user_kind)
      VALUES(@u,@t,false,'Synthetic historical receipt owner','historical-archive@example.test',@fOwn,1,
        @u::text,'synthetic-historical-tenant','Staff');
      INSERT INTO engagements(id,created_at,firm_id,generation,period_end,period_start,practice_client_id,
        professional_work_blocked,service_profile_id,service_route,status)
      VALUES(@eOwn,@t,@fOwn,1,'2026-12-31','2026-01-01',@cOwn,false,'Audit','Audit','Draft'),
        (@eSibling,@t,@fOwn,1,'2027-12-31','2027-01-01',@cOwn,false,'Audit','Audit','Draft'),
        (@eSecond,@t,@fOwn,1,'2026-12-31','2026-01-01',@cSecond,false,'Audit','Audit','Draft'),
        (@eForeign,@t,@fForeign,1,'2026-12-31','2026-01-01',@cForeign,false,'Audit','Audit','Draft');
      INSERT INTO m365_connection_revisions(id,cloud_profile,consent_state,created_at,created_by_user_id,firm_id,
        login_client_id_reference,revision,runtime_credential_reference,state,tenant_id)
      VALUES(@connection,'PUBLIC','REQUIRED',@t,@u,@fOwn,'synthetic-historical-no-login',1,
        'synthetic-historical-no-runtime','DRAFT','synthetic-historical-tenant');
      INSERT INTO client_share_point_sites(id,client_id,connection_revision_id,created_at,desired_digest,firm_id,
        member_object_ids_json,membership_state,ownership_marker,reason,requested_by_user_id,requested_url,state,tenant_id,title)
      VALUES(@site,@cOwn,@connection,@t,@staffHash,@fOwn,'[]','PENDING','Synthetic historical fixture only',
        'Isolated historical scope-FK specimen; no provider effect',@u,
        'https://example.sharepoint.com/sites/synthetic-historical','REQUESTED','synthetic-historical-tenant',
        'Synthetic historical site intent');
      """);
    await ExecuteAsync("""
      INSERT INTO archive_copy_target_revisions(id,authorized_staff_sha256,client_id,client_share_point_site_id,
        configuration_sha256,connection_revision_id,created_at,created_by_user_id,drive_id,expected_permissions_json,
        expected_permissions_sha256,firm_id,library_root_item_id,revision,site_id,staff_group_id,target_code,tenant_id)
      VALUES(@target,@staffHash,@cOwn,@site,@configHash,@connection,@t,@u,'synthetic-historical-drive','{}',@jsonHash,
        @fOwn,'synthetic-historical-root',1,'synthetic-historical-site','42','SECONDARY_ARCHIVE','synthetic-historical-tenant');
      INSERT INTO approvals(id,client_id,decided_at,decided_by_user_id,decision,engagement_id,firm_id,input_generation,
        manifest_digest,policy_generation,target_id,target_kind,target_revision)
      VALUES(@approval,@cOwn,@t,@u,'APPROVED',@eOwn,@fOwn,1,@jsonHash,1,@legacyTarget,'FINANCIAL_PACKAGE',1);
      INSERT INTO release_candidates(id,approval_id,client_id,created_at,engagement_id,firm_id,input_generation,
        manifest_digest,policy_generation,revision,status,target_id,target_kind,target_revision)
      VALUES(@candidate,@approval,@cOwn,@t,@eOwn,@fOwn,1,@jsonHash,1,1,'ISSUED',@legacyTarget,'FINANCIAL_PACKAGE',1);
      INSERT INTO release_checkpoints(id,authorized_release_key,candidate_revision,client_id,created_at,engagement_id,
        firm_id,manifest_digest,read_back_digest,release_candidate_id,stored_reference,verified_at,verified_status,verifier)
      VALUES(@checkpoint,'synthetic-historical-release',1,@cOwn,@t,@eOwn,@fOwn,@jsonHash,@jsonHash,@candidate,
        'synthetic-historical:no-external-artifact',@t,'VERIFIED','synthetic historical fixture');
      INSERT INTO releases(id,authorized_release_key,checkpoint_id,client_id,engagement_id,firm_id,manifest_digest,
        package_id,package_revision,release_candidate_id,released_at,released_by_user_id)
      VALUES(@release,'synthetic-historical-release',@checkpoint,@cOwn,@eOwn,@fOwn,@jsonHash,@legacyTarget,1,@candidate,@t,@u);
      INSERT INTO archives(id,client_id,created_at,engagement_id,firm_id,profile_id,profile_version,status)
      VALUES(@archive,@cOwn,@t,@eOwn,@fOwn,'SYNTHETIC-HISTORICAL-STRUCTURAL-ONLY',1,'ISSUED');
      INSERT INTO archive_manifests(id,archive_id,built_at,client_id,completeness_status,engagement_id,entry_count,
        firm_id,manifest_digest,status,version)
      VALUES(@manifest,@archive,@t,@cOwn,'COMPLETE',@eOwn,1,@fOwn,@manifestHash,'BUILT',1);
      INSERT INTO archive_manifest_entries(id,archive_manifest_id,byte_count,client_id,content_hash,engagement_id,
        entry_kind,firm_id,metadata_json,ordinal,relative_name,required,source_kind)
      VALUES(@entry,@manifest,@exportBytes,@cOwn,@exportHash,@eOwn,'STRUCTURED_EXPORT',@fOwn,'{}',1,
        'synthetic-historical-structured-export.json',true,'AuditSphereOps');
      INSERT INTO archive_structured_exports(id,archive_id,archive_manifest_id,byte_count,client_id,content_hash,
        created_at,engagement_id,firm_id,payload_json,schema,version)
      VALUES(@export,@archive,@manifest,@exportBytes,@cOwn,@exportHash,@t,@eOwn,@fOwn,@exportJson,'records-export.v2',1);
      UPDATE archive_manifests SET status='REVIEWED',reviewed_at=@t,reviewed_by_user_id=@u WHERE id=@manifest;
      """);
    // Literal legacy structural request encoding only: no claim of current canonical operation authentication.
    // These PENDING LOCAL parents prove nullable/composite scope relationships, never a provider completion.
    await ExecuteAsync("""
      INSERT INTO durable_operations(id,attempt_count,attempt_token,authority_mode,claimed_epoch,client_id,
        correlation_id,created_at,engagement_id,execution_group,execution_mode,expected_revision,firm_id,
        idempotency_key,is_reconciliation,next_attempt_at,operation_kind,payload_json,request_bytes,request_digest,
        schema_version,status,target_id)
      SELECT op,0,0,'LOCAL_VALIDATION',0,client,op,@t,engagement,'default','LOCAL',1,firm,
        'synthetic-historical-'||op::text,false,@t,'SyntheticHistoricalArchiveScope.v1','{}',convert_to('{}','UTF8'),
        @jsonHash,1,'PENDING',op
      FROM (VALUES
        (@opOwn,@fOwn,@cOwn,@eOwn),(@opForeign,@fForeign,@cForeign,@eForeign),
        (@opSame,@fOwn,@cOwn,@eOwn),(@opSecond,@fOwn,@cSecond,@eSecond),
        (@opSibling,@fOwn,@cOwn,@eSibling),(@opClient,@fOwn,@cOwn,NULL::uuid),
        (@opFirm,@fOwn,NULL::uuid,NULL::uuid)) AS scopes(op,firm,client,engagement);
      INSERT INTO archive_copy_file_receipts(id,archive_id,archive_manifest_id,archive_manifest_sha256,
        archive_manifest_version,attempt_token,checkpoint_id,client_id,destination_drive_id,destination_item_id,
        destination_library_root_item_id,destination_site_id,destination_tenant_id,destination_version,engagement_id,
        firm_id,has_exact_target_binding,operation_id,read_back_at,read_back_byte_count,read_back_sha256,relative_path,
        release_id,source_byte_count,source_id,source_key_sha256,source_kind,source_sha256,source_version,
        target_configuration_sha256,target_revision,target_revision_id,verification_outcome)
      VALUES(@fileReceipt,@archive,@manifest,@manifestHash,1,1,@checkpoint,@cOwn,'synthetic-historical-drive',
        'synthetic-historical-item','synthetic-historical-root','synthetic-historical-site','synthetic-historical-tenant',
        'synthetic-historical-version',@eOwn,@fOwn,false,@opOwn,@t,2,@jsonHash,'synthetic-historical-checkpoint.json',
        @release,2,@checkpoint,@jsonHash,'CHECKPOINT_MANIFEST',@jsonHash,@jsonHash,@configHash,1,@target,'MATCH');
      INSERT INTO archive_copy_permission_observations(id,archive_id,archive_manifest_id,archive_manifest_sha256,
        archive_manifest_version,attempt_token,authorized_staff_sha256,checkpoint_id,client_id,comparison_outcome,
        engagement_id,expected_permissions_json,expected_permissions_sha256,firm_id,has_exact_target_binding,
        has_unique_permission_scope,observed_at,observed_drive_id,observed_library_root_item_id,observed_permissions_json,
        observed_permissions_sha256,observed_site_id,observed_staff_sha256,observed_tenant_id,operation_id,release_id,
        staff_group_id,target_configuration_sha256,target_revision,target_revision_id)
      VALUES(@permissionReceipt,@archive,@manifest,@manifestHash,1,1,@staffHash,@checkpoint,@cOwn,'MATCH',@eOwn,
        '{}',@jsonHash,@fOwn,false,false,@t,'synthetic-historical-drive','synthetic-historical-root','{}',@jsonHash,
        'synthetic-historical-site',@staffHash,'synthetic-historical-tenant',@opOwn,@release,'42',@configHash,1,@target);
      """);
    await transaction.CommitAsync();
    return (new(ids["fOwn"], ids["cOwn"], ids["eOwn"], ids["opOwn"]),
      new(ids["fForeign"], ids["cForeign"], ids["eForeign"], ids["opForeign"]),
      new Dictionary<string, Guid>
      {
        ["same-scope operation"] = ids["opSame"],
        ["same-firm different-client operation"] = ids["opSecond"],
        ["same-client sibling-engagement operation"] = ids["opSibling"],
        ["client-only operation"] = ids["opClient"],
        ["firm-wide operation"] = ids["opFirm"]
      });
  }

}
