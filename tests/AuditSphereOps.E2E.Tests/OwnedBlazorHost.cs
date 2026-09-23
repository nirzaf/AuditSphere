using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.E2E.Tests;

internal sealed class OwnedBlazorHost : IAsyncDisposable
{
  private readonly List<Process> processes = [];
  private readonly string runRoot;
  private readonly OwnedPostgresDatabase pg;

  public PbcSeed.Fixture Fixture { get; }
  public Guid RequestId { get; }
  public string ClientUrl { get; private set; } = string.Empty;
  public string StaffUrl { get; private set; } = string.Empty;
  public string RunRoot => runRoot;
  public string ConnectionString => pg.ConnectionString;
  public DbContextOptions<AuditSphereDbContext> DbOptions => pg.Options;
  public string StagingRoot => Path.Combine(runRoot, "staging");

  private OwnedBlazorHost(OwnedPostgresDatabase pg, PbcSeed.Fixture fixture, Guid requestId, string runRoot)
  {
    this.pg = pg;
    Fixture = fixture;
    RequestId = requestId;
    this.runRoot = runRoot;
  }

  public static async Task<OwnedBlazorHost> StartAsync(
    bool startWorker = true, bool enableSetup = false, string? caseId = null,
    bool requireProtectionAttestation = false)
  {
    var repo = FindRepositoryRoot();
    OwnedPostgresDatabase? pg = null;
    OwnedBlazorHost? host = null;
    string? runRoot = null;
    try
    {
      pg = await OwnedPostgresDatabase.CreateAsync(caseId ?? (enableSetup ? "PROP-E2E-05" : "PROP-E2E-06"));
      runRoot = pg.RunRoot;
      var fixture = await PbcSeed.SeedAsync(pg);
      var requestId = await PbcSeed.CreateSentAcknowledgedRequestAsync(pg, fixture,
        PbcSeed.Actor(fixture.Staff, "Staff"), PbcSeed.Actor(fixture.Client, "ClientUser"));
      host = new OwnedBlazorHost(pg, fixture, requestId, runRoot);
      host.ClientUrl = await host.StartWebAsync(repo, fixture.Client, enableSetup);
      host.StaffUrl = await host.StartWebAsync(repo, fixture.Staff, enableSetup, requireProtectionAttestation);
      if (startWorker) await host.StartWorkerAsync(repo);
      return host;
    }
    catch
    {
      if (host is not null) await host.DisposeAsync();
      else if (pg is not null) await pg.DisposeAsync();
      if (runRoot is not null) DeleteOwnedDirectory(runRoot, Path.Combine(runRoot, "staging"));
      if (runRoot is not null) DeleteOwnedDirectory(runRoot, Path.Combine(runRoot, "provider"));
      if (runRoot is not null) DeleteOwnedDirectory(runRoot, Path.Combine(runRoot, "checkpoints"));
      throw;
    }
  }

  public AuditSphereDbContext CreateDbContext() => new(pg.Options);

  public async Task<string> StartUnrelatedClientWebAsync()
  {
    var user = PbcSeed.User(Fixture.FirmId, "Client");
    var clientId = Guid.NewGuid();
    var engagementId = Guid.NewGuid();
    await using (var db = CreateDbContext())
    {
      db.PracticeClients.Add(new AuditSphereOps.Domain.Practice.PracticeClient
      {
        Id = clientId, FirmId = Fixture.FirmId, LegalName = "UNRELATED SYNTHETIC CLIENT",
        CreatedAt = DateTimeOffset.UtcNow
      });
      db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement
      {
        Id = engagementId, FirmId = Fixture.FirmId, PracticeClientId = clientId,
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientSafetyStates.Add(new AuditSphereOps.Domain.Completion.ClientSafetyState
      {
        Id = clientId, FirmId = Fixture.FirmId
      });
      db.Users.Add(user);
      db.RoleGrants.Add(PbcSeed.Grant(Fixture.FirmId, user, "ClientUser", clientId, engagementId));
      await db.SaveChangesAsync();
    }
    return await StartWebAsync(FindRepositoryRoot(), user, enableSetup: false);
  }

  public Task<string> StartReviewerWebAsync() =>
    StartWebAsync(FindRepositoryRoot(), Fixture.Reviewer, enableSetup: false);

  public Task<string> StartWebForIdentityAsync(AuditSphereOps.Domain.Security.AppUser identity) =>
    StartWebAsync(FindRepositoryRoot(), identity, enableSetup: false);

  public async Task WaitForReceivedAsync(Guid uploadId)
  {
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    while (!timeout.IsCancellationRequested)
    {
      await using var db = CreateDbContext();
      var intent = await db.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.Id == uploadId, timeout.Token);
      if (intent.State == AuditSphereOps.Domain.Documents.PbcUploadStates.Received) return;
      var worker = processes.LastOrDefault(p => p.StartInfo.Environment.ContainsKey("Worker__Group"));
      if (worker?.HasExited == true)
        throw new InvalidOperationException($"The owned Test worker exited with {worker.ExitCode}; see {Path.Combine(runRoot, "worker.log")}.");
      await Task.Delay(250, timeout.Token);
    }
    throw new TimeoutException($"The owned Test worker did not receive upload {uploadId:D} within 45 seconds.");
  }

