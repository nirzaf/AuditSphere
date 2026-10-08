using AuditSphereOps.Api;
using AuditSphereOps.Api.Contracts;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

// Writes the canonical OpenAPI 3.1 contract that the real API host serves at /api/contract (CI drift check).
// The host runs in the Test environment on an ephemeral loopback port with the same overrides the former contract
// test used. No database is needed, because the contract endpoint does not read one.
if (args is not [{ Length: > 0 } outputPath])
{
  Console.Error.WriteLine("usage: AuditSphereOps.OpenApiEmitter <output-path>");
  return 2;
}

string[] hostArgs =
[
  // The contract's default operation tags come from the application name; the former test host used the API assembly.
  "--applicationName=AuditSphereOps.Api",
  "--environment=Test",
  "--urls=http://127.0.0.1:0",
  "--AngularUi:Enabled=false",
  "--AngularUi:CanonicalRoutes=false",
  "--DevelopmentIdentity:Enabled=true",
  "--DevelopmentIdentity:Subject=contract-subject",
  "--DevelopmentIdentity:TenantId=00000000-0000-0000-0000-000000000000",
  "--Application:AllowSimulationAdapters=true",
  "--ExternalEffects:Enabled=false"
];

await using var app = ApiHost.Create(hostArgs);
await app.StartAsync();
try
{
  var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault()
    ?? throw new InvalidOperationException("The API host did not report a listening address.");
  using var client = new HttpClient { BaseAddress = new Uri(address) };
  // The committed contract records the server URL the former in-memory test host used, so keep the same host header.
  client.DefaultRequestHeaders.Host = "localhost";
  var document = await client.GetStringAsync($"/api/contract/{ApiContract.DocumentName}.json");
  await File.WriteAllTextAsync(outputPath, document);
  Console.WriteLine($"Wrote OpenAPI contract to {outputPath}");
  return 0;
}
finally
{
  await app.StopAsync();
}
