using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Documents;

/// <summary>
/// Exact-version read-back of a selected-site version that is not registered yet. Implementations resolve the target
/// only from the engagement's current verified binding, never from browser input, and compare the complete SHA-256
/// and byte count without retaining the bytes.
/// </summary>
public interface IPbcRegistrationVerifier
{
  Task<CommandResult> VerifyUnregisteredAsync(PbcRemoteSource candidate, CancellationToken ct);
}

/// <summary>One dry-run row for registering an earlier provider-verified PBC transfer.</summary>
public sealed record PbcRegistrationBackfillItem(Guid UploadIntentId, Guid ClientId, Guid EngagementId,
  Guid TransferOperationId, string ProviderIdentity, string Sha256Hex, long ByteCount, string Disposition);

public sealed record PbcRegistrationBackfillRequest(Guid UploadIntentId, string ExpectedProviderIdentity, string ExpectedSha256Hex);

/// <summary>
/// SP-06 backfill: registers uploads whose live selected-site transfer completed before registration existed. The
/// provider identity comes only from the completed durable transfer result (never a filename or current item), the
/// exact version is read back and verified first, and the rows are the same ones a new publication writes. Upload,
/// request and acceptance revisions, receipts and source digests are not changed.
/// </summary>
public static partial class PbcService
{
  public const string RegistrationEligible = "ELIGIBLE";
  public const string RegistrationExists = "REGISTERED";
  private const int MaxBackfillPlanItems = 100;

  /// <summary>Adds the scoped reference, exact-version snapshot and registration for one verified selected-site item.</summary>
  internal static PbcDocumentRegistration AddSelectedSiteRegistration(IAuditSphereDbContext db, PbcUploadIntent intent,
    RepositoryBinding binding, PbcSelectedSiteItem item, string providerIdentity, string sha256Hex, Guid transferOperationId,
    string capturedBy, DateTimeOffset now)
  {
    var reference = new DocumentReference
    {
      Id = Guid.CreateVersion7(), FirmId = intent.FirmId, ClientId = intent.ClientId, EngagementId = intent.EngagementId,
      RepositoryBindingId = binding.Id, DriveId = item.DriveId, ItemId = item.ItemId,
      Path = PbcSelectedSiteIdentity.StoredName(intent.Id, intent.FileName), Purpose = "Evidence", CreatedAt = now
    };
    var snapshot = new DocumentSnapshot
    {
      Id = Guid.CreateVersion7(), FirmId = intent.FirmId, ClientId = intent.ClientId, EngagementId = intent.EngagementId,
      DocumentReferenceId = reference.Id, DriveId = item.DriveId, ItemId = item.ItemId, VersionId = item.VersionId,
      Sha256Hex = sha256Hex.ToLowerInvariant(), ByteCount = intent.DeclaredByteCount, CapturedBy = capturedBy, CapturedAt = now
    };
    var registration = new PbcDocumentRegistration
    {
      Id = Guid.CreateVersion7(), FirmId = intent.FirmId, ClientId = intent.ClientId, EngagementId = intent.EngagementId,
      PbcUploadIntentId = intent.Id, TransferOperationId = transferOperationId, DocumentReferenceId = reference.Id,
      DocumentSnapshotId = snapshot.Id, ProviderIdentity = providerIdentity,
      BindingSha256Hex = RepositoryBindingDigest.Compute(binding), RegisteredAt = now
    };
    db.DocumentReferences.Add(reference);
    db.DocumentSnapshots.Add(snapshot);
    db.PbcDocumentRegistrations.Add(registration);
    return registration;
  }