  public async ValueTask DisposeAsync()
  {
    foreach (var process in processes.AsEnumerable().Reverse())
    {
      try
      {
        if (!process.HasExited)
        {
          process.Kill(entireProcessTree: true);
          await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
      }
      catch (InvalidOperationException) { }
      catch (System.ComponentModel.Win32Exception) { }
      finally { process.Dispose(); }
    }
    await pg.DisposeAsync();
    DeleteOwnedDirectory(runRoot, Path.Combine(runRoot, "staging"));
    DeleteOwnedDirectory(runRoot, Path.Combine(runRoot, "provider"));
    DeleteOwnedDirectory(runRoot, Path.Combine(runRoot, "checkpoints"));
  }

  private async Task<string> StartWebAsync(string repo, AuditSphereOps.Domain.Security.AppUser identity, bool enableSetup,
    bool requireProtectionAttestation = false)
  {
    var url = $"http://127.0.0.1:{ReserveLoopbackPort()}";
    var logPath = Path.Combine(runRoot, $"web-{identity.UserKind}-{identity.Id:N}.log");
    var settings = new List<string>
    {
      "DOTNET_ENVIRONMENT", "Test",
      "ASPNETCORE_ENVIRONMENT", "Test",
      "ASPNETCORE_URLS", url,
      "ConnectionStrings__AuditSphere", pg.ConnectionString,
      "Application__FirmId", Fixture.FirmId.ToString("D"),
      "Application__AllowSimulationAdapters", "true",
      "ExternalEffects__Enabled", "false",
      "DevelopmentIdentity__Enabled", "true",
      "DevelopmentIdentity__Subject", identity.Subject,
      "DevelopmentIdentity__TenantId", identity.TenantId,
      "Storage__PbcStagingRoot", StagingRoot,
      "Storage__PbcProviderSimulationRoot", Path.Combine(runRoot, "provider"),
      "Storage__ReleaseCheckpointRoot", Path.Combine(runRoot, "checkpoints")
    };
    if (requireProtectionAttestation)
      settings.AddRange(["ReleaseSafety__RequireProtectionAttestation", "true"]);
    if (enableSetup)
    {
      settings.AddRange([
        "Setup__InstallationId", "synthetic-installation",
        "Setup__BootstrapProofHash", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(M365SetupJourneyTests.BootstrapProof))).ToLowerInvariant()
      ]);
    }
    var process = StartDotnet(repo, "src/AuditSphereOps.Web/AuditSphereOps.Web.csproj", logPath,
      [.. settings]);
    processes.Add(process);
    await pg.RecordProcessAsync($"web-{identity.UserKind}", process);
    await WaitForReadyAsync(process, url, logPath);
    return url;
  }

  private async Task StartWorkerAsync(string repo)
  {
    var logPath = Path.Combine(runRoot, "worker.log");
    var process = StartDotnet(repo, "src/AuditSphereOps.Worker/AuditSphereOps.Worker.csproj", logPath,
      "DOTNET_ENVIRONMENT", "Test",
      "ConnectionStrings__AuditSphere", pg.ConnectionString,
      "Worker__FirmId", Fixture.FirmId.ToString("D"),
      "Worker__DeploymentEpoch", "1",
      "Worker__Group", "general",
      "AllowSimulationAdapters", "true",
      "ExternalEffects__Enabled", "false",
      "Storage__PbcProviderSimulationRoot", Path.Combine(runRoot, "provider"),
      "Storage__ReleaseCheckpointRoot", Path.Combine(runRoot, "checkpoints"));
    processes.Add(process);
    await pg.RecordProcessAsync("worker-general", process);
    await Task.Yield();
    if (process.HasExited)
      throw new InvalidOperationException($"The owned Test worker exited with {process.ExitCode}; see {logPath}.");
  }

  private static Process StartDotnet(string repo, string project, string logPath, params string[] pairs)
  {
    var info = new ProcessStartInfo("dotnet")
    {
      WorkingDirectory = Path.Combine(repo, Path.GetDirectoryName(project)!),
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      CreateNoWindow = true
    };
    info.ArgumentList.Add("run");
    info.ArgumentList.Add("--project");
    info.ArgumentList.Add(Path.Combine(repo, project));
    info.ArgumentList.Add("--no-launch-profile");
    info.ArgumentList.Add("--no-build");
    info.ArgumentList.Add("--no-restore");
    info.ArgumentList.Add("--configuration");
    info.ArgumentList.Add(ConfigurationName());
    if (project.Contains("Web.csproj", StringComparison.Ordinal))
    {
      var urlIndex = Array.IndexOf(pairs, "ASPNETCORE_URLS");
      if (urlIndex >= 0)
      {
        info.ArgumentList.Add("--");
        info.ArgumentList.Add("--urls");
        info.ArgumentList.Add(pairs[urlIndex + 1]);
      }
    }
    foreach (var key in info.Environment.Keys.Where(k =>
      k.StartsWith("ConnectionStrings__", StringComparison.Ordinal) ||
      k.StartsWith("DevelopmentIdentity__", StringComparison.Ordinal) ||
      k.StartsWith("ExternalEffects__", StringComparison.Ordinal) ||
      k.StartsWith("Application__", StringComparison.Ordinal) ||
      k.StartsWith("Worker__", StringComparison.Ordinal) ||
      k.StartsWith("ReleaseSafety__", StringComparison.Ordinal) ||
      k.StartsWith("Storage__", StringComparison.Ordinal) ||
      k is "AllowSimulationAdapters" or "ASPNETCORE_ENVIRONMENT" or "ASPNETCORE_URLS" or "DOTNET_ENVIRONMENT").ToArray())
      info.Environment.Remove(key);
    for (var i = 0; i < pairs.Length; i += 2) info.Environment[pairs[i]] = pairs[i + 1];
    info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
    info.Environment["DOTNET_NOLOGO"] = "1";
    info.Environment["Logging__LogLevel__Default"] = "Warning";
    Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
    var output = new object();
    void Capture(string? line)
    {
      if (line is null) return;
      lock (output) File.AppendAllText(logPath, line + Environment.NewLine);
    }
    var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start the owned .NET host.");
    process.OutputDataReceived += (_, e) => Capture(e.Data);
    process.ErrorDataReceived += (_, e) => Capture(e.Data);
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    return process;
  }

  private static async Task WaitForReadyAsync(Process process, string url, string logPath)
  {
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    while (!timeout.IsCancellationRequested)
    {
      if (process.HasExited)
        throw new InvalidOperationException($"The owned Web host exited with {process.ExitCode}; log follows:\n{ReadLog(logPath)}");
      try
      {
        using var response = await client.GetAsync(url + "/health/live", timeout.Token);
        if (response.StatusCode == HttpStatusCode.OK) return;
      }
      catch (HttpRequestException) { }
      catch (TaskCanceledException) when (!timeout.IsCancellationRequested) { }
      await Task.Delay(200, timeout.Token);
    }
    throw new TimeoutException($"The owned Web host did not become live; log follows:\n{ReadLog(logPath)}");
  }

  private static string ReadLog(string path) => File.Exists(path) ? File.ReadAllText(path) : "<log file not created>";

  private static void DeleteOwnedDirectory(string root, string path)
  {
    if (!path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
      throw new InvalidOperationException("Refusing to clean a path outside the scenario-owned root.");
    try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
    catch (IOException) { }
  }

  private static int ReserveLoopbackPort()
  {
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    return ((IPEndPoint)listener.LocalEndpoint).Port;
  }

  private static string ConfigurationName() =>
    Directory.GetParent(AppContext.BaseDirectory)?.Parent?.Name is { Length: > 0 } name ? name : "Release";

  private static string FindRepositoryRoot()
  {
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
      if (File.Exists(Path.Combine(directory.FullName, "AuditSphereOps.slnx"))) return directory.FullName;
    throw new DirectoryNotFoundException("Could not locate AuditSphereOps.slnx from the E2E test output directory.");
  }
}
