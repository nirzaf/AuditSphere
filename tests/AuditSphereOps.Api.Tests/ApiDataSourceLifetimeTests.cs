using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AuditSphereOps.Api.Tests;

public sealed class ApiDataSourceLifetimeTests
{
  [Fact]
  public async Task HostsOwnDistinctDataSourcesAndDisposeOnlyTheirOwnConnections()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-DATA-SOURCE-LIFETIME");
    var settings = new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["Application:AllowSimulationAdapters"] = "true",
      ["ExternalEffects:Enabled"] = "false"
    };
    await using var first = new StandaloneApiApplicationFactory(settings);
    await using var second = new StandaloneApiApplicationFactory(settings);
    var firstSource = first.Services.GetRequiredService<NpgsqlDataSource>();
    var secondSource = second.Services.GetRequiredService<NpgsqlDataSource>();
    Assert.Same(firstSource, first.Services.GetRequiredService<NpgsqlDataSource>());
    Assert.Same(secondSource, second.Services.GetRequiredService<NpgsqlDataSource>());
    Assert.NotSame(firstSource, secondSource);

    // Exercise the real factory options/model and connection path before disposing either host.
    await using (var firstDb = await first.Services.GetRequiredService<IDbContextFactory<AuditSphereDbContext>>().CreateDbContextAsync())
    await using (var secondDb = await second.Services.GetRequiredService<IDbContextFactory<AuditSphereDbContext>>().CreateDbContextAsync())
    {
      Assert.Same(firstDb.Model, secondDb.Model);
      await firstDb.Database.OpenConnectionAsync();
      await secondDb.Database.OpenConnectionAsync();
    }

    await first.DisposeAsync();
    await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
    {
      await using var unexpected = await firstSource.OpenConnectionAsync();
    });
    // Disposing one host must not close a different host's singleton, even with the same database.
    await using (var connection = await secondSource.OpenConnectionAsync())
    await using (var command = new NpgsqlCommand("SELECT 1", connection))
      Assert.Equal(1, await command.ExecuteScalarAsync());

    await second.DisposeAsync();
    await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
    {
      await using var unexpected = await secondSource.OpenConnectionAsync();
    });
  }
}
