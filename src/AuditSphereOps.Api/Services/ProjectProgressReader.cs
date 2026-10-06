using System.Text.Json;

namespace AuditSphereOps.Api.Services;

/// <summary>Reads the published R2R task-card snapshot; never changes task status or application data.</summary>
public static class ProjectProgressReader
{
  public const int FreshnessWindowDays = 30;
  private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(5);
  public static IReadOnlyList<ProjectUntrackedArea> UntrackedAreas { get; } =
  [
    new("Practice leads", "/app/practice/leads"),
    new("Time and approvals", "/app/practice/time"),
    new("Firm finance", "/app/finance"),
    new("Documents and client portal", null),
    new("Durable operations", "/app/operations"),
    new("Firm and Microsoft 365 administration", "/app/administration")
  ];
  private static readonly IReadOnlyDictionary<int, string> ModuleNames = new Dictionary<int, string>
  {
    [20] = "Accounting setup",
    [21] = "Trial balance & general ledger",
    [22] = "Adjustments & journals",
    [23] = "Reconciliations & specialist schedules",
    [24] = "Financial statements",
    [25] = "Financial packages",
    [26] = "Group consolidation"
  };
  private static readonly IReadOnlyDictionary<int, string> AuditPhaseNames = new Dictionary<int, string>
  {
    [17] = "Audit foundation",
    [18] = "Core fieldwork",
    [19] = "Extended fieldwork",
    [20] = "Audit completion"
  };