  /// <summary>Bounded administrator dry-run over received uploads that have no registration yet.</summary>
  public static async Task<CommandResult<IReadOnlyList<PbcRegistrationBackfillItem>>> PlanRegistrationBackfillAsync(
    IAuditSphereDbContext db, ActorContext actor, int limit, CancellationToken ct = default)
  {
    if (limit is < 1 or > MaxBackfillPlanItems)
      return CommandResult<IReadOnlyList<PbcRegistrationBackfillItem>>.Fail("pbc.backfill-invalid",
        $"Plan between 1 and {MaxBackfillPlanItems} uploads.");
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct))
      return TenantAdministration.Denied<IReadOnlyList<PbcRegistrationBackfillItem>>();
    var ids = await db.PbcUploadIntents.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.State == PbcUploadStates.Received &&
        !db.PbcDocumentRegistrations.Any(r => r.FirmId == x.FirmId && r.PbcUploadIntentId == x.Id))
      .OrderBy(x => x.Id).Select(x => x.Id).Take(limit).ToListAsync(ct);
    var items = new List<PbcRegistrationBackfillItem>();
    foreach (var id in ids) items.Add((await EvaluateBackfillAsync(db, actor.FirmId, id, ct))!.Value.Item);
    return CommandResult<IReadOnlyList<PbcRegistrationBackfillItem>>.Ok(items);
  }

  /// <summary>
  /// Registers one earlier verified transfer after an exact-version read-back. A replay of an existing registration
  /// with the same provider identity returns it without another provider read.
  /// </summary>
  public static async Task<CommandResult<Guid>> BackfillRegistrationAsync(IAuditSphereDbContext db, ActorContext actor,
    IPbcRegistrationVerifier verifier, PbcRegistrationBackfillRequest request, CancellationToken ct = default)
  {
    if (verifier is null || request is null || request.UploadIntentId == Guid.Empty ||
        string.IsNullOrWhiteSpace(request.ExpectedProviderIdentity) || string.IsNullOrWhiteSpace(request.ExpectedSha256Hex))
      return CommandResult<Guid>.Fail("pbc.backfill-invalid", "An exact upload, provider identity and SHA-256 are required.");
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<Guid>();
    var evaluated = await EvaluateBackfillAsync(db, actor.FirmId, request.UploadIntentId, ct);
    if (evaluated is null) return TenantAdministration.Denied<Guid>();
    var facts = evaluated.Value;
    if (facts.Item.ProviderIdentity != request.ExpectedProviderIdentity || facts.Item.Sha256Hex != request.ExpectedSha256Hex)
      return StaleBackfill();
    if (facts.Item.Disposition == RegistrationExists) return await ExistingRegistrationAsync();
    if (facts.Item.Disposition != RegistrationEligible)
      return CommandResult<Guid>.Fail(facts.Item.Disposition, "This upload cannot be registered from its retained transfer evidence.");

    var verified = await verifier.VerifyUnregisteredAsync(facts.Candidate!, ct);
    if (!verified.Succeeded)
      return CommandResult<Guid>.Fail(verified.ErrorCode!, "The SharePoint version could not be verified; nothing was registered.");

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var locked = await db.PbcUploadIntents.FromSqlInterpolated($"""
      SELECT * FROM pbc_upload_intents WHERE firm_id = {actor.FirmId} AND id = {request.UploadIntentId} FOR UPDATE
      """).AsNoTracking().ToListAsync(ct);
    if (locked.Count != 1 || !await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct))
      return TenantAdministration.Denied<Guid>();
    var current = await EvaluateBackfillAsync(db, actor.FirmId, request.UploadIntentId, ct);
    if (current?.Item.Disposition == RegistrationExists) return await ExistingRegistrationAsync();
    if (current is null || current.Value.Item != facts.Item) return StaleBackfill();
    var registration = AddSelectedSiteRegistration(db, locked[0], current.Value.Binding!, current.Value.SelectedItem!,
      facts.Item.ProviderIdentity, facts.Item.Sha256Hex, facts.Item.TransferOperationId,
      "backfill:" + actor.UserId.ToString("D"), DateTimeOffset.UtcNow);
    try
    {
      await db.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);
    }
    catch (DbUpdateException)
    {
      return StaleBackfill();
    }
    return CommandResult<Guid>.Ok(registration.Id);

    async Task<CommandResult<Guid>> ExistingRegistrationAsync()
    {
      var existing = await db.PbcDocumentRegistrations.AsNoTracking().SingleAsync(x =>
        x.FirmId == actor.FirmId && x.PbcUploadIntentId == request.UploadIntentId, ct);
      return existing.ProviderIdentity == request.ExpectedProviderIdentity ? CommandResult<Guid>.Ok(existing.Id) : StaleBackfill();
    }

    static CommandResult<Guid> StaleBackfill() => CommandResult<Guid>.Fail(ErrorCodes.GenerationStale,
      "The upload's transfer evidence or registration changed; review a fresh plan.");
  }

  private readonly record struct BackfillFacts(PbcRegistrationBackfillItem Item, PbcRemoteSource? Candidate,
    RepositoryBinding? Binding, PbcSelectedSiteItem? SelectedItem);

  private static async Task<BackfillFacts?> EvaluateBackfillAsync(IAuditSphereDbContext db, Guid firmId,
    Guid uploadIntentId, CancellationToken ct)
  {
    var intent = await db.PbcUploadIntents.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.Id == uploadIntentId, ct);
    if (intent is null) return null;
    var operation = intent.TransferOperationId is not { } operationId ? null : await db.DurableOperations.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == firmId && x.Id == operationId, ct);
    var identity = operation?.ResultIdentity ?? string.Empty;
    var item = PbcSelectedSiteIdentity.Parse(identity);
    var bindings = item is null ? [] : await db.RepositoryBindings.AsNoTracking().Where(x =>
      x.FirmId == firmId && x.ClientId == intent.ClientId && x.EngagementId == intent.EngagementId &&
      x.Classification == "working" && x.CapabilityProfile == "selected-site").Take(2).ToListAsync(ct);
    var binding = bindings.Count == 1 && bindings[0].DriveId == item?.DriveId ? bindings[0] : null;

    string disposition;
    if (await db.PbcDocumentRegistrations.AsNoTracking().AnyAsync(x => x.FirmId == firmId && x.PbcUploadIntentId == intent.Id, ct))
      disposition = RegistrationExists;
    else if (intent.State != PbcUploadStates.Received || operation is null ||
      operation.OperationKind != PbcDocumentTransferHandler.Kind || operation.Status != OperationState.COMPLETED ||
      operation.TargetId != intent.Id)
      disposition = "pbc.backfill-transfer-incomplete";
    else if (item is null)
      disposition = "pbc.backfill-unsupported-receipt";
    else if (operation.ResultDigest is null || operation.ResultDigest != intent.ProviderReceiptDigest ||
      operation.ResultDigest != intent.FinalSha256Hex)
      disposition = "pbc.backfill-receipt-mismatch";
    else if (binding is null)
      disposition = "pbc.backfill-binding-mismatch";
    else if (await db.DocumentReferences.AsNoTracking().AnyAsync(x => x.FirmId == firmId && x.ClientId == intent.ClientId &&
      x.EngagementId == intent.EngagementId && x.RepositoryBindingId == binding.Id && x.ItemId == item.ItemId, ct))
      disposition = "pbc.backfill-item-claimed";
    else disposition = RegistrationEligible;

    var row = new PbcRegistrationBackfillItem(intent.Id, intent.ClientId, intent.EngagementId,
      intent.TransferOperationId ?? Guid.Empty, identity, intent.FinalSha256Hex ?? string.Empty, intent.DeclaredByteCount, disposition);
    var candidate = disposition != RegistrationEligible ? null : new PbcRemoteSource(firmId, intent.ClientId,
      intent.EngagementId, intent.Id, Guid.Empty, Guid.Empty, item!.DriveId, item.ItemId, item.VersionId,
      intent.FinalSha256Hex!, intent.DeclaredByteCount, RepositoryBindingDigest.Compute(binding!));
    return new BackfillFacts(row, candidate, binding, item);
  }
}
