using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Microsoft365;

public static class FolderTemplatePurposes
{
  public const string ClientWorkspace = "CLIENT_WORKSPACE";
  public const string EngagementWorkspace = "ENGAGEMENT_WORKSPACE";
}

public sealed record SaveFolderTemplateRequest(
  string Purpose,
  string ManifestJson,
  long? ExpectedCurrentVersion = null);

public sealed record FolderTemplateResult(
  Guid Id,
  string Purpose,
  long Version,
  string ManifestJson,
  string ManifestDigest,
  int NodeCount);

public sealed record FolderTemplateValidation(
  string Purpose,
  string CanonicalManifestJson,
  string ManifestDigest,
  int NodeCount);

public sealed record PrepareConnectionRevisionRequest(
  Guid SetupDraftId,
  long ExpectedDraftRevision,
  string LoginClientIdReference,
  string RuntimeCredentialReference,
  string CloudProfile = "PUBLIC");

public sealed record RecordVerificationEvidenceRequest(
  Guid SetupDraftId,
  Guid ConnectionRevisionId,
  string ResourceKind,
  string ResourceId,
  string Operation,
  string IdentityReference,
  string Result,
  string EvidenceReference);

public sealed record ActivateConnectionRequest(
  Guid SetupDraftId,
  Guid ConnectionRevisionId,
  Guid FolderTemplateVersionId,
  long ExpectedDraftRevision,
  string SiteId,
  string DriveId,
  string RootFolderId,
  string DisplayUrl,
  string AccessProfile);

/// <summary>
/// Local template/configuration boundary. It never resolves a Graph path or marks a
/// Microsoft capability verified; those effects require a separate provider proof.
/// </summary>
public static class Microsoft365ConfigurationService
{
  private static readonly HashSet<string> AllowedTokens = new(StringComparer.Ordinal)
  {
    "{{CLIENT_CODE}}", "{{CLIENT_NAME}}", "{{ENGAGEMENT_CODE}}", "{{SERVICE}}", "{{PERIOD}}"
  };

  private static readonly string[] RequiredEvidenceKinds = ["TENANT", "SITE", "DRIVE", "ROOT"];

