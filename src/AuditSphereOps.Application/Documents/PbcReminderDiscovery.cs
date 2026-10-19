using System.Globalization;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Documents;

/// <summary>Explicitly opt-in policy for first-party PBC email reminders.</summary>
public sealed record PbcReminderOptions(
  bool Enabled = false,
  int DaysBeforeDue = 3,
  int OverdueIntervalDays = 3,
  string TimeZoneId = "Asia/Qatar",
  string QuietHoursStart = "21:00",
  string QuietHoursEnd = "07:00",
  int BatchSize = 50,
  string PublicBaseUrl = "")
{
  public TimeZoneInfo Validate(bool liveMailWorker)
  {
    if (DaysBeforeDue is < 0 or > 30 || OverdueIntervalDays is < 1 or > 90 ||
        BatchSize is < 1 or > 200 ||
        !TimeOnly.TryParseExact(QuietHoursStart, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
        !TimeOnly.TryParseExact(QuietHoursEnd, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
      throw new InvalidOperationException("pbc-reminder-policy-invalid");

    TimeZoneInfo zone;
    try { zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId); }
    catch (TimeZoneNotFoundException) { throw new InvalidOperationException("pbc-reminder-time-zone-invalid"); }
    catch (InvalidTimeZoneException) { throw new InvalidOperationException("pbc-reminder-time-zone-invalid"); }

    if (Enabled && !liveMailWorker)
      throw new InvalidOperationException("pbc-reminders-require-isolated-live-mail-worker");
    if (Enabled && (!Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var uri) ||
        uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(uri.Host)))
      throw new InvalidOperationException("pbc-reminder-public-url-required");
    return zone;
  }
}

/// <summary>
/// Rechecks the exact client identity and upload window whenever a reminder is queued or dispatched.
/// Callers hold a database transaction. A null result means the reminder must be suppressed.
/// </summary>
public static class PbcReminderEligibility
{
  private static readonly string[] ReminderStates =
    [PbcStates.Sent, PbcStates.Acknowledged, PbcStates.PartiallyReceived, PbcStates.ClarificationRequired];

  public static async Task<(PbcRequest Request, AppUser ClientOwner)?> LockAndResolveAsync(
    IAuditSphereDbContext db, Guid firmId, Guid clientId, Guid engagementId, Guid requestId,
    DateTimeOffset now, CancellationToken ct = default)
  {
    if (firmId == Guid.Empty || clientId == Guid.Empty || engagementId == Guid.Empty || requestId == Guid.Empty)
      return null;

    var engagement = await db.Engagements.FromSqlInterpolated($"""
      SELECT * FROM engagements WHERE firm_id = {firmId} AND practice_client_id = {clientId}
        AND id = {engagementId} FOR UPDATE
      """).AsNoTracking().SingleOrDefaultAsync(ct);
    if (engagement is null || !string.Equals(engagement.Status, "Active", StringComparison.OrdinalIgnoreCase))
      return null;

    var request = await db.PbcRequests.FromSqlInterpolated($"""
      SELECT * FROM pbc_requests WHERE firm_id = {firmId} AND client_id = {clientId}
        AND engagement_id = {engagementId} AND id = {requestId} FOR UPDATE
      """).AsNoTracking().SingleOrDefaultAsync(ct);
    if (request is null || !ReminderStates.Contains(request.State, StringComparer.Ordinal))
      return null;

    var owner = await db.Users.FromSqlInterpolated($"""
      SELECT * FROM users WHERE firm_id = {firmId} AND id = {request.ClientOwnerUserId} FOR UPDATE
      """).AsNoTracking().SingleOrDefaultAsync(ct);
    if (owner is null || owner.Disabled || owner.UserKind != "Client" || string.IsNullOrWhiteSpace(owner.Email))
      return null;

    // Only an exact client or engagement ClientUser grant is eligible. A broad firm-wide grant
    // never becomes an implicit reminder destination.
    await db.RoleGrants.FromSqlInterpolated($"""
      SELECT * FROM role_grants WHERE firm_id = {firmId} AND user_id = {owner.Id} AND role = 'ClientUser'
        AND revoked_at IS NULL AND (expires_at IS NULL OR expires_at > {now})
        AND client_id = {clientId} AND (engagement_id IS NULL OR engagement_id = {engagementId})
      FOR UPDATE
      """).AsNoTracking().ToListAsync(ct);

    var actor = new AuditSphereOps.Application.Abstractions.ActorContext(
      owner.Id, firmId, owner.SessionEpoch, ["ClientUser"], clientId, engagementId);
    var authorization = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(firmId, clientId, engagementId, ["ClientUser"]), ct);
    if (!authorization.Succeeded) return null;
    if (!(await ClientPortalService.RequireFirstSignInAsync(db, actor, ct)).Succeeded ||
        !(await ClientPortalService.RequireUploadWindowAsync(db, actor, engagementId, ct)).Succeeded)
      return null;

    return (request, owner);
  }
}

