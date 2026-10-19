using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public static partial class FeeAgreementService
{
  /// <summary>Retained PAID evidence, adjusted for subsequent independently approved cash reversals.</summary>
  public static async Task<bool> AdvanceCurrentlyPaidAsync(
    IAuditSphereDbContext db, Guid firmId, Guid agreementId, CancellationToken ct = default)
  {
    var milestone = await db.FeeMilestones.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == firmId && x.AgreementId == agreementId && x.Kind == FeeMilestoneKinds.Advance, ct);
    return milestone is not null && await IsMilestoneCurrentlyPaidAsync(db, milestone, ct);
  }

  internal static async Task<bool> IsMilestoneCurrentlyPaidAsync(
    IAuditSphereDbContext db, FeeMilestone milestone, CancellationToken ct)
  {
    if (milestone.State != FeeMilestoneStates.Paid || milestone.InvoiceId is not { } invoiceId) return false;
    // A paid marker is retained history, not proof that cash remains applied.
    var invoiceMatches = await (from invoice in db.Invoices.AsNoTracking()
      join account in db.BillingAccounts.AsNoTracking() on invoice.BillingAccountId equals account.Id
      join agreement in db.EngagementFeeAgreements.AsNoTracking() on account.PracticeClientId equals agreement.PracticeClientId
      where invoice.FirmId == milestone.FirmId && account.FirmId == milestone.FirmId && agreement.FirmId == milestone.FirmId &&
        invoice.Id == invoiceId && agreement.Id == milestone.AgreementId &&
        invoice.Currency == agreement.Currency && account.Currency == agreement.Currency && invoice.Total == milestone.Amount &&
        (invoice.Status == BillingStates.InvoicePosted || invoice.Status == BillingStates.InvoiceSent)
      select invoice.Id).AnyAsync(ct);
    if (!invoiceMatches) return false;
    var applied = await BillingService.GetNetAppliedToInvoiceAsync(db, milestone.FirmId, invoiceId, ct);
    if (milestone.Kind == FeeMilestoneKinds.Balance)
      applied += await db.CreditNotes.AsNoTracking().Where(x => x.FirmId == milestone.FirmId &&
        x.InvoiceId == invoiceId && x.Status == BillingStates.CreditIssued).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
    return applied >= milestone.Amount;
  }

  private static async Task<CommandResult<Receipt?>> ResolveMilestoneReceiptAsync(
    IAuditSphereDbContext db, ActorContext actor, EngagementFeeAgreement agreement, FeeMilestone milestone,
    Invoice invoice, string fullReference, string legacyReference, CancellationToken ct)
  {
    static CommandResult<Receipt?> Conflict() => CommandResult<Receipt?>.Fail(
      "fee.reference-conflict", "This payment reference has conflicting retained billing evidence.");

    if (agreement.FirmId != actor.FirmId || milestone.FirmId != actor.FirmId ||
        milestone.AgreementId != agreement.Id || milestone.Kind is not (FeeMilestoneKinds.Advance or FeeMilestoneKinds.Balance) ||
        invoice.FirmId != actor.FirmId || invoice.Id != milestone.InvoiceId ||
        invoice.BillingAccountId == Guid.Empty || string.IsNullOrWhiteSpace(fullReference) ||
        string.IsNullOrWhiteSpace(legacyReference) || string.Equals(fullReference, legacyReference, StringComparison.Ordinal))
      return Conflict();

    var account = await db.BillingAccounts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == invoice.BillingAccountId &&
      x.PracticeClientId == agreement.PracticeClientId, ct);
    if (account is null || !string.Equals(account.Currency, agreement.Currency, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(invoice.Currency, agreement.Currency, StringComparison.OrdinalIgnoreCase)) return Conflict();
    var currency = agreement.Currency.ToUpperInvariant();

    async Task<(bool Conflict, bool Current, bool ProvenOther)> InspectAsync(Receipt receipt, bool legacy)
    {
      if (receipt.FirmId != actor.FirmId || receipt.BillingAccountId != account.Id ||
          !string.Equals(receipt.Currency, account.Currency, StringComparison.OrdinalIgnoreCase) ||
          receipt.Status != BillingStates.ReceiptRecorded || receipt.Amount <= 0 ||
          MoneyPolicy.Normalize(receipt.Amount) != receipt.Amount)
        return (true, false, false);

      var sourceRequestId = BillingCommandWorkspace.DeriveRequestId(
        $"fee-milestone:receipt:{agreement.Id:N}:{milestone.Kind}:{receipt.Reference}");
      var sourceCommands = receipt.RecordedByUserId is { } recorderId
        ? await db.BillingCommandReceipts.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
            x.ActorId == recorderId && x.RequestId == sourceRequestId).Take(2).ToListAsync(ct)
        : [];
      if (sourceCommands.Count > 1) return (true, false, false);

      var hasSourceCommand = false;
      if (sourceCommands.Count == 1)
      {
        var command = sourceCommands[0];
        if (command.ClientId != agreement.PracticeClientId || command.Action != "RECORD_RECEIPT" ||
            command.ResultId != receipt.Id)
          return (true, false, false);
        try
        {
          var outcome = JsonSerializer.Deserialize<BillingCommandOutcome>(command.OutcomeJson);
          if (outcome is null || outcome.Action != "RECORD_RECEIPT" || outcome.ResultId != receipt.Id ||
              outcome.ReceiptId != receipt.Id || outcome.InvoiceId is not null ||
              outcome.Amount != BillingCommandWorkspace.Decimal(receipt.Amount))
            return (true, false, false);

          var requestHashMatches = command.RequestHash == BillingCommandWorkspace.IntentHash(
            "RECORD_RECEIPT", actor.FirmId, agreement.PracticeClientId, account.Id.ToString("D"),
            BillingCommandWorkspace.Decimal(receipt.Amount), receipt.Reference.Trim(), null);
          if (!requestHashMatches)
          {
            // Npgsql stores timestamptz by truncating ticks relative to PostgreSQL's 2000-01-01 epoch.
            const long postgresEpochTicks = 630822816000000000L;
            var storedUtcTicks = receipt.ReceivedAt.UtcDateTime.Ticks;
            for (var deltaTicks = -9L; deltaTicks <= 9; deltaTicks++)
            {
              var originalUtcTicks = storedUtcTicks + deltaTicks;
              if (originalUtcTicks < DateTime.MinValue.Ticks || originalUtcTicks > DateTime.MaxValue.Ticks)
                continue;

              var postgresMicroseconds = (originalUtcTicks - postgresEpochTicks) / 10L;
              var quantizedUtcTicks = postgresMicroseconds * 10L + postgresEpochTicks;
              if (quantizedUtcTicks != storedUtcTicks) continue;

              var receivedAt = new DateTimeOffset(originalUtcTicks, TimeSpan.Zero);
              var explicitDateHash = BillingCommandWorkspace.IntentHash(
                "RECORD_RECEIPT", actor.FirmId, agreement.PracticeClientId, account.Id.ToString("D"),
                BillingCommandWorkspace.Decimal(receipt.Amount), receipt.Reference.Trim(),
                receivedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
              if (command.RequestHash == explicitDateHash)
              {
                requestHashMatches = true;
                break;
              }
            }
          }
          if (!requestHashMatches) return (true, false, false);
          hasSourceCommand = true;
        }
        catch (JsonException)
        {
          return (true, false, false);
        }
      }

      var allocations = await db.ReceiptAllocations.AsNoTracking().Where(x =>
        x.FirmId == actor.FirmId && x.ReceiptId == receipt.Id).OrderBy(x => x.Id).Take(2).ToListAsync(ct);
      if (allocations.Count > 1) return (true, false, false);

      var currentAllocation = false;
      var otherAllocation = false;
      if (allocations.Count == 1)
      {
        var allocation = allocations[0];
        var sourceRows = await (
          from source in db.BillingSourceAllocations.AsNoTracking()
          join line in db.InvoiceLines.AsNoTracking()
            on new { source.FirmId, source.InvoiceLineId } equals new { line.FirmId, InvoiceLineId = line.Id }
          where source.FirmId == actor.FirmId && line.InvoiceId == allocation.InvoiceId
          orderby source.Id
          select new
          {
            SourceKind = source.SourceKind,
            SourceId = source.SourceId,
            SourceRevision = source.SourceRevision,
            LineSourceKind = line.SourceKind,
            LineSourceId = line.SourceId,
            LineSourceRevision = line.SourceRevision
          }).Take(2).ToListAsync(ct);
        if (sourceRows.Count != 1) return (true, false, false);

        var binding = sourceRows[0];
        var sourceFactsAgree = binding.SourceKind == binding.LineSourceKind && binding.SourceId == binding.LineSourceId &&
          binding.SourceRevision == binding.LineSourceRevision && binding.SourceKind == MilestoneSourceKind &&
          binding.SourceRevision == 1;
        if (!sourceFactsAgree) return (true, false, false);

        if (allocation.InvoiceId == invoice.Id)
        {
          if (binding.SourceId != milestone.Id) return (true, false, false);
          currentAllocation = true;
        }
        else
        {
          if (binding.SourceId == milestone.Id) return (true, false, false);
          otherAllocation = await (
            from otherMilestone in db.FeeMilestones.AsNoTracking()
            join otherAgreement in db.EngagementFeeAgreements.AsNoTracking()
              on new { otherMilestone.FirmId, Id = otherMilestone.AgreementId }
              equals new { otherAgreement.FirmId, otherAgreement.Id }
            join otherInvoice in db.Invoices.AsNoTracking()
              on new { FirmId = otherMilestone.FirmId, Id = allocation.InvoiceId }
              equals new { otherInvoice.FirmId, otherInvoice.Id }
            join otherAccount in db.BillingAccounts.AsNoTracking()
              on new { otherInvoice.FirmId, Id = otherInvoice.BillingAccountId }
              equals new { otherAccount.FirmId, otherAccount.Id }
            where otherMilestone.FirmId == actor.FirmId && otherMilestone.Id == binding.SourceId &&
              otherMilestone.AgreementId != agreement.Id && otherMilestone.Kind == milestone.Kind &&
              otherMilestone.InvoiceId == allocation.InvoiceId && otherAgreement.PracticeClientId == agreement.PracticeClientId &&
              otherInvoice.BillingAccountId == account.Id && otherAgreement.Currency.ToUpper() == currency &&
              otherInvoice.Currency != null && otherInvoice.Currency.ToUpper() == currency &&
              otherAccount.PracticeClientId == agreement.PracticeClientId && otherAccount.Currency.ToUpper() == currency
            select otherMilestone.Id).AnyAsync(ct);
          if (!otherAllocation) return (true, false, false);
        }
      }

      if (hasSourceCommand && otherAllocation) return (true, false, false);
      if (hasSourceCommand || currentAllocation) return (false, true, false);
      if (legacy && otherAllocation) return (false, false, true);
      return (true, false, false);
    }

    var fullRows = await db.Receipts.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.BillingAccountId == account.Id && x.Reference == fullReference).OrderBy(x => x.Id).Take(2).ToListAsync(ct);
    if (fullRows.Count > 1) return Conflict();

    var legacyRows = await db.Receipts.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.BillingAccountId == account.Id && x.Reference == legacyReference).OrderBy(x => x.Id).Take(2).ToListAsync(ct);
    if (legacyRows.Count == 2 && await db.Receipts.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId &&
        x.BillingAccountId == account.Id && x.Reference == legacyReference &&
        x.Id != legacyRows[0].Id && x.Id != legacyRows[1].Id, ct))
      return Conflict();

    Receipt? fullMatch = null;
    if (fullRows.Count == 1)
    {
      var inspected = await InspectAsync(fullRows[0], legacy: false);
      if (inspected.Conflict || !inspected.Current) return Conflict();
      fullMatch = fullRows[0];
    }

    Receipt? legacyMatch = null;
    foreach (var candidate in legacyRows)
    {
      var inspected = await InspectAsync(candidate, legacy: true);
      if (inspected.Conflict) return Conflict();
      if (inspected.ProvenOther) continue;
      if (!inspected.Current || legacyMatch is not null) return Conflict();
      legacyMatch = candidate;
    }

    if (fullMatch is not null && legacyMatch is not null) return Conflict();
    return CommandResult<Receipt?>.Ok(fullMatch ?? legacyMatch);
  }
}
