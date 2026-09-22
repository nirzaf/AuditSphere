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
}