  public static async Task<CommandResult<Guid>> PrepareConnectionRevisionAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    PrepareConnectionRevisionRequest request,
    DateTimeOffset now,
    CancellationToken ct = default)
  {
    if (request.SetupDraftId == Guid.Empty || request.ExpectedDraftRevision < 1 ||
        !CredentialReference(request.LoginClientIdReference) || !CredentialReference(request.RuntimeCredentialReference) ||
        !string.Equals(request.CloudProfile.Trim(), "PUBLIC", StringComparison.OrdinalIgnoreCase))
      return CommandResult<Guid>.Fail("m365.connection.invalid", "A deployment-approved credential reference and public-cloud profile are required.");
    var auth = await FirmAdministratorAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var draft = await db.Microsoft365SetupDrafts.SingleOrDefaultAsync(x =>
      x.Id == request.SetupDraftId && x.FirmId == actor.FirmId, ct);
    if (draft is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The setup draft is unavailable.");
    if (draft.State == Microsoft365RevisionStates.Active)
      return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "The active connection is immutable; create a new configuration draft for reconfiguration.");
    if (draft.Revision != request.ExpectedDraftRevision)
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "The setup draft changed; reload it before connecting.");
    if (string.IsNullOrWhiteSpace(draft.ExpectedTenantId))
      return CommandResult<Guid>.Fail("m365.connection.invalid", "A verified tenant identifier is required before connection preparation.");
    if (draft.ConnectionRevisionId is { } existingId)
    {
      var existing = await db.Microsoft365ConnectionRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == existingId && x.FirmId == actor.FirmId, ct);
      if (existing is not null &&
          string.Equals(existing.LoginClientIdReference, request.LoginClientIdReference.Trim(), StringComparison.Ordinal) &&
          string.Equals(existing.RuntimeCredentialReference, request.RuntimeCredentialReference.Trim(), StringComparison.Ordinal) &&
          string.Equals(existing.CloudProfile, request.CloudProfile.Trim(), StringComparison.OrdinalIgnoreCase))
      {
        await tx.CommitAsync(ct);
        return CommandResult<Guid>.Ok(existing.Id);
      }
    }

    var revision = await db.Microsoft365ConnectionRevisions
      .Where(x => x.FirmId == actor.FirmId)
      .OrderByDescending(x => x.Revision).Select(x => (long?)x.Revision).FirstOrDefaultAsync(ct) ?? 0;
    var connection = new Microsoft365ConnectionRevision
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, Revision = revision + 1,
      TenantId = draft.ExpectedTenantId.Trim(),
      LoginClientIdReference = request.LoginClientIdReference.Trim(),
      RuntimeCredentialReference = request.RuntimeCredentialReference.Trim(),
      CloudProfile = "PUBLIC", State = Microsoft365RevisionStates.ConsentRequired,
      ConsentState = "REQUIRED", CreatedByUserId = actor.UserId, CreatedAt = now
    };
    db.Microsoft365ConnectionRevisions.Add(connection);
    draft.ConnectionRevisionId = connection.Id;
    draft.State = Microsoft365RevisionStates.ConsentRequired;
    draft.Revision++;
    draft.UpdatedAt = now;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(connection.Id);
  }

  public static async Task<CommandResult> RecordVerificationEvidenceAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    RecordVerificationEvidenceRequest request,
    DateTimeOffset now,
    CancellationToken ct = default)
  {
    var resourceKind = request.ResourceKind.Trim().ToUpperInvariant();
    var result = request.Result.Trim().ToUpperInvariant();
    if (request.SetupDraftId == Guid.Empty || request.ConnectionRevisionId == Guid.Empty ||
        resourceKind is not ("TENANT" or "SITE" or "DRIVE" or "ROOT") ||
        string.IsNullOrWhiteSpace(request.ResourceId) || string.IsNullOrWhiteSpace(request.Operation) ||
        string.IsNullOrWhiteSpace(request.IdentityReference) || string.IsNullOrWhiteSpace(request.EvidenceReference) ||
        result is not ("PASS" or "FAIL" or "BLOCKED"))
      return CommandResult.Fail("m365.verification.invalid", "Verification evidence is incomplete or uses an unsupported resource/result.");
    var auth = await FirmAdministratorAsync(db, actor, ct);
    if (!auth.Succeeded) return auth;

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var draft = await db.Microsoft365SetupDrafts.SingleOrDefaultAsync(x =>
      x.Id == request.SetupDraftId && x.FirmId == actor.FirmId, ct);
    var connection = await db.Microsoft365ConnectionRevisions.SingleOrDefaultAsync(x =>
      x.Id == request.ConnectionRevisionId && x.FirmId == actor.FirmId, ct);
    if (draft is null || connection is null || draft.ConnectionRevisionId != connection.Id)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "The verification revision is unavailable.");
    if (connection.State == Microsoft365RevisionStates.Active)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Active connection evidence cannot be amended; create a new revision.");

    db.IntegrationVerificationEvidences.Add(new IntegrationVerificationEvidence
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, SetupDraftId = draft.Id,
      ConnectionRevisionId = connection.Id, ResourceKind = resourceKind,
      ResourceId = request.ResourceId.Trim(), Operation = request.Operation.Trim(),
      IdentityReference = request.IdentityReference.Trim(), Result = result,
      EvidenceReference = request.EvidenceReference.Trim(), ObservedAt = now
    });
    await db.SaveChangesAsync(ct);
    draft.State = result == "PASS" ? Microsoft365RevisionStates.Validating : Microsoft365RevisionStates.Blocked;
    connection.State = result == "PASS" ? Microsoft365RevisionStates.Validating : Microsoft365RevisionStates.Blocked;
    if (result != "PASS") connection.ConsentState = "BLOCKED";

    if (result == "PASS")
    {
      var requiredIds = new Dictionary<string, string?>(StringComparer.Ordinal)
      {
        ["TENANT"] = draft.ExpectedTenantId,
        ["SITE"] = draft.SiteId,
        ["DRIVE"] = draft.DriveId,
        ["ROOT"] = draft.RootFolderId
      };
      var passes = await db.IntegrationVerificationEvidences.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.SetupDraftId == draft.Id &&
                    x.ConnectionRevisionId == connection.Id && x.Result == "PASS")
        .ToListAsync(ct);
      var allResources = RequiredEvidenceKinds.All(kind =>
        requiredIds[kind] is { Length: > 0 } expected &&
        passes.Any(x => x.ResourceKind == kind && x.ResourceId == expected));
      var consentObserved = passes.Any(x => x.ResourceKind == "TENANT" && x.Operation == "CONSENT");
      if (allResources && consentObserved)
      {
        draft.State = Microsoft365RevisionStates.Verified;
        connection.State = Microsoft365RevisionStates.Verified;
        connection.ConsentState = "OBSERVED";
        connection.VerifiedAt = now;
      }
    }
    draft.Revision++;
    draft.UpdatedAt = now;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<Guid>> ActivateConnectionAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    ActivateConnectionRequest request,
    DateTimeOffset now,
    CancellationToken ct = default)
  {
    if (request.SetupDraftId == Guid.Empty || request.ConnectionRevisionId == Guid.Empty ||
        request.FolderTemplateVersionId == Guid.Empty || request.ExpectedDraftRevision < 1 ||
        string.IsNullOrWhiteSpace(request.SiteId) || string.IsNullOrWhiteSpace(request.DriveId) ||
        string.IsNullOrWhiteSpace(request.RootFolderId) || !Https(request.DisplayUrl) ||
        request.AccessProfile is not (Microsoft365AccessProfiles.AppMediated or Microsoft365AccessProfiles.DirectStaffCollaboration))
      return CommandResult<Guid>.Fail("m365.activation.invalid", "A verified site, library, root, HTTPS URL, template and access profile are required.");
    var auth = await FirmAdministratorAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var draft = await db.Microsoft365SetupDrafts.SingleOrDefaultAsync(x =>
      x.Id == request.SetupDraftId && x.FirmId == actor.FirmId, ct);
    var connection = await db.Microsoft365ConnectionRevisions.SingleOrDefaultAsync(x =>
      x.Id == request.ConnectionRevisionId && x.FirmId == actor.FirmId, ct);
    var template = await db.FolderTemplateVersions.SingleOrDefaultAsync(x =>
      x.Id == request.FolderTemplateVersionId && x.FirmId == actor.FirmId &&
      x.Purpose == FolderTemplatePurposes.ClientWorkspace, ct);
    if (draft is null || connection is null || template is null || draft.ConnectionRevisionId != connection.Id)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The activation records are unavailable.");
    if (draft.Revision != request.ExpectedDraftRevision)
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "The setup draft changed; reload before activation.");
    if (connection.State != Microsoft365RevisionStates.Verified || template.ApprovedAt is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Connection evidence and an approved client template are required before activation.");
    if (!string.Equals(draft.ExpectedTenantId, connection.TenantId, StringComparison.Ordinal) ||
        !string.Equals(draft.SiteId, request.SiteId.Trim(), StringComparison.Ordinal) ||
        !string.Equals(draft.DriveId, request.DriveId.Trim(), StringComparison.Ordinal) ||
        !string.Equals(draft.RootFolderId, request.RootFolderId.Trim(), StringComparison.Ordinal) ||
        !string.Equals(draft.AccessProfile, request.AccessProfile, StringComparison.Ordinal))
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "The activation binding does not match the verified draft.");

    var requiredIds = new Dictionary<string, string>(StringComparer.Ordinal)
    {
      ["TENANT"] = connection.TenantId, ["SITE"] = draft.SiteId!,
      ["DRIVE"] = draft.DriveId!, ["ROOT"] = draft.RootFolderId!
    };
    var evidence = await db.IntegrationVerificationEvidences.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.SetupDraftId == draft.Id &&
                  x.ConnectionRevisionId == connection.Id && x.Result == "PASS")
      .ToListAsync(ct);
    if (!RequiredEvidenceKinds.All(kind => evidence.Any(x => x.ResourceKind == kind && x.ResourceId == requiredIds[kind])) ||
        !evidence.Any(x => x.ResourceKind == "TENANT" && x.Operation == "CONSENT"))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The exact tenant, site, library, root and consent evidence is incomplete.");

    var existing = await db.FirmWorkspaceConfigurations.SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ConnectionRevisionId == connection.Id, ct);
    if (existing is not null)
    {
      await tx.CommitAsync(ct);
      return CommandResult<Guid>.Ok(existing.Id);
    }
    await db.FirmWorkspaceConfigurations.Where(x => x.FirmId == actor.FirmId && x.DefaultForFutureClients)
      .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.DefaultForFutureClients, false), ct);
    var workspace = new FirmWorkspaceConfiguration
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ConnectionRevisionId = connection.Id,
      TenantId = connection.TenantId, SiteId = draft.SiteId!, DriveId = draft.DriveId!,
      RootFolderId = draft.RootFolderId!, DisplayUrl = request.DisplayUrl.Trim(),
      AccessProfile = draft.AccessProfile, FolderTemplateVersionId = template.Id,
      DefaultForFutureClients = true, CreatedAt = now
    };
    db.FirmWorkspaceConfigurations.Add(workspace);
    connection.State = Microsoft365RevisionStates.Active;
    connection.ApprovedByUserId = actor.UserId;
    connection.ActivatedAt = now;
    draft.State = Microsoft365RevisionStates.Active;
    draft.Revision++;
    draft.UpdatedAt = now;
    var session = await db.Microsoft365SetupSessions.SingleOrDefaultAsync(x =>
      x.Id == draft.SetupSessionId && x.FirmId == actor.FirmId, ct);
    if (session is not null)
    {
      session.State = Microsoft365SetupStates.Active;
      session.ConsumedAt = now;
      session.Revision++;
    }
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(workspace.Id);
  }

  public static async Task<CommandResult> ApproveFolderTemplateAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid templateId, DateTimeOffset now,
    CancellationToken ct = default)
  {
    if (templateId == Guid.Empty) return CommandResult.Fail("m365.template.invalid", "A template is required.");
    var auth = await FirmAdministratorAsync(db, actor, ct);
    if (!auth.Succeeded) return auth;
    var template = await db.FolderTemplateVersions.SingleOrDefaultAsync(x =>
      x.Id == templateId && x.FirmId == actor.FirmId, ct);
    if (template is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "The template is unavailable.");
    if (template.ApprovedAt is null)
    {
      template.ApprovedAt = now;
      template.ApprovedByUserId = actor.UserId;
      await db.SaveChangesAsync(ct);
    }
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<FolderTemplateResult>> SaveFolderTemplateAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    SaveFolderTemplateRequest request,
    DateTimeOffset now,
    CancellationToken ct = default)
  {
    var validation = ValidateFolderTemplate(request.Purpose, request.ManifestJson);
    if (!validation.Succeeded || validation.Value is null)
      return CommandResult<FolderTemplateResult>.Fail(validation.ErrorCode!, validation.Message!);
    if (request.ExpectedCurrentVersion is < 0)
      return CommandResult<FolderTemplateResult>.Fail("m365.template.invalid", "The expected template version is invalid.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: ["Administrator"], InternalOnly: true,
        RequireFirmWide: true), ct);
    if (!auth.Succeeded)
      return CommandResult<FolderTemplateResult>.Fail(auth.ErrorCode!, auth.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var current = await db.FolderTemplateVersions.FromSqlInterpolated($"""
      SELECT * FROM m365_folder_template_versions
      WHERE firm_id = {actor.FirmId} AND purpose = {validation.Value.Purpose}
      ORDER BY version DESC
      LIMIT 1
      FOR UPDATE
      """).SingleOrDefaultAsync(ct);

    if (request.ExpectedCurrentVersion.HasValue &&
        request.ExpectedCurrentVersion.Value != (current?.Version ?? 0))
      return CommandResult<FolderTemplateResult>.Fail(ErrorCodes.StaleRevision,
        "The template changed; reload the current version before saving.");

    if (current is not null && current.ManifestDigest == validation.Value.ManifestDigest)
    {
      await tx.CommitAsync(ct);
      return CommandResult<FolderTemplateResult>.Ok(ToResult(current, validation.Value.NodeCount));
    }

    var template = new FolderTemplateVersion
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId,
      Version = (current?.Version ?? 0) + 1, Purpose = validation.Value.Purpose,
      ManifestJson = validation.Value.CanonicalManifestJson,
      ManifestDigest = validation.Value.ManifestDigest, CreatedByUserId = actor.UserId,
      CreatedAt = now
    };
    db.FolderTemplateVersions.Add(template);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<FolderTemplateResult>.Ok(ToResult(template, validation.Value.NodeCount));
  }

  public static CommandResult<FolderTemplateValidation> ValidateFolderTemplate(
    string purpose, string manifestJson)
  {
    purpose = purpose.Trim().ToUpperInvariant();
    if (purpose is not (FolderTemplatePurposes.ClientWorkspace or FolderTemplatePurposes.EngagementWorkspace))
      return CommandResult<FolderTemplateValidation>.Fail("m365.template.invalid", "The folder-template purpose is not supported.");
    if (string.IsNullOrWhiteSpace(manifestJson) || manifestJson.Length > 20_000)
      return CommandResult<FolderTemplateValidation>.Fail("m365.template.invalid", "The folder-template manifest is required and bounded.");

    try
    {
      using var document = JsonDocument.Parse(manifestJson, new JsonDocumentOptions { MaxDepth = 16 });
      if (document.RootElement.ValueKind != JsonValueKind.Object ||
          !document.RootElement.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
        return CommandResult<FolderTemplateValidation>.Fail("m365.template.invalid", "A template must contain a nodes array.");

      var keys = new HashSet<string>(StringComparer.Ordinal);
      var nodeCount = 0;
      foreach (var node in nodes.EnumerateArray())
      {
        var result = ValidateNode(node, keys, ref nodeCount, 1, 0);
        if (!result.Succeeded)
          return CommandResult<FolderTemplateValidation>.Fail(result.ErrorCode!, result.Message!);
      }
      if (nodeCount is < 1 or > 200)
        return CommandResult<FolderTemplateValidation>.Fail("m365.template.invalid", "A template must contain between 1 and 200 folders.");

      var canonical = CanonicalJson(document.RootElement);
      return CommandResult<FolderTemplateValidation>.Ok(new(
        purpose, canonical, Hashing.Sha256Hex(Encoding.UTF8.GetBytes(canonical)), nodeCount));
    }
    catch (JsonException)
    {
      return CommandResult<FolderTemplateValidation>.Fail("m365.template.invalid", "The folder-template manifest is not valid JSON.");
    }
  }

  public static string DefaultManifest(string purpose) => purpose.Trim().ToUpperInvariant() switch
  {
    FolderTemplatePurposes.ClientWorkspace => """
      {"nodes":[{"key":"permanent","name":"00_Permanent","children":[{"key":"corporate","name":"01_Corporate_Records"},{"key":"terms","name":"02_Engagement_Terms"},{"key":"reference","name":"03_Shared_Reference"}]},{"key":"engagements","name":"Engagements"}]}
      """,
    FolderTemplatePurposes.EngagementWorkspace => """
      {"nodes":[{"key":"acceptance","name":"01_Acceptance_Terms"},{"key":"planning","name":"02_Planning"},{"key":"pbc","name":"03_PBC_Data_Intake"},{"key":"accounting","name":"04_Accounting"},{"key":"working-papers","name":"05_Audit_Working_Papers"},{"key":"review","name":"06_Review_Completion"},{"key":"deliverables","name":"07_Draft_Deliverables"}]}
      """,
    _ => "{\"nodes\":[]}" 
  };

  private static CommandResult ValidateNode(JsonElement node, HashSet<string> keys,
    ref int nodeCount, int depth, int parentPathLength)
  {
    if (node.ValueKind != JsonValueKind.Object || depth > 8)
      return CommandResult.Fail("m365.template.invalid", "Template folders must be bounded objects no deeper than eight levels.");
    var properties = new HashSet<string>(StringComparer.Ordinal);
    foreach (var property in node.EnumerateObject())
      if (!properties.Add(property.Name) || property.Name is not ("key" or "name" or "children"))
        return CommandResult.Fail("m365.template.invalid", "Template folders may contain only key, name and children.");
    if (!node.TryGetProperty("key", out var keyElement) || keyElement.ValueKind != JsonValueKind.String ||
        !node.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
      return CommandResult.Fail("m365.template.invalid", "Every template folder requires a key and name.");

    var key = keyElement.GetString()?.Trim() ?? string.Empty;
    var name = nameElement.GetString()?.Trim() ?? string.Empty;
    if (key.Length is < 1 or > 80 || !key.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.'))
      return CommandResult.Fail("m365.template.invalid", "Folder keys must use bounded letters, numbers, dots, dashes or underscores.");
    if (!keys.Add(key))
      return CommandResult.Fail("m365.template.invalid", "Folder keys must be unique after normalization.");
    if (!ValidFolderName(name))
      return CommandResult.Fail("m365.template.invalid", "Folder names contain an invalid or unsafe path segment.");
    nodeCount++;
    var expandedLength = ExpandedLength(name);
    if (parentPathLength + 1 + expandedLength > 4_000)
      return CommandResult.Fail("m365.template.invalid", "The decoded folder path exceeds the supported length budget.");

    if (node.TryGetProperty("children", out var children))
    {
      if (children.ValueKind != JsonValueKind.Array)
        return CommandResult.Fail("m365.template.invalid", "Folder children must be an array.");
      foreach (var child in children.EnumerateArray())
      {
        var result = ValidateNode(child, keys, ref nodeCount, depth + 1, parentPathLength + 1 + expandedLength);
        if (!result.Succeeded) return result;
      }
    }
    return CommandResult.Ok();
  }

  private static bool ValidFolderName(string name)
  {
    if (name.Length is < 1 or > 120 || name is "." or ".." || name.EndsWith('.') || name.EndsWith(' '))
      return false;
    if (name.Any(char.IsControl) || name.Contains('/') || name.Contains('\\') || name.Contains(':') || name.Contains('*') || name.Contains('?') || name.Contains('"') || name.Contains('<') || name.Contains('>') || name.Contains('|'))
      return false;
    for (var i = 0; i < name.Length;)
    {
      if (name[i] == '}') return false;
      if (name[i] != '{') { i++; continue; }
      if (i + 1 >= name.Length || name[i + 1] != '{') return false;
      var end = name.IndexOf("}}", i + 2, StringComparison.Ordinal);
      if (end < 0) return false;
      var token = name[i..(end + 2)];
      if (!AllowedTokens.Contains(token)) return false;
      i = end + 2;
    }
    return true;
  }

  private static int ExpandedLength(string name)
  {
    var length = name.Length;
    length += Count(name, "{{CLIENT_CODE}}") * 64;
    length += Count(name, "{{CLIENT_NAME}}") * 120;
    length += Count(name, "{{ENGAGEMENT_CODE}}") * 64;
    length += Count(name, "{{SERVICE}}") * 40;
    length += Count(name, "{{PERIOD}}") * 20;
    return length;
  }

  private static int Count(string value, string token)
  {
    var count = 0;
    for (var index = value.IndexOf(token, StringComparison.Ordinal); index >= 0;
         index = value.IndexOf(token, index + token.Length, StringComparison.Ordinal)) count++;
    return count;
  }

  private static string CanonicalJson(JsonElement element)
  {
    using var stream = new MemoryStream();
    using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
    {
      WriteCanonical(writer, element);
    }
    return Encoding.UTF8.GetString(stream.ToArray());
  }

  private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
  {
    switch (element.ValueKind)
    {
      case JsonValueKind.Object:
        writer.WriteStartObject();
        foreach (var property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
        {
          writer.WritePropertyName(property.Name);
          WriteCanonical(writer, property.Value);
        }
        writer.WriteEndObject();
        break;
      case JsonValueKind.Array:
        writer.WriteStartArray();
        foreach (var item in element.EnumerateArray()) WriteCanonical(writer, item);
        writer.WriteEndArray();
        break;
      default:
        element.WriteTo(writer);
        break;
    }
  }

  private static FolderTemplateResult ToResult(FolderTemplateVersion template, int nodeCount) =>
    new(template.Id, template.Purpose, template.Version, template.ManifestJson, template.ManifestDigest, nodeCount);

  private static Task<CommandResult> FirmAdministratorAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: ["Administrator"], InternalOnly: true,
        RequireFirmWide: true), ct);

  private static bool CredentialReference(string value)
  {
    var trimmed = value.Trim();
    return trimmed.Length is > 0 and <= 500 && !trimmed.Contains('\r') && !trimmed.Contains('\n') &&
      !trimmed.Contains("BEGIN ", StringComparison.OrdinalIgnoreCase) &&
      !trimmed.Contains("password", StringComparison.OrdinalIgnoreCase) &&
      !trimmed.Contains("client_secret", StringComparison.OrdinalIgnoreCase);
  }

  private static bool Https(string value) =>
    Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
    uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
    string.IsNullOrWhiteSpace(uri.UserInfo) && !string.IsNullOrWhiteSpace(uri.Host);
}
