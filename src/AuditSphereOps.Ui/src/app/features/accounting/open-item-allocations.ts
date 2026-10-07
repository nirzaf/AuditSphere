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
interface SettlementOptions { periods: { id: string; code: string; startDate: string; endDate: string; currency: string }[]; counterparties: { id: string; role: string; displayName: string }[]; cashAccounts: { accountCode: string; accountName: string }[] }
interface SettlementPreview { digest: string; sourceKind: string; amount: string; currency: string; controlAccountCode: string; cashAccountCode: string; reference: string; evidenceReference: string }
interface ReconciliationRow { role: string; accountId: string; accountCode: string; accountName: string; ledgerBalance: string; openItemBalance: string; difference: string; openItemCount: number; status: string }
interface Reconciliation { clientId: string; periodId: string; periodCode: string; currency: string; asOfDate: string; ledgerBasis: string; allocationBasis: string; openingDetailStatus: string; unlinkedOpenItemCount: number; accounts: ReconciliationRow[]; reconciled: boolean }
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
    <p>Balances are derived from posted client invoices, credits, and reviewed settlements. Recording a settlement creates a client ledger journal; it does not initiate or verify a bank payment.</p>
    <label>Ageing as of <input type="date" [(ngModel)]="asOfDate" name="asOfDate" /></label>
    <button matButton type="button" [disabled]="busy()" (click)="load()">Refresh balances</button>
    @if (reconciliation(); as recon) { <section aria-label="Receivables and payables control reconciliation"><h4>AR/AP control reconciliation · {{ recon.periodCode }} · {{ recon.asOfDate }}</h4>
      <p>Ledger basis: posted native client journals through the selected date. Current approved allocations are included because they have no separate effective date. Opening item detail is not included in this reconciliation.</p>
      @if (recon.unlinkedOpenItemCount) { <p role="alert">{{ recon.unlinkedOpenItemCount }} open item(s) could not be linked to one approved control-account line.</p> }
      <div class="table-scroll"><table><caption>AR/AP control balances and open item totals</caption><thead><tr><th>Role</th><th>Control account</th><th>Ledger</th><th>Open items</th><th>Difference</th><th>Status</th></tr></thead><tbody>
        @for (row of recon.accounts; track row.accountId) { <tr><td>{{ row.role }}</td><td>{{ row.accountCode }} · {{ row.accountName }}</td><td>{{ row.ledgerBalance }} {{ recon.currency }}</td><td>{{ row.openItemBalance }} {{ recon.currency }} ({{ row.openItemCount }})</td><td>{{ row.difference }}</td><td>{{ row.status }}</td></tr> }
        @empty { <tr><td colspan="6">No approved AR/AP control-account activity is available.</td></tr> }
      </tbody></table></div><p role="status">{{ recon.reconciled ? 'AR/AP balances reconcile.' : 'AR/AP balances do not reconcile.' }}</p>
    </section> }
    @if (error()) { <p role="alert">{{ error() }}</p> }
    @if (busy()) { <p role="status">Updating client balances…</p> }
    <div class="table-scroll"><table><caption>Open client items and as-of ageing</caption><thead><tr><th>Type</th><th>Party</th><th>Document</th><th>Due</th><th>Currency</th><th>Original</th><th>Applied</th><th>Open</th><th>Status</th></tr></thead><tbody>
      @for (row of balances(); track row.openItemId) { <tr><td>{{ row.kind.replaceAll('_', ' ') }}</td><td>{{ row.counterpartyName }}</td><td>{{ row.sourceDocumentId }}</td><td>{{ row.dueDate ?? '—' }}</td><td>{{ row.currency }}</td><td>{{ row.originalAmount }}</td><td>{{ row.appliedAmount }}</td><td>{{ row.openAmount }}</td><td>{{ row.status }}</td></tr> }
      @empty { <tr><td colspan="9">No posted open items are available for this client and date.</td></tr> }
    </tbody></table></div>
    <section aria-label="Record client settlement">
      <h4>Record a settlement already made</h4>
      <p>Use this for a receipt or supplier payment completed outside AuditSphere. The journal will follow independent submission, review, and posting.</p>
      <label>Type <select name="settlementKind" [(ngModel)]="settlementKind" (ngModelChange)="settlementChanged(); loadSettlementOptions()"><option value="SALES_RECEIPT">Customer receipt</option><option value="SUPPLIER_PAYMENT">Supplier payment</option></select></label>
      <label>Posting date <input type="date" name="settlementDate" [(ngModel)]="settlementDate" (ngModelChange)="settlementChanged(); loadSettlementOptions()" /></label>
      <label>Period <select name="settlementPeriod" [(ngModel)]="settlementPeriodId" (ngModelChange)="settlementChanged()" required><option value="">Choose an open period</option>@for (period of settlementOptions()?.periods ?? []; track period.id) { <option [value]="period.id">{{ period.code }} · {{ period.currency }}</option> }</select></label>
      <label>Customer or supplier <select name="settlementParty" [(ngModel)]="settlementPartyId" (ngModelChange)="settlementChanged()" required><option value="">Choose a party</option>@for (party of settlementOptions()?.counterparties ?? []; track party.id) { <option [value]="party.id">{{ party.displayName }}</option> }</select></label>
      <label>Cash or bank ledger account <select name="settlementCash" [(ngModel)]="settlementCashCode" (ngModelChange)="settlementChanged()" required><option value="">Choose an asset account</option>@for (account of settlementOptions()?.cashAccounts ?? []; track account.accountCode) { <option [value]="account.accountCode">{{ account.accountCode }} · {{ account.accountName }}</option> }</select></label>
      <label>Amount <input name="settlementAmount" [(ngModel)]="settlementAmount" (ngModelChange)="settlementChanged()" required inputmode="decimal" pattern="(?:0|[1-9][0-9]{0,12})(?:\.[0-9]{1,6})?" /></label>
      <label>External reference <input name="settlementReference" [(ngModel)]="settlementReference" (ngModelChange)="settlementChanged()" required maxlength="200" /></label>
      <label>Evidence reference <input name="settlementEvidence" [(ngModel)]="settlementEvidence" (ngModelChange)="settlementChanged()" required maxlength="1000" /></label>
      <label>Journal number <input name="settlementNumber" [(ngModel)]="settlementNumber" (ngModelChange)="settlementChanged()" required maxlength="100" /></label>
      <label>Description <input name="settlementDescription" [(ngModel)]="settlementDescription" (ngModelChange)="settlementChanged()" required maxlength="1000" /></label>
      <label><input name="settlementReviewed" type="checkbox" [(ngModel)]="settlementReviewed" /> I reviewed the party, amount, date, reference, and evidence.</label>
      <button matButton type="button" [disabled]="busy() || !settlementValid() || !settlementReviewed" (click)="previewSettlement()">Preview settlement journal</button>
      @if (settlementPreview(); as p) { <p role="status">{{ p.sourceKind.replaceAll('_', ' ') }} · {{ p.amount }} {{ p.currency }} · @if (p.sourceKind === 'SALES_RECEIPT') { Dr {{ p.cashAccountCode }} / Cr {{ p.controlAccountCode }} } @else { Dr {{ p.controlAccountCode }} / Cr {{ p.cashAccountCode }} } · reference {{ p.reference }}.</p>
        <button matButton type="button" [disabled]="busy() || !settlementReviewed" (click)="createSettlement()">Create draft for independent review</button> }
      @if (settlementReceipt()) { <p role="status">Settlement draft {{ settlementReceipt() }} created. Continue in Client operational journals to submit it for independent review and posting.</p> }
    </section>
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
  readonly periods = input<{ id: string; code: string; startDate: string; endDate: string; currency: string }[]>([]);
  readonly balances = signal<OpenItem[]>([]); readonly pending = signal<PendingPreview[]>([]); readonly preview = signal<Preview | null>(null);
  readonly reconciliation = signal<Reconciliation | null>(null);
  readonly busy = signal(false); readonly error = signal(''); readonly receipt = signal('');
  readonly settlementOptions = signal<SettlementOptions | null>(null); readonly settlementPreview = signal<SettlementPreview | null>(null); readonly settlementReceipt = signal('');
  asOfDate = new Date().toISOString().slice(0, 10); sourceId = ''; targetId = ''; amount = ''; reference = ''; reason = ''; reviewed = false;
  settlementKind = 'SALES_RECEIPT'; settlementDate = new Date().toISOString().slice(0, 10); settlementPeriodId = ''; settlementPartyId = ''; settlementCashCode = '';
  settlementAmount = ''; settlementReference = ''; settlementEvidence = ''; settlementNumber = ''; settlementDescription = ''; settlementReviewed = false;
  private settlementCommandId = crypto.randomUUID();
  readonly reviewReason: Record<string, string> = {};
  private readonly http = inject(HttpClient); private readonly destroyRef = inject(DestroyRef); private operation?: Subscription;
  constructor() {
    effect(() => { const client = this.clientId(); if (client) untracked(() => { this.load(); this.loadSettlementOptions(); }); });
    this.destroyRef.onDestroy(() => this.operation?.unsubscribe());
  }
  credits(): OpenItem[] { return this.balances().filter(x => ['SALES_CREDIT', 'PURCHASE_CREDIT', 'SALES_RECEIPT', 'SUPPLIER_PAYMENT'].includes(x.kind) && x.openAmount !== '0.000000'); }
  targets(): OpenItem[] { const source = this.credits().find(x => x.openItemId === this.sourceId); return this.balances().filter(x => x.kind.endsWith('_INVOICE') && x.openAmount !== '0.000000' && (!source || x.counterpartyId === source.counterpartyId && x.currency === source.currency && x.kind === (['SALES_CREDIT', 'SALES_RECEIPT'].includes(source.kind) ? 'SALES_INVOICE' : 'PURCHASE_INVOICE'))); }
  selectedParty(): string { return this.credits().find(x => x.openItemId === this.sourceId)?.counterpartyName ?? ''; }
  load(): void {
    const client = this.clientId(); if (!guidPattern.test(client)) return;
    this.busy.set(true); this.error.set(''); this.preview.set(null); this.receipt.set(''); this.operation?.unsubscribe();
    this.operation = this.http.get<unknown>(`/api/ui/accounting/clients/${client}/open-item-balances`, { params: { asOf: this.asOfDate } }).pipe(timeout(15000)).subscribe({
      next: value => { try { this.balances.set(decodeBalances(value)); this.loadReconciliation(client); this.loadPending(client); } catch { this.busy.set(false); this.error.set('The client balance response was not valid. Refresh the client and review persisted postings.'); } },
      error: () => { this.busy.set(false); this.error.set('Client balances could not be loaded.'); }
    });
  }
  private loadReconciliation(client: string): void {
    const period = this.periods().find(x => x.currency === this.balances()[0]?.currency && this.asOfDate >= x.startDate && this.asOfDate <= x.endDate) ??
      this.periods().find(x => this.asOfDate >= x.startDate && this.asOfDate <= x.endDate);
    if (!period) { this.reconciliation.set(null); return; }
    this.http.get<Reconciliation>(`/api/ui/accounting/clients/${client}/open-item-control-reconciliation`, { params: { periodId: period.id, asOf: this.asOfDate } }).pipe(timeout(15000)).subscribe({
      next: value => this.reconciliation.set(value), error: () => this.reconciliation.set(null)
    });
  }
  private loadPending(client: string): void {
    this.http.get<unknown>(`/api/ui/accounting/clients/${client}/open-item-allocation-submissions`).pipe(timeout(15000)).subscribe({
      next: value => { try { this.pending.set(records(value) as unknown as PendingPreview[]); } catch { this.error.set('Pending allocations could not be read.'); } this.busy.set(false); },
      error: () => { this.pending.set([]); this.busy.set(false); }
    });
  }
  loadSettlementOptions(): void {
    const client = this.clientId(); if (!guidPattern.test(client) || !/^\d{4}-\d{2}-\d{2}$/.test(this.settlementDate)) return;
    this.http.get<SettlementOptions>(`/api/ui/accounting/clients/${client}/manual-settlement-options`, { params: { postingDate: this.settlementDate, sourceKind: this.settlementKind } }).pipe(timeout(15000)).subscribe({
      next: options => { this.settlementOptions.set(options); if (!options.periods.some(x => x.id === this.settlementPeriodId)) this.settlementPeriodId = options.periods[0]?.id ?? ''; if (!options.counterparties.some(x => x.id === this.settlementPartyId)) this.settlementPartyId = ''; if (!options.cashAccounts.some(x => x.accountCode === this.settlementCashCode)) this.settlementCashCode = options.cashAccounts[0]?.accountCode ?? ''; },
      error: () => { this.settlementOptions.set(null); this.error.set('Manual settlement options could not be loaded for this client and date.'); }
    });
  }
  settlementValid(): boolean { return !!this.settlementPeriodId && !!this.settlementPartyId && !!this.settlementCashCode && decimal.test(this.settlementAmount) && Number(this.settlementAmount) > 0 && !!this.settlementReference.trim() && !!this.settlementEvidence.trim() && !!this.settlementNumber.trim() && !!this.settlementDescription.trim(); }
  settlementChanged(): void { this.settlementPreview.set(null); this.settlementReceipt.set(''); this.settlementReviewed = false; this.settlementCommandId = crypto.randomUUID(); }
  private settlementBody(digest = ''): Record<string, unknown> { return { commandId: this.settlementCommandId, periodId: this.settlementPeriodId, counterpartyId: this.settlementPartyId, sourceKind: this.settlementKind, journalNumber: this.settlementNumber.trim(), description: this.settlementDescription.trim(), postingDate: this.settlementDate, cashAccountCode: this.settlementCashCode, amount: this.settlementAmount, reference: this.settlementReference.trim(), evidenceReference: this.settlementEvidence.trim(), previewDigest: digest, reviewed: this.settlementReviewed }; }
  previewSettlement(): void {
    if (!this.settlementValid() || !this.settlementReviewed) return; this.busy.set(true); this.error.set(''); this.settlementReceipt.set('');
    this.http.post<SettlementPreview>(`/api/ui/accounting/clients/${this.clientId()}/manual-settlements/preview`, this.settlementBody()).pipe(timeout(15000)).subscribe({
      next: value => { this.settlementPreview.set(value); this.busy.set(false); }, error: () => { this.error.set('Settlement preview failed. Confirm the open period, approved AR/AP control, active party, and cash account.'); this.busy.set(false); }
    });
  }
  createSettlement(): void {
    const preview = this.settlementPreview(); if (!preview || !this.settlementReviewed) return; this.busy.set(true); this.error.set('');
    this.http.post<{ journalId: string }>(`/api/ui/accounting/clients/${this.clientId()}/manual-settlements`, this.settlementBody(preview.digest)).pipe(timeout(15000)).subscribe({
      next: result => { this.settlementReceipt.set(result.journalId); this.settlementPreview.set(null); this.settlementCommandId = crypto.randomUUID(); this.busy.set(false); this.load(); },
      error: () => { this.error.set('Settlement outcome is unconfirmed. Check client operational journals before retrying.'); this.busy.set(false); }
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
