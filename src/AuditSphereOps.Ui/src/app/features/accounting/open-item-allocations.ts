import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { Subscription, timeout } from 'rxjs';
import { guidPattern } from '../../core/contracts';

interface OpenItem {
  kind: 'SALES_INVOICE' | 'SALES_CREDIT' | 'PURCHASE_INVOICE' | 'PURCHASE_CREDIT' | 'SALES_RECEIPT' | 'SUPPLIER_PAYMENT'; openItemId: string; sourceDocumentId: string;
  counterpartyId: string; counterpartyName: string; currency: string; originalAmount: string; appliedAmount: string;
  openAmount: string; dueDate: string | null; asOfDate: string; status: string;
}
interface TargetLine { lineNumber: number; targetKind: 'SALES_INVOICE' | 'PURCHASE_INVOICE'; targetOpenItemId: string; amount: string; reversesAllocationLineId: string | null }
interface Preview { submissionId: string; digest: string; disposition: 'ALLOCATE' | 'UNALLOCATE'; sourceKind: 'SALES_CREDIT' | 'PURCHASE_CREDIT' | 'SALES_RECEIPT' | 'SUPPLIER_PAYMENT';
  sourceItemId: string; counterpartyId: string; currency: string; sourceOriginalAmount: string; sourceAvailableAmount: string;
  lines: TargetLine[]; targetAvailableAmounts: string[] }
interface PendingPreview extends Preview { submissionId: string }
const decimal = /^(?:0|[1-9]\d{0,12})(?:\.\d{1,6})?$/;

function records(value: unknown): Record<string, unknown>[] {
  if (!Array.isArray(value) || value.length > 1000) throw new Error('Invalid open-item response');
  return value.map(x => { if (!x || typeof x !== 'object' || Array.isArray(x)) throw new Error('Invalid open-item row'); return x as Record<string, unknown>; });
}
function decodeBalances(value: unknown): OpenItem[] {
  return records(value).map(x => {
    const kinds = ['SALES_INVOICE', 'SALES_CREDIT', 'PURCHASE_INVOICE', 'PURCHASE_CREDIT', 'SALES_RECEIPT', 'SUPPLIER_PAYMENT'];
    if (!kinds.includes(String(x['kind'])) || ![x['openItemId'], x['sourceDocumentId'], x['counterpartyId']].every(v => typeof v === 'string' && guidPattern.test(v)) ||
      typeof x['counterpartyName'] !== 'string' || typeof x['currency'] !== 'string' || !/^[A-Z]{3}$/.test(x['currency']) ||
      !['originalAmount', 'appliedAmount', 'openAmount'].every(k => typeof x[k] === 'string' && decimal.test(x[k])) ||
      typeof x['asOfDate'] !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(x['asOfDate']) || typeof x['status'] !== 'string' ||
      !(x['dueDate'] === null || (typeof x['dueDate'] === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(x['dueDate'])))) throw new Error('Invalid scoped open-item data');
    return x as unknown as OpenItem;
  });
}

