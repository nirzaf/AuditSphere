using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AuditSphereOps.Domain.Completion;

/// <summary>Immutable evidence of a local operator transition, not evidence of a provider outcome.</summary>
public sealed class OperatorCommandEvent
{
  public const int MaximumSnapshotLength = 150000;
  private OperatorCommandEvent() { }
  public Guid Id { get; private set; }
  public Guid FirmId { get; private set; }
  public Guid ActorId { get; private set; }
  public long ActorEpoch { get; private set; }
  public Guid RequestId { get; private set; }
  public string RequestHash { get; private set; } = "";
  public string Action { get; private set; } = "";
  public Guid? OperationId { get; private set; }
  public Guid? RecoverySessionId { get; private set; }
  public Guid? OperationEventId { get; private set; }
  public string BeforeJson { get; private set; } = "";
  public string AfterJson { get; private set; } = "";
  public DateTimeOffset CreatedAt { get; private set; }

  public static OperatorCommandEvent Create(Guid id, Guid firmId, Guid actorId, long actorEpoch,
    Guid requestId, string hash, string action, Guid? operationId, Guid? sessionId, Guid? operationEventId,
    string beforeJson, string afterJson, DateTimeOffset createdAt)
  {
    OperatorEvidenceChecks.Identity(id, firmId, actorId, actorEpoch, requestId, hash, action, createdAt);
    OperatorEvidenceChecks.Snapshot(beforeJson);
    OperatorEvidenceChecks.Snapshot(afterJson);
    if (operationId == Guid.Empty || sessionId == Guid.Empty || operationEventId == Guid.Empty ||
        (action is "RETRY" or "CANCEL") != operationId.HasValue ||
        (action is "RETRY" or "CANCEL") != operationEventId.HasValue ||
        (action is "RETRY" or "CANCEL" && sessionId.HasValue) ||
        (action == "APPROVE_RECOVERY_RESTART" && sessionId is null))
      throw new ArgumentException("The operator effect requires its exact target and real source event.");
    OperatorEvidenceChecks.Transition(action, actorId, operationId, sessionId, beforeJson, afterJson);
    return new()
    {
      Id = id, FirmId = firmId, ActorId = actorId, ActorEpoch = actorEpoch, RequestId = requestId,
      RequestHash = hash, Action = action, OperationId = operationId, RecoverySessionId = sessionId,
      OperationEventId = operationEventId, BeforeJson = beforeJson, AfterJson = afterJson, CreatedAt = createdAt.ToUniversalTime()
    };
  }
}

public sealed class OperatorCommandReceipt
{
  public const int MaximumInputLength = 65000;
  public const int MaximumPreviewLength = 250000;
  private OperatorCommandReceipt() { }
  public Guid Id { get; private set; }
  public Guid FirmId { get; private set; }
  public Guid ActorId { get; private set; }
  public long ActorEpoch { get; private set; }
  public Guid RequestId { get; private set; }
  public string RequestHash { get; private set; } = "";
  public string Action { get; private set; } = "";
  public Guid EventId { get; private set; }
  public string InputJson { get; private set; } = "";
  public string PreviewJson { get; private set; } = "";
  public DateTimeOffset CreatedAt { get; private set; }

  public static OperatorCommandReceipt Create(Guid id, OperatorCommandEvent effect, string inputJson,
    string previewJson, DateTimeOffset createdAt)
  {
    ArgumentNullException.ThrowIfNull(effect);
    OperatorEvidenceChecks.Identity(id, effect.FirmId, effect.ActorId, effect.ActorEpoch,
      effect.RequestId, effect.RequestHash, effect.Action, createdAt);
    using var input = OperatorEvidenceChecks.Object(inputJson, MaximumInputLength);
    using var preview = OperatorEvidenceChecks.Object(previewJson, MaximumPreviewLength);
    var p = preview.RootElement;
    if (input.RootElement.GetProperty("Action").GetString() != effect.Action ||
        p.GetProperty("RequestId").GetGuid() != effect.RequestId ||
        p.GetProperty("RequestHash").GetString() != effect.RequestHash ||
        !OperatorEvidenceChecks.Hash(p.GetProperty("ReviewBasis").GetString()) ||
        !p.GetProperty("CanProceed").GetBoolean() ||
        !OperatorEvidenceChecks.Equivalent(p.GetProperty("CanonicalInput").GetRawText(), inputJson) ||
        !OperatorEvidenceChecks.Equivalent(p.GetProperty("Before").GetRawText(), effect.BeforeJson))
      throw new ArgumentException("The receipt must bind the exact actor-owned event, canonical intent and reviewed before-state.");
    return new()
    {
      Id = id, FirmId = effect.FirmId, ActorId = effect.ActorId, ActorEpoch = effect.ActorEpoch,
      RequestId = effect.RequestId, RequestHash = effect.RequestHash, Action = effect.Action,
      EventId = effect.Id, InputJson = inputJson, PreviewJson = previewJson, CreatedAt = createdAt.ToUniversalTime()
    };
  }
}

