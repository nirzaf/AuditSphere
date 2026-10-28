using System.Data.Common;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class FinanceCommandAuthorityTests
{
  public static IEnumerable<object[]> Commands =>
    from command in new[] { "invoice-submit", "invoice-approve", "invoice-post", "invoice-send",
      "journal-submit", "journal-approve", "journal-post", "period-close", "period-reopen",
      "rate-revise", "rate-approve" }
    from revoke in new[] { false, true }
    select new object[] { command, revoke };

  [Theory]
  [MemberData(nameof(Commands))]
  public async Task CommandWaitingForFirmFence_UsesCommittedAuthority(string command, bool revoke)
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    var ct = deadline.Token;
    var role = command is "invoice-approve" or "journal-approve" or "period-close"
      ? "FinanceReviewer" : command.StartsWith("rate-", StringComparison.Ordinal) ? "Manager" : "FinanceManager";
    var candidate = TenantAdministrationFixture.User(f.FirmId, f.TenantId, "finance@example.test", "Finance actor");
    var other = TenantAdministrationFixture.User(f.FirmId, f.TenantId, "other@example.test", "Other finance actor");
    var maker = TenantAdministrationFixture.User(f.FirmId, f.TenantId, "maker@example.test", "Finance maker");
    var checker = TenantAdministrationFixture.User(f.FirmId, f.TenantId, "checker@example.test", "Finance checker");
    var grant = Grant(candidate, role);
    var otherGrant = Grant(other, role);
    await using var setup = f.Db();
    setup.Users.AddRange(candidate, other, maker, checker);
    setup.RoleGrants.AddRange(grant, otherGrant, Grant(maker, "FinanceManager"), Grant(maker, "Manager"),
      Grant(checker, "FinanceReviewer"), Grant(checker, "Partner"));
    setup.FirmFinanceProfiles.Add(new FirmFinanceProfile
    {
      Id = Guid.CreateVersion7(), FirmId = f.FirmId, FunctionalCurrency = "QAR",
      ProfileKind = BillingStates.TestProfile, Approved = true, ApprovedByUserId = checker.Id,
      ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
    });
    await setup.SaveChangesAsync(ct);
    var actor = Actor(candidate, role);
    var prepare = Actor(maker, "FinanceManager", "Manager");
    var review = Actor(checker, "FinanceReviewer", "Partner");
    var target = await PrepareAsync(setup, prepare, review, f.ClientId, command, ct);
    var before = await BusinessSnapshotAsync(setup, ct);

    // The real revoker owns the fence, but has not changed access yet. The finance
    // command must finish its initial authorization and reach that same fence.
    var revocationFence = new FirmFenceBarrier(pauseAfterAcquisition: true);
    var commandFence = new FirmFenceBarrier(pauseAfterAcquisition: false);
    await using var revoker = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(f.Pg.Options)
      .AddInterceptors(revocationFence).Options);
    await using var writer = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(f.Pg.Options)
      .AddInterceptors(commandFence).Options);
    var revokedGrant = revoke ? grant : otherGrant;
    var revocation = RoleAdministrationService.RevokeRoleGrantAsync(revoker, f.AdminActor, new(revokedGrant.Id), ct);
    Task<CommandResult>? pending = null;
    try
    {
      await revocationFence.Acquired.Task.WaitAsync(ct);
      pending = ExecuteAsync(writer, actor, command, target, ct);
      await commandFence.Reached.Task.WaitAsync(ct);
      Assert.False(pending.IsCompleted);
    }
    finally
    {
      revocationFence.Release.TrySetResult(true);
    }
    var revoked = await revocation.WaitAsync(ct);
    Assert.True(revoked.Succeeded, revoked.Message);
    var result = await pending!.WaitAsync(ct);
    await using var proof = f.Db();
    Assert.NotNull(await proof.RoleGrants.Where(x => x.Id == revokedGrant.Id).Select(x => x.RevokedAt).SingleAsync(ct));
    Assert.Equal(revokedGrant.UserId == candidate.Id ? actor.SessionEpoch + 1 : actor.SessionEpoch,
      await proof.Users.Where(x => x.Id == candidate.Id).Select(x => x.SessionEpoch).SingleAsync(ct));
    if (revoke)
    {
      Assert.False(result.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, result.ErrorCode);
      Assert.Equal(before, await BusinessSnapshotAsync(proof, ct));
      // Refreshing the epoch does not restore the revoked role.
      var currentEpoch = actor with { SessionEpoch = actor.SessionEpoch + 1 };
      var denied = await ExecuteAsync(proof, currentEpoch, command, target, ct);
      Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
      Assert.Equal(before, await BusinessSnapshotAsync(proof, ct));
    }
    else
    {
      Assert.True(result.Succeeded, result.Message);
      await AssertOutcomeAsync(proof, command, target, ct);
      if (command != "rate-revise")
      {
        var after = await BusinessSnapshotAsync(proof, ct);
        Assert.True((await ExecuteAsync(proof, actor, command, target, ct)).Succeeded);
        Assert.Equal(after, await BusinessSnapshotAsync(proof, ct));
      }
    }
  }

  private static async Task<Guid> PrepareAsync(AuditSphereDbContext db, ActorContext maker, ActorContext checker,
    Guid clientId, string command, CancellationToken ct)
  {
    if (command.StartsWith("invoice-", StringComparison.Ordinal))
    {
      var account = await BillingService.CreateBillingAccountAsync(db, maker, new(clientId, "QAR"), ct);
      Assert.True(account.Succeeded, account.Message);
      var draft = await BillingService.CreateInvoiceDraftAsync(db, maker, new(account.Value, "FENCE-INV", [new("Service", 1m, 125.12m)]), ct);
      Assert.True(draft.Succeeded, draft.Message);
      if (command != "invoice-submit") Assert.True((await BillingService.SubmitInvoiceAsync(db, maker, draft.Value, ct)).Succeeded);
      if (command is "invoice-post" or "invoice-send") Assert.True((await BillingService.ApproveInvoiceAsync(db, checker, draft.Value, ct)).Succeeded);
      if (command == "invoice-send") Assert.True((await BillingService.PostInvoiceAsync(db, maker, draft.Value, ct)).Succeeded);
      return draft.Value;
    }
    if (command.StartsWith("rate-", StringComparison.Ordinal))
    {
      var draft = await PracticeTimeService.ReviseRateCardAsync(db, maker, new("Staff", "Fence", "QAR", 125.12m), ct);
      Assert.True(draft.Succeeded, draft.Message);
      return draft.Value;
    }
    var period = await LedgerService.CreateFirmPeriodAsync(db, maker, new("2026-10"), ct);
    Assert.True(period.Succeeded, period.Message);
    if (command.StartsWith("period-", StringComparison.Ordinal))
    {
      if (command == "period-reopen") Assert.True((await LedgerService.CloseFiscalPeriodAsync(db, checker, period.Value, "Reviewed close", ct)).Succeeded);
      return period.Value;
    }
    var cash = await LedgerService.CreateFirmAccountAsync(db, maker, new("CASH", "Cash", LedgerStates.AccountAsset, LedgerStates.Debit), ct);
    var equity = await LedgerService.CreateFirmAccountAsync(db, maker, new("EQUITY", "Equity", LedgerStates.AccountEquity, LedgerStates.Credit), ct);
    Assert.True(cash.Succeeded, cash.Message);
    Assert.True(equity.Succeeded, equity.Message);
    var journal = await LedgerService.CreateFirmJournalDraftAsync(db, maker,
      new(period.Value, "FENCE-JOURNAL", "MANUAL", "FENCE-SOURCE", 1, "OPENING_BALANCE", "QAR",
        [new(cash.Value, "Cash", 125.12m, 0), new(equity.Value, "Equity", 0, 125.12m)],
        "support.txt", "text/plain", "Synthetic authority fixture"u8.ToArray()), ct);
    Assert.True(journal.Succeeded, journal.Message);
    if (command != "journal-submit") Assert.True((await LedgerService.SubmitFirmJournalAsync(db, maker, journal.Value, ct)).Succeeded);
    if (command == "journal-post") Assert.True((await LedgerService.ApproveFirmJournalAsync(db, checker, journal.Value, ct)).Succeeded);
    return journal.Value;
  }

  private static async Task<CommandResult> ExecuteAsync(AuditSphereDbContext db, ActorContext actor, string command, Guid target, CancellationToken ct)
  {
    if (command == "journal-post")
    {
      var result = await LedgerService.PostFirmJournalAsync(db, actor, target, ct);
      return new(result.Succeeded, result.ErrorCode, result.Message);
    }
    if (command == "rate-revise")
    {
      var result = await PracticeTimeService.ReviseRateCardAsync(db, actor, new("Staff", "Fence", "QAR", 150m, ExpectedVersion: 1), ct);
      return new(result.Succeeded, result.ErrorCode, result.Message);
    }
    return command switch
    {
      "invoice-submit" => await BillingService.SubmitInvoiceAsync(db, actor, target, ct),
      "invoice-approve" => await BillingService.ApproveInvoiceAsync(db, actor, target, ct),
      "invoice-post" => await BillingService.PostInvoiceAsync(db, actor, target, ct),
      "invoice-send" => await BillingService.SendInvoiceAsync(db, actor, target, ct),
      "journal-submit" => await LedgerService.SubmitFirmJournalAsync(db, actor, target, ct),
      "journal-approve" => await LedgerService.ApproveFirmJournalAsync(db, actor, target, ct),
      "period-close" => await LedgerService.CloseFiscalPeriodAsync(db, actor, target, "Reviewed close", ct),
      "period-reopen" => await LedgerService.RequestPeriodReopenAsync(db, actor, target, "Reviewed reopen", ct),
      "rate-approve" => await PracticeTimeService.ApproveRateCardAsync(db, actor, target, ct),
      _ => throw new ArgumentOutOfRangeException(nameof(command))
    };
  }

  private static async Task AssertOutcomeAsync(AuditSphereDbContext db, string command, Guid target, CancellationToken ct)
  {
    var expected = command switch
    {
      "invoice-submit" => BillingStates.InvoiceReviewRequired, "invoice-approve" => BillingStates.InvoiceApproved,
      "invoice-post" => BillingStates.InvoicePosted, "invoice-send" => BillingStates.InvoiceSent,
      "journal-submit" => LedgerStates.JournalReviewRequired, "journal-approve" => LedgerStates.JournalApproved,
      "journal-post" => LedgerStates.JournalPosted, "period-close" => LedgerStates.PeriodClosed,
      "period-reopen" => LedgerStates.PeriodOpen, "rate-approve" => PracticeTimeStates.RateApproved,
      _ => PracticeTimeStates.RateDraft
    };
    var actual = command.StartsWith("invoice-", StringComparison.Ordinal)
      ? await db.Invoices.Where(x => x.Id == target).Select(x => x.Status).SingleAsync(ct)
      : command.StartsWith("journal-", StringComparison.Ordinal)
        ? await db.FirmJournals.Where(x => x.Id == target).Select(x => x.Status).SingleAsync(ct)
        : command.StartsWith("period-", StringComparison.Ordinal)
          ? await db.FirmPeriods.Where(x => x.Id == target).Select(x => x.Status).SingleAsync(ct)
          : await db.RateCardVersions.OrderByDescending(x => x.Version).Select(x => x.Status).FirstAsync(ct);
    Assert.Equal(expected, actual);
    if (command == "rate-revise") Assert.Equal(2, await db.RateCardVersions.MaxAsync(x => x.Version, ct));
    if (command == "journal-post")
    {
      var posting = Assert.Single(await db.FirmPostings.ToListAsync(ct));
      Assert.Equal(target, posting.JournalId);
      var lines = await db.FirmPostingLines.Where(x => x.PostingId == posting.Id).ToListAsync(ct);
      Assert.Equal(125.12m, lines.Sum(x => x.Debit));
      Assert.Equal(125.12m, lines.Sum(x => x.Credit));
      Assert.Single(await db.LedgerSourceLinks.ToListAsync(ct));
      Assert.Single(await db.LedgerPostingReceipts.ToListAsync(ct));
    }
  }

  private static async Task<string> BusinessSnapshotAsync(AuditSphereDbContext db, CancellationToken ct) => JsonSerializer.Serialize(new
  {
    Invoices = await db.Invoices.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct),
    Journals = await db.FirmJournals.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct),
    Periods = await db.FirmPeriods.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct),
    Rates = await db.RateCardVersions.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct),
    Postings = await db.FirmPostings.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct),
    Lines = await db.FirmPostingLines.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct),
    Sources = await db.LedgerSourceLinks.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct),
    Receipts = await db.LedgerPostingReceipts.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct),
    BillingReceipts = await db.BillingCommandReceipts.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct),
    Decisions = await db.PeriodCloseDecisions.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct)
  });

  private static ActorContext Actor(AppUser user, params string[] roles) => new(user.Id, user.FirmId, user.SessionEpoch, roles);
  private static RoleGrant Grant(AppUser user, string role) => new()
  {
    Id = Guid.CreateVersion7(), FirmId = user.FirmId, UserId = user.Id, Role = role,
    GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
  };

  private sealed class FirmFenceBarrier(bool pauseAfterAcquisition) : DbCommandInterceptor
  {
    public TaskCompletionSource<bool> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Acquired { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static bool IsFence(DbCommand command) => command.CommandText.Contains("firm_safety_states", StringComparison.Ordinal)
      && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
      CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
      if (IsFence(command)) Reached.TrySetResult(true);
      return ValueTask.FromResult(result);
    }

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
      CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
      if (IsFence(command))
      {
        Acquired.TrySetResult(true);
        if (pauseAfterAcquisition) await Release.Task.WaitAsync(cancellationToken);
      }
      return result;
    }
  }
}