/// <summary>
/// Finds due PBC requests and appends one reminder email plus an idempotency receipt. PbcMailDiscovery
/// subsequently hands the email to the existing durable mail outbox; this class never calls a provider.
/// </summary>
public sealed class PbcReminderDiscovery(
  IAuditSphereDbContextFactory factory,
  WorkerOptions worker,
  PbcReminderOptions options,
  TimeProvider clock) : IPendingOperationDiscovery
{
  public static readonly Guid RuleRevisionId = Guid.Parse("922c35ad-ef52-4a92-ae4d-e015a3e71b09");
  private readonly SemaphoreSlim scanGate = new(1, 1);
  private (string DueDate, Guid Id)? after;

  public async Task<int> EnqueuePendingAsync(CancellationToken ct = default)
  {
    await scanGate.WaitAsync(ct);
    try { return await EnqueueNextBatchAsync(ct); }
    finally { scanGate.Release(); }
  }

  private async Task<int> EnqueueNextBatchAsync(CancellationToken ct)
  {
    if (!options.Enabled) return 0;
    var zone = options.Validate(liveMailWorker: true);
    var now = clock.GetUtcNow();
    var local = TimeZoneInfo.ConvertTime(now, zone);
    if (IsQuiet(local.TimeOfDay, ParseTime(options.QuietHoursStart), ParseTime(options.QuietHoursEnd)))
      return 0;

    var today = DateOnly.FromDateTime(local.DateTime);
    var maximumDueDate = today.AddDays(options.DaysBeforeDue).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    await using var read = await factory.CreateAsync(ct);
    var candidateQuery = read.PbcRequests.AsNoTracking()
      .Where(x => x.FirmId == worker.FirmId &&
        (x.State == PbcStates.Sent || x.State == PbcStates.Acknowledged ||
         x.State == PbcStates.PartiallyReceived || x.State == PbcStates.ClarificationRequired) &&
        string.Compare(x.DueDate, maximumDueDate) <= 0);
    if (after is { } cursor)
      candidateQuery = candidateQuery.Where(x => string.Compare(x.DueDate, cursor.DueDate) > 0 ||
        x.DueDate == cursor.DueDate && x.Id.CompareTo(cursor.Id) > 0);
    var candidates = await candidateQuery
      .OrderBy(x => x.DueDate).ThenBy(x => x.Id).Take(options.BatchSize)
      .Select(x => new { x.Id, x.ClientId, x.EngagementId, x.DueDate }).ToListAsync(ct);
    if (candidates.Count == 0)
    {
      after = null;
      return 0;
    }

    var queued = 0;
    foreach (var candidate in candidates)
    {
      await using var db = await factory.CreateAsync(ct);
      await using var tx = await db.Database.BeginTransactionAsync(ct);
      var eligible = await PbcReminderEligibility.LockAndResolveAsync(db, worker.FirmId,
        candidate.ClientId, candidate.EngagementId, candidate.Id, now, ct);
      if (eligible is null) continue;
      var request = eligible.Value.Request;
      if (!DateOnly.TryParseExact(request.DueDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
          DateTimeStyles.None, out var dueDate))
        continue;
      var actionKey = ActionKey(dueDate, today, options.OverdueIntervalDays);
      if (actionKey is null || await db.PbcReminderReceipts.AnyAsync(x =>
            x.FirmId == worker.FirmId && x.RuleRevisionId == RuleRevisionId &&
            x.PbcRequestId == request.Id && x.ActionKey == actionKey, ct))
        continue;

      if (!Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out var baseUri))
        throw new InvalidOperationException("pbc-reminder-public-url-required");
      var portalUrl = new Uri(baseUri, $"portal/requests/{request.Id:D}").AbsoluteUri;
      if (portalUrl.Length > 900) throw new InvalidOperationException("pbc-reminder-public-url-invalid");
      var communication = new PbcCommunication
      {
        Id = Guid.CreateVersion7(), FirmId = request.FirmId, ClientId = request.ClientId,
        EngagementId = request.EngagementId, PbcRequestId = request.Id,
        AuthorUserId = request.FirmOwnerUserId, Kind = PbcCommunicationKinds.Email,
        RecipientEmail = eligible.Value.ClientOwner.Email.Trim(),
        Subject = $"Reminder: {request.Area} documents are due",
        PortalUrl = portalUrl,
        Body = $"Reminder: {request.Area} documents are due {request.DueDate}. Sign in to the client portal to upload them securely: {portalUrl}",
        DeliveryState = PbcDeliveryStates.Queued, CreatedAt = now
      };
      db.PbcCommunications.Add(communication);
      db.PbcReminderReceipts.Add(new PbcReminderReceipt
      {
        Id = Guid.CreateVersion7(), FirmId = request.FirmId, ClientId = request.ClientId,
        EngagementId = request.EngagementId, PbcRequestId = request.Id,
        RuleRevisionId = RuleRevisionId, ActionKey = actionKey,
        CommunicationId = communication.Id, CreatedAt = now
      });
      await db.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);
      queued++;
    }
    // This singleton scan position only provides fairness between bounded polls.
    // Advance past skipped rows too; receipts and locked eligibility remain the
    // correctness boundary. A restart or failed page safely replays those checks.
    after = (candidates[^1].DueDate, candidates[^1].Id);
    return queued;
  }

  public static string? ActionKey(DateOnly dueDate, DateOnly today, int overdueIntervalDays)
  {
    var daysUntilDue = dueDate.DayNumber - today.DayNumber;
    if (daysUntilDue >= 0)
      return $"PRE_DUE:{dueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
    if (overdueIntervalDays < 1) return null;
    var overdueDay = -daysUntilDue;
    var window = (overdueDay - 1) / overdueIntervalDays;
    return $"OVERDUE:{window.ToString(CultureInfo.InvariantCulture)}";
  }

  public static bool IsQuiet(TimeSpan now, TimeOnly start, TimeOnly end)
  {
    if (start == end) return false;
    var time = TimeOnly.FromTimeSpan(now);
    return start < end ? time >= start && time < end : time >= start || time < end;
  }

  private static TimeOnly ParseTime(string value) =>
    TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);
}
