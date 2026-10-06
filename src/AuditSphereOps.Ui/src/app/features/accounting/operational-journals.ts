import { ChangeDetectorRef, Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import { guidPattern } from '../../core/contracts';

interface PeriodOption { id: string; code: string; start: string; end: string; currency: string; status: string }
interface JournalLine { lineNumber: number; accountId: string; accountCode: string; accountName: string; description: string; debit: string; credit: string }
interface ReviewDecision { revision: string; decision: string; reason: string; actorUserId: string; createdAt: string }
interface Journal { id: string; clientId: string; periodId: string; journalNumber: string; description: string; postingDate: string; currency: string; status: string; revision: string; createdByUserId: string; lines: JournalLine[]; decisions: ReviewDecision[] }
interface JournalSnapshot { journalId: string; clientId: string; revision: string; capturedAt: string; journalNumber: string;
  description: string; postingDate: string; currency: string; lines: JournalLine[] }
interface JournalPreview { journalId: string; clientId: string; periodId: string; revision: string; status: string; currency: string;
  totalDebit: string; totalCredit: string; digest: string; lines: JournalLine[] }
interface LedgerView { clientId: string; periodId: string; periodCode: string; currency: string; basis: string; page: number; pageSize: number; totalEntries: number;
  accounts: { accountId: string; accountCode: string; accountName: string; debitMovement: string; creditMovement: string; netMovement: string }[];
  entries: { journalId: string; journalNumber: string; postingDate: string; lineNumber: number; accountCode: string; accountName: string; description: string; debit: string; credit: string }[] }
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
  if (!Array.isArray(v['decisions']) || v['decisions'].length > 10000) throw new Error('Invalid journal review history');
  for (const raw of v['decisions']) {
    const d = object(raw);
    if (typeof d['revision'] !== 'string' || !/^[1-9]\d{0,18}$/.test(d['revision']) ||
        !['APPROVE', 'RETURN'].includes(String(d['decision'])) || typeof d['reason'] !== 'string' || !d['reason'].trim() ||
        typeof d['actorUserId'] !== 'string' || !guidPattern.test(d['actorUserId']) || typeof d['createdAt'] !== 'string')
      throw new Error('Invalid journal review history');
  }
  return v as unknown as Journal;
}
export function decodeOperationalLedger(value: unknown, clientId: string, periodId: string): LedgerView {
  const v = object(value);
  if (v['clientId'] !== clientId || v['periodId'] !== periodId ||
      !['periodCode', 'currency', 'basis'].every(k => typeof v[k] === 'string') ||
      !['page', 'pageSize', 'totalEntries'].every(k => Number.isSafeInteger(v[k]) && Number(v[k]) >= 0) ||
      Number(v['pageSize']) < 1 || Number(v['pageSize']) > 200 ||
      !Array.isArray(v['accounts']) || v['accounts'].length > 10000 || !Array.isArray(v['entries']) || v['entries'].length > 200) throw new Error('Invalid ledger response');
  for (const raw of v['accounts']) {
    const account = object(raw);
    if (typeof account['accountId'] !== 'string' || !guidPattern.test(account['accountId']) ||
        !['accountCode', 'accountName', 'debitMovement', 'creditMovement', 'netMovement'].every(k => typeof account[k] === 'string') ||
        ![account['debitMovement'], account['creditMovement']].every(x => amountPattern.test(String(x))) ||
        !amountPattern.test(String(account['netMovement']).replace(/^-/, ''))) throw new Error('Invalid ledger account');
  }
  for (const raw of v['entries']) {
    const entry = object(raw);
    if (typeof entry['journalId'] !== 'string' || !guidPattern.test(entry['journalId']) ||
        !['journalNumber', 'postingDate', 'accountCode', 'accountName', 'description', 'debit', 'credit'].every(k => typeof entry[k] === 'string') ||
        !/^\d{4}-\d{2}-\d{2}$/.test(String(entry['postingDate'])) ||
        ![entry['debit'], entry['credit']].every(x => amountPattern.test(String(x))) ||
        !Number.isSafeInteger(entry['lineNumber']) || Number(entry['lineNumber']) < 1) throw new Error('Invalid ledger entry');
  }
  return v as unknown as LedgerView;
}
export function decodeJournalPreview(value: unknown, journal: Journal): JournalPreview {
  const p = object(value);
  if (p['journalId'] !== journal.id || p['clientId'] !== journal.clientId || p['periodId'] !== journal.periodId ||
      p['revision'] !== journal.revision || p['status'] !== journal.status || p['currency'] !== journal.currency ||
      typeof p['digest'] !== 'string' || !/^[a-f0-9]{64}$/.test(p['digest']) ||
      typeof p['totalDebit'] !== 'string' || typeof p['totalCredit'] !== 'string' ||
      !amountPattern.test(p['totalDebit']) || p['totalDebit'] !== p['totalCredit']) throw new Error('Invalid journal preview');
  const parsed = decodeOperationalJournal({ ...journal, lines: p['lines'] }, journal.clientId);
  if (JSON.stringify(parsed.lines) !== JSON.stringify(journal.lines)) throw new Error('Preview lines changed');
  return p as unknown as JournalPreview;
}
export function decodeJournalSnapshots(value: unknown, journal: Journal): JournalSnapshot[] {
  if (!Array.isArray(value) || value.length > 10000) throw new Error('Invalid journal versions');
  const seen = new Set<string>();
  for (const raw of value) {
    const v = object(raw);
    if (v['journalId'] !== journal.id || v['clientId'] !== journal.clientId || typeof v['revision'] !== 'string' ||
        !/^[1-9]\d{0,18}$/.test(v['revision']) || seen.has(v['revision']) ||
        !['capturedAt', 'journalNumber', 'description', 'postingDate', 'currency'].every(k => typeof v[k] === 'string'))
      throw new Error('Invalid submitted journal version');
    seen.add(v['revision']);
    decodeOperationalJournal({ ...journal, ...v, id: journal.id, status: 'SUBMITTED', decisions: [] }, journal.clientId);
  }
  return value as JournalSnapshot[];
}
export function nativeJournalAmount(value: string): boolean { return /^(?:0|[1-9]\d{0,12})(?:\.\d{1,6})?$/.test(value); }
function minor(value: string): bigint { if (!nativeJournalAmount(value)) throw new Error('Invalid native journal amount'); const [whole, fraction = ''] = value.split('.'); return BigInt(whole) * 1_000_000n + BigInt(fraction.padEnd(6, '0')); }

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
          @if (j.decisions.length) {
            <section aria-label="Journal review history"><h5>Review history</h5>
              @for (d of j.decisions; track d.revision + ':' + d.decision) {
                <p>Revision {{ d.revision }} · {{ d.decision }} · {{ d.reason }} · {{ d.createdAt }}</p>
              }
            </section>
          }
          <button matButton type="button" [disabled]="busy()" (click)="loadSnapshots(j)">View submitted versions</button>
          @if (snapshots(); as versions) {
            @if (!versions.length) { <p>No submitted content was captured for this journal. Earlier review content is unavailable.</p> }
            @for (v of versions; track v.revision) {
              <details><summary>Submitted revision {{ v.revision }}</summary>
                <p>{{ v.description }} · {{ v.postingDate }} · {{ v.currency }} · captured {{ v.capturedAt }}</p>
                <div class="table-scroll"><table><caption>Preserved submitted journal lines</caption>
                  <thead><tr><th>Account</th><th>Description</th><th>Debit</th><th>Credit</th></tr></thead>
                  <tbody>@for (line of v.lines; track line.lineNumber) { <tr><td>{{ line.accountCode }} · {{ line.accountName }}</td><td>{{ line.description }}</td><td>{{ line.debit }}</td><td>{{ line.credit }}</td></tr> }</tbody>
                </table></div>
              </details>
            }
          }
          @if (j.status !== 'POSTED') {
            <button matButton type="button" [disabled]="busy() || uncertain()" (click)="loadPreview(j)">Preview accounting effect</button>
            @if (preview(); as p) {
              <p role="status">Server-validated preview · {{ p.currency }} · Debits {{ p.totalDebit }} · Credits {{ p.totalCredit }} · revision {{ p.revision }}</p>
              <details><summary>Reviewed intent identity</summary><code>{{ p.digest }}</code></details>
            }
          }
          @if (j.status === 'RETURNED' && j.createdByUserId === userId()) {
            <button matButton type="button" [disabled]="busy() || uncertain() || !!editing()" (click)="editReturned(j)">Edit returned journal</button>
          }
          @if ((j.status === 'DRAFT' || j.status === 'RETURNED') && !editing()) {
            <label><input type="checkbox" [checked]="reviewed()" (change)="setReviewed($any($event.target).checked)" /> I reviewed this client, journal, posting date, account selection and exact amounts.</label>
            <button matButton [disabled]="busy() || !preview() || !reviewed() || uncertain()" (click)="submit(j)">Submit for independent review</button>
          }
          @if (j.status === 'SUBMITTED') {
            <label><input type="checkbox" [checked]="reviewed()" (change)="setReviewed($any($event.target).checked)" /> I independently reviewed this exact journal revision and its balanced lines.</label>
            <label>Review reason <input [(ngModel)]="reason" maxlength="2000" /></label>
            <button matButton [disabled]="busy() || !preview() || !reviewed() || !reason.trim() || j.createdByUserId === userId() || uncertain()" (click)="approve(j)">Approve and post</button>
            <button matButton type="button" [disabled]="busy() || !reviewed() || !reason.trim() || j.createdByUserId === userId() || uncertain()" (click)="returnJournal(j)">Return for rework</button>
          }
        </article>
      }
      <section aria-labelledby="posted-ledger-heading">
        <h4 id="posted-ledger-heading">Posted General Ledger activity</h4>
        <p>Native client journal movements for the selected period. This view does not include opening balances, imported GL, or reporting adjustments.</p>
        <button matButton type="button" [disabled]="busy() || !periodId" (click)="loadLedger()">Refresh posted ledger</button>
        @if (ledgerError()) { <p role="alert">{{ ledgerError() }}</p> }
        @if (ledger(); as l) {
          <p>{{ l.periodCode }} · {{ l.basis }} · {{ l.currency }} · {{ l.totalEntries }} posted lines</p>
          @if (!l.entries.length) { <p>No posted native journal lines in this period.</p> }
          @if (l.accounts.length) { <div class="table-scroll"><table><caption>Account debit, credit and net movement</caption><thead><tr><th>Account</th><th>Debits</th><th>Credits</th><th>Net movement</th></tr></thead>
            <tbody>@for (a of l.accounts; track a.accountId) { <tr><td>{{ a.accountCode }} · {{ a.accountName }}</td><td>{{ a.debitMovement }}</td><td>{{ a.creditMovement }}</td><td>{{ a.netMovement }}</td></tr> }</tbody></table></div> }
          @if (l.entries.length) { <div class="table-scroll"><table><caption>Posted journal line detail</caption><thead><tr><th>Date / journal</th><th>Account</th><th>Description</th><th>Debit</th><th>Credit</th></tr></thead>
            <tbody>@for (e of l.entries; track e.journalId + ':' + e.lineNumber) { <tr><td><button matButton type="button" (click)="lookupId = e.journalId; load()">{{ e.postingDate }} · {{ e.journalNumber }}</button></td>
              <td>{{ e.accountCode }} · {{ e.accountName }}</td><td>{{ e.description }}</td><td>{{ e.debit }}</td><td>{{ e.credit }}</td></tr> }</tbody></table></div> }
        }
      </section>
      <form #createForm="ngForm" (ngSubmit)="createForm.valid && create()">
        <h4>{{ editing() ? "Rework returned journal" : "Create manual journal draft" }}</h4>
        <label>Reporting period <select [disabled]="!!editing()" name="period" [(ngModel)]="periodId" (ngModelChange)="reviewed.set(false)" required>
          <option value="">Choose period</option>@for (p of periods(); track p.id) { <option [value]="p.id" [disabled]="p.status === 'CLOSED'">{{ p.code }} · {{ p.currency }} · {{ p.status }}</option> }
        </select></label>
        <label>Journal number <input [disabled]="!!editing()" name="number" [(ngModel)]="number" (ngModelChange)="reviewed.set(false)" required maxlength="100" /></label>
        <label>Description <input name="description" [(ngModel)]="description" (ngModelChange)="reviewed.set(false)" required maxlength="1000" /></label>
        <label>Accounting date <input name="date" type="date" [(ngModel)]="postingDate" (ngModelChange)="reviewed.set(false)" required /></label>
        <div class="table-scroll"><table><caption>Balanced journal draft lines</caption><thead><tr><th>Account code</th><th>Description</th><th>Debit</th><th>Credit</th><th></th></tr></thead>
          <tbody>@for (line of lines(); track $index) { <tr>
            <td><input aria-label="Account code" [(ngModel)]="line.accountCode" [name]="'account' + $index" (ngModelChange)="reviewed.set(false)" required maxlength="100" /></td>
            <td><input aria-label="Line description" [(ngModel)]="line.description" [name]="'lineDescription' + $index" (ngModelChange)="reviewed.set(false)" maxlength="1000" /></td>
            <td><input aria-label="Debit" [(ngModel)]="line.debit" [name]="'debit' + $index" (ngModelChange)="reviewed.set(false)" inputmode="decimal" pattern="(?:0|[1-9][0-9]{0,12})(?:\\.[0-9]{1,6})?" required /></td>
            <td><input aria-label="Credit" [(ngModel)]="line.credit" [name]="'credit' + $index" (ngModelChange)="reviewed.set(false)" inputmode="decimal" pattern="(?:0|[1-9][0-9]{0,12})(?:\\.[0-9]{1,6})?" required /></td>
            <td><button matButton type="button" [disabled]="lines().length <= 2" (click)="removeLine($index)">Remove</button></td>
          </tr> }</tbody></table></div>
        <p>Debits {{ totalDebit() }} · Credits {{ totalCredit() }} · {{ balanced() ? 'Balanced' : 'Out of balance' }}</p>
        <button matButton type="button" [disabled]="lines().length >= 100" (click)="addLine()">Add line</button>
        <label><input type="checkbox" [checked]="reviewed()" (change)="setReviewed($any($event.target).checked)" name="createReview" /> I reviewed the selected client, period, date and exact balanced intent.</label>
        <button matButton type="submit" [disabled]="createForm.invalid || !balanced() || !reviewed() || busy() || uncertain()">{{ editing() ? "Save rework draft" : "Save journal draft" }}</button>
        @if (editing()) { <button matButton type="button" [disabled]="busy()" (click)="resetDraft()">Cancel rework</button> }
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
  private readonly changeDetector = inject(ChangeDetectorRef);
  private readonly session = inject(SessionService);
  readonly editing = signal<Journal | null>(null);
  readonly journal = signal<Journal | null>(null);
  readonly preview = signal<JournalPreview | null>(null);
  readonly snapshots = signal<JournalSnapshot[] | null>(null);
  readonly ledger = signal<LedgerView | null>(null);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly ledgerError = signal('');
  readonly uncertain = signal(false);
  readonly lines = signal([{ accountCode: '', description: '', debit: '0', credit: '0' }, { accountCode: '', description: '', debit: '0', credit: '0' }]);
  periodId = ''; number = ''; description = ''; postingDate = ''; lookupId = ''; reason = '';
  readonly reviewed = signal(false);
  private operation?: Subscription;
  private readonly invalidate = effect(() => {
    const id = this.clientId(); this.session.invalidation();
    untracked(() => { this.operation?.unsubscribe(); this.journal.set(null); this.snapshots.set(null); this.preview.set(null); this.ledger.set(null); this.error.set(''); this.ledgerError.set(''); this.uncertain.set(false); this.busy.set(false); this.lookupId = ''; this.reason = ''; this.resetDraft(); });
    void id;
  });
  constructor() { inject(DestroyRef).onDestroy(() => this.operation?.unsubscribe()); }
  setReviewed(value: boolean): void {
    this.reviewed.set(value);
    // Render the acknowledged value before a subsequent edit can revoke it.
    this.changeDetector.detectChanges();
  }
  userId(): string { return this.session.current()?.userId ?? ''; }
  totalDebit(): string { return this.total('debit'); }
  totalCredit(): string { return this.total('credit'); }
  balanced(): boolean { try { const d = this.lines().reduce((sum, x) => sum + minor(x.debit), 0n); const c = this.lines().reduce((sum, x) => sum + minor(x.credit), 0n); return d > 0n && d === c; } catch { return false; } }
  private total(key: 'debit' | 'credit'): string {
    try { const units = this.lines().reduce((sum, x) => sum + minor(x[key]), 0n); return `${units / 1_000_000n}.${(units % 1_000_000n).toString().padStart(6, '0')}`; } catch { return '—'; }
  }
  addLine(): void { this.reviewed.set(false); if (this.lines().length < 100) this.lines.update(lines => [...lines, { accountCode: '', description: '', debit: '0', credit: '0' }]); }
  removeLine(index: number): void { this.reviewed.set(false); if (this.lines().length > 2) this.lines.update(lines => lines.filter((_, i) => i !== index)); }
  resetDraft(): void {
    this.editing.set(null); this.periodId = ''; this.number = ''; this.description = ''; this.postingDate = '';
    this.lines.set([{ accountCode: '', description: '', debit: '0', credit: '0' }, { accountCode: '', description: '', debit: '0', credit: '0' }]);
    this.reviewed.set(false);
  }
  editReturned(journal: Journal): void {
    if (this.busy() || this.uncertain() || journal.clientId !== this.clientId() || journal.status !== 'RETURNED' || journal.createdByUserId !== this.userId()) return;
    this.editing.set(journal); this.periodId = journal.periodId; this.number = journal.journalNumber;
    this.description = journal.description; this.postingDate = journal.postingDate;
    this.lines.set(journal.lines.map(({ accountCode, description, debit, credit }) => ({ accountCode, description, debit, credit })));
    this.preview.set(null); this.reviewed.set(false);
  }
  create(): void {
    const clientId = this.clientId(); const period = this.periods().find(p => p.id === this.periodId && p.status !== 'CLOSED');
    if (!period || !this.reviewed() || !this.balanced() || this.busy() || this.uncertain()) return;
    if (this.postingDate < period.start || this.postingDate > period.end || period.currency !== this.bookCurrency()) { this.error.set('Choose a posting date and period matching the native book currency.'); return; }
    const edit = this.editing();
    if (edit && (edit.clientId !== clientId || edit.id !== this.journal()?.id || edit.revision !== this.journal()?.revision || edit.periodId !== period.id)) return;
    const generation = this.session.invalidation(); this.busy.set(true); this.error.set('');
    const url = `/api/ui/accounting/clients/${clientId}/operational-journals` + (edit ? `/${edit.id}/rework` : '');
    this.operation = this.http.post<{ id: string }>(url, {
      ...(edit ? { revision: edit.revision } : {}),
      periodId: period.id, journalNumber: this.number.trim(), description: this.description.trim(), postingDate: this.postingDate,
      reviewed: true, lines: this.lines().map(line => ({ ...line, accountCode: line.accountCode.trim(), description: line.description.trim() })),
    }).pipe(timeout(15000)).subscribe({
      next: value => { if (generation !== this.session.invalidation()) return; this.busy.set(false); if (!value?.id || !guidPattern.test(value.id)) { this.failedUnknown(); return; } this.editing.set(null); this.lookupId = value.id; this.load(); },
      error: failure => { if (generation !== this.session.invalidation()) return; this.failedUnknown(); if (failure.status === 401) this.session.clear(); },
    });
  }
  load(): void {
    const clientId = this.clientId(); const id = this.lookupId.trim();
    if (!guidPattern.test(id) || this.busy()) { if (id) this.error.set('Enter a valid journal ID.'); return; }
    this.editing.set(null);
    const generation = this.session.invalidation(); this.preview.set(null); this.snapshots.set(null); this.reviewed.set(false); this.busy.set(true); this.error.set('');
    this.operation = this.http.get<unknown>(`/api/ui/accounting/clients/${clientId}/operational-journals/${id}`).pipe(timeout(15000)).subscribe({
      next: value => { if (generation !== this.session.invalidation()) return; try { const journal = decodeOperationalJournal(value, clientId); if (journal.id !== id) throw new Error(); this.journal.set(journal); this.uncertain.set(false); this.reviewed.set(false); this.busy.set(false); if (journal.status === 'POSTED') this.loadLedger(journal.periodId); } catch { this.busy.set(false); this.error.set('Journal details could not be validated for this client.'); } },
      error: failure => { if (generation !== this.session.invalidation()) return; this.busy.set(false); this.error.set('Journal could not be loaded in this client scope.'); if (failure.status === 401) this.session.clear(); },
    });
  }
  loadLedger(forPeriodId = this.periodId): void {
    const clientId = this.clientId(); const period = this.periods().find(p => p.id === forPeriodId);
    if (!period || this.busy()) return;
    const generation = this.session.invalidation(); this.ledgerError.set('');
    this.operation = this.http.get<unknown>(`/api/ui/accounting/clients/${clientId}/operational-ledger`, {
      params: { periodId: forPeriodId, page: '0', pageSize: '100' },
    }).pipe(timeout(15000)).subscribe({
      next: value => { if (generation !== this.session.invalidation()) return; try { this.ledger.set(decodeOperationalLedger(value, clientId, forPeriodId)); }
        catch { this.ledger.set(null); this.ledgerError.set('Posted ledger response did not match this client and period.'); } },
      error: failure => { if (generation === this.session.invalidation()) { this.ledger.set(null); this.ledgerError.set('Posted client ledger is unavailable. Retry or refresh the client.'); if (failure.status === 401) this.session.clear(); } },
    });
  }
  loadSnapshots(journal: Journal): void {
    if (this.busy() || journal.clientId !== this.clientId()) return;
    const generation = this.session.invalidation(); this.busy.set(true); this.snapshots.set(null); this.error.set('');
    this.operation = this.http.get<unknown>(`/api/ui/accounting/clients/${journal.clientId}/operational-journals/${journal.id}/snapshots`).pipe(timeout(15000)).subscribe({
      next: value => { if (generation !== this.session.invalidation() || this.journal()?.id !== journal.id) return;
        this.busy.set(false); try { this.snapshots.set(decodeJournalSnapshots(value, journal)); }
        catch { this.error.set('Submitted content did not match this client journal.'); } },
      error: failure => { if (generation !== this.session.invalidation()) return; this.busy.set(false); this.error.set('Submitted versions could not be loaded.'); if (failure.status === 401) this.session.clear(); },
    });
  }
  loadPreview(journal: Journal): void {
    if (this.busy() || this.uncertain() || journal.clientId !== this.clientId()) return;
    const generation = this.session.invalidation(); this.busy.set(true); this.preview.set(null); this.reviewed.set(false); this.error.set('');
    this.operation = this.http.get<unknown>(`/api/ui/accounting/clients/${journal.clientId}/operational-journals/${journal.id}/preview`).pipe(timeout(15000)).subscribe({
      next: value => { if (generation !== this.session.invalidation() || journal.clientId !== this.clientId()) return;
        this.busy.set(false); try { this.preview.set(decodeJournalPreview(value, journal)); }
        catch { this.error.set('The server preview does not match this saved journal. Reload it before reviewing.'); } },
      error: failure => { if (generation !== this.session.invalidation()) return; this.busy.set(false); this.error.set('The journal preview is unavailable or its accounting context is no longer valid.'); if (failure.status === 401) this.session.clear(); },
    });
  }
  submit(journal: Journal): void { this.act(journal, 'submit', { revision: journal.revision, previewDigest: this.preview()?.digest, reviewed: true }, 'Journal submitted for independent review.'); }
  returnJournal(journal: Journal): void { this.act(journal, 'return', { revision: journal.revision, reason: this.reason.trim(), reviewed: true }, 'Journal returned to its preparer.'); }
  approve(journal: Journal): void { this.act(journal, 'post', { revision: journal.revision, reason: this.reason.trim(), previewDigest: this.preview()?.digest, reviewed: true }, 'Journal approved and posted to the client book.'); }
  private act(journal: Journal, action: string, body: object, success: string): void {
    if (this.editing() || !this.reviewed() || (action !== 'return' && (!this.preview() || this.preview()?.journalId !== journal.id || this.preview()?.revision !== journal.revision)) || this.busy() || this.uncertain() || journal.clientId !== this.clientId()) return;
    const generation = this.session.invalidation(); this.busy.set(true); this.error.set('');
    this.operation = this.http.post(`/api/ui/accounting/clients/${journal.clientId}/operational-journals/${journal.id}/${action}`, body).pipe(timeout(15000)).subscribe({
      next: () => { if (generation !== this.session.invalidation()) return; this.busy.set(false); this.error.set(success); this.reviewed.set(false); this.load(); },
      error: failure => { if (generation !== this.session.invalidation()) return; this.failedUnknown(); if (failure.status === 401) this.session.clear(); },
    });
  }
  private failedUnknown(): void { this.busy.set(false); this.uncertain.set(true); this.reviewed.set(false); this.error.set('The journal action outcome could not be confirmed. Inspect the persisted journal before retrying.'); }
}
