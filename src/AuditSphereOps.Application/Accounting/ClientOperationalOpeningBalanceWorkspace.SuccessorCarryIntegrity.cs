using System.Globalization;
using System.Text;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Application.Accounting;

public static partial class ClientOperationalOpeningBalanceWorkspace
{
  private static bool ValidSuccessorCarry(SuccessorCarriedPopulation population, SuccessorFacts facts)
  {
    if (population.Version != SuccessorCarryVersion || population.Items is null ||
        population.Count is < 1 or > SuccessorCarryLimit || population.Count != population.Items.Count ||
        string.IsNullOrEmpty(population.PopulationHash) || !IsSha256(population.PopulationHash) ||
        string.IsNullOrEmpty(population.ControlStateHash) || !IsSha256(population.ControlStateHash) ||
        string.IsNullOrEmpty(population.ControlStateJson) || Encoding.UTF8.GetByteCount(population.ControlStateJson) > 512 * 1024 ||
        SuccessorHash(population.ControlStateJson) != population.ControlStateHash ||
        facts.Controls is null || facts.Controls.Count != 2 ||
        facts.Controls.Any(x => x is null) || facts.Controls.Select(x => x.Role).Distinct().Count() != 2 ||
        facts.Controls.Any(x => x.Role is not ("AR" or "AP")) ||
        population.Items.Any(x => x is null) ||
        population.PopulationHash != SuccessorCarriedPopulationHash(population.Items, population.ControlStateHash) ||
        facts.PopulationHash != population.PopulationHash ||
        !population.Items.SequenceEqual(population.Items.OrderBy(x => x.OriginalKind, StringComparer.Ordinal).ThenBy(x => x.OriginalOpenItemId)) ||
        population.Items.Select(x => x.OriginalOpenItemId).Distinct().Count() != population.Count)
      return false;
    foreach (var item in population.Items)
    {
      if (item.OriginalKind is not ("OPENING_AR_INVOICE" or "OPENING_AP_INVOICE" or "SALES_INVOICE" or "PURCHASE_INVOICE") ||
          item.Role != (item.OriginalKind is "OPENING_AR_INVOICE" or "SALES_INVOICE" ? "AR" : "AP") ||
          item.OriginalOpenItemId == Guid.Empty || item.OriginalDocumentId == Guid.Empty ||
          item.CounterpartyId == Guid.Empty || item.AccountId == Guid.Empty || item.Currency != facts.Currency ||
          string.IsNullOrWhiteSpace(item.CounterpartyName) || string.IsNullOrWhiteSpace(item.Reference) ||
          string.IsNullOrWhiteSpace(item.ReferenceKind) || string.IsNullOrWhiteSpace(item.AccountCode) ||
          string.IsNullOrWhiteSpace(item.SourceFactsJson) || Encoding.UTF8.GetByteCount(item.SourceFactsJson) > 16_777_216 ||
          string.IsNullOrEmpty(item.SourceFactsHash) || !IsSha256(item.SourceFactsHash) || SuccessorHash(item.SourceFactsJson) != item.SourceFactsHash ||
          !decimal.TryParse(item.OriginalAmount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) ||
          amount <= 0m || amount != MoneyPolicy.Normalize(amount) ||
          !decimal.TryParse(item.BaselineAppliedAmount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var applied) || applied != 0m ||
          !decimal.TryParse(item.BaselineOpenAmount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var open) || open != amount ||
          !facts.Controls.Any(x => x.Role == item.Role && x.AccountId == item.AccountId))
        return false;
      if (item.OriginalKind is "OPENING_AR_INVOICE" or "OPENING_AP_INVOICE")
      {
        if (item.OriginalOpeningId != item.OriginalDocumentId || item.SubmissionId is not null ||
            item.DraftId is not null || item.JournalId is not null || item.SourceAsOfDate is null ||
            item.SourceAsOfDate > facts.PriorEnd)
          return false;
      }
      else if (item.OriginalOpeningId is not null || item.SubmissionId is null || item.DraftId is null ||
               item.JournalId is null || item.DocumentDate is null || item.AccountingDate is null ||
               item.AccountingDate > facts.PriorEnd)
        return false;
    }
    return facts.Controls.All(control =>
      population.Items.Where(x => x.Role == control.Role && x.AccountId == control.AccountId)
        .Sum(x => decimal.Parse(x.BaselineOpenAmount, CultureInfo.InvariantCulture)) ==
      (control.Role == "AR" ? decimal.Parse(control.ClosingDebit, CultureInfo.InvariantCulture) - decimal.Parse(control.ClosingCredit, CultureInfo.InvariantCulture) :
        decimal.Parse(control.ClosingCredit, CultureInfo.InvariantCulture) - decimal.Parse(control.ClosingDebit, CultureInfo.InvariantCulture)));
  }

  private static NativeSuccessorCarriedPopulationView? CarryView(SuccessorCarriedPopulation? population) =>
    population is null ? null : new(population.Version, population.Count, population.PopulationHash,
      population.Items.Select(x => new NativeSuccessorCarriedItemView(x.OriginalKind, x.OriginalOpenItemId,
        x.OriginalDocumentId, x.Role, x.CounterpartyId, x.CounterpartyName, x.Reference, x.ReferenceKind,
        x.AccountId, x.AccountCode, x.Currency, x.OriginalAmount, x.BaselineAppliedAmount, x.BaselineOpenAmount,
        x.SourceAsOfDate, x.DocumentDate, x.AccountingDate, x.DueDate, x.SourceFactsHash)).ToArray());
}
