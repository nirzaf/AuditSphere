using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class AuditDeliverablesTests
{
  // One projection's legacy-role read compatibility; never a stored grant or command authorization fixture.
  private sealed class LegacyEngagementLeaderGrantRead(Guid actorId, Guid firmId, Guid clientId, Guid engagementId,
    DateTimeOffset? expiresAt) : DbCommandInterceptor
  {
    public int MatchedQueries { get; private set; }
    public int AlteredRows { get; private set; }
    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
      DbDataReader result, CancellationToken cancellationToken = default)
    {
      if (!command.CommandText.Contains("FROM role_grants", StringComparison.Ordinal)) return ValueTask.FromResult(result);
      object? ParameterFor(string column)
      {
        var match = Regex.Match(command.CommandText, @"\b\w+\." + column + @" = (@\w+)");
        Assert.True(match.Success, $"Unexpected targeted grant predicate: {column}");
        return command.Parameters.Cast<DbParameter>().Single(x => "@" + x.ParameterName.TrimStart('@') == match.Groups[1].Value).Value;
      }
      if (!Equals(ParameterFor("user_id"), actorId)) return ValueTask.FromResult(result);
      Assert.StartsWith("SELECT", command.CommandText.TrimStart());
      Assert.Equal(firmId, Assert.IsType<Guid>(ParameterFor("firm_id")));
      Assert.Contains("revoked_at IS NULL", command.CommandText);
      Assert.Equal(new[] { "role", "client_id", "engagement_id", "expires_at" },
        Enumerable.Range(0, result.FieldCount).Select(result.GetName));
      MatchedQueries++;
      return ValueTask.FromResult<DbDataReader>(new LegacyGrantRoleReader(result, clientId, engagementId, expiresAt, () => AlteredRows++));
    }
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
      InterceptionResult<int> result, CancellationToken cancellationToken = default)
      => throw new InvalidOperationException("The legacy compatibility context must remain read-only.");
  }

  private sealed class LegacyGrantRoleReader(DbDataReader source, Guid clientId, Guid engagementId,
    DateTimeOffset? expiresAt, Action observed) : DbDataReader
  {
    private int rows;
    private bool Observe(bool hasRow)
    {
      if (!hasRow) { Assert.Equal(1, rows); return false; }
      Assert.Equal(1, ++rows);
      Assert.Equal("Staff", source.GetString(0));
      Assert.Equal(clientId, source.GetGuid(1));
      Assert.Equal(engagementId, source.GetGuid(2));
      Assert.Equal(expiresAt, source.IsDBNull(3) ? (DateTimeOffset?)null : source.GetFieldValue<DateTimeOffset>(3));
      observed();
      return true;
    }
    public override bool Read() => Observe(source.Read());
    public override async Task<bool> ReadAsync(CancellationToken ct) => Observe(await source.ReadAsync(ct));
    public override string GetString(int i) => i == 0 ? "EngagementLeader" : source.GetString(i);
    public override object GetValue(int i) => i == 0 ? "EngagementLeader" : source.GetValue(i);
    public override T GetFieldValue<T>(int i) => i == 0 ? (T)(object)"EngagementLeader" : source.GetFieldValue<T>(i);
    public override Task<T> GetFieldValueAsync<T>(int i, CancellationToken ct)
    {
      if (i != 0) return source.GetFieldValueAsync<T>(i, ct);
      ct.ThrowIfCancellationRequested();
      return Task.FromResult((T)(object)"EngagementLeader");
    }
    public override int GetValues(object[] values)
    { var count = source.GetValues(values); if (count > 0) values[0] = "EngagementLeader"; return count; }
    public override object this[int i] => GetValue(i);
    public override object this[string name] => GetValue(GetOrdinal(name));
    public override int Depth => source.Depth;
    public override int FieldCount => source.FieldCount;
    public override bool HasRows => source.HasRows;
    public override bool IsClosed => source.IsClosed;
    public override int RecordsAffected => source.RecordsAffected;
    public override string GetName(int i) => source.GetName(i);
    public override int GetOrdinal(string name) => source.GetOrdinal(name);
    public override Type GetFieldType(int i) => source.GetFieldType(i);
    public override string GetDataTypeName(int i) => source.GetDataTypeName(i);
    public override bool GetBoolean(int i) => source.GetBoolean(i);
    public override byte GetByte(int i) => source.GetByte(i);
    public override long GetBytes(int i, long offset, byte[]? buffer, int bufferOffset, int length) => source.GetBytes(i, offset, buffer, bufferOffset, length);
    public override char GetChar(int i) => source.GetChar(i);
    public override long GetChars(int i, long offset, char[]? buffer, int bufferOffset, int length) => source.GetChars(i, offset, buffer, bufferOffset, length);
    public override DateTime GetDateTime(int i) => source.GetDateTime(i);
    public override decimal GetDecimal(int i) => source.GetDecimal(i);
    public override double GetDouble(int i) => source.GetDouble(i);
    public override float GetFloat(int i) => source.GetFloat(i);
    public override Guid GetGuid(int i) => source.GetGuid(i);
    public override short GetInt16(int i) => source.GetInt16(i);
    public override int GetInt32(int i) => source.GetInt32(i);
    public override long GetInt64(int i) => source.GetInt64(i);
    public override bool IsDBNull(int i) => source.IsDBNull(i);
    public override Task<bool> IsDBNullAsync(int i, CancellationToken ct) => source.IsDBNullAsync(i, ct);
    public override bool NextResult() => source.NextResult();
    public override Task<bool> NextResultAsync(CancellationToken ct) => source.NextResultAsync(ct);
    public override System.Collections.IEnumerator GetEnumerator() => new DbEnumerator(this, false);
    public override DataTable? GetSchemaTable() => source.GetSchemaTable();
    public override void Close() => source.Close();
    protected override void Dispose(bool disposing) { if (disposing) source.Dispose(); base.Dispose(disposing); }
    public override ValueTask DisposeAsync() => source.DisposeAsync();
  }
}
