import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import { guidPattern } from '../../core/contracts';

interface PeriodOption { id: string; code: string; start: string; end: string; currency: string; status: string }
interface JournalLine { lineNumber: number; accountId: string; accountCode: string; accountName: string; description: string; debit: string; credit: string }
interface Journal { id: string; clientId: string; periodId: string; journalNumber: string; description: string; postingDate: string; currency: string; status: string; revision: string; createdByUserId: string; lines: JournalLine[] }
const amountPattern = /^(?:0|[1-9]\d{0,14})(?:\.\d{1,6})?$/;
function object(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('Invalid journal response');
  return value as Record<string, unknown>;
}
export function decodeOperationalJournal(value: unknown, clientId: string): Journal {
  const v = object(value);
  if (v['clientId'] !== clientId || typeof v['id'] !== 'string' || !guidPattern.test(v['id']) ||
      typeof v['periodId'] !== 'string' || !guidPattern.test(v['periodId']) ||
      !['journalNumber', 'description', 'postingDate', 'currency', 'status', 'revision'].every(k => typeof v[k] === 'string') ||
      !/^[1-9]\d{0,18}$/.test(String(v['revision'])) || typeof v['createdByUserId'] !== 'string' || !guidPattern.test(v['createdByUserId']) ||
      !Array.isArray(v['lines']) || v['lines'].length < 2 || v['lines'].length > 100) throw new Error('Invalid journal response');
  for (const raw of v['lines']) {
    const line = object(raw);
    if (!Number.isSafeInteger(line['lineNumber']) || Number(line['lineNumber']) < 1 ||
        typeof line['accountId'] !== 'string' || !guidPattern.test(line['accountId']) ||
        !['accountCode', 'accountName', 'description', 'debit', 'credit'].every(k => typeof line[k] === 'string') ||
        !amountPattern.test(String(line['debit'])) || !amountPattern.test(String(line['credit']))) throw new Error('Invalid journal line');
  }
  return v as unknown as Journal;
}
function minor(value: string): bigint { const [whole, fraction = ''] = value.split('.'); return BigInt(whole) * 1_000_000n + BigInt(fraction.padEnd(6, '0')); }

