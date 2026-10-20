using Npgsql;

namespace AuditSphereOps.Domain.Tests;

[CollectionDefinition("PgTestSchemaTemplateLock", DisableParallelization = true)]
public sealed class PgTestSchemaTemplateLockCollection
{
}

[Collection("PgTestSchemaTemplateLock")]
public sealed class PgTestSchemaTemplateLockTests
{
  [Fact, Trait("Profile", "Database")]
  public async Task EnsureTemplateUsesPostgresAdvisoryLockWhenAdminStringTargetsAuditsphereTests()
  {
    var rawBuilder = new NpgsqlConnectionStringBuilder(
      Environment.GetEnvironmentVariable("AUDITSPHERE_TEST_CONNECTION") ??
      "Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=postgres")
    {
      Pooling = false,
      Timeout = 60
    };
    Assert.Equal("127.0.0.1", rawBuilder.Host);
    Assert.Equal("auditsphere_tests", rawBuilder.Database);

    var postgresAdminBuilder = new NpgsqlConnectionStringBuilder(rawBuilder.ConnectionString)
    {
      Database = "postgres",
      Pooling = false
    };
    var postgresAdmin = postgresAdminBuilder.ConnectionString;
    var applicationName = $"template-lock-{Guid.NewGuid():N}";
    var callerAdmin = new NpgsqlConnectionStringBuilder(rawBuilder.ConnectionString)
    {
      Database = "auditsphere_tests",
      Pooling = false,
      ApplicationName = applicationName
    }.ConnectionString;

    // Ensure the template exists before taking the lock, so the assertion covers lock contention
    // without doing migration DDL while the holder is active.
    var expectedTemplate = await PgTestSchema.EnsureTemplateAsync(rawBuilder.ConnectionString, postgresAdmin);

    await using var lockHolder = new NpgsqlConnection(postgresAdmin);
    await lockHolder.OpenAsync();
    await using (var acquire = new NpgsqlCommand(
      "SELECT pg_advisory_lock(hashtext('auditsphere_tpl'))", lockHolder))
      await acquire.ExecuteNonQueryAsync();
    await using var pidCommand = new NpgsqlCommand("SELECT pg_backend_pid()", lockHolder);
    var blockingPid = (int)(await pidCommand.ExecuteScalarAsync())!;

    await using var observer = new NpgsqlConnection(postgresAdmin);
    await observer.OpenAsync();
    var ensureTask = PgTestSchema.EnsureTemplateAsync(rawBuilder.ConnectionString, callerAdmin);
    var observedCanonicalBlock = false;
    Exception? observationFailure = null;
    var released = false;
    try
    {
      try
      {
        observedCanonicalBlock = await WaitForCanonicalTemplateLockBlockAsync(
          observer, applicationName, blockingPid);
      }
      catch (Exception exception)
      {
        observationFailure = exception;
      }
    }
    finally
    {
      await using var release = new NpgsqlCommand(
        "SELECT pg_advisory_unlock(hashtext('auditsphere_tpl'))", lockHolder);
      released = (bool)(await release.ExecuteScalarAsync())!;
      await lockHolder.CloseAsync();
    }

    var actualTemplate = await ensureTask.WaitAsync(TimeSpan.FromSeconds(30));
    Assert.True(released, "The test did not release its session-level advisory lock.");
    Assert.True(observationFailure is null, $"Could not inspect PostgreSQL lock state: {observationFailure}");
    Assert.True(observedCanonicalBlock,
      "EnsureTemplateAsync did not wait on the advisory lock held in the postgres database.");
    Assert.Equal(expectedTemplate, actualTemplate);
  }

  private static async Task<bool> WaitForCanonicalTemplateLockBlockAsync(
    NpgsqlConnection observer,
    string applicationName,
    int blockingPid)
  {
    var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
    while (DateTimeOffset.UtcNow < deadline)
    {
      await using (var clearSnapshot = new NpgsqlCommand("SELECT pg_stat_clear_snapshot()", observer))
        await clearSnapshot.ExecuteNonQueryAsync();

      await using var command = new NpgsqlCommand("""
        SELECT EXISTS (
          SELECT 1
          FROM pg_stat_activity AS waiting
          JOIN pg_locks AS pending ON pending.pid = waiting.pid
          WHERE waiting.application_name = @application_name
            AND waiting.datname = 'postgres'
            AND waiting.wait_event_type = 'Lock'
            AND pending.locktype = 'advisory'
            AND pending.database = (SELECT oid FROM pg_database WHERE datname = 'postgres')
            AND NOT pending.granted
            AND @blocking_pid = ANY(pg_blocking_pids(waiting.pid))
        )
        """, observer);
      command.Parameters.AddWithValue("application_name", applicationName);
      command.Parameters.AddWithValue("blocking_pid", blockingPid);
      if ((bool)(await command.ExecuteScalarAsync())!)
        return true;

      await Task.Delay(TimeSpan.FromMilliseconds(25));
    }

    return false;
  }
}
