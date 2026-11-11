using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.E2E.Tests;

internal sealed class OwnedHost : IAsyncDisposable
{
  internal const string BootstrapProof = "AuditSphere synthetic E2E bootstrap proof";
  private readonly List<Process> processes = [];
  private readonly string runRoot;
  private readonly OwnedPostgresDatabase pg;

  private readonly PbcSeed.Fixture? fixture;
  public PbcSeed.Fixture Fixture => fixture ?? throw new InvalidOperationException("This host uses supported scenario setup, not the legacy PBC fixture.");
  public Guid FirmId { get; }
  public Guid RequestId { get; }
  public string ClientUrl { get; private set; } = string.Empty;
  public string StaffUrl { get; private set; } = string.Empty;
  public string RunRoot => runRoot;
  public string EvidenceRoot { get; }
  public string ConnectionString => pg.ConnectionString;
  public DbContextOptions<AuditSphereDbContext> DbOptions => pg.Options;
  public string StagingRoot => Path.Combine(runRoot, "staging");

  private OwnedHost(OwnedPostgresDatabase pg, PbcSeed.Fixture? fixture, Guid requestId, string runRoot, Guid? firmId = null)
  {
    this.pg = pg;
    this.fixture = fixture;
    FirmId = firmId ?? fixture!.FirmId;
    RequestId = requestId;
    this.runRoot = runRoot;
    // Only the explicitly allowlisted synthetic browser/lifecycle metadata goes to uploaded evidence.
    // Existing host stdout and private fixture storage stay in the original local run root.
    EvidenceRoot = Environment.GetEnvironmentVariable("VERIFICATION_LOG_DIR") is { Length: > 0 } logs
      ? Path.Combine(Path.GetFullPath(logs), "owned-hosts", Path.GetFileName(runRoot)) : runRoot;
    Directory.CreateDirectory(EvidenceRoot);
  }

  public static async Task<OwnedHost> StartAsync(
    bool startWorker = true, bool enableSetup = false, string? caseId = null,
    bool requireProtectionAttestation = false)
  {
    var repo = FindRepositoryRoot();
    OwnedPostgresDatabase? pg = null;
    OwnedHost? host = null;
    string? runRoot = null;
    try
    {
      pg = await OwnedPostgresDatabase.CreateAsync(caseId ?? (enableSetup ? "PROP-E2E-05" : "PROP-E2E-06"));
      runRoot = pg.RunRoot;
      var fixture = await PbcSeed.SeedAsync(pg);
      var requestId = await PbcSeed.CreateSentAcknowledgedRequestAsync(pg, fixture,
        PbcSeed.Actor(fixture.Staff, "Staff"), PbcSeed.Actor(fixture.Client, "ClientUser"));
      host = new OwnedHost(pg, fixture, requestId, runRoot);
      host.ClientUrl = await host.StartApiAsync(repo, fixture.Client, enableSetup);
      host.StaffUrl = await host.StartApiAsync(repo, fixture.Staff, enableSetup, requireProtectionAttestation);
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

  /// <summary>Starts no implicit identities, PBC requests or hosts. The scenario builds its entire
  /// ordinary prerequisite chain through supported owners in its isolated database.</summary>
  public static async Task<OwnedHost> StartSupportedAsync(string caseId,
    Func<ITestPostgresDatabase, Task<Guid>> initialize)
  {
    var pg = await OwnedPostgresDatabase.CreateAsync(caseId);
    try
    {
      var firmId = await initialize(pg);
      if (firmId == Guid.Empty) throw new InvalidOperationException("Supported setup must return its owned firm.");
      return new OwnedHost(pg, null, Guid.Empty, pg.RunRoot, firmId);
    }
    catch
    {
      await pg.DisposeAsync();
      throw;
    }
  }

  public AuditSphereDbContext CreateDbContext() => new(pg.Options);

  internal ITestPostgresDatabase Database => pg;

  public async Task<string> StartUnrelatedClientApiAsync()
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
      db.ClientPortalFirstSignIns.Add(PbcSeed.FirstSignIn(user));
      await db.SaveChangesAsync();
    }
    return await StartApiAsync(FindRepositoryRoot(), user, enableSetup: false);
  }

  public Task<string> StartReviewerApiAsync() =>
    StartApiAsync(FindRepositoryRoot(), Fixture.Reviewer, enableSetup: false);

