using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
  [Fact]
  [Trait("DimensionSchema", "Database")]
  public async Task DimensionSchemaDraft_PublishesAndResolvesExactImmutableSuccessorRevisions()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");

    DimensionSchemaDraftReceiptDto firstDraft;
    DimensionSchemaPublicationReceiptDto firstPublished;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.Add(Grant(scope.FirmId, scope.Preparer, "AccountingReviewer"));
      await db.SaveChangesAsync();
      var makerReviewer = new ActorContext(scope.Preparer.Id, scope.FirmId, scope.Preparer.SessionEpoch,
        ["AccountingPreparer", "AccountingReviewer"]);

      var saved = await ClientAccountingService.SaveDimensionSchemaDraftAsync(db, makerReviewer,
        new DimensionSchemaDraftDto(scope.ClientA, null,
        [new DimensionSchemaDefinitionDraftDto(AccountingDimensionTypes.Department, "DEPT", "Department", true,
          [new DimensionSchemaValueDraftDto("FIN", "Finance", new DateOnly(2026, 1, 1), null, true)])]));
      Assert.True(saved.Succeeded, saved.Message);
      firstDraft = saved.Value!;
      Assert.NotEqual(Guid.Empty, firstDraft.Definitions[0].DefinitionId);
      Assert.NotEqual(Guid.Empty, firstDraft.Definitions[0].Values[0].ValueId);
      Assert.Equal(AccountingWorkflowStates.Draft,
        await db.ClientAccountingDimensionSchemaRevisions.Where(x => x.Id == firstDraft.DraftRevisionRef)
          .Select(x => x.Status).SingleAsync());

      var publishRequest = new DimensionSchemaDraftDto(scope.ClientA, null, firstDraft.Definitions,
        firstDraft.DraftRevisionRef, ImpactAcknowledged: true, ExpectedCanonicalDigest: firstDraft.CanonicalDigest);
      var selfPublish = await ClientAccountingService.PublishDimensionSchemaAsync(db, makerReviewer, publishRequest);
      Assert.False(selfPublish.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingInvalid, selfPublish.ErrorCode);

      var published = await ClientAccountingService.PublishDimensionSchemaAsync(db, reviewer, publishRequest);
      Assert.True(published.Succeeded, published.Message);
      firstPublished = published.Value!;
      Assert.Equal(AccountingWorkflowStates.Approved, firstPublished.Status);
      Assert.Equal(reviewer.UserId, (await db.ClientAccountingDimensionSchemaRevisions
        .SingleAsync(x => x.Id == firstDraft.DraftRevisionRef)).PublishedByUserId);

      var immutable = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE client_accounting_dimension_schema_revisions SET canonical_digest = {new string('0', 64)} WHERE id = {firstDraft.DraftRevisionRef}"));
      Assert.Equal("23514", immutable.SqlState);

      var resolved = await ClientAccountingService.ResolvePublishedDimensionSchemaRevisionAsync(db, preparer,
        scope.ClientA, new DimensionSchemaRevisionRef(firstPublished.SchemaId, firstPublished.SchemaRevisionRef,
          firstPublished.Revision, firstPublished.CanonicalDigestVersion, firstPublished.CanonicalDigest));
      Assert.True(resolved.Succeeded, resolved.Message);
      Assert.Equal(scope.FirmId, resolved.Value!.FirmId);
      Assert.Equal(scope.ClientA, resolved.Value.ClientId);
      Assert.Equal(firstPublished.SchemaRevisionRef, resolved.Value.Reference.RevisionId);

      var badDigest = await ClientAccountingService.ResolvePublishedDimensionSchemaRevisionAsync(db, preparer,
        scope.ClientA, new DimensionSchemaRevisionRef(firstPublished.SchemaId, firstPublished.SchemaRevisionRef,
          firstPublished.Revision, firstPublished.CanonicalDigestVersion, new string('0', 64)));
      Assert.False(badDigest.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, badDigest.ErrorCode);

      var wrongClient = await ClientAccountingService.ResolvePublishedDimensionSchemaRevisionAsync(db, preparer,
        scope.ClientB, new DimensionSchemaRevisionRef(firstPublished.SchemaId, firstPublished.SchemaRevisionRef,
          firstPublished.Revision, firstPublished.CanonicalDigestVersion, firstPublished.CanonicalDigest));
      Assert.False(wrongClient.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, wrongClient.ErrorCode);
    }

    DimensionSchemaDraftReceiptDto secondDraft;
    DimensionSchemaPublicationReceiptDto secondPublished;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var priorDefinition = firstDraft.Definitions[0];
      var priorValue = priorDefinition.Values[0];
      var saveSuccessor = await ClientAccountingService.SaveDimensionSchemaDraftAsync(db, preparer,
        new DimensionSchemaDraftDto(scope.ClientA, firstPublished.SchemaRevisionRef,
        [priorDefinition with
        {
          Name = "Departments",
          Values = [priorValue with { Active = false },
            new DimensionSchemaValueDraftDto("HR", "Human resources", new DateOnly(2026, 1, 1), null, true)]
        }]));
      Assert.True(saveSuccessor.Succeeded, saveSuccessor.Message);
      secondDraft = saveSuccessor.Value!;
      Assert.Equal(firstPublished.SchemaId, secondDraft.SchemaId);
      Assert.Equal(2, secondDraft.Revision);
      Assert.Equal(priorDefinition.DefinitionId, secondDraft.Definitions[0].DefinitionId);
      Assert.Equal(priorValue.ValueId, secondDraft.Definitions[0].Values.Single(x => x.Code == "FIN").ValueId);

      var publishRequest = new DimensionSchemaDraftDto(scope.ClientA, firstPublished.SchemaRevisionRef,
        secondDraft.Definitions, secondDraft.DraftRevisionRef, ImpactAcknowledged: true,
        ExpectedCanonicalDigest: secondDraft.CanonicalDigest);
      var published = await ClientAccountingService.PublishDimensionSchemaAsync(db, reviewer, publishRequest);
      Assert.True(published.Succeeded, published.Message);
      secondPublished = published.Value!;

      Assert.Equal(2, await db.ClientAccountingDimensionSchemaRevisions.CountAsync(x => x.FirmId == scope.FirmId && x.ClientId == scope.ClientA));
      Assert.Equal(2, await db.ClientAccountingDimensionDefinitionRevisionRecords.CountAsync(x => x.FirmId == scope.FirmId && x.ClientId == scope.ClientA));
      Assert.Equal(3, await db.ClientAccountingDimensionValueRevisionRecords.CountAsync(x => x.FirmId == scope.FirmId && x.ClientId == scope.ClientA));
    }

    await using var verify = new AuditSphereDbContext(pg.Options);
    var exactPrior = await ClientAccountingService.ResolvePublishedDimensionSchemaRevisionAsync(verify, preparer,
      scope.ClientA, new DimensionSchemaRevisionRef(firstPublished.SchemaId, firstPublished.SchemaRevisionRef,
        firstPublished.Revision, firstPublished.CanonicalDigestVersion, firstPublished.CanonicalDigest));
    Assert.True(exactPrior.Succeeded, exactPrior.Message);
    var exactSuccessor = await ClientAccountingService.ResolvePublishedDimensionSchemaRevisionAsync(verify, preparer,
      scope.ClientA, new DimensionSchemaRevisionRef(secondPublished.SchemaId, secondPublished.SchemaRevisionRef,
        secondPublished.Revision, secondPublished.CanonicalDigestVersion, secondPublished.CanonicalDigest));
    Assert.True(exactSuccessor.Succeeded, exactSuccessor.Message);
  }

  [Fact]
  public void DimensionSchemaRevision_CanonicalDigestIsOrderIndependentAndSnapshotIsImmutable()
  {
    ClientAccountingDimensionSchemaDraft DraftInOrder(bool reverse)
    {
      var departmentValues = new[]
      {
        Value(11, "FIN", "Finance", new DateOnly(2026, 1, 1)),
        Value(12, "HR", "Human resources", new DateOnly(2026, 1, 1))
      };
      var projectValues = new[]
      {
        Value(13, "P-02", "Project 2", new DateOnly(2026, 1, 1)),
        Value(14, "P-01", "Project 1", new DateOnly(2026, 1, 1))
      };
      ClientAccountingDimensionSchemaDefinitionDraft department = Definition(21, AccountingDimensionTypes.Department,
        "DEPT", "Department", isRequired: true, values: departmentValues);
      ClientAccountingDimensionSchemaDefinitionDraft project = Definition(22, AccountingDimensionTypes.Project,
        "PROJ", "Project", isRequired: false, values: projectValues);
      return Draft(reverse ? [project, department] : [department, project]);
    }

    var first = ClientAccountingDimensionSchemaRevision.Create(DraftInOrder(reverse: false));
    var reordered = ClientAccountingDimensionSchemaRevision.Create(DraftInOrder(reverse: true));

    Assert.True(first.IsValid);
    Assert.True(reordered.IsValid);
    Assert.Equal(first.Value!.CanonicalDigest, reordered.Value!.CanonicalDigest);

    var mutableValues = new List<ClientAccountingDimensionSchemaValueDraft>
    {
      Value(31, "FIN", "Finance", new DateOnly(2026, 1, 1))
    };
    var snapshotted = ClientAccountingDimensionSchemaRevision.Create(
      Draft(Definition(32, AccountingDimensionTypes.Department, "DEPT", "Department", true, mutableValues)));
    mutableValues.Add(Value(33, "HR", "Human resources", new DateOnly(2026, 1, 1)));

    Assert.True(snapshotted.IsValid);
    Assert.Single(snapshotted.Value!.Definitions[0].Values);
  }

  [Fact]
  public void DimensionSchemaRevision_RejectsUnsupportedAndDuplicateDefinitionsAndValues()
  {
    var duplicateCodeValues = new[]
    {
      Value(41, "FIN", "Finance", new DateOnly(2026, 1, 1)),
      Value(42, " fin ", "Finance duplicate", new DateOnly(2026, 1, 1))
    };
    var draft = Draft(
      Definition(43, AccountingDimensionTypes.Department, "DEPT", "Department", true, duplicateCodeValues),
      Definition(44, "INVALID_TYPE", "INVALID", "Invalid", false, []),
      Definition(45, AccountingDimensionTypes.Department, "DEPT2", "Department 2", false, []),
      Definition(46, AccountingDimensionTypes.Project, " dept ", "Repeated code", false, []));

    var result = ClientAccountingDimensionSchemaRevision.Create(draft);

    Assert.False(result.IsValid);
    Assert.Contains(result.Issues, x => x.Code == "dimension-schema.definition.type.unsupported");
    Assert.Contains(result.Issues, x => x.Code == "dimension-schema.definition.type.duplicate");
    Assert.Contains(result.Issues, x => x.Code == "dimension-schema.value.code.duplicate");
  }

  [Fact]
  public void DimensionSchemaSelections_RejectUnknownOutOfPeriodDuplicateAndForeignScopes()
  {
    var schema = BuildSchema(
      Definition(51, AccountingDimensionTypes.Department, "DEPT", "Department", true,
      [
        Value(52, "FIN", "Finance", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31))
      ]),
      Definition(53, AccountingDimensionTypes.Project, "PROJ", "Project", false,
      [
        Value(54, "OLD", "Prior project", new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31))
      ]));

    var invalid = schema.ValidateSelections(FirmId, ClientId, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2026, 1, 1),
    [
      new(" department ", "FIN"),
      new("DEPARTMENT", "UNKNOWN"),
      new("PROJECT", "OLD"),
      new("BRANCH", "BR-1"),
      new("INVALID_TYPE", "X")
    ]);

    Assert.Contains(invalid, x => x.Code == "dimension-schema.selection.scope.duplicate");
    Assert.Contains(invalid, x => x.Code == "dimension-schema.selection.value.unknown");
    Assert.Contains(invalid, x => x.Code == "dimension-schema.selection.out-of-period");
    Assert.Contains(invalid, x => x.Code == "dimension-schema.selection.definition.unknown");
    Assert.Contains(invalid, x => x.Code == "dimension-schema.selection.type.unknown");

    var missingRequired = schema.ValidateSelections(FirmId, ClientId, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2026, 1, 1), []);
    Assert.Contains(missingRequired, x => x.Code == "dimension-schema.selection.required");

    var foreignScope = schema.ValidateSelections(FirmId, OtherClientId, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2026, 1, 1), []);
    Assert.Contains(foreignScope, x => x.Code == "dimension-schema.scope.mismatch");
  }

  [Fact]
  public void DimensionSchemaSuccessor_PreservesExistingIdsAcrossCodeChangesAndRetainsHistory()
  {
    var baseRevision = BuildSchema(
      Definition(201, AccountingDimensionTypes.Department, "DEPT", "Department", false,
      [Value(202, "FIN", "Finance", new DateOnly(2026, 1, 1))]));
    var successorDraft = Draft(
      Definition(201, AccountingDimensionTypes.Department, "DEPT", "Departments", false,
      [
        Value(202, "FINANCE", "Finance Unit", new DateOnly(2026, 1, 1), isActive: false),
        Value(203, "HR", "Human resources", new DateOnly(2026, 1, 1))
      ])) with
    {
      RevisionId = StableId(204),
      Revision = 2,
      BaseRevisionId = baseRevision.RevisionId
    };

    var successorResult = ClientAccountingDimensionSchemaRevision.Create(successorDraft);

    Assert.True(successorResult.IsValid);
    var successor = successorResult.Value!;
    Assert.Empty(successor.ValidateSuccessorFrom(baseRevision));
    Assert.Equal(baseRevision.Definitions[0].Id, successor.Definitions[0].Id);
    Assert.Equal(baseRevision.Definitions[0].Values[0].Id, successor.Definitions[0].Values[0].Id);
    Assert.False(successor.Definitions[0].Values[0].IsActive);

    var droppedValue = Draft(
      Definition(201, AccountingDimensionTypes.Department, "DEPT", "Department", false, [])) with
    {
      RevisionId = StableId(205),
      Revision = 2,
      BaseRevisionId = baseRevision.RevisionId
    };
    var dropped = ClientAccountingDimensionSchemaRevision.Create(droppedValue);

    Assert.True(dropped.IsValid);
    Assert.Contains(dropped.Value!.ValidateSuccessorFrom(baseRevision),
      x => x.Code == "dimension-schema.successor.value-identity-missing");
  }

  [Fact]
  public void DimensionSchemaDraftEdit_RequiresPreviouslyAssignedDefinitionAndValueIds()
  {
    var priorDraft = Draft(
      Definition(211, AccountingDimensionTypes.Department, "DEPT", "Department", false,
      [Value(212, "FIN", "Finance", new DateOnly(2026, 1, 1))])) with
    {
      RevisionId = StableId(213)
    };
    var changedIdentity = Draft(
      Definition(214, AccountingDimensionTypes.Department, "DEPT", "Department", false,
      [Value(215, "FIN", "Finance", new DateOnly(2026, 1, 1))])) with
    {
      RevisionId = priorDraft.RevisionId
    };
    var prior = ClientAccountingDimensionSchemaRevision.Create(priorDraft).Value!;
    var changed = ClientAccountingDimensionSchemaRevision.Create(changedIdentity).Value!;

    var issues = changed.ValidateDraftEditFrom(prior);

    Assert.Contains(issues, x => x.Code == "dimension-schema.successor.definition-identity-missing");
    Assert.Contains(issues, x => x.Code == "dimension-schema.successor.value-identity-missing");
  }

  [Fact]
  public void DimensionSchemaMapper_RejectsAResolvedPredecessorDifferentFromRequestedBase()
  {
    var requestedBase = BuildSchema(
      Definition(221, AccountingDimensionTypes.Department, "DEPT", "Department", false,
        [Value(222, "FIN", "Finance", new DateOnly(2026, 1, 1))]));
    var accidentalBaseResult = ClientAccountingDimensionSchemaRevision.Create(
      Draft(Definition(223, AccountingDimensionTypes.Department, "DEPT", "Department", false,
        [Value(224, "FIN", "Finance", new DateOnly(2026, 1, 1))])) with
      {
        RevisionId = StableId(226)
      });
    Assert.True(accidentalBaseResult.IsValid);
    var accidentallyResolvedBase = accidentalBaseResult.Value!;

    var result = DimensionSchemaDraftMapper.BuildSnapshot(
      FirmId, DraftDto(requestedBase, requestedBase.RevisionId), requestedBase.SchemaId,
      StableId(225), 2, accidentallyResolvedBase);

    Assert.False(result.IsValid);
    Assert.Contains(result.Issues, x => x.Code == "dimension-schema.base.reference-mismatch");
  }

  [Fact]
  public void DimensionSchemaMapper_RejectsPredecessorOrEditedDraftFromDifferentScope()
  {
    var foreignOwnerBaseDraft = Draft(
      Definition(231, AccountingDimensionTypes.Department, "DEPT", "Department", false,
        [Value(232, "FIN", "Finance", new DateOnly(2026, 1, 1))])) with { ClientId = OtherClientId };
    var foreignOwnerBaseResult = ClientAccountingDimensionSchemaRevision.Create(foreignOwnerBaseDraft);
    Assert.True(foreignOwnerBaseResult.IsValid);
    var foreignOwnerBase = foreignOwnerBaseResult.Value!;
    var foreignBaseRequest = DraftDto(foreignOwnerBase, foreignOwnerBase.RevisionId);

    var wrongBase = DimensionSchemaDraftMapper.BuildSnapshot(
      FirmId, foreignBaseRequest, foreignOwnerBase.SchemaId, StableId(233), 2, foreignOwnerBase);
    Assert.False(wrongBase.IsValid);
    Assert.Contains(wrongBase.Issues, x => x.Code == "dimension-schema.base.scope-mismatch");

    var draft = BuildSchema(
      Definition(234, AccountingDimensionTypes.Department, "DEPT", "Department", false,
        [Value(235, "FIN", "Finance", new DateOnly(2026, 1, 1))]));
    var wrongDraftRequest = DraftDto(draft, null, draftRevisionId: StableId(236));
    var wrongDraft = DimensionSchemaDraftMapper.BuildSnapshot(
      FirmId, wrongDraftRequest, draft.SchemaId, draft.RevisionId, draft.Revision, null, draft);

    Assert.False(wrongDraft.IsValid);
    Assert.Contains(wrongDraft.Issues, x => x.Code == "dimension-schema.draft.owner-or-lineage-mismatch");

    var foreignDraftResult = ClientAccountingDimensionSchemaRevision.Create(
      Draft(Definition(237, AccountingDimensionTypes.Department, "DEPT", "Department", false,
        [Value(238, "FIN", "Finance", new DateOnly(2026, 1, 1))])) with
      {
        ClientId = OtherClientId,
        RevisionId = StableId(239)
      });
    Assert.True(foreignDraftResult.IsValid);
    var foreignDraft = foreignDraftResult.Value!;
    var foreignDraftRequest = DraftDto(foreignDraft, null, draftRevisionId: foreignDraft.RevisionId);
    var wrongOwnerDraft = DimensionSchemaDraftMapper.BuildSnapshot(
      FirmId, foreignDraftRequest, foreignDraft.SchemaId, foreignDraft.RevisionId, foreignDraft.Revision,
      null, foreignDraft);

    Assert.False(wrongOwnerDraft.IsValid);
    Assert.Contains(wrongOwnerDraft.Issues, x => x.Code == "dimension-schema.draft.owner-or-lineage-mismatch");
  }

  private static DimensionSchemaDraftDto DraftDto(
    ClientAccountingDimensionSchemaRevision source,
    Guid? baseRevisionId,
    Guid? draftRevisionId = null)
  {
    var definitions = source.Definitions.Select(definition => new DimensionSchemaDefinitionDraftDto(
      definition.DimensionType, definition.Code, definition.Name, definition.IsRequired,
      definition.Values.Select(value => new DimensionSchemaValueDraftDto(
        value.Code, value.Name, value.EffectiveFrom, value.EffectiveTo, value.IsActive, value.Id)).ToArray(),
      definition.Id)).ToArray();
    return new DimensionSchemaDraftDto(ClientId, baseRevisionId, definitions, draftRevisionId);
  }

  private static ClientAccountingDimensionSchemaRevision BuildSchema(
    params ClientAccountingDimensionSchemaDefinitionDraft[] definitions)
  {
    var result = ClientAccountingDimensionSchemaRevision.Create(Draft(definitions));
    Assert.True(result.IsValid, string.Join(", ", result.Issues.Select(x => $"{x.Code}@{x.Path}")));
    return result.Value!;
  }

  private static ClientAccountingDimensionSchemaDraft Draft(
    params ClientAccountingDimensionSchemaDefinitionDraft[] definitions) =>
    new(FirmId, ClientId, StableId(1), StableId(2), 1, definitions);

  private static ClientAccountingDimensionSchemaDefinitionDraft Definition(
    int id,
    string type,
    string code,
    string name,
    bool isRequired,
    IReadOnlyList<ClientAccountingDimensionSchemaValueDraft> values) =>
    new(StableId(id), type, code, name, isRequired, values);

  private static ClientAccountingDimensionSchemaValueDraft Value(
    int id,
    string code,
    string name,
    DateOnly effectiveFrom,
    DateOnly? effectiveTo = null,
    bool isActive = true) =>
    new(StableId(id), code, name, isActive, effectiveFrom, effectiveTo);

  private static Guid StableId(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:D12}");

  private static readonly Guid FirmId = StableId(100);
  private static readonly Guid ClientId = StableId(101);
  private static readonly Guid OtherClientId = StableId(102);
}
