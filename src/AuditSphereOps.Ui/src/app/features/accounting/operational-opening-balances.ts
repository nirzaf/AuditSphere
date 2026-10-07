import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import { guidPattern } from '../../core/contracts';

interface Period { id: string; revision: string; code: string; start: string; end: string; currency: string; status: string }
interface OpeningLine { accountCode: string; accountName: string; debit: string; credit: string }
interface Opening { id: string; clientId: string; periodId: string; chartVersionId: string; periodRevision: string; asOfDate: string; currency: string;
  evidenceReference: string; evidenceSha256: string; manifestSha256: string; createdByUserId: string; createdAt: string;
  approvedByUserId: string | null; approvedAt: string | null; lines: OpeningLine[] }
interface EntryLine { accountCode: string; debit: string; credit: string }

const amountPattern = /^(?:0|[1-9]\d{0,14})(?:\.\d{1,6})?$/;
const hashPattern = /^[a-fA-F0-9]{64}$/;
const datePattern = /^\d{4}-\d{2}-\d{2}$/;
function object(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('Invalid opening balance response');
  return value as Record<string, unknown>;
}
function validAmount(value: unknown): value is string { return typeof value === 'string' && amountPattern.test(value); }
function units(value: string): bigint { const [whole, fraction = ''] = value.split('.'); return BigInt(whole) * 1_000_000n + BigInt(fraction.padEnd(6, '0')); }

export function decodeOpeningBalance(value: unknown, clientId: string, period: Period): Opening | null {
  if (value === null) return null;
  const v = object(value);
  if (v['clientId'] !== clientId || v['periodId'] !== period.id ||
    !['id', 'chartVersionId', 'createdByUserId'].every(k => typeof v[k] === 'string' && guidPattern.test(String(v[k]))) ||
    typeof v['periodRevision'] !== 'string' || !/^[1-9]\d{0,18}$/.test(v['periodRevision']) ||
    !['asOfDate', 'currency', 'evidenceReference', 'evidenceSha256', 'manifestSha256', 'createdAt'].every(k => typeof v[k] === 'string') ||
    v['asOfDate'] !== period.start || v['currency'] !== period.currency ||
    !hashPattern.test(String(v['evidenceSha256'])) || !hashPattern.test(String(v['manifestSha256'])) ||
    !String(v['evidenceReference']).trim() ||
    (v['approvedByUserId'] !== null && (typeof v['approvedByUserId'] !== 'string' || !guidPattern.test(v['approvedByUserId']))) ||
    (v['approvedAt'] !== null && typeof v['approvedAt'] !== 'string') ||
    !Array.isArray(v['lines']) || v['lines'].length < 2 || v['lines'].length > 5000)
    throw new Error('Opening balance response did not match the selected client period');
  const seen = new Set<string>(); let debit = 0n; let credit = 0n;
  for (const raw of v['lines']) {
    const line = object(raw);
    if (!['accountCode', 'accountName'].every(k => typeof line[k] === 'string' && String(line[k]).trim()) ||
      !validAmount(line['debit']) || !validAmount(line['credit']) || seen.has(String(line['accountCode'])) ||
      (units(line['debit']) > 0n && units(line['credit']) > 0n) || (units(line['debit']) === 0n && units(line['credit']) === 0n))
      throw new Error('Opening balance contains an invalid or repeated account row');
    seen.add(String(line['accountCode'])); debit += units(line['debit']); credit += units(line['credit']);
  }
  if (debit !== credit) throw new Error('Opening balance manifest is not balanced');
  return value as Opening;
}

