namespace AuditSphereOps.Application.Accounting;

/// <summary>Original source identity and immutable unallocated baseline, not a new invoice.</summary>
public sealed record NativeSuccessorCarriedItemView(string OriginalKind, Guid OriginalOpenItemId,
  Guid OriginalDocumentId, string Role, Guid CounterpartyId, string CounterpartyName,
  string Reference, string ReferenceKind, Guid AccountId, string AccountCode, string Currency,
  string OriginalAmount, string BaselineAppliedAmount, string BaselineOpenAmount,
  DateOnly? SourceAsOfDate, DateOnly? DocumentDate, DateOnly? AccountingDate, DateOnly? DueDate,
  string SourceFactsHash);

public sealed record NativeSuccessorCarriedPopulationView(string Version, int Count, string PopulationHash,
  IReadOnlyList<NativeSuccessorCarriedItemView> Items);

public static partial class ClientOperationalOpeningBalanceWorkspace
{
  internal const string SuccessorCarryVersion = "native-successor-carried-population.v1";
  internal const int SuccessorCarryLimit = 5000;

  // SourceFactsJson contains only actual versioned source facts. Its digest is not an
  // uploaded-file hash; unavailable original document/accounting dates remain null.
  internal sealed record SuccessorCarriedItem(string OriginalKind, Guid OriginalOpenItemId,
    Guid OriginalDocumentId, Guid? OriginalOpeningId, Guid? SubmissionId, Guid? DraftId, Guid? JournalId,
    string Role, Guid CounterpartyId, string CounterpartyName, string Reference, string ReferenceKind,
    Guid AccountId, string AccountCode, string Currency, string OriginalAmount,
    string BaselineAppliedAmount, string BaselineOpenAmount, DateOnly? SourceAsOfDate,
    DateOnly? DocumentDate, DateOnly? AccountingDate, DateOnly? DueDate,
    string SourceFactsJson, string SourceFactsHash);

  internal sealed record SuccessorCarriedPopulation(string Version, int Count, string PopulationHash,
    string ControlStateHash, IReadOnlyList<SuccessorCarriedItem> Items, string ControlStateJson);
}
