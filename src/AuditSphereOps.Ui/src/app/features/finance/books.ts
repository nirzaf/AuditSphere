import { Component, DestroyRef, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { Api, CommandState } from '../../core/api';
import { Drafts } from '../../core/drafts';
import { arr, bool, date, dec, decimalInput, guid, nat, nullable, obj, str, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

const expense = obj({ id: guid, expenseDate: date, category: text, payee: text, description: text, amount: dec, currency: str(3),
  evidenceFileName: text, evidenceSha256: text, status: text, reviewComment: nullable(text), preparedByMe: bool });
type ExpenseAction = { expenseId: string; action: 'submit'; target: null } |
  { expenseId: string; action: 'review'; target: 'APPROVED' | 'REJECTED' };
type ExpenseActionReconciliation = 'APPLIED' | 'UNCHANGED' | 'UNVERIFIABLE' | null;
export const decodeBooks = obj({
  accounts: arr(obj({ id: guid, code: text, name: text, accountType: text })),
  expenses: arr(expense, 200), categories: arr(text, 20), canPrepare: bool, canReview: bool, maxEvidenceBytes: nat, maxReviewCommentLength: nat,
});
const tbRow = obj({ accountId: guid, code: text, name: text, accountType: text, openingDebit: dec, openingCredit: dec,
  movementDebit: dec, movementCredit: dec, closingDebit: dec, closingCredit: dec });
export const decodeTrialBalance = obj({ fromPeriod: text, toPeriod: text, rows: arr(tbRow), totalDebit: dec, totalCredit: dec, balanced: bool,
  revenue: dec, expenses: dec, profit: dec, assets: dec, liabilities: dec, equity: dec, positionReconciles: bool });

@Component({
  selector: 'audit-firm-expense-rejection-dialog',
  imports: [FormsModule, MatButtonModule, MatDialogModule],
  template: `
    <h2 mat-dialog-title>Return expense for correction</h2>
    <mat-dialog-content>
      <p>Explain what needs to be corrected. This reason will be retained with the review.</p>
      <label for="expense-rejection-reason">Rejection reason</label>
      <textarea id="expense-rejection-reason" name="reason" [(ngModel)]="reason" [attr.maxlength]="maxLength" rows="4"
        required aria-describedby="expense-rejection-help"></textarea>
      <p id="expense-rejection-help">Enter a clear, specific reason (up to {{ maxLength }} characters).</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close type="button">Cancel</button>
      <button matButton="filled" type="button" (click)="submit()" [disabled]="!reason.trim()">Reject expense</button>
    </mat-dialog-actions>
  `,
})
export class FirmExpenseRejectionDialog {
  readonly maxLength = inject<number>(MAT_DIALOG_DATA);
  readonly dialog = inject(MatDialogRef<FirmExpenseRejectionDialog, string>);
  reason = '';

  submit(): void {
    const reason = this.reason.trim();
    if (reason) this.dialog.close(reason);
  }
}

@Component({
  selector: 'audit-firm-books',
  imports: [FormsModule, MatButtonModule, MatDialogModule, ...SHARED],
  template: `
    <audit-page-header title="Firm books" eyebrow="Economics"
      description="The firm's own operating expenses with source documents, posted through the firm ledger after independent review, and the calculated firm trial balance." />
    <audit-state [loading]="books.loading()" [error]="books.error()" label="firm books" />
    @if (books.data(); as b) {
      <section class="panel" aria-labelledby="expense-heading">
        <h2 id="expense-heading">Operating expenses</h2>
        @if (b.canPrepare) {
          <form class="inline-form" (submit)="$event.preventDefault(); record()">
            <label>Date <input type="date" name="date" [(ngModel)]="draft.date" (ngModelChange)="touch()" required /></label>
            <label>Category <select name="category" [(ngModel)]="draft.category" (ngModelChange)="touch()">@for (c of b.categories; track c) { <option [value]="c">{{ c.replaceAll('_', ' ').toLowerCase() }}</option> }</select></label>
            <label>Payee <input name="payee" [(ngModel)]="draft.payee" (ngModelChange)="touch()" maxlength="200" required /></label>
            <label>Description <input name="description" [(ngModel)]="draft.description" (ngModelChange)="touch()" maxlength="500" required /></label>
            <label>Amount <input name="amount" inputmode="decimal" [(ngModel)]="draft.amount" (ngModelChange)="touch()" required /></label>
            <label>Currency <input name="currency" [(ngModel)]="draft.currency" (ngModelChange)="touch()" maxlength="3" required /></label>
            <label>Expense account <select name="expenseAccount" [(ngModel)]="draft.expenseAccount" (ngModelChange)="touch()"><option value="">Select</option>
              @for (a of b.accounts; track a.id) { @if (a.accountType === 'EXPENSE') { <option [value]="a.id">{{ a.code }} {{ a.name }}</option> } }</select></label>
            <label>Paid from <select name="paymentAccount" [(ngModel)]="draft.paymentAccount" (ngModelChange)="touch()"><option value="">Select</option>
              @for (a of b.accounts; track a.id) { @if (a.accountType === 'ASSET' || a.accountType === 'LIABILITY') { <option [value]="a.id">{{ a.code }} {{ a.name }}</option> } }</select></label>
            <label>Source document <input type="file" (change)="pick($event, b.maxEvidenceBytes)" /></label>
            <button matButton="filled" type="submit" [disabled]="cmd.busy() || cmd.uncertain()">Record expense</button>
          </form>
        }
        <div class="table-scroll"><table>
          <caption>Firm expenses</caption>
          <thead><tr><th>Date</th><th>Category</th><th>Payee</th><th class="number">Amount</th><th>Evidence</th><th>Status</th><th><span class="sr-only">Actions</span></th></tr></thead>
          <tbody>
            @for (e of b.expenses; track e.id) {
              <tr><td>{{ e.expenseDate }}</td><td>{{ e.category }}</td><td>{{ e.payee }}<small>{{ e.description }}</small></td>
                <td class="number">{{ e.amount | money }} {{ e.currency }}</td>
                <td>{{ e.evidenceFileName }} <code>{{ e.evidenceSha256.slice(0, 10) }}</code></td>
                <td><audit-status [value]="e.status" />@if (e.reviewComment) { <small>{{ e.reviewComment }}</small> }</td>
                <td class="actions">
                  @if (e.status === 'DRAFT' && b.canPrepare) { <button matButton (click)="act(e.id, 'submit', 'Submitted; its journal awaits review.')" [disabled]="cmd.busy() || cmd.uncertain()">Submit</button> }
                  @if (e.status === 'SUBMITTED' && b.canReview && !e.preparedByMe) {
                    <button matButton (click)="review(e.id, true)" [disabled]="cmd.busy() || cmd.uncertain()">Approve</button>
                    <button matButton (click)="review(e.id, false, b.maxReviewCommentLength)" [disabled]="cmd.busy() || cmd.uncertain()">Reject</button>
                  }
                  @if (e.status === 'APPROVED' && b.canPrepare) { <button matButton (click)="act(e.id, 'post', 'Posted to the firm ledger.')" [disabled]="cmd.busy() || cmd.uncertain()">Post to ledger</button> }
                </td></tr>
            } @empty { <tr><td colspan="7">No firm expenses recorded yet.</td></tr> }
          </tbody>
        </table></div>
      </section>
      <section class="panel" aria-labelledby="ftb-heading">
        <h2 id="ftb-heading">Firm trial balance</h2>
        <form class="inline-form" (submit)="$event.preventDefault(); trialBalance()">
          <label>From period <input name="from" [(ngModel)]="from" maxlength="7" placeholder="YYYY-MM" /></label>
          <label>To period <input name="to" [(ngModel)]="to" maxlength="7" placeholder="YYYY-MM" /></label>
          <button matButton="outlined" type="submit" [disabled]="tbBusy()">Calculate</button>
        </form>
        @if (tbError()) { <p role="alert" class="error-text">{{ tbError() }}</p> }
        @if (tb(); as t) {
          <div class="table-scroll"><table>
            <caption>Firm trial balance {{ t.fromPeriod }} to {{ t.toPeriod }}</caption>
            <thead><tr><th>Account</th><th class="number">Opening Dr</th><th class="number">Opening Cr</th><th class="number">Movement Dr</th><th class="number">Movement Cr</th><th class="number">Closing Dr</th><th class="number">Closing Cr</th></tr></thead>
            <tbody>@for (r of t.rows; track r.accountId) {
              <tr><th scope="row">{{ r.code }} {{ r.name }}</th><td class="number">{{ r.openingDebit | money }}</td><td class="number">{{ r.openingCredit | money }}</td>
                <td class="number">{{ r.movementDebit | money }}</td><td class="number">{{ r.movementCredit | money }}</td><td class="number">{{ r.closingDebit | money }}</td><td class="number">{{ r.closingCredit | money }}</td></tr>
            }</tbody>
            <tfoot><tr><th>Totals</th><td></td><td></td><td></td><td></td><td class="number"><strong>{{ t.totalDebit | money }}</strong></td><td class="number"><strong>{{ t.totalCredit | money }}</strong></td></tr></tfoot>
          </table></div>
          <p aria-label="Firm financial summary">{{ t.balanced ? 'Balanced.' : 'NOT balanced.' }} Revenue {{ t.revenue | money }}, expenses {{ t.expenses | money }}, profit {{ t.profit | money }}.
            Assets {{ t.assets | money }} = liabilities {{ t.liabilities | money }} + equity {{ t.equity | money }} + profit: {{ t.positionReconciles ? 'reconciles' : 'does not reconcile' }}.</p>
        }
      </section>
    }
    @if (cmd.uncertain() && pendingPostId()) {
      <section class="panel" aria-labelledby="post-recovery-heading">
        <h2 id="post-recovery-heading">Verify the saved firm-books state</h2>
        <p>The post response was lost. Refresh the exact expense before taking another action. AuditSphere will not resend the unresolved request.</p>
        @if (postVerificationMessage()) { <p role="status">{{ postVerificationMessage() }}</p> }
        @if (postReconciliation() === 'POSTED') {
          <p role="status">Persisted state confirms this expense is posted to the firm ledger.</p>
          <button matButton="outlined" (click)="clearUnresolvedPost()" [disabled]="verifyingPost()">Acknowledge verified posting</button>
        } @else if (postReconciliation() === 'APPROVED') {
          <p role="status">Persisted state confirms the expense remains approved. Review it before allowing a deliberate, idempotent post attempt.</p>
          <button matButton="outlined" (click)="clearUnresolvedPost()" [disabled]="verifyingPost()">Acknowledge and allow a new post attempt</button>
        } @else {
          <button matButton="outlined" (click)="verifyPostState()" [disabled]="verifyingPost()">{{ verifyingPost() ? 'Refreshing persisted state…' : 'Refresh persisted firm-books state' }}</button>
        }
      </section>
    }
    @if (cmd.uncertain()) {
      @if (pendingExpenseAction(); as pending) {
        <section class="panel" aria-labelledby="expense-action-recovery-heading">
          <h2 id="expense-action-recovery-heading">Verify the saved expense action</h2>
          <p>The {{ pending.action === 'submit' ? 'submission' : pending.target === 'APPROVED' ? 'approval' : 'rejection' }} response was not confirmed. Refresh the exact expense before repeating anything. AuditSphere will not retry this action automatically.</p>
          @if (expenseActionVerificationMessage()) { <p role="status">{{ expenseActionVerificationMessage() }}</p> }
          @if (expenseActionReconciliation() === 'APPLIED') {
            <p role="status">Persisted state confirms the requested expense action was saved.</p>
            <button matButton="outlined" (click)="clearUnresolvedExpenseAction()">Acknowledge verified expense action</button>
          } @else if (expenseActionReconciliation() === 'UNCHANGED') {
            <p role="status">The exact expense is still {{ unchangedExpenseStatus() }}. Review it before allowing a deliberate retry.</p>
            <button matButton="outlined" (click)="clearUnresolvedExpenseAction()">Acknowledge unchanged state and allow deliberate retry</button>
          } @else {
            <button matButton="outlined" (click)="verifyExpenseAction()" [disabled]="verifyingExpenseAction()">
              {{ verifyingExpenseAction() ? 'Refreshing persisted expense state…' : 'Refresh persisted expense state' }}
            </button>
          }
        </section>
      }
    }
    @if (!cmd.uncertain() || !pendingPostId() && !pendingExpenseAction()) {
      <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
    }
  `,
})
export class FirmBooks {
  private readonly api = inject(Api);
  private readonly drafts = inject(Drafts);
  private readonly dialog = inject(MatDialog);
  readonly books = this.api.resource(() => '/api/ui/finance/books', decodeBooks, 'Firm books require a firm-wide finance assignment.');
  readonly cmd = new CommandState(this.api);
  readonly pendingPostId = signal<string | null>(null);
  readonly pendingExpenseAction = signal<ExpenseAction | null>(null);
  readonly expenseActionReconciliation = signal<ExpenseActionReconciliation>(null);
  readonly expenseActionVerificationMessage = signal('');
  readonly unchangedExpenseStatus = signal('');
  readonly verifyingExpenseAction = signal(false);
  readonly postReconciliation = signal<'POSTED' | 'APPROVED' | 'UNVERIFIABLE' | null>(null);
  readonly postVerificationMessage = signal('');
  readonly verifyingPost = signal(false);
  readonly tb = signal<ReturnType<typeof decodeTrialBalance> | null>(null);
  readonly tbError = signal('');
  readonly tbBusy = signal(false);
  private readonly month = new Date().toISOString().slice(0, 7);
  from = this.month;
  to = this.month;
  draft = { date: new Date().toISOString().slice(0, 10), category: 'RENT', payee: '', description: '', amount: '', currency: 'QAR', expenseAccount: '', paymentAccount: '' };
  private file: File | null = null;
  private draftTimer?: ReturnType<typeof setTimeout>;

  private static validDraft(value: unknown): FirmBooks['draft'] | null {
    if (!value || typeof value !== 'object') return null;
    const v = value as Record<string, unknown>;
    const str = (k: string, max: number) => typeof v[k] === 'string' && (v[k] as string).length <= max ? v[k] as string : null;
    return str('date', 10) && str('category', 40) && str('payee', 200) && str('description', 500) &&
      str('amount', 30) && str('currency', 3) && str('expenseAccount', 64) && str('paymentAccount', 64)
      ? { date: str('date', 10)!, category: str('category', 40)!, payee: str('payee', 200)!, description: str('description', 500)!,
          amount: str('amount', 30)!, currency: str('currency', 3)!, expenseAccount: str('expenseAccount', 64)!, paymentAccount: str('paymentAccount', 64)! }
      : null;
  }
  constructor() {
    const saved = this.drafts.load('firm-books-expense', FirmBooks.validDraft);
    if (saved) this.draft = saved;
    inject(DestroyRef).onDestroy(() => clearTimeout(this.draftTimer));
  }
  touch(): void {
    clearTimeout(this.draftTimer);
    this.draftTimer = setTimeout(() => this.drafts.save('firm-books-expense', this.draft), 800);
  }

  pick(event: Event, max: number): void {
    const f = (event.target as HTMLInputElement).files?.[0] ?? null;
    this.file = f && f.size > 0 && f.size <= max ? f : null;
    if (f && !this.file) { this.cmd.failed.set(true); this.cmd.message.set('The source document must be between 1 byte and 5 MB.'); }
  }
  record(): void {
    const amount = decimalInput(this.draft.amount, 2);
    if (!amount || !this.file || !this.draft.expenseAccount || !this.draft.paymentAccount) {
      this.cmd.failed.set(true);
      this.cmd.message.set('Enter a valid amount (up to 2 decimals), both accounts and a source document.');
      return;
    }
    const form = new FormData();
    form.set('expenseDate', this.draft.date); form.set('category', this.draft.category); form.set('payee', this.draft.payee);
    form.set('description', this.draft.description); form.set('amount', amount); form.set('currency', this.draft.currency.toUpperCase());
    form.set('expenseAccountId', this.draft.expenseAccount); form.set('paymentAccountId', this.draft.paymentAccount); form.set('evidence', this.file);
    this.cmd.run('/api/ui/finance/books/expenses', form, 'Expense recorded as a draft.', () => {
      this.draft = { ...this.draft, payee: '', description: '', amount: '' };
      this.drafts.save('firm-books-expense', this.draft);
      this.file = null;
    }).finally(() => this.books.reload());
  }
  act(id: string, action: 'submit' | 'post', success: string): void {
    if (action === 'post') {
      this.pendingPostId.set(id);
      this.postReconciliation.set(null);
      this.postVerificationMessage.set('');
    } else {
      this.pendingExpenseAction.set({ expenseId: id, action, target: null });
      this.expenseActionReconciliation.set(null);
      this.expenseActionVerificationMessage.set('');
    }
    void this.cmd.run(`/api/ui/finance/books/expenses/${id}/${action}`, {}, success).then(succeeded => {
      if (action === 'post' && (succeeded || !this.cmd.uncertain())) this.pendingPostId.set(null);
      if (action === 'submit' && (succeeded || !this.cmd.uncertain())) this.pendingExpenseAction.set(null);
    }).finally(() => this.books.reload());
  }
  review(id: string, approve: boolean, maxReviewCommentLength = 1000): void {
    if (approve) {
      this.submitReview(id, true, 'Agreed to the source document.');
      return;
    }

    this.dialog.open(FirmExpenseRejectionDialog, {
      width: 'min(560px, calc(100vw - 32px))',
      ariaLabel: 'Return expense for correction',
      data: maxReviewCommentLength,
    }).afterClosed().subscribe((reason: string | undefined) => {
      if (reason) this.submitReview(id, false, reason);
    });
  }

  private submitReview(id: string, approve: boolean, comment: string): void {
    this.pendingExpenseAction.set({ expenseId: id, action: 'review', target: approve ? 'APPROVED' : 'REJECTED' });
    this.expenseActionReconciliation.set(null);
    this.expenseActionVerificationMessage.set('');
    this.cmd.run(`/api/ui/finance/books/expenses/${id}/review`, { approve, comment },
      approve ? 'Approved.' : 'Rejected with the recorded reason.').then(succeeded => {
        if (succeeded || !this.cmd.uncertain()) this.pendingExpenseAction.set(null);
      }).finally(() => this.books.reload());
  }

  async verifyExpenseAction(): Promise<void> {
    const pending = this.pendingExpenseAction();
    if (!pending || !this.cmd.uncertain() || this.verifyingExpenseAction()) return;
    this.verifyingExpenseAction.set(true);
    this.expenseActionReconciliation.set(null);
    this.expenseActionVerificationMessage.set('');
    this.unchangedExpenseStatus.set('');
    try {
      const persisted = await this.api.get('/api/ui/finance/books', decodeBooks);
      const target = persisted.expenses.find(expense => expense.id === pending.expenseId);
      if (!target) {
        this.expenseActionReconciliation.set('UNVERIFIABLE');
        this.expenseActionVerificationMessage.set('The exact expense was not present in the refreshed list. Keep this action unresolved and refresh again.');
      } else {
        const applied = pending.action === 'submit'
          ? ['SUBMITTED', 'APPROVED', 'REJECTED', 'POSTED'].includes(target.status)
          : target.status === pending.target || pending.target === 'APPROVED' && target.status === 'POSTED';
        const unchanged = pending.action === 'submit' ? target.status === 'DRAFT' : target.status === 'SUBMITTED';
        if (applied) {
          this.expenseActionReconciliation.set('APPLIED');
          this.expenseActionVerificationMessage.set('The exact expense was found in refreshed persisted firm-books state.');
        } else if (unchanged) {
          this.expenseActionReconciliation.set('UNCHANGED');
          this.unchangedExpenseStatus.set(target.status);
          this.expenseActionVerificationMessage.set('The exact expense was found in refreshed persisted firm-books state.');
        } else {
          this.expenseActionReconciliation.set('UNVERIFIABLE');
          this.expenseActionVerificationMessage.set('The expense changed to a different state while the request was unresolved. Keep the action unresolved and review the latest state.');
        }
      }
      this.books.reload();
    } catch {
      this.expenseActionReconciliation.set('UNVERIFIABLE');
      this.expenseActionVerificationMessage.set('Persisted firm-books state could not be verified. Keep this action unresolved and retry the refresh.');
    } finally {
      this.verifyingExpenseAction.set(false);
    }
  }

  clearUnresolvedExpenseAction(): void {
    if (this.verifyingExpenseAction() || !['APPLIED', 'UNCHANGED'].includes(this.expenseActionReconciliation() ?? '')) return;
    const applied = this.expenseActionReconciliation() === 'APPLIED';
    this.cmd.uncertain.set(false);
    this.cmd.failed.set(false);
    this.cmd.message.set(applied
      ? 'Persisted state confirms the saved expense action. The lost request was not repeated.'
      : 'Persisted state confirms the expense remains unchanged. A deliberate retry is available after review.');
    this.pendingExpenseAction.set(null);
    this.expenseActionReconciliation.set(null);
    this.expenseActionVerificationMessage.set('');
    this.unchangedExpenseStatus.set('');
  }

  async verifyPostState(): Promise<void> {
    const id = this.pendingPostId();
    if (!id || this.verifyingPost()) return;
    this.verifyingPost.set(true);
    this.postVerificationMessage.set('');
    try {
      const persisted = await this.api.get('/api/ui/finance/books', decodeBooks);
      const target = persisted.expenses.find(expense => expense.id === id);
      const status = target?.status === 'POSTED' ? 'POSTED' : target?.status === 'APPROVED' ? 'APPROVED' : 'UNVERIFIABLE';
      this.postReconciliation.set(status);
      this.postVerificationMessage.set(status === 'UNVERIFIABLE'
        ? 'The exact expense could not be confirmed in an approved or posted state. Keep this action unresolved and refresh again.'
        : 'The exact expense was found in refreshed persisted firm-books state.');
      this.books.reload();
    } catch {
      this.postReconciliation.set(null);
      this.postVerificationMessage.set('Persisted firm-books state could not be verified. Keep this action unresolved and retry the refresh.');
    } finally {
      this.verifyingPost.set(false);
    }
  }

  clearUnresolvedPost(): void {
    const status = this.postReconciliation();
    if (this.verifyingPost() || status !== 'POSTED' && status !== 'APPROVED') return;
    this.cmd.uncertain.set(false);
    this.cmd.failed.set(false);
    this.cmd.message.set(status === 'POSTED'
      ? 'Persisted state confirms this expense is posted. The lost request was not repeated.'
      : 'Persisted state confirms this expense remains approved. A new post attempt is available after review.');
    this.pendingPostId.set(null);
    this.postReconciliation.set(null);
    this.postVerificationMessage.set('');
  }

  async trialBalance(): Promise<void> {
    this.tbBusy.set(true); this.tbError.set(''); this.tb.set(null);
    try {
      this.tb.set(await this.api.get(`/api/ui/finance/books/trial-balance?from=${encodeURIComponent(this.from)}&to=${encodeURIComponent(this.to)}`, decodeTrialBalance));
    } catch (e) {
      this.tbError.set(e instanceof Error && e.message ? e.message : 'Trial balance unavailable.');
    } finally { this.tbBusy.set(false); }
  }
}
