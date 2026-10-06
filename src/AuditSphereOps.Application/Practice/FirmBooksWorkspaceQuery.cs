using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record FirmBooksAccount(Guid Id, string Code, string Name, string AccountType);
public sealed record FirmBooksExpense(Guid Id, DateOnly ExpenseDate, string Category, string Payee, string Description, decimal Amount, string Currency,
  string EvidenceFileName, string EvidenceSha256, string Status, string? ReviewComment, bool PreparedByMe);
public sealed record FirmBooksWorkspace(IReadOnlyList<FirmBooksAccount> Accounts, IReadOnlyList<FirmBooksExpense> Expenses, IReadOnlyList<string> Categories,
  bool CanPrepare, bool CanReview, int MaxEvidenceBytes, int MaxReviewCommentLength);

/// <summary>Firm-wide finance projection for the firm-books workbench. Evidence bytes never leave the server.</summary>
public static class FirmBooksWorkspaceQuery
{
  public static async Task<CommandResult<FirmBooksWorkspace>> GetAsync(IClientAccountingDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    var prepare = await AuthorizeAsync(db, actor, ["FinanceManager"], ct);
    var review = await AuthorizeAsync(db, actor, ["FinanceReviewer"], ct);
    if (!prepare && !review) return CommandResult<FirmBooksWorkspace>.Fail(ErrorCodes.ScopeDenied, "Firm books require a firm-wide finance assignment.");
    var accounts = await db.FirmAccounts.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.PostingAllowed).OrderBy(x => x.Code)
      .Select(x => new FirmBooksAccount(x.Id, x.Code, x.Name, x.AccountType)).ToListAsync(ct);
    var expenses = (await FirmExpenseService.ListAsync(db, actor, ct)).Select(x => new FirmBooksExpense(x.Id, x.ExpenseDate, x.Category, x.Payee, x.Description,
      x.Amount, x.Currency, x.EvidenceFileName, x.EvidenceSha256, x.Status, x.ReviewComment, x.PreparedByUserId == actor.UserId)).ToList();
    return CommandResult<FirmBooksWorkspace>.Ok(new(accounts, expenses, FirmExpenseCategories.All, prepare, review,
      FirmExpenseService.MaxEvidenceBytes, FirmExpenseService.MaxReviewCommentLength));
  }

  private static async Task<bool> AuthorizeAsync(IClientAccountingDbContext db, ActorContext actor, string[] roles, CancellationToken ct) =>
    (await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, RequiredRoles: roles, InternalOnly: true, RequireFirmWide: true), ct)).Succeeded;
}
