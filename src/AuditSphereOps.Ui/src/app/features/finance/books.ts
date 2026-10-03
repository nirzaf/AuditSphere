import { Component, DestroyRef, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { Api, CommandState } from '../../core/api';
import { Drafts } from '../../core/drafts';
import { arr, bool, date, dec, decimalInput, guid, nat, nullable, obj, str, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

const expense = obj({ id: guid, expenseDate: date, category: text, payee: text, description: text, amount: dec, currency: str(3),
  evidenceFileName: text, evidenceSha256: text, status: text, reviewComment: nullable(text), preparedByMe: bool });
export const decodeBooks = obj({
  accounts: arr(obj({ id: guid, code: text, name: text, accountType: text })),
  expenses: arr(expense, 200), categories: arr(text, 20), canPrepare: bool, canReview: bool, maxEvidenceBytes: nat,
});
const tbRow = obj({ accountId: guid, code: text, name: text, accountType: text, openingDebit: dec, openingCredit: dec,
  movementDebit: dec, movementCredit: dec, closingDebit: dec, closingCredit: dec });
export const decodeTrialBalance = obj({ fromPeriod: text, toPeriod: text, rows: arr(tbRow), totalDebit: dec, totalCredit: dec, balanced: bool,
  revenue: dec, expenses: dec, profit: dec, assets: dec, liabilities: dec, equity: dec, positionReconciles: bool });

@Component({
  selector: 'audit-firm-books',
  imports: [FormsModule, MatButtonModule, ...SHARED],
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
            <button matButton="filled" type="submit" [disabled]="cmd.busy()">Record expense</button>
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
                  @if (e.status === 'DRAFT' && b.canPrepare) { <button matButton (click)="act(e.id, 'submit', 'Submitted; its journal awaits review.')" [disabled]="cmd.busy()">Submit</button> }
                  @if (e.status === 'SUBMITTED' && b.canReview && !e.preparedByMe) {
                    <button matButton (click)="review(e.id, true)" [disabled]="cmd.busy()">Approve</button>
                    <button matButton (click)="review(e.id, false)" [disabled]="cmd.busy()">Reject</button>
                  }
                  @if (e.status === 'APPROVED' && b.canPrepare) { <button matButton (click)="act(e.id, 'post', 'Posted to the firm ledger.')" [disabled]="cmd.busy()">Post to ledger</button> }
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
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
})
export class FirmBooks {
  private readonly api = inject(Api);
  private readonly drafts = inject(Drafts);
  readonly books = this.api.resource(() => '/api/ui/finance/books', decodeBooks, 'Firm books require a firm-wide finance assignment.');
  readonly cmd = new CommandState(this.api);
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
    this.cmd.run(`/api/ui/finance/books/expenses/${id}/${action}`, {}, success).finally(() => this.books.reload());
  }
  review(id: string, approve: boolean): void {
    this.cmd.run(`/api/ui/finance/books/expenses/${id}/review`, { approve, comment: approve ? 'Agreed to the source document.' : 'Returned for correction.' },
      approve ? 'Approved.' : 'Rejected.').finally(() => this.books.reload());
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