@Component({
  selector: 'audit-client-operational-journals',
  imports: [FormsModule, MatButtonModule],
  template: `
    <section aria-labelledby="operational-journals-heading">
      <h3 id="operational-journals-heading">Client bookkeeping journals</h3>
      <p>Native client book · {{ bookCurrency() }}. These official-book journals are separate from imported GL and reporting adjustments.</p>
      @if (error()) { <p role="alert">{{ error() }}</p> }
      @if (journal(); as j) {
        <article aria-label="Selected client journal">
          <h4>{{ j.journalNumber }} · {{ j.status }}</h4>
          <p>{{ j.description }} · {{ j.postingDate }} · {{ j.currency }} · revision {{ j.revision }}</p>
          <div class="table-scroll"><table><caption>Immutable journal lines</caption><thead><tr><th>Account</th><th>Description</th><th>Debit</th><th>Credit</th></tr></thead>
            <tbody>@for (line of j.lines; track line.lineNumber) { <tr><td>{{ line.accountCode }} · {{ line.accountName }}</td><td>{{ line.description }}</td><td>{{ line.debit }}</td><td>{{ line.credit }}</td></tr> }</tbody></table></div>
          @if (j.status === 'DRAFT' || j.status === 'RETURNED') {
            <label><input type="checkbox" [(ngModel)]="reviewed" /> I reviewed this client, journal, posting date, account selection and exact amounts.</label>
            <button matButton [disabled]="busy() || !reviewed || uncertain()" (click)="submit(j)">Submit for independent review</button>
          }
          @if (j.status === 'SUBMITTED') {
            <label><input type="checkbox" [(ngModel)]="reviewed" /> I independently reviewed this exact journal revision and its balanced lines.</label>
            <label>Approval reason <input [(ngModel)]="reason" maxlength="2000" /></label>
            <button matButton [disabled]="busy() || !reviewed || !reason.trim() || j.createdByUserId === userId() || uncertain()" (click)="approve(j)">Approve and post</button>
          }
        </article>
      }
      <form #createForm="ngForm" (ngSubmit)="createForm.valid && create()">
        <h4>Create manual journal draft</h4>
        <label>Reporting period <select name="period" [(ngModel)]="periodId" (ngModelChange)="reviewed = false" required>
          <option value="">Choose period</option>@for (p of periods(); track p.id) { <option [value]="p.id" [disabled]="p.status === 'CLOSED'">{{ p.code }} · {{ p.currency }} · {{ p.status }}</option> }
        </select></label>
        <label>Journal number <input name="number" [(ngModel)]="number" (ngModelChange)="reviewed = false" required maxlength="100" /></label>
        <label>Description <input name="description" [(ngModel)]="description" (ngModelChange)="reviewed = false" required maxlength="1000" /></label>
        <label>Accounting date <input name="date" type="date" [(ngModel)]="postingDate" (ngModelChange)="reviewed = false" required /></label>
        <div class="table-scroll"><table><caption>Balanced journal draft lines</caption><thead><tr><th>Account code</th><th>Description</th><th>Debit</th><th>Credit</th><th></th></tr></thead>
          <tbody>@for (line of lines(); track $index) { <tr>
            <td><input aria-label="Account code" [(ngModel)]="line.accountCode" [name]="'account' + $index" (ngModelChange)="reviewed = false" required maxlength="100" /></td>
            <td><input aria-label="Line description" [(ngModel)]="line.description" [name]="'lineDescription' + $index" (ngModelChange)="reviewed = false" maxlength="1000" /></td>
            <td><input aria-label="Debit" [(ngModel)]="line.debit" [name]="'debit' + $index" (ngModelChange)="reviewed = false" inputmode="decimal" pattern="(?:0|[1-9][0-9]{0,14})(?:\\.[0-9]{1,6})?" required /></td>
            <td><input aria-label="Credit" [(ngModel)]="line.credit" [name]="'credit' + $index" (ngModelChange)="reviewed = false" inputmode="decimal" pattern="(?:0|[1-9][0-9]{0,14})(?:\\.[0-9]{1,6})?" required /></td>
            <td><button matButton type="button" [disabled]="lines().length <= 2" (click)="removeLine($index)">Remove</button></td>
          </tr> }</tbody></table></div>
        <p>Debits {{ totalDebit() }} · Credits {{ totalCredit() }} · {{ balanced() ? 'Balanced' : 'Out of balance' }}</p>
        <button matButton type="button" [disabled]="lines().length >= 100" (click)="addLine()">Add line</button>
        <label><input type="checkbox" [(ngModel)]="reviewed" name="createReview" /> I reviewed the selected client, period, date and exact balanced intent.</label>
        <button matButton type="submit" [disabled]="createForm.invalid || !balanced() || !reviewed || busy() || uncertain()">Save journal draft</button>
      </form>
      <label>Open a saved journal by ID <input [(ngModel)]="lookupId" /></label>
      <button matButton type="button" [disabled]="busy()" (click)="load()">Open journal</button>
      @if (uncertain()) { <p role="alert">The last action outcome is unknown. Open the authorized journal by ID and inspect its persisted state before any further action.</p> }
    </section>
  `,
})
export class ClientOperationalJournals {
  readonly clientId = input.required<string>();
  readonly periods = input.required<PeriodOption[]>();
  readonly bookCurrency = input.required<string>();
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  readonly journal = signal<Journal | null>(null);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly uncertain = signal(false);
  readonly lines = signal([{ accountCode: '', description: '', debit: '0', credit: '0' }, { accountCode: '', description: '', debit: '0', credit: '0' }]);
  periodId = ''; number = ''; description = ''; postingDate = ''; lookupId = ''; reviewed = false; reason = '';
  private operation?: Subscription;
  private readonly invalidate = effect(() => {
    const id = this.clientId(); this.session.invalidation();
    untracked(() => { this.operation?.unsubscribe(); this.journal.set(null); this.error.set(''); this.uncertain.set(false); this.busy.set(false); this.lookupId = ''; this.reviewed = false; });
    void id;
  });
  constructor() { inject(DestroyRef).onDestroy(() => this.operation?.unsubscribe()); }
  userId(): string { return this.session.current()?.userId ?? ''; }
  totalDebit(): string { return this.total('debit'); }
  totalCredit(): string { return this.total('credit'); }
  balanced(): boolean { try { const d = this.lines().reduce((sum, x) => sum + minor(x.debit), 0n); const c = this.lines().reduce((sum, x) => sum + minor(x.credit), 0n); return d > 0n && d === c; } catch { return false; } }
  private total(key: 'debit' | 'credit'): string {
    try { const units = this.lines().reduce((sum, x) => sum + minor(x[key]), 0n); return `${units / 1_000_000n}.${(units % 1_000_000n).toString().padStart(6, '0')}`; } catch { return '—'; }
  }
  addLine(): void { if (this.lines().length < 100) this.lines.update(lines => [...lines, { accountCode: '', description: '', debit: '0', credit: '0' }]); }
  removeLine(index: number): void { if (this.lines().length > 2) this.lines.update(lines => lines.filter((_, i) => i !== index)); }
  create(): void {
    const clientId = this.clientId(); const period = this.periods().find(p => p.id === this.periodId && p.status !== 'CLOSED');
    if (!period || !this.reviewed || !this.balanced() || this.busy() || this.uncertain()) return;
    if (this.postingDate < period.start || this.postingDate > period.end || period.currency !== this.bookCurrency()) { this.error.set('Choose a posting date and period matching the native book currency.'); return; }
    const generation = this.session.invalidation(); this.busy.set(true); this.error.set('');
    this.operation = this.http.post<{ id: string }>(`/api/ui/accounting/clients/${clientId}/operational-journals`, {
      periodId: period.id, journalNumber: this.number.trim(), description: this.description.trim(), postingDate: this.postingDate,
      reviewed: true, lines: this.lines().map(line => ({ ...line, accountCode: line.accountCode.trim(), description: line.description.trim() })),
    }).pipe(timeout(15000)).subscribe({
      next: value => { if (generation !== this.session.invalidation()) return; this.busy.set(false); if (!value?.id || !guidPattern.test(value.id)) { this.failedUnknown(); return; } this.lookupId = value.id; this.load(); },
      error: failure => { if (generation !== this.session.invalidation()) return; this.failedUnknown(); if (failure.status === 401) this.session.clear(); },
    });
  }
  load(): void {
    const clientId = this.clientId(); const id = this.lookupId.trim();
    if (!guidPattern.test(id) || this.busy()) { if (id) this.error.set('Enter a valid journal ID.'); return; }
    const generation = this.session.invalidation(); this.busy.set(true); this.error.set('');
    this.operation = this.http.get<unknown>(`/api/ui/accounting/clients/${clientId}/operational-journals/${id}`).pipe(timeout(15000)).subscribe({
      next: value => { if (generation !== this.session.invalidation()) return; try { const journal = decodeOperationalJournal(value, clientId); if (journal.id !== id) throw new Error(); this.journal.set(journal); this.uncertain.set(false); this.reviewed = false; this.busy.set(false); } catch { this.busy.set(false); this.error.set('Journal details could not be validated for this client.'); } },
      error: failure => { if (generation !== this.session.invalidation()) return; this.busy.set(false); this.error.set('Journal could not be loaded in this client scope.'); if (failure.status === 401) this.session.clear(); },
    });
  }
  submit(journal: Journal): void { this.act(journal, 'submit', { revision: journal.revision, reviewed: true }, 'Journal submitted for independent review.'); }
  approve(journal: Journal): void { this.act(journal, 'post', { revision: journal.revision, reason: this.reason.trim(), reviewed: true }, 'Journal approved and posted to the client book.'); }
  private act(journal: Journal, action: string, body: object, success: string): void {
    if (!this.reviewed || this.busy() || this.uncertain() || journal.clientId !== this.clientId()) return;
    const generation = this.session.invalidation(); this.busy.set(true); this.error.set('');
    this.operation = this.http.post(`/api/ui/accounting/clients/${journal.clientId}/operational-journals/${journal.id}/${action}`, body).pipe(timeout(15000)).subscribe({
      next: () => { if (generation !== this.session.invalidation()) return; this.busy.set(false); this.error.set(success); this.reviewed = false; this.load(); },
      error: failure => { if (generation !== this.session.invalidation()) return; this.failedUnknown(); if (failure.status === 401) this.session.clear(); },
    });
  }
  private failedUnknown(): void { this.busy.set(false); this.uncertain.set(true); this.reviewed = false; this.error.set('The journal action outcome could not be confirmed. Inspect the persisted journal before retrying.'); }
}