@Component({ selector: 'audit-open-item-allocations', imports: [FormsModule, MatButtonModule], template: `
  <section aria-label="Client receivables, payables and allocations">
    <h3>Receivables, payables and credit allocations</h3>
    <p>Balances are derived from posted client invoices and approved credit allocations as of the selected date. No cash or payment is created here.</p>
    <label>Ageing as of <input type="date" [(ngModel)]="asOfDate" name="asOfDate" /></label>
    <button matButton type="button" [disabled]="busy()" (click)="load()">Refresh balances</button>
    @if (error()) { <p role="alert">{{ error() }}</p> }
    @if (busy()) { <p role="status">Updating client balances…</p> }
    <div class="table-scroll"><table><caption>Open client items and as-of ageing</caption><thead><tr><th>Type</th><th>Party</th><th>Document</th><th>Due</th><th>Currency</th><th>Original</th><th>Applied</th><th>Open</th><th>Status</th></tr></thead><tbody>
      @for (row of balances(); track row.openItemId) { <tr><td>{{ row.kind.replaceAll('_', ' ') }}</td><td>{{ row.counterpartyName }}</td><td>{{ row.sourceDocumentId }}</td><td>{{ row.dueDate ?? '—' }}</td><td>{{ row.currency }}</td><td>{{ row.originalAmount }}</td><td>{{ row.appliedAmount }}</td><td>{{ row.openAmount }}</td><td>{{ row.status }}</td></tr> }
      @empty { <tr><td colspan="9">No posted open items are available for this client and date.</td></tr> }
    </tbody></table></div>
    <form #allocation="ngForm" (ngSubmit)="allocation.valid && previewAllocation()">
      <h4>Apply a credit or imported settlement</h4>
      <label>Settlement source <select name="source" [(ngModel)]="sourceId" required><option value="">Choose an approved credit or sealed imported receipt/payment</option>@for (row of credits(); track row.openItemId) { <option [value]="row.openItemId">{{ row.kind.replaceAll('_', ' ') }} · {{ row.counterpartyName }} · {{ row.openAmount }} {{ row.currency }}</option> }</select></label>
      <label>Target invoice <select name="target" [(ngModel)]="targetId" required><option value="">Choose a matching invoice</option>@for (row of targets(); track row.openItemId) { <option [value]="row.openItemId">{{ row.kind.replaceAll('_', ' ') }} · {{ row.counterpartyName }} · {{ row.openAmount }} {{ row.currency }}</option> }</select></label>
      <label>Amount <input name="amount" [(ngModel)]="amount" required inputmode="decimal" pattern="(?:0|[1-9][0-9]{0,12})(?:\.[0-9]{1,6})?" /></label>
      <label>Reference <input name="reference" [(ngModel)]="reference" required maxlength="200" /></label>
      <label>Reason <input name="reason" [(ngModel)]="reason" required maxlength="2000" /></label>
      <label><input name="reviewed" type="checkbox" [(ngModel)]="reviewed" /> I reviewed the client, party, currency, amount, and source linkage.</label>
      <button matButton type="submit" [disabled]="allocation.invalid || busy() || !reviewed">Preview allocation</button>
    </form>
    @if (preview(); as p) { <section aria-label="Allocation preview"><h4>Review before submission</h4><p>{{ p.disposition }} {{ amount }} {{ p.currency }} from {{ p.sourceKind }} for {{ selectedParty() }} to {{ p.lines[0].targetKind }}. Source balance before: {{ p.sourceAvailableAmount }}. Target balance before: {{ p.targetAvailableAmounts[0] }}.</p>
      <button matButton type="button" [disabled]="busy() || !reviewed" (click)="submitAllocation()">Submit for independent approval</button></section> }
    @if (receipt()) { <p role="status">Allocation request {{ receipt() }} was submitted. A different authorized reviewer must approve it.</p> }
    @if (pending().length) { <section aria-label="Pending allocation reviews"><h4>Pending allocations for independent review</h4>@for (p of pending(); track p.submissionId) { <article><p>{{ p.sourceKind }} · {{ p.sourceAvailableAmount }} {{ p.currency }} · {{ p.lines.length }} target line(s)</p>
      <label>Decision reason <input [(ngModel)]="reviewReason[p.submissionId]" [name]="'reason-' + p.submissionId" required maxlength="2000" /></label>
      <button matButton type="button" [disabled]="busy() || !reviewReason[p.submissionId]?.trim()" (click)="review(p, 'APPROVE')">Approve allocation</button>
      <button matButton type="button" [disabled]="busy() || !reviewReason[p.submissionId]?.trim()" (click)="review(p, 'RETURN')">Return for correction</button></article> }</section> }
  </section>` })