internal static class OperatorEvidenceChecks
{
  internal static bool Hash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
  internal static void Identity(Guid id, Guid firm, Guid actor, long epoch, Guid request, string hash, string action, DateTimeOffset at)
  {
    if (id == Guid.Empty || firm == Guid.Empty || actor == Guid.Empty || epoch < 1 || request == Guid.Empty ||
        !Hash(hash) || action is not ("RETRY" or "CANCEL" or "LIFT_QUARANTINE" or "APPROVE_RECOVERY_RESTART") || at == default)
      throw new ArgumentException("A bounded current actor/request identity is required.");
  }
  internal static JsonDocument Object(string json, int maximum)
  {
    if (string.IsNullOrWhiteSpace(json) || json.Length > maximum) throw new ArgumentException("Evidence exceeds its bound.");
    var document = JsonDocument.Parse(json);
    if (document.RootElement.ValueKind == JsonValueKind.Object) return document;
    document.Dispose();
    throw new ArgumentException("Evidence must be an object.");
  }
  internal static void Snapshot(string json)
  {
    using var document = Object(json, OperatorCommandEvent.MaximumSnapshotLength);
    var root = document.RootElement;
    Number(root.GetProperty("DeploymentEpoch"), 1);
    Number(root.GetProperty("RecoveryEpoch"), 0);
    Number(root.GetProperty("PolicyGeneration"), 1);
    if (root.GetProperty("Operation") is { ValueKind: JsonValueKind.Object } op)
    {
      Number(op.GetProperty("ExpectedRevision"), 1);
      Number(op.GetProperty("AttemptToken"), 0);
      Number(op.GetProperty("AttemptCount"), 0);
    }
    if (root.GetProperty("RecoverySession") is { ValueKind: JsonValueKind.Object } session)
      Number(session.GetProperty("ExternalEpoch"), 1);
  }
  internal static void Transition(string action, Guid actorId, Guid? operationId, Guid? sessionId, string beforeJson, string afterJson)
  {
    using var before = JsonDocument.Parse(beforeJson);
    using var after = JsonDocument.Parse(afterJson);
    var b = before.RootElement;
    var a = after.RootElement;
    if (Equivalent(beforeJson, afterJson)) throw new ArgumentException("A new request cannot attribute a no-op to itself.");
    if (action is "RETRY" or "CANCEL")
    {
      var oldOperation = b.GetProperty("Operation");
      var newOperation = a.GetProperty("Operation");
      if (oldOperation.GetProperty("Id").GetGuid() != operationId || newOperation.GetProperty("Id").GetGuid() != operationId ||
          oldOperation.GetProperty("AttemptToken").GetString() != newOperation.GetProperty("AttemptToken").GetString() ||
          oldOperation.GetProperty("HasLease").GetBoolean() || newOperation.GetProperty("HasLease").GetBoolean() ||
          b.GetProperty("DeploymentEpoch").GetString() != a.GetProperty("DeploymentEpoch").GetString() ||
          b.GetProperty("RecoveryEpoch").GetString() != a.GetProperty("RecoveryEpoch").GetString() ||
          b.GetProperty("OperatingMode").GetString() != a.GetProperty("OperatingMode").GetString())
        throw new ArgumentException("An operator command must bind its exact unleased operation attempt.");
      if (!EqualExcept(b, a, "Operation") || !EqualExcept(oldOperation, newOperation,
          action == "RETRY" ? ["Status", "AttemptCount", "IsReconciliation", "ErrorCode", "NextAttemptAt"] : ["Status", "CancellationDisposition", "ErrorCode", "NextAttemptAt"]))
        throw new ArgumentException("An operator transition cannot rewrite unrelated state or target identity.");
      var oldStatus = oldOperation.GetProperty("Status").GetString();
      var newStatus = newOperation.GetProperty("Status").GetString();
      if (action == "RETRY" && (oldStatus is not ("PROVIDER_BLOCKED" or "DEAD_LETTER" or "AUTHORIZATION_BLOCKED" or "RESULT_UNCERTAIN") ||
          newStatus != "RETRY_WAIT" || newOperation.GetProperty("AttemptCount").GetString() != "0" ||
          newOperation.GetProperty("IsReconciliation").GetBoolean() != (oldOperation.GetProperty("IsReconciliation").GetBoolean() || oldStatus == "RESULT_UNCERTAIN")))
        throw new ArgumentException("Retry must preserve reconciliation and change a retryable state.");
      if (action == "CANCEL" && (oldStatus is not ("PENDING" or "RETRY_WAIT") || oldOperation.GetProperty("IsReconciliation").GetBoolean() ||
          newStatus != "CANCELLED_WITH_DISPOSITION" || string.IsNullOrWhiteSpace(newOperation.GetProperty("CancellationDisposition").GetString())))
        throw new ArgumentException("Cancellation requires queued work without an unknown effect.");
    }
    else
    {
      if (b.GetProperty("Operation").ValueKind != JsonValueKind.Null || a.GetProperty("Operation").ValueKind != JsonValueKind.Null ||
          b.GetProperty("OperatingMode").GetString() != "RECOVERY_QUARANTINE" || a.GetProperty("OperatingMode").GetString() != "LOCAL_ONLY" ||
          b.GetProperty("RecoveryEpoch").GetString() != a.GetProperty("RecoveryEpoch").GetString() ||
          long.Parse(a.GetProperty("DeploymentEpoch").GetString()!, CultureInfo.InvariantCulture) !=
          checked(long.Parse(b.GetProperty("DeploymentEpoch").GetString()!, CultureInfo.InvariantCulture) + 1))
        throw new ArgumentException("A recovery effect must lift the exact quarantine once.");
      if (!EqualExcept(b, a, action == "LIFT_QUARANTINE" ? ["OperatingMode", "DeploymentEpoch"] : ["OperatingMode", "DeploymentEpoch", "RecoverySession"]))
        throw new ArgumentException("Recovery cannot rewrite unrelated firm state.");
      var oldSession = b.GetProperty("RecoverySession");
      var newSession = a.GetProperty("RecoverySession");
      if (sessionId is not null && (oldSession.GetProperty("Id").GetGuid() != sessionId || newSession.GetProperty("Id").GetGuid() != sessionId))
        throw new ArgumentException("Recovery must bind the exact session.");
      if (sessionId is null && (oldSession.ValueKind != JsonValueKind.Null || newSession.ValueKind != JsonValueKind.Null || b.GetProperty("RecoveryEpoch").GetString() != "0"))
        throw new ArgumentException("Only the guarded original sessionless quarantine may omit a session.");
      if (action == "APPROVE_RECOVERY_RESTART" && (!EqualExcept(oldSession, newSession, "Findings", "ApprovedRestartAt", "ApprovedByUserId") || oldSession.GetProperty("ApprovedRestartAt").ValueKind != JsonValueKind.Null ||
          newSession.GetProperty("ApprovedRestartAt").ValueKind == JsonValueKind.Null || newSession.GetProperty("ApprovedByUserId").ValueKind == JsonValueKind.Null || newSession.GetProperty("ApprovedByUserId").GetGuid() != actorId))
        throw new ArgumentException("Approval must record a new decision before restart.");
      if (action == "LIFT_QUARANTINE" && !Equivalent(oldSession.GetRawText(), newSession.GetRawText()))
        throw new ArgumentException("Lifting cannot manufacture a recovery approval.");
    }
  }
  internal static bool Equivalent(string left, string right) => JsonNode.DeepEquals(Normalize(JsonNode.Parse(left)), Normalize(JsonNode.Parse(right)));
  private static bool EqualExcept(JsonElement left, JsonElement right, params string[] fields)
  {
    var l = JsonNode.Parse(left.GetRawText())!.AsObject();
    var r = JsonNode.Parse(right.GetRawText())!.AsObject();
    foreach (var field in fields) { l.Remove(field); r.Remove(field); }
    return JsonNode.DeepEquals(Normalize(l), Normalize(r));
  }
  private static JsonNode? Normalize(JsonNode? node)
  {
    if (node is JsonObject obj)
    {
      foreach (var key in obj.Select(x => x.Key).ToArray())
      {
        if (key is "CreatedAt" or "NextAttemptAt" or "CompletedAt" or "ApprovedRestartAt" && obj[key] is JsonValue value &&
            value.TryGetValue<string>(out var text) && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
          obj[key] = time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        else Normalize(obj[key]);
      }
    }
    return node;
  }
  private static void Number(JsonElement value, long minimum)
  {
    var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < minimum ||
        number.ToString(CultureInfo.InvariantCulture) != text) throw new ArgumentException("Counters must be canonical exact decimal strings.");
  }
}
