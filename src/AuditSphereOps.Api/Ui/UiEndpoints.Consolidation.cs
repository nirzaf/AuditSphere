using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record CreateConsolidationGroupInput(string? Code, string? Name);
  public sealed record AddConsolidationGroupMemberInput(
    Guid ClientId, DateOnly? EffectiveFrom, DateOnly? EffectiveTo, string? ControlMethod,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal? OwnershipPercent,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal? EconomicInterestPercent,
    string? EvidenceReference);

  public sealed record AdvancedScheduleInput(string SourceManifestJson, string InputSnapshotJson);

  public sealed record AddConsolidationOwnershipInput(
    Guid ParentClientId, Guid ChildClientId, string? EffectiveFrom, string? EffectiveTo,
    string? OwnershipPercent, string? EconomicInterestPercent, string? ControlAssessment,
    string? Method, string? EvidenceReference, long ExpectedGroupRevision, bool Reviewed);

  private static bool OwnershipPercentage(string? value, out decimal percentage)
  {
    percentage = 0;
    return value is { Length: > 0 and <= 27 } &&
      System.Text.RegularExpressions.Regex.IsMatch(value, @"^[0-9]{1,20}(?:\.[0-9]{1,6})?$") &&
      TryDecimal(value, out percentage) && percentage is >= 0 and <= 100;
  }

  public sealed record CreateConsolidationScopeInput(
    Guid GroupId,
    Guid PeriodId,
    string ReportingCurrency,
    string Method,
    string OpeningBasis,
    Guid? ExchangeRateSetVersionId,
    Guid? TranslationPolicyVersionId,
    DateOnly? TranslationRateDate,
    string? TranslationRateType,
    Guid? PriorScopeVersionId,
    long ExpectedGroupRevision);

  public sealed record SubmitComponentInput(
    Guid ClientId,
    Guid EngagementId,
    Guid PackageId,
    decimal OwnershipPercent,
    string ControlMethod,
    string PeriodBasis,
    string TaxonomyVersion,
    string MappingVersion);

  public sealed record CreateConsolidationJournalInput(
    string JournalNumber,
    string JournalType,
    string Currency,
    string EvidenceReference,
    IReadOnlyList<ConsolidationJournalLineInput> Lines);

  public sealed record ReturnConsolidationJournalInput(string Reason);
  public sealed record AddIntercompanyMatchInput(
    Guid SellerClientId, Guid BuyerClientId, string? AccountNature, string? PeriodCode, string? Currency,
    string? TransactionReference, string? SellerAmount, string? BuyerAmount, string? MatchedAmount,
    string? EvidenceReference, string? SellerTaxonomyCode, string? BuyerTaxonomyCode,
    string? DifferenceReason, string? MatchMode, string? MatchGroupReference,
    bool OutsidePerimeterReview, long ExpectedGroupRevision, bool Reviewed);
  public sealed record ApproveIntercompanyMatchInput(long ExpectedGroupRevision, bool Reviewed);

  public sealed record ExternalComponentPackLineInput(
    string? TaxonomyCode, string? Amount, string? Currency, string? SourceLineReference);
  public sealed record SubmitExternalComponentPackInput(
    Guid ClientId, Guid EngagementId, string? PeriodStart, string? PeriodEnd, string? Framework,
    string? ReportingCurrency, string? PeriodBasis, string? TaxonomyVersion, string? MappingVersion,
    string? SourceReference, string? RawSourceHash, string? NormalizedSourceDigest, string? DeclaredSignedTotal,
    IReadOnlyList<ExternalComponentPackLineInput?>? Lines, Guid? PriorPackId, string? CompatibilityBridgeReference,
    long ExpectedGroupRevision, bool Reviewed);
  public sealed record ReconcileExternalComponentPackInput(
    long ExpectedGroupRevision, bool Reviewed, string? EvidenceReference);
  public sealed record ReturnExternalComponentPackInput(
    long ExpectedGroupRevision, bool Reviewed, string? Reason);
  public sealed record ApproveExternalComponentPackInput(long ExpectedGroupRevision, bool Reviewed);
  public sealed record PinExternalComponentInput(
    Guid ExternalComponentPackId, string? OwnershipPercent, string? ControlMethod,
    long ExpectedGroupRevision, bool Reviewed);

  private sealed record ExternalPackScopeRevision(Guid GroupId, long PinnedRevision, long CurrentRevision);

  private static bool TryExternalPackAmount(string? value, out decimal amount)
  {
    amount = 0;
    return value is { Length: > 0 and <= 21 } &&
      System.Text.RegularExpressions.Regex.IsMatch(value, @"^-?\d{1,13}(?:\.\d{1,6})?$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant) && TryDecimal(value, out amount);
  }

  private static bool ExternalPackText(string? value, int maxLength, bool optional = false) =>
    optional ? value is null || value.Length <= maxLength :
      value is not null && value.Length is > 0 && value.Length <= maxLength && !string.IsNullOrWhiteSpace(value);

  private static bool ExternalPackSha256(string? value) =>
    value is { Length: 64 } && System.Text.RegularExpressions.Regex.IsMatch(value, @"^[0-9a-f]{64}$",
      System.Text.RegularExpressions.RegexOptions.CultureInvariant);

  private static async Task<CommandResult<ExternalPackScopeRevision>> CheckExternalPackScopeRevisionAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid scopeId, long expectedGroupRevision,
    Guid? packId, CancellationToken ct)
  {
    var scope = await db.ConsolidationScopeVersions.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == scopeId, ct);
    if (scope is null)
      return CommandResult<ExternalPackScopeRevision>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var authorization = await AuthorizationDecision.AuthorizeGroupAsync(db, actor, scope.GroupId,
      ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"], ct);
    if (!authorization.Succeeded)
      return CommandResult<ExternalPackScopeRevision>.Fail(authorization.ErrorCode!, authorization.Message!);
    if (packId is { } id && !await db.ExternalComponentPacks.AsNoTracking().AnyAsync(x =>
        x.FirmId == actor.FirmId && x.GroupId == scope.GroupId && x.ScopeVersionId == scope.Id && x.Id == id, ct))
      return CommandResult<ExternalPackScopeRevision>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var currentRevision = await db.ClientGroups.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.Id == scope.GroupId)
      .Select(x => (long?)x.Revision).SingleOrDefaultAsync(ct);
    if (currentRevision is null)
      return CommandResult<ExternalPackScopeRevision>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (expectedGroupRevision < 1 || expectedGroupRevision != scope.GroupRevision || expectedGroupRevision != currentRevision.Value)
      return CommandResult<ExternalPackScopeRevision>.Fail(ErrorCodes.GenerationStale,
        "The group perimeter changed; review the current group revision and create a new scope version.");
    return CommandResult<ExternalPackScopeRevision>.Ok(new(scope.GroupId, scope.GroupRevision, currentRevision.Value));
  }

  private static bool TryIntercompanyAmount(string? value, out decimal amount)
  {
    amount = 0;
    return value is { Length: > 0 and <= 21 } &&
      System.Text.RegularExpressions.Regex.IsMatch(value, @"^-?\d{1,13}(?:\.\d{1,6})?$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant) && TryDecimal(value, out amount);
  }

  private static bool IntercompanyText(string? value, int maxLength, bool optional = false) =>
    optional ? value is null || value.Length <= maxLength :
      value is not null && value.Length > 0 && value.Length <= maxLength && !string.IsNullOrWhiteSpace(value);

  private static void MapConsolidationEndpoints(RouteGroupBuilder group)
  {
    group.MapUiGet("/consolidation", http => ReadAsync(http, (db, actor, ct) => ConsolidationOverviewQuery.GetAsync(db, actor, ct)))
      .Produces<ConsolidationOverview>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json");
    group.MapPost("/consolidation/groups", (CreateConsolidationGroupInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ConsolidationService.CreateGroupAsync(db, actor,
        new ClientGroupRequest(input.Code ?? string.Empty, input.Name ?? string.Empty), ct)))
      .WithDescription("Creates a firm-wide consolidation group from its code and name. Success is returned in the standard value envelope.")
      .Produces<UiValueResponse<Guid>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json");
    group.MapGet("/consolidation/groups/{groupId:guid}/members",
      (Guid groupId, HttpContext http, int page = 0, int pageSize = 25, Guid? clientId = null, DateOnly? effectiveFrom = null) =>
        ReadAsync(http, (db, actor, ct) => ConsolidationGroupWorkspaceQuery.GetAsync(
          db, actor, groupId, page, pageSize, clientId, effectiveFrom, ct)))
      .WithDescription("Returns the authorized member page and eligible same-firm client choices. Percentage fields are emitted as exact invariant decimal strings. page defaults to 0, pageSize to 25; clientId and effectiveFrom form an optional exact membership filter.")
      .Produces<ConsolidationGroupMembershipWorkspace>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json");
    group.MapGet("/consolidation/groups/{groupId:guid}/scope-options", (Guid groupId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => ConsolidationScopeCreationQuery.GetAsync(db, actor, groupId, ct)))
      .Produces<ConsolidationScopeCreationOptions>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json");
    group.MapPost("/consolidation/groups/{groupId:guid}/members",
      (Guid groupId, AddConsolidationGroupMemberInput input, HttpContext http) =>
        CommandAsync(http, (db, actor, ct) =>
        {
          if (input.EffectiveFrom is not { } effectiveFrom || input.OwnershipPercent is not { } ownershipPercent ||
              input.EconomicInterestPercent is not { } economicInterestPercent)
            return Task.FromResult(CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid,
              "Effective date and both percentage-point values are required."));
          return ConsolidationService.AddMembershipAsync(db, actor,
            new GroupMembershipRequest(groupId, input.ClientId, effectiveFrom, input.EffectiveTo,
              input.ControlMethod ?? string.Empty, ownershipPercent, economicInterestPercent,
              input.EvidenceReference ?? string.Empty), ct);
        }))
      .WithDescription("Adds an approved group membership. Ownership and economic-interest values are percentage points; exact decimal strings are canonical and JSON numbers are also accepted. EffectiveFrom and both percentages are required.")
      .Produces<UiValueResponse<bool>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json");

    // Scope workspace detail & lifecycle
    group.MapGet("/consolidation/scopes/{scopeId:guid}", (Guid scopeId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => ConsolidationScopeWorkspaceQuery.GetScopeWorkspaceAsync(db, actor, scopeId, ct)))
      .Produces<ConsolidationScopeWorkspaceDto>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json");
    group.MapGet("/consolidation/scopes/{scopeId:guid}/intercompany-match-options", (Guid scopeId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => ConsolidationQuery.GetIntercompanyMatchOptionsAsync(db, actor, scopeId, ct)))
      .Produces<IntercompanyMatchOptions>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json");
    group.MapGet("/consolidation/scopes/{scopeId:guid}/intercompany-matches",
      (Guid scopeId, HttpContext http, int page = 1, int pageSize = 100, bool unresolvedOnly = false) =>
        ReadAsync(http, (db, actor, ct) => ConsolidationQuery.GetIntercompanyExceptionsAsync(
          db, actor, scopeId, unresolvedOnly, page, pageSize, ct)))
      .WithDescription("Returns a bounded page of retained intercompany matches. Amount and difference fields are emitted as exact invariant decimal strings. unresolvedOnly defaults to false so resolved and approved history remains visible; set it to true for the exception view. page defaults to 1 and pageSize to 100 (maximum 500).")
      .Produces<IntercompanyExceptionsPage>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json");
    group.MapPost("/consolidation/scopes/{scopeId:guid}/intercompany-matches",
      (Guid scopeId, AddIntercompanyMatchInput? input, HttpContext http) =>
        CommandAsync(http, (db, actor, ct) =>
        {
          if (input is null || !input.Reviewed || input.ExpectedGroupRevision < 1 ||
              input.SellerClientId == Guid.Empty || input.BuyerClientId == Guid.Empty ||
              !TryIntercompanyAmount(input.SellerAmount, out var sellerAmount) ||
              !TryIntercompanyAmount(input.BuyerAmount, out var buyerAmount) ||
              !TryIntercompanyAmount(input.MatchedAmount, out var matchedAmount) ||
              !IntercompanyText(input.AccountNature, 100) || !IntercompanyText(input.PeriodCode, 100) ||
              !IntercompanyText(input.Currency, 3) || !IntercompanyText(input.TransactionReference, 200) ||
              !IntercompanyText(input.EvidenceReference, 2000) ||
              !IntercompanyText(input.SellerTaxonomyCode, 100) || !IntercompanyText(input.BuyerTaxonomyCode, 100) ||
              !IntercompanyText(input.DifferenceReason, 2000, optional: true) ||
              !IntercompanyText(input.MatchMode, 30) || !IntercompanyText(input.MatchGroupReference, 200, optional: true))
            return Task.FromResult(CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid,
              "Review the current group revision and provide bounded match details with exact signed amounts to six decimal places."));
          return ConsolidationService.AddIntercompanyMatchAsync(db, actor,
            new IntercompanyMatchRequest(scopeId, input.SellerClientId, input.BuyerClientId,
              input.AccountNature!, input.PeriodCode!, input.Currency!, input.TransactionReference!,
              sellerAmount, buyerAmount, matchedAmount, input.EvidenceReference!,
              input.SellerTaxonomyCode!, input.BuyerTaxonomyCode!, input.DifferenceReason ?? string.Empty,
              input.MatchMode!, input.MatchGroupReference ?? string.Empty, input.OutsidePerimeterReview,
              input.ExpectedGroupRevision), ct);
        }))
      .WithDescription("Appends a scope-bound intercompany match for independent review. SellerAmount, BuyerAmount and MatchedAmount are signed exact decimal strings with at most 13 integral and six fractional digits. The request must set Reviewed=true and supply the current ExpectedGroupRevision. Match approval does not create an elimination journal or write to component books.")
      .Produces<UiValueResponse<Guid>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json");
    group.MapPost("/consolidation/intercompany-matches/{matchId:guid}/approve",
      (Guid matchId, ApproveIntercompanyMatchInput? input, HttpContext http) =>
        CommandAsync(http, (db, actor, ct) => input is null || !input.Reviewed || input.ExpectedGroupRevision < 1
          ? Task.FromResult(CommandResult.Fail(ErrorCodes.Accounting.ReconciliationRejected,
            "Review the complete retained match under the current group revision before approval."))
          : ConsolidationService.ApproveIntercompanyMatchAsync(db, actor, matchId, input.ExpectedGroupRevision, ct)))
      .WithDescription("Independently approves a submitted intercompany match under its current group grant and revision. The request must set Reviewed=true and supply the current ExpectedGroupRevision; the preparer cannot approve their own match.")
      .Produces<UiValueResponse<bool>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json");
    group.MapGet("/consolidation/scopes/{scopeId:guid}/ownership-interests",
      (Guid scopeId, HttpContext http, int page = 0, int pageSize = 25, Guid? parentClientId = null, Guid? childClientId = null) =>
        ReadAsync(http, (db, actor, ct) => ConsolidationOwnershipWorkspaceQuery.GetAsync(
          db, actor, scopeId, page, pageSize, parentClientId, childClientId, ct)))
      .WithDescription("Returns a bounded ownership-interest history page and eligible current member options. Ownership and economic-interest percentages are emitted as exact invariant decimal strings; page defaults to 0 and pageSize to 25 (maximum 100).")
      .Produces<ConsolidationOwnershipWorkspace>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json");
    group.MapPost("/consolidation/scopes/{scopeId:guid}/ownership-interests",
      (Guid scopeId, AddConsolidationOwnershipInput? input, HttpContext http) =>
        CommandAsync(http, (db, actor, ct) =>
        {
          if (input is null || !input.Reviewed || input.ExpectedGroupRevision < 1 ||
              !TryDate(input.EffectiveFrom, out var effectiveFrom) ||
              input.EffectiveTo is not null && !TryDate(input.EffectiveTo, out _) ||
              !OwnershipPercentage(input.OwnershipPercent, out var ownership) ||
              !OwnershipPercentage(input.EconomicInterestPercent, out var economic))
            return Task.FromResult(CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid,
              "Review the exact scope revision, ISO dates and percentage-point strings from 0 to 100 with at most six decimal places."));
          DateOnly? effectiveTo = input.EffectiveTo is null ? null : DateOnly.ParseExact(input.EffectiveTo, "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture);
          return ConsolidationService.AddOwnershipInterestAsync(db, actor,
            new OwnershipInterestRequest(scopeId, input.ParentClientId, input.ChildClientId,
              effectiveFrom, effectiveTo, ownership, economic, input.ControlAssessment ?? "", input.Method ?? "",
              input.EvidenceReference ?? "", input.ExpectedGroupRevision), ct);
        }))
      .WithDescription("Appends an immutable ownership relationship to the current authorized draft scope. " +
        "Effective dates use ISO yyyy-MM-dd strings; percentage points are exact decimal strings from 0 to 100 with at most six fractional digits. The request must set Reviewed=true and supply the current ExpectedGroupRevision. Existing relationships require a new scope for corrections. Scope and method approvals remain separate.")
      .Produces<UiValueResponse<Guid>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json");
    group.MapPost("/consolidation/scopes", (CreateConsolidationScopeInput? i, HttpContext http) =>
      i is null || i.ExpectedGroupRevision < 1
        ? Task.FromResult(Invalid("Load and review the current group options and revision before creating a scope."))
        : CommandAsync(http, (db, actor, ct) => ConsolidationService.CreateScopeAsync(db, actor,
        new ConsolidationScopeRequest(i.GroupId, i.PeriodId, i.ReportingCurrency, i.Method, i.OpeningBasis,
          i.ExchangeRateSetVersionId, i.TranslationPolicyVersionId, i.TranslationRateDate, i.TranslationRateType ?? "",
          i.PriorScopeVersionId, i.ExpectedGroupRevision), ct)))
      .WithDescription("Creates a new consolidation scope version using the selected group, reporting period, method and pinned approved translation inputs. The expected group revision is checked by the command.")
      .Produces<UiValueResponse<Guid>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json");
    group.MapPost("/consolidation/scopes/{id:guid}/approve", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ConsolidationService.ApproveScopeAsync(db, actor, id, ct)))
      .Produces<UiValueResponse<bool>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json");

    // External component source-pack workflow. The pack list is a bounded summary;
    // retained source lines and eligible intake options use independent bounded pages.
    group.MapGet("/consolidation/scopes/{scopeId:guid}/external-packs",
      (Guid scopeId, HttpContext http, int page = 1, int pageSize = 25,
        int clientPage = 1, int clientPageSize = 25, string? clientSearch = null,
        int engagementPage = 1, int engagementPageSize = 25, string? engagementSearch = null,
        Guid? selectedClientId = null, Guid? selectedEngagementId = null) =>
        ReadAsync(http, (db, actor, ct) => ConsolidationExternalComponentPackQuery.GetPageAsync(
          db, actor, scopeId, page, pageSize,
          clientPage, clientPageSize, clientSearch, engagementPage, engagementPageSize, engagementSearch,
          selectedClientId, selectedEngagementId, ct)))
      .WithDescription("Returns a bounded page of retained external component packs and independently paged eligible group entities and engagements. clientPage/engagementPage default to 1, their page sizes default to 25 (maximum 100), searches are bounded to 100 characters, and selectedClientId/selectedEngagementId return exact server-resolved selections when still eligible. page defaults to 1 and pageSize to 25 (maximum 100). Amounts and hashes are exact strings.")
      .Produces<ExternalComponentPackPage>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json");
    group.MapGet("/consolidation/scopes/{scopeId:guid}/external-packs/{packId:guid}",
      (Guid scopeId, Guid packId, HttpContext http, int linePage = 1, int linePageSize = 100) =>
        ReadAsync(http, (db, actor, ct) => ConsolidationExternalComponentPackQuery.GetPackAsync(
          db, actor, scopeId, packId, linePage, linePageSize, ct)))
      .WithDescription("Returns the exact retained source identity, review lineage and a page of typed lines for this pack. linePage defaults to 1 and linePageSize to 100 (maximum 100); HasPreviousLines and HasNextLines page through every retained line (at most 500 per valid pack). Decimal amounts are exact invariant strings.")
      .Produces<ExternalComponentPackDetail>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json");
    group.MapPost("/consolidation/scopes/{scopeId:guid}/external-packs",
      (Guid scopeId, SubmitExternalComponentPackInput? input, HttpContext http) =>
        CommandAsync(http, async (db, actor, ct) =>
        {
          if (input is null || !input.Reviewed || input.ExpectedGroupRevision < 1 ||
              input.ClientId == Guid.Empty || input.EngagementId == Guid.Empty || input.PriorPackId == Guid.Empty ||
              !TryDate(input.PeriodStart, out _) || !TryDate(input.PeriodEnd, out _) ||
              !ExternalPackText(input.Framework, 100) || !ExternalPackText(input.ReportingCurrency, 3) ||
              !System.Text.RegularExpressions.Regex.IsMatch(input.ReportingCurrency!, @"^[A-Z]{3}$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant) ||
              !ExternalPackText(input.PeriodBasis, 100) || !ExternalPackText(input.TaxonomyVersion, 100) ||
              !ExternalPackText(input.MappingVersion, 100) || !ExternalPackText(input.SourceReference, 2000) ||
              !ExternalPackSha256(input.RawSourceHash) || !ExternalPackSha256(input.NormalizedSourceDigest) ||
              !TryExternalPackAmount(input.DeclaredSignedTotal, out var declaredTotal) ||
              !ExternalPackText(input.CompatibilityBridgeReference, 2000, optional: true) ||
              input.Lines is not { Count: > 0 and <= ConsolidationExternalComponentPackQuery.MaximumPackLines })
            return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid,
              "Review the current group revision and provide a bounded, typed source pack with exact hashes and decimal strings.");

          var lines = new List<ExternalComponentPackLineRequest>(input.Lines.Count);
          foreach (var line in input.Lines)
          {
            if (line is null || !ExternalPackText(line.TaxonomyCode, 100) ||
                !TryExternalPackAmount(line.Amount, out var amount) ||
                !string.Equals(line.Currency, input.ReportingCurrency, StringComparison.Ordinal) ||
                !ExternalPackText(line.SourceLineReference, 200))
              return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid,
                "Every source line needs a bounded taxonomy code, exact amount, matching uppercase currency and source-line reference.");
            lines.Add(new ExternalComponentPackLineRequest(
              line.TaxonomyCode!, amount, line.Currency!, line.SourceLineReference!));
          }

          var revision = await CheckExternalPackScopeRevisionAsync(db, actor, scopeId,
            input.ExpectedGroupRevision, packId: null, ct);
          if (!revision.Succeeded)
            return CommandResult<Guid>.Fail(revision.ErrorCode!, revision.Message!);
          if (!DateOnly.TryParseExact(input.PeriodStart, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var periodStart) ||
              !DateOnly.TryParseExact(input.PeriodEnd, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var periodEnd) || periodEnd < periodStart)
            return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid,
              "The reporting period must use ordered ISO yyyy-MM-dd dates.");

          return await ConsolidationService.SubmitExternalComponentPackAsync(db, actor,
            new ExternalComponentPackRequest(scopeId, input.ClientId, input.EngagementId,
              input.PeriodStart!, input.PeriodEnd!, input.Framework!, input.ReportingCurrency!, input.PeriodBasis!,
              input.TaxonomyVersion!, input.MappingVersion!, input.SourceReference!, input.RawSourceHash!,
              input.NormalizedSourceDigest!, declaredTotal, lines, input.PriorPackId,
              input.CompatibilityBridgeReference ?? string.Empty, input.ExpectedGroupRevision), ct);
        }))
      .WithDescription("Submits a typed external component source pack into the selected draft scope. The caller must provide SHA-256 hashes derived from the selected source bytes and normalized lines, exact decimal strings, the current group revision and Reviewed=true. Up to 500 lines are accepted. Resubmission links a retained returned pack using PriorPackId; changed dates or basis require a documented compatibility bridge. No component-book write or provider action occurs.")
      .Produces<UiValueResponse<Guid>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json");
    group.MapPost("/consolidation/scopes/{scopeId:guid}/external-packs/{packId:guid}/reconcile",
      (Guid scopeId, Guid packId, ReconcileExternalComponentPackInput? input, HttpContext http) =>
        CommandAsync(http, async (db, actor, ct) =>
        {
          if (input is null || !input.Reviewed || input.ExpectedGroupRevision < 1 ||
              !ExternalPackText(input.EvidenceReference, 2000))
            return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid,
              "Review the current group revision and provide independent reconciliation evidence.");
          var revision = await CheckExternalPackScopeRevisionAsync(db, actor, scopeId,
            input.ExpectedGroupRevision, packId, ct);
          if (!revision.Succeeded)
            return CommandResult.Fail(revision.ErrorCode!, revision.Message!);
          return await ConsolidationService.ReconcileExternalComponentPackAsync(db, actor,
            new ExternalComponentReconciliationRequest(packId, input.EvidenceReference!, input.ExpectedGroupRevision), ct);
        }))
      .WithDescription("Independently reconciles a submitted external pack using evidence under the current group revision. The submitter cannot reconcile their own pack.")
      .Produces<UiValueResponse<bool>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json");
    group.MapPost("/consolidation/scopes/{scopeId:guid}/external-packs/{packId:guid}/return",
      (Guid scopeId, Guid packId, ReturnExternalComponentPackInput? input, HttpContext http) =>
        CommandAsync(http, async (db, actor, ct) =>
        {
          if (input is null || !input.Reviewed || input.ExpectedGroupRevision < 1 ||
              !ExternalPackText(input.Reason, 2000))
            return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid,
              "Review the current group revision and provide a bounded return reason.");
          var revision = await CheckExternalPackScopeRevisionAsync(db, actor, scopeId,
            input.ExpectedGroupRevision, packId, ct);
          if (!revision.Succeeded)
            return CommandResult.Fail(revision.ErrorCode!, revision.Message!);
          return await ConsolidationService.ReturnExternalComponentPackAsync(
            db, actor, packId, input.Reason!, ct, input.ExpectedGroupRevision);
        }))
      .WithDescription("Returns an active submitted external pack with a reason. The submitter cannot return their own pack; resubmission creates a new immutable linked version.")
      .Produces<UiValueResponse<bool>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json");
    group.MapPost("/consolidation/scopes/{scopeId:guid}/external-packs/{packId:guid}/approve",
      (Guid scopeId, Guid packId, ApproveExternalComponentPackInput? input, HttpContext http) =>
        CommandAsync(http, async (db, actor, ct) =>
        {
          if (input is null || !input.Reviewed || input.ExpectedGroupRevision < 1)
            return CommandResult.Fail(ErrorCodes.Accounting.ReconciliationRejected,
              "Review the retained reconciled pack under the current group revision before approval.");
          var revision = await CheckExternalPackScopeRevisionAsync(db, actor, scopeId,
            input.ExpectedGroupRevision, packId, ct);
          if (!revision.Succeeded)
            return CommandResult.Fail(revision.ErrorCode!, revision.Message!);
          return await ConsolidationService.ApproveExternalComponentPackAsync(
            db, actor, packId, ct, input.ExpectedGroupRevision);
        }))
      .WithDescription("Independently approves a submitted and reconciled pack under the current group revision. The preparer cannot approve their own source pack.")
      .Produces<UiValueResponse<bool>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json");
    group.MapPost("/consolidation/scopes/{scopeId:guid}/external-components",
      (Guid scopeId, PinExternalComponentInput? input, HttpContext http) =>
        CommandAsync(http, async (db, actor, ct) =>
        {
          if (input is null || !input.Reviewed || input.ExpectedGroupRevision < 1 ||
              input.ExternalComponentPackId == Guid.Empty ||
              !OwnershipPercentage(input.OwnershipPercent, out var ownershipPercent) ||
              !string.Equals(input.ControlMethod, "CONTROLLED", StringComparison.OrdinalIgnoreCase))
            return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid,
              "Review the approved pack, current group revision, exact ownership percentage points and supported control method.");
          var revision = await CheckExternalPackScopeRevisionAsync(db, actor, scopeId,
            input.ExpectedGroupRevision, input.ExternalComponentPackId, ct);
          if (!revision.Succeeded)
            return CommandResult<Guid>.Fail(revision.ErrorCode!, revision.Message!);
          return await ConsolidationService.SubmitExternalComponentAsync(db, actor,
            new ExternalComponentRequest(scopeId, input.ExternalComponentPackId, ownershipPercent,
              input.ControlMethod!, input.ExpectedGroupRevision), ct);
        }))
      .WithDescription("Pins an approved external pack as a consolidation component through SubmitExternalComponentAsync. OwnershipPercent is an exact decimal percentage-point string; the existing service requires 100 and CONTROLLED. Component approval remains a separate action. This route does not write to client books.")
      .Produces<UiValueResponse<Guid>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json");

    // Component commands
    group.MapPost("/consolidation/scopes/{scopeId:guid}/components", (Guid scopeId, SubmitComponentInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ConsolidationService.SubmitComponentAsync(db, actor,
        new ConsolidationComponentRequest(scopeId, i.ClientId, i.EngagementId, i.PackageId, i.OwnershipPercent,
          i.ControlMethod, i.PeriodBasis, i.TaxonomyVersion, i.MappingVersion), ct)))
      .WithDescription("Submits a pinned client package as a consolidation component. OwnershipPercent is an exact decimal percentage-point value; the command validates the selected scope and source package.")
      .Produces<UiValueResponse<Guid>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json");
    group.MapPost("/consolidation/components/{id:guid}/approve", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ConsolidationService.ApproveComponentAsync(db, actor, id, ct)))
      .Produces<UiValueResponse<bool>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json");

    // Elimination journals
    group.MapPost("/consolidation/scopes/{scopeId:guid}/journals", (Guid scopeId, CreateConsolidationJournalInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ConsolidationService.CreateConsolidationJournalAsync(db, actor,
        new ConsolidationJournalRequest(scopeId, i.JournalNumber, i.JournalType, i.Currency, i.EvidenceReference, i.Lines ?? []), ct)))
      .WithDescription("Creates a group-only consolidation journal. Monetary line amounts are exact decimals serialized as invariant strings; request JSON numbers are accepted for compatibility. This route never posts to a component book.")
      .Produces<UiValueResponse<Guid>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json");
    group.MapPost("/consolidation/journals/{id:guid}/approve", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ConsolidationService.ApproveConsolidationJournalAsync(db, actor, id, ct)))
      .Produces<UiValueResponse<bool>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json");
    group.MapPost("/consolidation/journals/{id:guid}/return", (Guid id, ReturnConsolidationJournalInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ConsolidationService.ReturnConsolidationJournalAsync(db, actor, id, i.Reason ?? "", ct)))
      .Produces<UiValueResponse<bool>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json");
    group.MapPost("/consolidation/journals/{id:guid}/resubmit", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ConsolidationService.ResubmitConsolidationJournalAsync(db, actor, id, ct)))
      .Produces<UiValueResponse<bool>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json");

    // Consolidation calculation & run approval
    group.MapPost("/consolidation/scopes/{scopeId:guid}/run", (Guid scopeId, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ConsolidationService.RunAsync(db, actor, scopeId, ct)))
      .Produces<UiValueResponse<Guid>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json");
    group.MapPost("/consolidation/runs/{id:guid}/approve", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ConsolidationService.ApproveRunAsync(db, actor, id, ct)))
      .Produces<UiValueResponse<bool>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json");

    // Advanced schedules and executions
    group.MapGet("/consolidation/advanced/{scopeId:guid}", (Guid scopeId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AdvancedConsolidationWorkspaceQuery.GetAsync(db, actor, scopeId, ct)))
      .Produces<AdvancedConsolidationWorkspace>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json");
    group.MapPost("/consolidation/advanced/{scopeId:guid}/schedules", (Guid scopeId, AdvancedScheduleInput i, HttpContext http) =>
      CommandAsync(http, async (db, actor, ct) =>
      {
        var workspace = await AdvancedConsolidationWorkspaceQuery.GetAsync(db, actor, scopeId, ct);
        if (!workspace.Succeeded) return Domain.Shared.CommandResult<Guid>.Fail(workspace.ErrorCode!, workspace.Message!);
        return await ConsolidationService.CreateAdvancedMethodScheduleAsync(db, actor, new AdvancedConsolidationMethodScheduleRequest(scopeId, workspace.Value!.Scope.Method,
          "IFRS", i.SourceManifestJson ?? "", i.InputSnapshotJson ?? ""), ct);
      }))
      .WithDescription("Creates an advanced method schedule from the source manifest and input snapshot JSON. The schedule is independently reviewed before execution.")
      .Produces<UiValueResponse<Guid>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json");
    group.MapPost("/consolidation/schedules/{id:guid}/approve", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ConsolidationService.ApproveAdvancedMethodScheduleAsync(db, actor, id, ct)))
      .Produces<UiValueResponse<bool>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json");
    group.MapPost("/consolidation/advanced/{scopeId:guid}/executions", (Guid scopeId, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AdvancedConsolidationWorkspaceQuery.RunExecutionAsync(db, actor, scopeId, ct)))
      .Produces<UiValueResponse<Guid>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json");
    group.MapPost("/consolidation/executions/{id:guid}/approve", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ConsolidationService.ApproveAdvancedExecutionAsync(db, actor, id, ct)))
      .Produces<UiValueResponse<bool>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json");
  }
}