export class OpenItemAllocations {
  readonly clientId = input.required<string>();
  readonly balances = signal<OpenItem[]>([]); readonly pending = signal<PendingPreview[]>([]); readonly preview = signal<Preview | null>(null);
  readonly busy = signal(false); readonly error = signal(''); readonly receipt = signal('');
  asOfDate = new Date().toISOString().slice(0, 10); sourceId = ''; targetId = ''; amount = ''; reference = ''; reason = ''; reviewed = false;
  readonly reviewReason: Record<string, string> = {};
  private readonly http = inject(HttpClient); private readonly destroyRef = inject(DestroyRef); private operation?: Subscription;
  constructor() {
    effect(() => { const client = this.clientId(); if (client) untracked(() => this.load()); });
    this.destroyRef.onDestroy(() => this.operation?.unsubscribe());
  }
  credits(): OpenItem[] { return this.balances().filter(x => ['SALES_CREDIT', 'PURCHASE_CREDIT', 'SALES_RECEIPT', 'SUPPLIER_PAYMENT'].includes(x.kind) && x.openAmount !== '0.000000'); }
  targets(): OpenItem[] { const source = this.credits().find(x => x.openItemId === this.sourceId); return this.balances().filter(x => x.kind.endsWith('_INVOICE') && x.openAmount !== '0.000000' && (!source || x.counterpartyId === source.counterpartyId && x.currency === source.currency && x.kind === (['SALES_CREDIT', 'SALES_RECEIPT'].includes(source.kind) ? 'SALES_INVOICE' : 'PURCHASE_INVOICE'))); }
  selectedParty(): string { return this.credits().find(x => x.openItemId === this.sourceId)?.counterpartyName ?? ''; }
  load(): void {
    const client = this.clientId(); if (!guidPattern.test(client)) return;
    this.busy.set(true); this.error.set(''); this.preview.set(null); this.receipt.set(''); this.operation?.unsubscribe();
    this.operation = this.http.get<unknown>(`/api/ui/accounting/clients/${client}/open-item-balances`, { params: { asOf: this.asOfDate } }).pipe(timeout(15000)).subscribe({
      next: value => { try { this.balances.set(decodeBalances(value)); this.loadPending(client); } catch { this.busy.set(false); this.error.set('The client balance response was not valid. Refresh the client and review persisted postings.'); } },
      error: () => { this.busy.set(false); this.error.set('Client balances could not be loaded.'); }
    });
  }
  private loadPending(client: string): void {
    this.http.get<unknown>(`/api/ui/accounting/clients/${client}/open-item-allocation-submissions`).pipe(timeout(15000)).subscribe({
      next: value => { try { this.pending.set(records(value) as unknown as PendingPreview[]); } catch { this.error.set('Pending allocations could not be read.'); } this.busy.set(false); },
      error: () => { this.pending.set([]); this.busy.set(false); }
    });
  }
  previewAllocation(): void {
    const source = this.credits().find(x => x.openItemId === this.sourceId), target = this.targets().find(x => x.openItemId === this.targetId);
    if (!source || !target || !decimal.test(this.amount) || Number(this.amount) <= 0 || !this.reference.trim() || !this.reason.trim()) return;
    this.busy.set(true); this.error.set('');
    const body = { commandId: crypto.randomUUID(), sourceKind: source.kind, sourceItemId: source.openItemId, disposition: 'ALLOCATE',
      reference: this.reference.trim(), reason: this.reason.trim(), lines: [{ lineNumber: 1, targetKind: target.kind, targetOpenItemId: target.openItemId, amount: this.amount, reversesAllocationLineId: null }], previewDigest: '' };
    this.http.post<Preview>(`/api/ui/accounting/clients/${this.clientId()}/open-item-allocations/preview`, body).pipe(timeout(15000)).subscribe({
      next: result => { this.preview.set(result); this.busy.set(false); }, error: () => { this.error.set('The allocation could not be previewed. Check that the source and invoice still have available balances.'); this.busy.set(false); }
    });
  }
  submitAllocation(): void {
    const p = this.preview(); if (!p) return;
    this.busy.set(true); this.error.set('');
    const body = { commandId: crypto.randomUUID(), sourceKind: p.sourceKind, sourceItemId: p.sourceItemId, disposition: p.disposition,
      reference: this.reference.trim(), reason: this.reason.trim(), lines: p.lines, previewDigest: p.digest, reviewed: this.reviewed };
    this.http.post<{ submissionId: string }>(`/api/ui/accounting/clients/${this.clientId()}/open-item-allocations/submit`, body).pipe(timeout(15000)).subscribe({
      next: result => { this.receipt.set(result.submissionId); this.preview.set(null); this.busy.set(false); this.load(); }, error: () => { this.error.set('Submission outcome is unconfirmed. Refresh balances and check whether this request is pending before retrying.'); this.busy.set(false); }
    });
  }
  review(p: PendingPreview, decision: 'APPROVE' | 'RETURN'): void {
    this.busy.set(true); this.error.set('');
    const body = { commandId: crypto.randomUUID(), submissionId: p.submissionId, decision, reason: this.reviewReason[p.submissionId].trim(), previewDigest: p.digest, reviewed: true };
    this.http.post(`/api/ui/accounting/clients/${this.clientId()}/open-item-allocation-reviews`, body).pipe(timeout(15000)).subscribe({
      next: () => this.load(), error: () => { this.error.set('The review outcome is unconfirmed. Refresh pending requests before retrying.'); this.busy.set(false); }
    });
  }
}