@Component({
  selector: 'audit-client-operational-opening-balances',
  imports: [FormsModule, MatButtonModule],
  template: `<section aria-labelledby="native-openings-heading">
    <h4 id="native-openings-heading">Reviewed client opening balance</h4>
    <p>Choose an open period to enter its opening position. The source reference and SHA-256 identify your retained evidence; evidence files are not uploaded here. Account codes are checked against the approved client chart by the server.</p>
    <label for="opening-period">Opening period</label>
    <select id="opening-period" [ngModel]="periodId" (ngModelChange)="selectPeriod($event)">
      <option value="">Choose period</option>@for (period of periods(); track period.id) { <option [value]="period.id">{{ period.code }} · {{ period.start }} – {{ period.end }} · {{ period.currency }} · {{ period.status }}</option> }
    </select>
    @if (error()) { <p role="alert">{{ error() }}</p> }
    @if (periodId && !opening() && !loading() && readSucceeded()) {
      <form #openingForm="ngForm" (ngSubmit)="openingForm.valid && create()">
        <fieldset [disabled]="busy() || uncertain() || !selected() || selected()?.status === 'CLOSED'"><legend>Opening position · {{ selected()?.start }} · {{ selected()?.currency }}</legend>
          <p>Opening rows must balance and use active balance-sheet posting accounts. No balancing plug is generated.</p>
          <label>Source evidence reference <input name="openingEvidenceReference" [(ngModel)]="evidenceReference" maxlength="1000" required (ngModelChange)="reviewed.set(false)" /></label>
          <label>Source evidence SHA-256 <input name="openingEvidenceHash" [(ngModel)]="evidenceSha256" minlength="64" maxlength="64" pattern="[A-Fa-f0-9]{64}" required (ngModelChange)="reviewed.set(false)" /></label>
          <div class="table-scroll"><table><caption>Opening balance lines</caption><thead><tr><th>Account code</th><th>Debit</th><th>Credit</th><th>Action</th></tr></thead>
            <tbody>@for (line of lines(); track $index; let i = $index) { <tr><td><input [name]="'openingAccount' + i" [attr.aria-label]="'Opening account code row ' + (i + 1)" [(ngModel)]="line.accountCode" maxlength="100" required (ngModelChange)="edited()" /></td>
              <td><input [name]="'openingDebit' + i" [attr.aria-label]="'Opening debit row ' + (i + 1)" [(ngModel)]="line.debit" inputmode="decimal" required pattern="(?:0|[1-9][0-9]{0,14})(?:\\.[0-9]{1,6})?" (ngModelChange)="edited()" /></td>
              <td><input [name]="'openingCredit' + i" [attr.aria-label]="'Opening credit row ' + (i + 1)" [(ngModel)]="line.credit" inputmode="decimal" required pattern="(?:0|[1-9][0-9]{0,14})(?:\\.[0-9]{1,6})?" (ngModelChange)="edited()" /></td>
              <td><button matButton type="button" [disabled]="busy() || lines().length <= 2" (click)="removeLine(i)">Remove line</button></td></tr> }</tbody>
            <tfoot><tr><th>Total</th><td>{{ total('debit') }}</td><td>{{ total('credit') }}</td><td></td></tr></tfoot>
          </table></div>
          <button matButton type="button" [disabled]="busy() || lines().length >= 5000" (click)="addLine()">Add account line</button>
          <label><input name="openingReviewed" type="checkbox" [ngModel]="reviewed()" (ngModelChange)="reviewed.set($event)" /> I reviewed the client, cutover date, source reference, account mapping, and exact opening balances.</label>
          <button matButton type="submit" [disabled]="openingForm.invalid || busy() || uncertain() || !reviewed() || !balanced() || selected()?.status === 'CLOSED'">Save opening snapshot for independent review</button>
        </fieldset>
      </form>
      @if (selected()?.status === 'CLOSED') { <p role="note">This period is closed; a new opening snapshot cannot be created.</p> }
    }
    @if (opening(); as snapshot) {
      <section aria-label="Immutable opening snapshot">
        <p>{{ snapshot.asOfDate }} · {{ snapshot.currency }} · period revision {{ snapshot.periodRevision }} · chart {{ snapshot.chartVersionId }}</p>
        <p>Source: {{ snapshot.evidenceReference }} · SHA-256 {{ snapshot.evidenceSha256 }}</p>
        <p>Manifest SHA-256 {{ snapshot.manifestSha256 }} · prepared by {{ snapshot.createdByUserId }} · {{ snapshot.createdAt }}</p>
        <div class="table-scroll"><table><caption>Retained opening balance source</caption><thead><tr><th>Account</th><th>Debit</th><th>Credit</th></tr></thead>
          <tbody>@for (line of snapshot.lines; track line.accountCode) { <tr><td>{{ line.accountCode }} · {{ line.accountName }}</td><td>{{ line.debit }}</td><td>{{ line.credit }}</td></tr> }</tbody></table></div>
        @if (snapshot.approvedByUserId) { <p role="status">Independently approved by {{ snapshot.approvedByUserId }} · {{ snapshot.approvedAt }}. This snapshot is immutable.</p> }
        @else {
          <p role="status">Awaiting independent review. The preparer cannot approve this snapshot.</p>
          @if (snapshot.createdByUserId !== userId() && selected()?.status !== 'CLOSED' && selected()?.revision === snapshot.periodRevision) {
            <label><input type="checkbox" [ngModel]="approvalReviewed()" (ngModelChange)="approvalReviewed.set($event)" /> I reviewed this exact opening manifest, evidence identity, period revision, and account lines.</label>
            <button matButton type="button" [disabled]="busy() || uncertain() || !approvalReviewed()" (click)="approve(snapshot)">Approve opening snapshot</button>
          }
        }
      </section>
    }
    @if (uncertain()) { <p role="alert">The last action outcome is unknown. Refresh the selected period before taking another action.</p> }
    <button matButton type="button" [disabled]="!periodId || busy()" (click)="load()">Refresh opening snapshot</button>
  </section>`
})
export class ClientOperationalOpeningBalances {
  readonly clientId = input.required<string>();
  readonly periods = input.required<Period[]>();
  readonly currency = input.required<string>();
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  readonly opening = signal<Opening | null>(null);
  readonly loading = signal(false); readonly busy = signal(false); readonly uncertain = signal(false);
  readonly error = signal(''); readonly readSucceeded = signal(false); readonly reviewed = signal(false); readonly approvalReviewed = signal(false);
  readonly lines = signal<EntryLine[]>([{ accountCode: '', debit: '0', credit: '0' }, { accountCode: '', debit: '0', credit: '0' }]);
  periodId = ''; evidenceReference = ''; evidenceSha256 = '';
  private requestId = 0; private operation?: Subscription;
  private readonly invalidate = effect(() => {
    this.clientId(); this.periods(); this.currency(); this.session.invalidation();
    untracked(() => { this.operation?.unsubscribe(); ++this.requestId; this.periodId = ''; this.opening.set(null); this.loading.set(false); this.busy.set(false); this.uncertain.set(false); this.error.set(''); this.readSucceeded.set(false); this.reset(); });
  });
  constructor() { inject(DestroyRef).onDestroy(() => this.operation?.unsubscribe()); }
  selected(): Period | undefined { return this.periods().find(p => p.id === this.periodId); }
  userId(): string { return this.session.current()?.userId ?? ''; }
  selectPeriod(id: string): void { this.periodId = id; this.opening.set(null); this.reset(); this.uncertain.set(false); this.error.set(''); this.readSucceeded.set(false); if (id) this.load(); }
  reset(): void { this.evidenceReference = ''; this.evidenceSha256 = ''; this.reviewed.set(false); this.approvalReviewed.set(false); this.lines.set([{ accountCode: '', debit: '0', credit: '0' }, { accountCode: '', debit: '0', credit: '0' }]); }
  edited(): void { this.reviewed.set(false); }
  addLine(): void { if (this.lines().length < 5000) this.lines.update(rows => [...rows, { accountCode: '', debit: '0', credit: '0' }]); this.edited(); }
  removeLine(index: number): void { if (this.lines().length > 2) this.lines.update(rows => rows.filter((_, i) => i !== index)); this.edited(); }
  balanced(): boolean {
    const rows = this.lines();
    try { const codes = rows.map(x => x.accountCode.trim()); return rows.length >= 2 && codes.every(Boolean) && new Set(codes).size === codes.length &&
      rows.every(x => validAmount(x.debit) && validAmount(x.credit) && !(units(x.debit) > 0n && units(x.credit) > 0n) && (units(x.debit) > 0n || units(x.credit) > 0n)) &&
      rows.reduce((n, x) => n + units(x.debit), 0n) === rows.reduce((n, x) => n + units(x.credit), 0n) && rows.some(x => units(x.debit) > 0n); }
    catch { return false; }
  }
  total(side: 'debit' | 'credit'): string {
    try { const sum = this.lines().reduce((n, x) => n + (validAmount(x[side]) ? units(x[side]) : 0n), 0n); return `${sum / 1_000_000n}.${(sum % 1_000_000n).toString().padStart(6, '0')}`; }
    catch { return '—'; }
  }
  load(): void {
    const period = this.selected(); if (!period || this.busy()) return;
    const request = ++this.requestId, generation = this.session.invalidation(), client = this.clientId();
    this.loading.set(true); this.error.set(''); this.readSucceeded.set(false);
    this.operation = this.http.get<unknown>(`/api/ui/accounting/clients/${client}/operational-opening-balances/${period.id}`).pipe(timeout(15000)).subscribe({
      next: value => { if (request !== this.requestId || generation !== this.session.invalidation() || client !== this.clientId()) return;
        try { this.opening.set(decodeOpeningBalance(value, client, period)); this.readSucceeded.set(true); this.uncertain.set(false); this.approvalReviewed.set(false); this.error.set(''); }
        catch { this.opening.set(null); this.error.set('Opening snapshot did not match the selected client period.'); }
        this.loading.set(false);
      }, error: failure => { if (request === this.requestId) { this.loading.set(false); this.opening.set(null); this.error.set('Opening snapshot is unavailable. Retry before creating or reviewing it.'); if (failure.status === 401) this.session.clear(); } }
    });
  }
  create(): void {
    const period = this.selected(); if (!period || period.status === 'CLOSED' || !this.readSucceeded() || this.opening() || this.busy() || this.uncertain() || !this.reviewed() || !this.balanced() || !hashPattern.test(this.evidenceSha256.trim())) return;
    const request = ++this.requestId, generation = this.session.invalidation(), client = this.clientId(); this.busy.set(true); this.error.set('');
    this.operation = this.http.post<unknown>(`/api/ui/accounting/clients/${client}/operational-opening-balances`, {
      periodId: period.id, periodRevision: period.revision, asOfDate: period.start, currency: period.currency,
      evidenceReference: this.evidenceReference.trim(), evidenceSha256: this.evidenceSha256.trim().toLowerCase(),
      lines: this.lines().map(x => ({ accountCode: x.accountCode.trim(), debit: x.debit, credit: x.credit })), reviewed: true
    }).pipe(timeout(20000)).subscribe({ next: () => { if (request !== this.requestId || generation !== this.session.invalidation()) return; this.busy.set(false); this.uncertain.set(true); this.load(); },
      error: failure => { if (request === this.requestId) { this.busy.set(false); this.uncertain.set(true); this.error.set('Creation outcome is unconfirmed. Refresh the selected period before retrying.'); if (failure.status === 401) this.session.clear(); } }
    });
  }
  approve(snapshot: Opening): void {
    const period = this.selected(); if (!period || period.status === 'CLOSED' || period.revision !== snapshot.periodRevision || snapshot.createdByUserId === this.userId() || !this.approvalReviewed() || this.busy() || this.uncertain()) return;
    const request = ++this.requestId, generation = this.session.invalidation(), client = this.clientId(); this.busy.set(true); this.error.set('');
    this.operation = this.http.post(`/api/ui/accounting/clients/${client}/operational-opening-balances/${snapshot.id}/approve`, {
      periodRevision: snapshot.periodRevision, manifestSha256: snapshot.manifestSha256, reviewed: true
    }).pipe(timeout(20000)).subscribe({ next: () => { if (request !== this.requestId || generation !== this.session.invalidation()) return; this.busy.set(false); this.uncertain.set(true); this.load(); },
      error: failure => { if (request === this.requestId) { this.busy.set(false); this.uncertain.set(true); this.error.set('Approval outcome is unconfirmed. Refresh the selected period before another action.'); if (failure.status === 401) this.session.clear(); } }
    });
  }
}
