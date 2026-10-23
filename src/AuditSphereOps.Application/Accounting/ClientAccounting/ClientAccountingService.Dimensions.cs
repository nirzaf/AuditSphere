using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class ClientAccountingService
{
  public static async Task<CommandResult<DimensionSchemaDraftReceiptDto>> SaveDimensionSchemaDraftAsync(
    IClientAccountingDbContext db,
    ActorContext actor,
    DimensionSchemaDraftDto request,
    CancellationToken ct = default)
  {
    if (request is null || request.ClientId == Guid.Empty || request.Definitions is null ||
        request.Definitions.Count < 1 || request.Definitions.Count > DimensionSchemaDraftMapper.MaximumDefinitionsPerRequest ||
        request.BaseRevisionRef == Guid.Empty || request.DraftRevisionRef == Guid.Empty)
      return CommandResult<DimensionSchemaDraftReceiptDto>.Fail(
        ErrorCodes.Accounting.MappingInvalid, "A valid client, revision reference and dimension definition set are required.");

    var initialAuth = await AuthorizeClientAsync(db, actor, request.ClientId, PreparerRoles, ct);
    if (!initialAuth.Succeeded)
      return CommandResult<DimensionSchemaDraftReceiptDto>.Fail(initialAuth.ErrorCode!, initialAuth.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var clients = await db.PracticeClients
      .FromSqlInterpolated($"SELECT * FROM practice_clients WHERE id = {request.ClientId} AND firm_id = {actor.FirmId} FOR UPDATE")
      .ToListAsync(ct);
    if (clients.Count != 1)
      return CommandResult<DimensionSchemaDraftReceiptDto>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await AuthorizeClientAsync(db, actor, request.ClientId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<DimensionSchemaDraftReceiptDto>.Fail(auth.ErrorCode!, auth.Message!);

    ClientAccountingDimensionSchemaRevisionRecord? editable = null;
    ClientAccountingDimensionSchemaRevision? editableSnapshot = null;
    if (request.DraftRevisionRef is { } draftId)
    {
      var scopedDraft = await LoadScopedSchemaRevisionAsync(db, actor.FirmId, request.ClientId, draftId, ct);
      if (scopedDraft is null)
        return CommandResult<DimensionSchemaDraftReceiptDto>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

      var lockedDraftRows = await db.ClientAccountingDimensionSchemaRevisions
        .FromSqlInterpolated($"SELECT * FROM client_accounting_dimension_schema_revisions WHERE id = {draftId} AND firm_id = {actor.FirmId} AND client_id = {request.ClientId} FOR UPDATE")
        .ToListAsync(ct);
      editable = lockedDraftRows.SingleOrDefault(x => x.Id == draftId && x.FirmId == actor.FirmId && x.ClientId == request.ClientId);
      if (editable is null)
        return CommandResult<DimensionSchemaDraftReceiptDto>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

      // Re-query with children after locking only the root row, avoiding an outer-join FOR UPDATE.
      editable = await LoadScopedSchemaRevisionAsync(db, actor.FirmId, request.ClientId, draftId, ct);
      if (editable is null)
        return CommandResult<DimensionSchemaDraftReceiptDto>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
      auth = await AuthorizeClientAsync(db, actor, request.ClientId, PreparerRoles, ct);
      if (!auth.Succeeded)
        return CommandResult<DimensionSchemaDraftReceiptDto>.Fail(auth.ErrorCode!, auth.Message!);
      if (editable.Status != AccountingWorkflowStates.Draft)
        return CommandResult<DimensionSchemaDraftReceiptDto>.Fail(ErrorCodes.ProtectedState, "Only an unvalidated schema draft can be edited.");
      if (string.IsNullOrWhiteSpace(request.ExpectedCanonicalDigest) ||
          !string.Equals(request.ExpectedCanonicalDigest, editable.CanonicalDigest, StringComparison.Ordinal))
        return CommandResult<DimensionSchemaDraftReceiptDto>.Fail(ErrorCodes.StaleRevision, "The schema draft changed; reload it before saving.");

      var rehydratedDraft = DimensionSchemaDraftMapper.Rehydrate(editable);
      if (!rehydratedDraft.IsValid)
        return CommandResult<DimensionSchemaDraftReceiptDto>.Fail(ErrorCodes.GateBlocked, "The saved schema draft failed its integrity check.");
      editableSnapshot = rehydratedDraft.Value!;
    }
    else if (request.ExpectedCanonicalDigest is not null)
    {
      return CommandResult<DimensionSchemaDraftReceiptDto>.Fail(ErrorCodes.StaleRevision, "A new schema draft cannot carry an expected prior digest.");
    }

    ClientAccountingDimensionSchemaRevisionRecord? predecessorRecord = null;
    ClientAccountingDimensionSchemaRevision? predecessor = null;
    if (request.BaseRevisionRef is { } baseId)
    {
      predecessorRecord = await LoadScopedSchemaRevisionAsync(db, actor.FirmId, request.ClientId, baseId, ct);
      if (predecessorRecord is null)
        return CommandResult<DimensionSchemaDraftReceiptDto>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
      if (predecessorRecord.Status != AccountingWorkflowStates.Approved)
        return CommandResult<DimensionSchemaDraftReceiptDto>.Fail(ErrorCodes.StaleRevision, "The selected schema base is not published.");
      var rehydratedBase = DimensionSchemaDraftMapper.Rehydrate(predecessorRecord);
      if (!rehydratedBase.IsValid)
        return CommandResult<DimensionSchemaDraftReceiptDto>.Fail(ErrorCodes.GateBlocked, "The published schema base failed its integrity check.");
      predecessor = rehydratedBase.Value!;
    }

    Guid schemaId;
    Guid revisionId;
    long revision;
    if (editable is not null)
    {
      schemaId = editable.SchemaId;
      revisionId = editable.Id;
      revision = editable.Revision;
      if (editable.BaseRevisionId != request.BaseRevisionRef)
        return CommandResult<DimensionSchemaDraftReceiptDto>.Fail(ErrorCodes.StaleRevision, "The schema draft base changed; reload it before saving.");
    }
    else if (predecessorRecord is not null)
    {
      if (predecessorRecord.Revision == long.MaxValue)
        return CommandResult<DimensionSchemaDraftReceiptDto>.Fail(ErrorCodes.ProtectedState, "The schema revision sequence cannot advance further.");
      schemaId = predecessorRecord.SchemaId;
      revisionId = Guid.CreateVersion7();
      revision = predecessorRecord.Revision + 1;
    }
    else
    {
      schemaId = Guid.CreateVersion7();
      revisionId = Guid.CreateVersion7();
      revision = 1;
    }

    var editableRevisionId = editable?.Id;
    var competingRevisionExists = await db.ClientAccountingDimensionSchemaRevisions.AnyAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.Revision >= revision &&
      (!editableRevisionId.HasValue || x.Id != editableRevisionId.Value), ct);
    if (competingRevisionExists)
      return CommandResult<DimensionSchemaDraftReceiptDto>.Fail(ErrorCodes.StaleRevision, "A newer schema revision already exists.");

    var built = DimensionSchemaDraftMapper.BuildSnapshot(
      actor.FirmId, request, schemaId, revisionId, revision, predecessor, editableSnapshot);
    if (!built.IsValid)
      return CommandResult<DimensionSchemaDraftReceiptDto>.Fail(
        ErrorCodes.Accounting.MappingInvalid, "The dimension schema draft is invalid or its member identities do not match the exact base/draft.");

    var snapshot = built.Value!;
    if (editable is null)
    {
      editable = ClientAccountingDimensionSchemaRevisionRecord.FromSnapshot(snapshot, actor.UserId, DateTimeOffset.UtcNow);
      editable.SetDraftImpactAcknowledgement(request.ImpactAcknowledged);
      db.ClientAccountingDimensionSchemaRevisions.Add(editable);
    }
    else
    {
      var oldValues = await db.ClientAccountingDimensionValueRevisionRecords
        .Where(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.SchemaRevisionId == editable.Id)
        .ToListAsync(ct);
      var oldDefinitions = await db.ClientAccountingDimensionDefinitionRevisionRecords
        .Where(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.SchemaRevisionId == editable.Id)
        .ToListAsync(ct);
      db.ClientAccountingDimensionValueRevisionRecords.RemoveRange(oldValues);
      await db.SaveChangesAsync(ct);
      db.ClientAccountingDimensionDefinitionRevisionRecords.RemoveRange(oldDefinitions);
      await db.SaveChangesAsync(ct);
      if (!editable.ReplaceDraftSnapshot(snapshot, request.ImpactAcknowledged))
        return CommandResult<DimensionSchemaDraftReceiptDto>.Fail(ErrorCodes.StaleRevision, "The schema draft changed; reload it before saving.");
    }

    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<DimensionSchemaDraftReceiptDto>.Ok(ToDraftReceipt(snapshot));
  }

  public static async Task<CommandResult<DimensionSchemaPublicationReceiptDto>> PublishDimensionSchemaAsync(
    IClientAccountingDbContext db,
    ActorContext actor,
    DimensionSchemaDraftDto request,
    CancellationToken ct = default)
  {
    if (request is null || request.ClientId == Guid.Empty || request.DraftRevisionRef is not { } draftId ||
        request.ExpectedCanonicalDigest is not { Length: 64 } || !IsLowercaseSha256(request.ExpectedCanonicalDigest) ||
        !request.ImpactAcknowledged)
      return CommandResult<DimensionSchemaPublicationReceiptDto>.Fail(
        ErrorCodes.Accounting.MappingInvalid, "A draft reference, expected digest and impact acknowledgement are required.");

    var initialAuth = await AuthorizeClientAsync(db, actor, request.ClientId, ReviewerRoles, ct);
    if (!initialAuth.Succeeded)
      return CommandResult<DimensionSchemaPublicationReceiptDto>.Fail(initialAuth.ErrorCode!, initialAuth.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var clients = await db.PracticeClients
      .FromSqlInterpolated($"SELECT * FROM practice_clients WHERE id = {request.ClientId} AND firm_id = {actor.FirmId} FOR UPDATE")
      .ToListAsync(ct);
    if (clients.Count != 1)
      return CommandResult<DimensionSchemaPublicationReceiptDto>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await AuthorizeClientAsync(db, actor, request.ClientId, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<DimensionSchemaPublicationReceiptDto>.Fail(auth.ErrorCode!, auth.Message!);

    var lockedRows = await db.ClientAccountingDimensionSchemaRevisions
      .FromSqlInterpolated($"SELECT * FROM client_accounting_dimension_schema_revisions WHERE id = {draftId} AND firm_id = {actor.FirmId} AND client_id = {request.ClientId} FOR UPDATE")
      .ToListAsync(ct);
    var lockedDraft = lockedRows.SingleOrDefault(x => x.Id == draftId && x.FirmId == actor.FirmId && x.ClientId == request.ClientId);
    if (lockedDraft is null)
      return CommandResult<DimensionSchemaPublicationReceiptDto>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var draft = await LoadScopedSchemaRevisionAsync(db, actor.FirmId, request.ClientId, draftId, ct);
    if (draft is null)
      return CommandResult<DimensionSchemaPublicationReceiptDto>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    auth = await AuthorizeClientAsync(db, actor, request.ClientId, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<DimensionSchemaPublicationReceiptDto>.Fail(auth.ErrorCode!, auth.Message!);
    if (draft.Status != AccountingWorkflowStates.Draft)
      return CommandResult<DimensionSchemaPublicationReceiptDto>.Fail(ErrorCodes.ProtectedState, "Only an unvalidated schema draft can be published.");
    if (draft.CreatedByUserId == actor.UserId)
      return CommandResult<DimensionSchemaPublicationReceiptDto>.Fail(
        ErrorCodes.Accounting.MappingInvalid, "The schema draft author cannot publish the same revision.");

    if (!string.Equals(request.ExpectedCanonicalDigest, draft.CanonicalDigest, StringComparison.Ordinal))
      return CommandResult<DimensionSchemaPublicationReceiptDto>.Fail(ErrorCodes.GenerationStale, "The reviewed schema content changed.");
    if (request.BaseRevisionRef != draft.BaseRevisionId)
      return CommandResult<DimensionSchemaPublicationReceiptDto>.Fail(ErrorCodes.StaleRevision, "The schema draft base does not match the requested base.");

    var persisted = DimensionSchemaDraftMapper.Rehydrate(draft);
    if (!persisted.IsValid)
      return CommandResult<DimensionSchemaPublicationReceiptDto>.Fail(ErrorCodes.GateBlocked, "The saved schema draft failed its integrity check.");

    ClientAccountingDimensionSchemaRevision? predecessor = null;
    if (draft.BaseRevisionId is { } baseId)
    {
      var baseRecord = await LoadScopedSchemaRevisionAsync(db, actor.FirmId, request.ClientId, baseId, ct);
      if (baseRecord is null || baseRecord.SchemaId != draft.SchemaId || baseRecord.Status != AccountingWorkflowStates.Approved ||
          baseRecord.Revision == long.MaxValue || draft.Revision != baseRecord.Revision + 1)
        return CommandResult<DimensionSchemaPublicationReceiptDto>.Fail(ErrorCodes.StaleRevision, "The exact published schema base is no longer current.");
      var newer = await db.ClientAccountingDimensionSchemaRevisions.AnyAsync(x =>
        x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.Revision > draft.Revision, ct);
      if (newer)
        return CommandResult<DimensionSchemaPublicationReceiptDto>.Fail(ErrorCodes.StaleRevision, "A newer schema revision already exists.");
      var rehydratedBase = DimensionSchemaDraftMapper.Rehydrate(baseRecord);
      if (!rehydratedBase.IsValid)
        return CommandResult<DimensionSchemaPublicationReceiptDto>.Fail(ErrorCodes.GateBlocked, "The published schema base failed its integrity check.");
      predecessor = rehydratedBase.Value!;
    }
    else if (draft.Revision != 1 || await db.ClientAccountingDimensionSchemaRevisions.AnyAsync(x =>
        x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.Id != draft.Id, ct))
    {
      return CommandResult<DimensionSchemaPublicationReceiptDto>.Fail(ErrorCodes.StaleRevision, "The first schema revision has invalid lineage.");
    }

    var rebuilt = DimensionSchemaDraftMapper.BuildSnapshot(
      actor.FirmId, request, draft.SchemaId, draft.Id, draft.Revision, predecessor, persisted.Value);
    if (!rebuilt.IsValid || !string.Equals(rebuilt.Value!.CanonicalDigest, draft.CanonicalDigest, StringComparison.Ordinal))
      return CommandResult<DimensionSchemaPublicationReceiptDto>.Fail(ErrorCodes.GenerationStale, "The submitted schema differs from the exact persisted draft.");

    // Module 20 requires independent technical publication. Reuse the same reviewer-role and
    // maker-checker boundary as chart authoring; this does not represent a professional opinion
    // or define a new evidence/approval policy.
    var validatedAt = DateTimeOffset.UtcNow;
    if (!draft.MarkValidated(validatedAt))
      return CommandResult<DimensionSchemaPublicationReceiptDto>.Fail(ErrorCodes.StaleRevision, "The schema draft changed before validation.");
    await db.SaveChangesAsync(ct);
    if (!draft.TryPublish(actor.UserId, DateTimeOffset.UtcNow, request.ImpactAcknowledged))
      return CommandResult<DimensionSchemaPublicationReceiptDto>.Fail(ErrorCodes.ProtectedState, "The schema revision did not satisfy publication invariants.");
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);

    return CommandResult<DimensionSchemaPublicationReceiptDto>.Ok(new DimensionSchemaPublicationReceiptDto(
      draft.Id, draft.SchemaId, draft.Revision, draft.CanonicalDigestVersion, draft.CanonicalDigest, draft.Status));
  }

  /// <summary>
  /// Resolves only the exact server-persisted published revision requested by the caller.
  /// It never substitutes the latest revision and verifies the persisted content digest.
  /// </summary>
  public static async Task<CommandResult<ResolvedDimensionSchemaRevisionDto>> ResolvePublishedDimensionSchemaRevisionAsync(
    IClientAccountingDbContext db,
    ActorContext actor,
    Guid clientId,
    DimensionSchemaRevisionRef reference,
    CancellationToken ct = default)
  {
    if (clientId == Guid.Empty || reference is null || reference.SchemaId == Guid.Empty ||
        reference.RevisionId == Guid.Empty || reference.Revision < 1 ||
        reference.CanonicalDigestVersion != ClientAccountingDimensionSchemaRevision.CanonicalDigestVersion ||
        !IsLowercaseSha256(reference.CanonicalDigest))
      return CommandResult<ResolvedDimensionSchemaRevisionDto>.Fail(
        ErrorCodes.Accounting.MappingInvalid, "An exact schema revision identity and canonical digest are required.");

    var auth = await AuthorizeClientAsync(db, actor, clientId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<ResolvedDimensionSchemaRevisionDto>.Fail(auth.ErrorCode!, auth.Message!);

    var record = await LoadScopedSchemaRevisionAsync(db, actor.FirmId, clientId, reference.RevisionId, ct);
    if (record is null || record.SchemaId != reference.SchemaId || record.Revision != reference.Revision)
      return CommandResult<ResolvedDimensionSchemaRevisionDto>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (record.Status != AccountingWorkflowStates.Approved)
      return CommandResult<ResolvedDimensionSchemaRevisionDto>.Fail(
        ErrorCodes.ProtectedState, "Only the exact published schema revision can be resolved.");
    if (record.CanonicalDigestVersion != reference.CanonicalDigestVersion ||
        !string.Equals(record.CanonicalDigest, reference.CanonicalDigest, StringComparison.Ordinal))
      return CommandResult<ResolvedDimensionSchemaRevisionDto>.Fail(
        ErrorCodes.GenerationStale, "The schema revision digest does not match the exact persisted revision.");

    var rehydrated = DimensionSchemaDraftMapper.Rehydrate(record);
    if (!rehydrated.IsValid)
      return CommandResult<ResolvedDimensionSchemaRevisionDto>.Fail(
        ErrorCodes.GateBlocked, "The saved schema revision failed its integrity check.");

    auth = await AuthorizeClientAsync(db, actor, clientId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<ResolvedDimensionSchemaRevisionDto>.Fail(auth.ErrorCode!, auth.Message!);

    return CommandResult<ResolvedDimensionSchemaRevisionDto>.Ok(new ResolvedDimensionSchemaRevisionDto(
      actor.FirmId, clientId, new DimensionSchemaRevisionRef(
        record.SchemaId, record.Id, record.Revision,
        record.CanonicalDigestVersion, record.CanonicalDigest)));
  }

  private static async Task<ClientAccountingDimensionSchemaRevisionRecord?> LoadScopedSchemaRevisionAsync(
    IClientAccountingDbContext db,
    Guid firmId,
    Guid clientId,
    Guid revisionId,
    CancellationToken ct) =>
    await db.ClientAccountingDimensionSchemaRevisions
      .Include(x => x.Definitions)
      .ThenInclude(x => x.Values)
      .SingleOrDefaultAsync(x => x.Id == revisionId && x.FirmId == firmId && x.ClientId == clientId, ct);

  private static DimensionSchemaDraftReceiptDto ToDraftReceipt(ClientAccountingDimensionSchemaRevision snapshot) =>
    new(snapshot.RevisionId, snapshot.SchemaId, snapshot.Revision,
      ClientAccountingDimensionSchemaRevision.CanonicalDigestVersion, snapshot.CanonicalDigest,
      Array.AsReadOnly(snapshot.Definitions.Select(definition => new DimensionSchemaDefinitionDraftDto(
        definition.DimensionType, definition.Code, definition.Name, definition.IsRequired,
        Array.AsReadOnly(definition.Values.Select(value => new DimensionSchemaValueDraftDto(
          value.Code, value.Name, value.EffectiveFrom, value.EffectiveTo, value.IsActive, value.Id)).ToArray()),
        definition.Id)).ToArray()));

  private static bool IsLowercaseSha256(string? value) =>
    value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