  public static ProjectProgressSnapshot Read(string contentDirectory, DateTimeOffset? nowUtc = null)
  {
    var root = Path.GetFullPath(contentDirectory);
    using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tracking", "pack_manifest.json")));
    if (manifest.RootElement.ValueKind != JsonValueKind.Object)
      throw new InvalidDataException("Published task-card manifest is invalid.");
    if (!manifest.RootElement.TryGetProperty("published_at_utc", out var publishedAtValue) ||
        publishedAtValue.ValueKind != JsonValueKind.String || !publishedAtValue.TryGetDateTimeOffset(out var publishedAtUtc) ||
        publishedAtUtc.Offset != TimeSpan.Zero)
      throw new InvalidDataException("Published task-card manifest has no valid UTC publication time.");
    var observedAtUtc = (nowUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
    if (publishedAtUtc > observedAtUtc + AllowedClockSkew)
      throw new InvalidDataException("Published task-card manifest has a future publication time.");
    if (!manifest.RootElement.TryGetProperty("tasks", out var taskEntries) || taskEntries.ValueKind != JsonValueKind.Array)
      throw new InvalidDataException("Published task-card manifest is invalid.");
    var publicationAge = observedAtUtc - publishedAtUtc;
    var publicationAgeDays = Math.Max(0, (int)Math.Floor(publicationAge.TotalDays));
    var isStale = publicationAge > TimeSpan.FromDays(FreshnessWindowDays);
    var publishedTasks = taskEntries.EnumerateArray().ToArray();
    if (publishedTasks.Length == 0)
      throw new InvalidDataException("Published task-card manifest contains no task cards.");
    var rows = new List<ProjectTaskProgress>();
    foreach (var item in publishedTasks)
    {
      if (item.ValueKind != JsonValueKind.Object)
        throw new InvalidDataException("Published task-card entry is invalid.");
      var id = item.GetProperty("id").GetString() ?? throw new InvalidDataException("Task ID is missing.");
      var relative = item.GetProperty("file").GetString() ?? throw new InvalidDataException($"Task {id} has no file.");
      var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
      if (!full.StartsWith(Path.Combine(root, "tasks") + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
          !File.Exists(full))
        throw new InvalidDataException($"Task {id} is missing from the published tracker.");
      var lines = File.ReadAllLines(full);
      if (lines.Length < 3 || lines[0] != "---") throw new InvalidDataException($"Task {id} has invalid front matter.");
      var fields = new Dictionary<string, string>(StringComparer.Ordinal);
      var index = 1;
      for (; index < lines.Length && lines[index] != "---"; index++)
      {
        var separator = lines[index].IndexOf(':');
        if (separator < 1) continue;
        var value = lines[index][(separator + 1)..].Trim();
        try { fields[lines[index][..separator]] = JsonSerializer.Deserialize<string>(value) ?? string.Empty; }
        catch (JsonException) { /* Array and Boolean fields are not displayed by this read model. */ }
      }
      if (index == lines.Length || fields.GetValueOrDefault("id") != id ||
          !fields.TryGetValue("status", out var status) || !TaskStates.Contains(status))
        throw new InvalidDataException($"Task {id} has inconsistent tracking metadata.");
      var title = lines.Skip(index + 1).FirstOrDefault(x => x.StartsWith("# ", StringComparison.Ordinal))?[2..] ?? id;
      if (title.StartsWith(id + " — ", StringComparison.Ordinal)) title = title[(id.Length + 3)..];
      var modules = item.GetProperty("module_ids").EnumerateArray().Select(x => x.GetInt32()).ToArray();
      if (modules.Any(x => !ModuleNames.ContainsKey(x)) || modules.Distinct().Count() != modules.Length ||
          rows.Any(x => x.Id == id))
        throw new InvalidDataException($"Task {id} has inconsistent module or ID tracking metadata.");
      var workPackage = item.GetProperty("work_package").GetString() ?? "";
      if (workPackage.StartsWith("AUD-", StringComparison.Ordinal) &&
          !AuditPhaseNames.Keys.Any(phase => workPackage == $"AUD-{phase}"))
        throw new InvalidDataException($"Task {id} has an unknown audit phase.");
      rows.Add(new(id, title, status, workPackage,
        modules, fields.GetValueOrDefault("blocked_reason") ?? string.Empty));
    }
    return new(rows.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray(),
      ModuleNames.Select(x => new ProjectModuleProgress(x.Key, x.Value,
        rows.Where(t => t.Modules.Contains(x.Key)).OrderBy(t => t.Id, StringComparer.Ordinal).ToArray())).ToArray(),
      rows.Where(x => x.WorkPackage.StartsWith("AUD-", StringComparison.Ordinal)).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray(),
      rows.Where(x => x.Modules.Count == 0 && !x.WorkPackage.StartsWith("AUD-", StringComparison.Ordinal))
        .OrderBy(x => x.Id, StringComparer.Ordinal).ToArray(),
      AuditPhaseNames.Select(x => new ProjectModuleProgress(x.Key, x.Value,
        rows.Where(t => t.WorkPackage == $"AUD-{x.Key}").OrderBy(t => t.Id, StringComparer.Ordinal).ToArray())).ToArray(),
      publishedAtUtc, publicationAgeDays, isStale);
  }

  private static readonly HashSet<string> TaskStates =
    ["NOT_STARTED", "IN_PROGRESS", "IN_REVIEW", "BLOCKED", "COMPLETED", "REOPENED"];
}

public sealed record ProjectUntrackedArea(string Name, string? Route);
public sealed record ProjectTaskProgress(string Id, string Title, string Status, string WorkPackage,
  IReadOnlyList<int> Modules, string BlockedReason);
public sealed record ProjectModuleProgress(int Number, string Name, IReadOnlyList<ProjectTaskProgress> Tasks)
{
  public int Completed => Tasks.Count(x => x.Status == "COMPLETED");
  public int CompletionPercent => Tasks.Count == 0 ? 0 : Completed * 100 / Tasks.Count;
  public int Active => Tasks.Count(x => x.Status is "IN_PROGRESS" or "IN_REVIEW");
  public int Blocked => Tasks.Count(x => x.Status == "BLOCKED");
  public int Pending => Tasks.Count - Completed - Active - Blocked;
}
public sealed record ProjectProgressSnapshot(IReadOnlyList<ProjectTaskProgress> Tasks,
  IReadOnlyList<ProjectModuleProgress> Modules, IReadOnlyList<ProjectTaskProgress> AuditTasks,
  IReadOnlyList<ProjectTaskProgress> SharedTasks, IReadOnlyList<ProjectModuleProgress> AuditPhases,
  DateTimeOffset PublishedAtUtc, int PublicationAgeDays, bool IsStale)
{
  public int FreshnessWindowDays => ProjectProgressReader.FreshnessWindowDays;
  public int AuditCompleted => AuditTasks.Count(x => x.Status == "COMPLETED");
  public int SharedCompleted => SharedTasks.Count(x => x.Status == "COMPLETED");
  public int Completed => Tasks.Count(x => x.Status == "COMPLETED");
  public int CompletionPercent => Tasks.Count == 0 ? 0 : Completed * 100 / Tasks.Count;
  public int Active => Tasks.Count(x => x.Status is "IN_PROGRESS" or "IN_REVIEW");
  public int Blocked => Tasks.Count(x => x.Status == "BLOCKED");
  public int Pending => Tasks.Count - Completed - Active - Blocked;
}