  public Task<string> StartApiForIdentityAsync(AuditSphereOps.Domain.Security.AppUser identity,
    IReadOnlyDictionary<string, string>? extraSettings = null, int? loopbackPort = null) =>
    StartApiAsync(FindRepositoryRoot(), identity, enableSetup: false, extraSettings: extraSettings, loopbackPort: loopbackPort);

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
    ReportPhase("host-cleanup-started");
    try
    {
      await CleanupOwnedResourcesAsync(processes.Select(process => new ProcessCleanup(
        () => RecordLifecycle(
          $"pid={process.Id}; exitedBeforeCleanup={process.HasExited}; exitCode={(process.HasExited ? process.ExitCode.ToString() : "running")}; utc={DateTimeOffset.UtcNow:O}\n"),
        () => { if (!process.HasExited) process.Kill(entireProcessTree: true); },
        async () =>
        {
          if (!process.HasExited) await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        },
        () => { if (process.HasExited) RecordLifecycle($"stopped pid={process.Id}; exitCode={process.ExitCode}; utc={DateTimeOffset.UtcNow:O}\n"); },
        process.Dispose)),
        () => pg.DisposeAsync().AsTask(),
        () => DeleteOwnedDirectory(runRoot, Path.Combine(runRoot, "staging")),
        () => DeleteOwnedDirectory(runRoot, Path.Combine(runRoot, "provider")),
        () => DeleteOwnedDirectory(runRoot, Path.Combine(runRoot, "checkpoints")));
    }
    finally
    {
      // Attempts finished does not assert that every owned process terminated.
      ReportPhase("host-cleanup-attempts-finished");
    }
  }

  internal sealed record ProcessCleanup(Action RecordBefore, Action Stop, Func<Task> Wait,
    Action RecordAfter, Action Dispose);

  internal static async Task CleanupOwnedResourcesAsync(IEnumerable<ProcessCleanup> ownedProcesses,
    Func<Task> database, Action staging, Action provider, Action checkpoints)
  {
    List<Exception> failures = [];
    void Attempt(Action action)
    {
      try { action(); }
      catch (Exception error) { failures.Add(error); }
    }
    async Task AttemptAsync(Func<Task> action)
    {
      try { await action(); }
      catch (Exception error) { failures.Add(error); }
    }
    foreach (var process in ownedProcesses.Reverse())
    {
      Attempt(process.RecordBefore);
      Attempt(process.Stop);
      await AttemptAsync(process.Wait);
      Attempt(process.RecordAfter);
      Attempt(process.Dispose);
    }
    await AttemptAsync(database);
    Attempt(staging);
    Attempt(provider);
    Attempt(checkpoints);
    if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
    if (failures.Count > 1) throw new AggregateException("Owned host cleanup failed.", failures);
  }

  internal Action<string>? PhaseOutput { get; set; }

  internal void ReportPhase(string phase)
  {
    if (PhaseOutput is null) return;
    if (phase is not ("provisioning-finished" or "browser-ready" or "scenario-finished"
      or "successor-proof-response-observed" or "successor-proof-render-started" or "successor-proof-render-ready"
      or "portfolio-progress-response-observed" or "portfolio-progress-render-started" or "portfolio-progress-render-ready"
      or "snapshot-before-started" or "snapshot-before-finished" or "snapshot-after-started" or "snapshot-after-finished"
      or "snapshot-route-0" or "snapshot-route-1" or "snapshot-route-2" or "snapshot-route-3" or "snapshot-route-4"
      or "snapshot-route-5" or "snapshot-route-6" or "snapshot-route-7" or "snapshot-route-8" or "snapshot-route-9"
      or "snapshot-route-10" or "snapshot-route-11" or "snapshot-route-12" or "snapshot-route-13"
      or "snapshot-route-14" or "snapshot-route-15" or "snapshot-route-16"
      or "host-cleanup-started" or "host-cleanup-attempts-finished")) return;
    // Diagnostic output must not interrupt owned cleanup or change scenario results.
    try
    {
      var marker = $"phase={phase}; utc={DateTimeOffset.UtcNow:O}";
      RecordLifecycle(marker + Environment.NewLine);
      PhaseOutput(marker);
    }
    catch (Exception) { }
  }

  private void RecordLifecycle(string value)
  {
    // Evidence failure must never prevent terminating the processes this test owns.
    try { File.AppendAllText(Path.Combine(EvidenceRoot, "host-lifecycle.log"), value); }
    catch (IOException) { }
    catch (UnauthorizedAccessException) { }
  }

  private async Task<string> StartApiAsync(string repo, AuditSphereOps.Domain.Security.AppUser identity, bool enableSetup,
    bool requireProtectionAttestation = false, IReadOnlyDictionary<string, string>? extraSettings = null, int? loopbackPort = null)
  {
    // A named loopback-only port permits built-in browser inspection of this same
    // disposable fixture. Automated runs reserve an ephemeral port and retry only
    // a confirmed bind collision, closing the race between reservation and Kestrel bind.
    if (loopbackPort is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(loopbackPort));
    var attempts = loopbackPort is null ? 3 : 1;
    for (var attempt = 1; attempt <= attempts; attempt++)
    {
      var url = $"http://127.0.0.1:{loopbackPort ?? ReserveLoopbackPort()}";
      var logPath = Path.Combine(runRoot, $"api-{identity.UserKind}-{identity.Id:N}-attempt-{attempt}-{Guid.NewGuid():N}.log");
      var settings = new List<string>
      {
        "DOTNET_ENVIRONMENT", "Test",
        "ASPNETCORE_ENVIRONMENT", "Test",
        "ASPNETCORE_URLS", url,
        "ConnectionStrings__AuditSphere", pg.ConnectionString,
        "Application__FirmId", FirmId.ToString("D"),
        "Application__AllowSimulationAdapters", "true",
        "ExternalEffects__Enabled", "false",
        "DevelopmentIdentity__Enabled", "true",
        "DevelopmentIdentity__Subject", identity.Subject,
        "DevelopmentIdentity__TenantId", identity.TenantId,
        "DevelopmentIdentity__Email", identity.Email,
        "DevelopmentIdentity__DisplayName", identity.DisplayName,
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
          "Setup__BootstrapProofHash", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(BootstrapProof))).ToLowerInvariant()
        ]);
      }
      foreach (var (key, value) in extraSettings ?? new Dictionary<string, string>())
        settings.AddRange([key, value]);
      var process = StartDotnet(repo, "src/AuditSphereOps.Api/AuditSphereOps.Api.csproj", logPath,
        [.. settings]);
      processes.Add(process);
      RecordLifecycle(
        $"started pid={process.Id}; actor={identity.Id:D}; startUtc={process.StartTime.ToUniversalTime():O}; utc={DateTimeOffset.UtcNow:O}\n");
      await pg.RecordProcessAsync($"api-{identity.UserKind}", process);
      try
      {
        await WaitForReadyAsync(process, url, logPath);
        RecordLifecycle(
          $"ready pid={process.Id}; actor={identity.Id:D}; ownedListeningConfirmed=true; utc={DateTimeOffset.UtcNow:O}\n");
        return url;
      }
      catch (OwnedHostStartupException ex) when (loopbackPort is null && attempt < attempts && ex.BindCollision)
      {
        processes.Remove(process);
        process.Dispose();
      }
    }
    throw new InvalidOperationException("Could not start the owned Web host after retrying confirmed ephemeral-port collisions.");
  }

  /// <summary>
  /// Starts the isolated live selected-site PBC worker process (Acceptance, external effects, group "pbc").
  /// Used only by separately controlled live tests with private selected-site credentials.
  /// </summary>
  public async Task StartLivePbcWorkerAsync(IReadOnlyDictionary<string, string> selectedSite)
  {
    var repo = FindRepositoryRoot();
    var logPath = Path.Combine(runRoot, "worker-pbc.log");
    var pairs = new List<string>
    {
      "DOTNET_ENVIRONMENT", "Acceptance",
      "ConnectionStrings__AuditSphere", pg.ConnectionString,
      "Worker__FirmId", Fixture.FirmId.ToString("D"),
      "Worker__DeploymentEpoch", "1",
      "Worker__Group", "pbc",
      "ExternalEffects__Enabled", "true",
      "Storage__ReleaseCheckpointRoot", Path.Combine(runRoot, "checkpoints")
    };
    foreach (var (key, value) in selectedSite) pairs.AddRange([key, value]);
    var process = StartDotnet(repo, "src/AuditSphereOps.Worker/AuditSphereOps.Worker.csproj", logPath, [.. pairs]);
    processes.Add(process);
    await pg.RecordProcessAsync("worker-pbc", process);
    await Task.Delay(2000);
    if (process.HasExited)
      throw new InvalidOperationException($"The live pbc worker exited with {process.ExitCode}; see {logPath}.");
  }

  public Task StartGeneralWorkerAsync() => StartWorkerAsync(FindRepositoryRoot());

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
    if (Environment.GetEnvironmentVariable("AUDITSPHERE_TEST_ARTIFACTS_ROOT") is { Length: > 0 } artifacts)
    {
      // Isolated builds let independent test consumers retain their original binaries.
      var name = Path.GetFileNameWithoutExtension(project);
      var assembly = Path.Combine(Path.GetFullPath(artifacts), "bin", name, ConfigurationName().ToLowerInvariant(), name + ".dll");
      if (!File.Exists(assembly)) throw new InvalidOperationException("Build the selected isolated test host before starting it.");
      info.ArgumentList.Add(assembly);
    }
    else
    {
    info.ArgumentList.Add("run");
    info.ArgumentList.Add("--project");
    info.ArgumentList.Add(Path.Combine(repo, project));
    info.ArgumentList.Add("--no-launch-profile");
    info.ArgumentList.Add("--no-build");
    info.ArgumentList.Add("--no-restore");
    info.ArgumentList.Add("--configuration");
    info.ArgumentList.Add(ConfigurationName());
    }
    if (project.Contains("Api.csproj", StringComparison.Ordinal))
    {
      var urlIndex = Array.IndexOf(pairs, "ASPNETCORE_URLS");
      if (urlIndex >= 0)
      {
        if (Environment.GetEnvironmentVariable("AUDITSPHERE_TEST_ARTIFACTS_ROOT") is not { Length: > 0 }) info.ArgumentList.Add("--");
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
      k.StartsWith("TenantAdministration__", StringComparison.Ordinal) ||
      k.StartsWith("SelectedSite__", StringComparison.Ordinal) ||
      k.StartsWith("PbcTransfer__", StringComparison.Ordinal) ||
      k.StartsWith("DirectoryReader__", StringComparison.Ordinal) ||
      k.StartsWith("TenantConsent__", StringComparison.Ordinal) ||
      k is "AllowSimulationAdapters" or "ASPNETCORE_ENVIRONMENT" or "ASPNETCORE_URLS" or "DOTNET_ENVIRONMENT").ToArray())
      info.Environment.Remove(key);
    for (var i = 0; i < pairs.Length; i += 2) info.Environment[pairs[i]] = pairs[i + 1];
    info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
    info.Environment["DOTNET_NOLOGO"] = "1";
    info.Environment["Logging__LogLevel__Default"] = "Warning";
    // The exact listening announcement must come from this child, not an arbitrary health listener.
    info.Environment["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "Information";
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
    Capture($"Owned process started: pid={process.Id}; utc={DateTimeOffset.UtcNow:O}");
    process.EnableRaisingEvents = true;
    process.Exited += (_, _) => Capture($"Owned process exited: pid={process.Id}; code={process.ExitCode}; utc={DateTimeOffset.UtcNow:O}");
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
        throw new OwnedHostStartupException(process.ExitCode,
          ReadLog(logPath).Contains("address already in use", StringComparison.OrdinalIgnoreCase));
      try
      {
        // Kestrel emits this only after binding. Checking a health response alone can accept another
        // test's listener during the ephemeral-port reservation gap. Never accept that as our host.
        var listening = ReadLog(logPath).Split('\n').Any(line =>
          Regex.IsMatch(line.Trim(), @"^(?:\[\d{2}:\d{2}:\d{2} INF\] )?Now listening on: " + Regex.Escape(url) + "$",
            RegexOptions.CultureInvariant));
        if (listening)
        {
          using var response = await client.GetAsync(url + "/health/live", timeout.Token);
          if (response.StatusCode == HttpStatusCode.OK && !process.HasExited) return;
        }
      }
      catch (HttpRequestException) { }
      catch (TaskCanceledException) when (!timeout.IsCancellationRequested) { }
      await Task.Delay(200, timeout.Token);
    }
    throw new TimeoutException("The owned Web host did not become live; classification=readiness-timeout.");
  }

  // Console/TRX output is uploaded in CI. Keep raw stdout private and export only this allowlist.
  private sealed class OwnedHostStartupException(int exitCode, bool bindCollision)
    : InvalidOperationException($"The owned Web host exited with {exitCode}; classification={(bindCollision ? "bind-collision" : "process-exit")}.")
  {
    public bool BindCollision { get; } = bindCollision;
  }

  private static string ReadLog(string path) => File.Exists(path) ? File.ReadAllText(path) : "<log file not created>";

  private static void DeleteOwnedDirectory(string root, string path)
  {
    if (!path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
      throw new InvalidOperationException("Refusing to clean a path outside the scenario-owned root.");
    try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
    catch (DirectoryNotFoundException) { }
  }

  private static int ReserveLoopbackPort()
  {
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    return ((IPEndPoint)listener.LocalEndpoint).Port;
  }

  private static string ConfigurationName() =>
    Environment.GetEnvironmentVariable("AUDITSPHERE_TEST_ARTIFACTS_ROOT") is { Length: > 0 }
      ? "Release" : Directory.GetParent(AppContext.BaseDirectory)?.Parent?.Name is { Length: > 0 } name ? name : "Release";

  private static string FindRepositoryRoot()
  {
    if (Environment.GetEnvironmentVariable("AUDITSPHERE_TEST_REPOSITORY_ROOT") is { Length: > 0 } root)
    {
      root = Path.GetFullPath(root);
      if (!File.Exists(Path.Combine(root, "AuditSphereOps.slnx"))) throw new DirectoryNotFoundException("The explicit E2E repository root must contain AuditSphereOps.slnx.");
      return root;
    }
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
      if (File.Exists(Path.Combine(directory.FullName, "AuditSphereOps.slnx"))) return directory.FullName;
    for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
      if (File.Exists(Path.Combine(directory.FullName, "AuditSphereOps.slnx"))) return directory.FullName;
    throw new DirectoryNotFoundException("Could not locate AuditSphereOps.slnx from the E2E test output directory or working directory.");
  }
}
