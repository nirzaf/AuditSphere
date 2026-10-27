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
  bool CanPrepare, bool CanReview, int MaxEvidenceBytes, int MaxReviewCommentLength, int ExpensePage = 1, bool HasMoreExpenses = false,
  string AccessMode = "OPERATIONAL", string? AccessReason = null);

/// <summary>Firm-wide finance projection for the firm-books workbench. Evidence bytes never leave the server.</summary>
public static class FirmBooksWorkspaceQuery
{
  public static async Task<CommandResult<FirmBooksWorkspace>> GetAsync(IClientAccountingDbContext db, ActorContext actor, CancellationToken ct = default, int expensePage = 1, Guid? expenseId = null)
  {
    if (expensePage is < 1 or > 100000 || expenseId == Guid.Empty || (expenseId.HasValue && expensePage != 1))
      return CommandResult<FirmBooksWorkspace>.Fail("finance.page-invalid", "Choose a valid expense page or exact expense.");
    var prepare = await AuthorizeAsync(db, actor, ["FinanceManager"], ct);
    var review = await AuthorizeAsync(db, actor, ["FinanceReviewer"], ct);
    if (!prepare && !review && !(await AdministratorOversightReadAuthorization.AuthorizeAsync(db, actor, ct)).Succeeded)
      return CommandResult<FirmBooksWorkspace>.Fail(ErrorCodes.ScopeDenied, "Firm books require a firm-wide finance assignment.");
    var accounts = await db.FirmAccounts.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.PostingAllowed).OrderBy(x => x.Code)
      .Select(x => new FirmBooksAccount(x.Id, x.Code, x.Name, x.AccountType)).ToListAsync(ct);
    var source = await FirmExpenseService.ListAsync(db, actor, ct, expensePage, expenseId, includeContinuation: true);
    var expenses = source.Take(200).Select(x => new FirmBooksExpense(x.Id, x.ExpenseDate, x.Category, x.Payee, x.Description,
      x.Amount, x.Currency, x.EvidenceFileName, x.EvidenceSha256, x.Status, x.ReviewComment, x.PreparedByUserId == actor.UserId)).ToList();
    prepare = await AuthorizeAsync(db, actor, ["FinanceManager"], ct);
    review = await AuthorizeAsync(db, actor, ["FinanceReviewer"], ct);
    if (!prepare && !review && !(await AdministratorOversightReadAuthorization.AuthorizeAsync(db, actor, ct)).Succeeded)
      return CommandResult<FirmBooksWorkspace>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    return CommandResult<FirmBooksWorkspace>.Ok(new(accounts, expenses, FirmExpenseCategories.All, prepare, review,
      FirmExpenseService.MaxEvidenceBytes, FirmExpenseService.MaxReviewCommentLength, expensePage, source.Count > 200,
      prepare || review ? "OPERATIONAL" : AdministratorOversightReadAuthorization.ReadOnly,
      prepare || review ? null : AdministratorOversightReadAuthorization.Reason));
  }

  private static async Task<bool> AuthorizeAsync(IClientAccountingDbContext db, ActorContext actor, string[] roles, CancellationToken ct) =>
    (await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, RequiredRoles: roles, InternalOnly: true, RequireFirmWide: true), ct)).Succeeded;
}
