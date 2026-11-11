using Npgsql;
using Microsoft.EntityFrameworkCore;
using AuditSphereOps.Infrastructure.Persistence;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class AuditDeliverablesTests
{
  private static async Task ImportExactOpinionOutputsAsync(PgTestSchema source, PgTestSchema target,
    Guid[] srms, Guid[] clearances, Guid[] opinions)
  {
    await using (var boundary = new AuditSphereDbContext(target.Options))
      Assert.Equal("20261029124914_ArchiveCopyOperationScope", (await boundary.Database.GetAppliedMigrationsAsync()).Last());
    await using var from = new NpgsqlConnection(source.ConnectionString);
    await using var to = new NpgsqlConnection(target.ConnectionString);
    await from.OpenAsync();
    await to.OpenAsync();
    // Fixed actual-baseline columns only. No whole-table copy, inferred closure, or current approval synthesis.
    foreach (var (table, columns, ids) in new[]
    {
      ("audit_deliverables", "id,firm_id,client_id,engagement_id,kind,version,template_version,input_digest,input_summary_json,file_name,content_type,content,content_sha256,signed_from_deliverable_id,created_by_user_id,created_at", srms),
      ("partner_completion_clearances", "id,firm_id,client_id,engagement_id,summary_review_memorandum_id,key_risk_areas_comment,financial_statement_notes_comment,partner_user_id,cleared_at", clearances),
      ("audit_opinion_decisions", "id,firm_id,client_id,engagement_id,partner_clearance_id,opinion_type,affected_taxonomy_node_id,focus_area,basis_text,decided_by_user_id,decided_at", opinions)
    })
    {
      Assert.Equal(2, ids.Length);
      async Task<string> ReadAsync(NpgsqlConnection connection)
      {
        await using var read = new NpgsqlCommand($"SELECT COALESCE(jsonb_agg(to_jsonb(r) ORDER BY r.id), '[]'::jsonb)::text FROM (SELECT {columns} FROM {table} WHERE id=ANY(@ids)) r", connection);
        read.Parameters.AddWithValue("ids", ids);
        return (string)(await read.ExecuteScalarAsync())!;
      }
      var retainedSource = await ReadAsync(from);
      await using var insert = new NpgsqlCommand($"INSERT INTO {table} ({columns}) SELECT {columns} FROM jsonb_populate_recordset(NULL::{table}, CAST(@rows AS jsonb))", to);
      insert.Parameters.AddWithValue("rows", retainedSource);
      Assert.Equal(2, await insert.ExecuteNonQueryAsync());
      Assert.Equal(retainedSource, await ReadAsync(to));
    }
  }
}
