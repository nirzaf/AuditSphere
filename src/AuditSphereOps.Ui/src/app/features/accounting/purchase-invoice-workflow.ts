import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import { guidPattern } from '../../core/contracts';
import { decodeCounterparties } from './counterparties-contract';

interface Period { id: string; code: string; status: string }
interface PurchaseLineInput { accountCode: string; description: string; quantity: string; unitPrice: string; discount: string }
interface PurchaseDraft { id: string; invoiceId: string; clientId: string; periodId: string; supplierId: string; voucherReference: string; supplierInvoiceReference: string; revision: string; snapshotHash: string; createdByUserId: string; state: string;
  snapshot: { version: string; profileId: string; chartVersionId: string; supplierId: string; supplierName: string; voucherReference: string; supplierInvoiceReference: string; normalizedSupplierReference: string; currency: string; receiptDate: string; documentDate: string; accountingDate: string; supplyDate: string; dueDate: string; policy: { currency: string; decimalPlaces: number; midpointRule: string; residualTreatment: string; maximumResidualMinorUnits: number; roundingAccountCode: string }; lines: { lineNumber: number; accountCode: string; accountName: string; description: string; quantity: string; unitPrice: string; discount: string; net: string; tax: string; gross: string }[]; net: string; tax: string; gross: string; statedNet: string; statedTax: string; statedGross: string; lateArrival: boolean; possibleDuplicateCount: number; evidenceReference: string } }
interface PurchasePreview { invoiceId: string; revision: string; currency: string; net: string; tax: string; gross: string; digest: string; lateArrival: boolean; duplicateWarnings: string[]; manifest: { lines: { lineNumber: number; accountCode: string; accountName: string; description: string; debit: string; credit: string }[] } }
interface PurchaseReview { invoiceId: string; submissionId: string; journalId: string; journalRevision: string; digest: string; canPost: boolean; postingBlock: string | null; duplicateWarning: boolean; manifest: { supplierInvoiceReference: string; voucherReference: string; lateArrival: boolean; duplicateWarnings: string[]; lines: { lineNumber: number; accountCode: string; accountName: string; description: string; debit: string; credit: string }[] } }
interface PurchaseRow { invoiceId: string; supplierId: string; supplierInvoiceReference: string; voucherReference: string; state: string; currency: string; gross: string; dueDate: string; receiptDate: string; lateArrival: boolean; duplicateWarning: boolean; submissionId: string; journalId: string | null; makerId: string; decision: string | null; decisionReason: string | null; duplicateResolutionReason: string | null; openItemCreated: boolean }
const guid = (value: unknown): value is string => typeof value === 'string' && guidPattern.test(value);
const datePattern = /^\d{4}-\d{2}-\d{2}$/;
const amountPattern = /^(?:0|[1-9]\d{0,12})(?:\.\d{1,6})?$/;

@Component({ selector: 'audit-client-purchase-invoices', imports: [FormsModule, MatButtonModule], template: `<section aria-label="Client purchase invoices">
  <h4>Client supplier invoices</h4>
  <p>Capture the supplier document against this client’s own expense or asset accounts and AP control. Untaxed purchases work without the optional tax module; tax-bearing purchases remain separately gated.</p>
  <button matButton type="button" [disabled]="busy()" (click)="refresh()">Refresh supplier invoice history</button>
  @if (error()) { <p role="alert">{{ error() }}</p> }
  @if (!draft()) {
    <form #form="ngForm" (ngSubmit)="form.valid && saveDraft()"><fieldset [disabled]="busy()"><legend>Capture supplier document</legend>
      <button matButton type="button" [disabled]="busy()" (click)="loadSuppliers()">Load client suppliers</button>
      <label>Supplier <select name="supplier" [(ngModel)]="supplierId" required><option value="">Choose supplier</option>@for (supplier of suppliers(); track supplier.id) { <option [value]="supplier.id">{{ supplier.displayName }}</option> }</select></label>
      <label>Internal voucher reference <input name="voucher" [(ngModel)]="voucher" required maxlength="100" /></label>
      <label>Supplier invoice number <input name="supplierInvoice" [(ngModel)]="supplierReference" required maxlength="200" /></label>
      <label>Posting period <select name="period" [(ngModel)]="periodId" required><option value="">Choose period</option>@for (period of periods(); track period.id) { <option [value]="period.id" [disabled]="period.status !== 'OPEN'">{{ period.code }} · {{ period.status }}</option> }</select></label>
      <label>Receipt date <input name="receiptDate" type="date" [(ngModel)]="receiptDate" required /></label>
      <label>Supplier document date <input name="documentDate" type="date" [(ngModel)]="documentDate" required /></label>
      <label>Accounting date <input name="accountingDate" type="date" [(ngModel)]="accountingDate" required /></label>
      <label>Supply / tax date <input name="supplyDate" type="date" [(ngModel)]="supplyDate" required /></label>
      <label>Due date <input name="dueDate" type="date" [(ngModel)]="dueDate" required /></label>
      <label>Approved AP role ID <input name="apRole" [(ngModel)]="apRoleId" required [pattern]="guidPattern.source" maxlength="36" /></label>
      <label>Supplier evidence reference <textarea name="evidence" [(ngModel)]="evidence" required maxlength="2000"></textarea></label>
      <fieldset><legend>Purchase lines</legend>@for (line of lines; track $index) { <div>
        <label>Expense or asset account code <input [name]="'account-' + $index" [(ngModel)]="line.accountCode" required maxlength="100" /></label>
        <label>Description <input [name]="'description-' + $index" [(ngModel)]="line.description" required maxlength="2000" /></label>
        <label>Quantity <input [name]="'quantity-' + $index" inputmode="decimal" [(ngModel)]="line.quantity" required /></label>
        <label>Unit price <input [name]="'price-' + $index" inputmode="decimal" [(ngModel)]="line.unitPrice" required /></label>
        <label>Discount <input [name]="'discount-' + $index" inputmode="decimal" [(ngModel)]="line.discount" required /></label>
      </div> }
      <button matButton type="button" [disabled]="lines.length >= 99" (click)="addLine()">Add purchase line</button></fieldset>
      <label>Supplier-stated net <input name="statedNet" inputmode="decimal" [(ngModel)]="statedNet" required /></label>
      <label>Supplier-stated tax <input name="statedTax" inputmode="decimal" [(ngModel)]="statedTax" required /></label>
      <label>Supplier-stated gross <input name="statedGross" inputmode="decimal" [(ngModel)]="statedGross" required /></label>
      <p>Totals must match the exact untaxed calculation. Unsupported tax treatment or a material discrepancy blocks posting. Receipt and document dates remain separate, including late-arriving evidence.</p>
      <label><input type="checkbox" [ngModel]="draftAssent()" (ngModelChange)="draftAssent.set($event)" name="draftAssent" /> I checked this supplier, client, document dates, coding, supplier-stated totals and evidence reference.</label>
      <button matButton type="submit" [disabled]="!draftAssent() || busy() || form.invalid">Save client purchase draft</button>
    </fieldset></form>
  } @else {
    <article><h5>{{ draft()?.voucherReference }} · {{ draft()?.supplierInvoiceReference }}</h5>
      <p>{{ draft()?.snapshot.supplierName }} · Draft revision {{ draft()?.revision }} · {{ draft()?.snapshot.gross }} {{ currency() }}</p>
      <p>Receipt {{ draft()?.snapshot.receiptDate }} · Document {{ draft()?.snapshot.documentDate }} · Accounting {{ draft()?.snapshot.accountingDate }} · Supply {{ draft()?.snapshot.supplyDate }} · Due {{ draft()?.snapshot.dueDate }}</p>
      @if (draft()?.snapshot.lateArrival) { <p role="status">Late-arriving supplier document: receipt date is after the supplier document date.</p> }
      @if (draft()?.snapshot.possibleDuplicateCount) { <p role="status">Possible same-supplier invoice duplicate warning. A reviewer must record a reason to resolve a legitimate duplicate.</p> }
      <table><caption>Captured client purchase lines</caption><thead><tr><th>Account</th><th>Description</th><th>Net</th><th>Tax</th><th>Gross</th></tr></thead><tbody>@for (line of draft()?.snapshot.lines ?? []; track line.lineNumber) { <tr><td>{{ line.accountCode }} · {{ line.accountName }}</td><td>{{ line.description }}</td><td>{{ line.net }}</td><td>{{ line.tax }}</td><td>{{ line.gross }}</td></tr> }</tbody></table>
      <button matButton type="button" [disabled]="busy()" (click)="previewDraft()">Preview AP posting</button>
      @if (preview(); as p) { <p>Exact server preview: {{ p.net }} net + {{ p.tax }} tax = {{ p.gross }} {{ p.currency }}. Late arrival: {{ p.lateArrival ? 'Yes' : 'No' }}.</p>
        @for (warning of p.duplicateWarnings; track warning) { <p role="alert">Possible duplicate: {{ warning }}</p> }
        <table><caption>Proposed client GL journal</caption><thead><tr><th>Account</th><th>Description</th><th>Debit</th><th>Credit</th></tr></thead><tbody>@for (line of p.manifest.lines; track line.lineNumber) { <tr><td>{{ line.accountCode }} · {{ line.accountName }}</td><td>{{ line.description }}</td><td>{{ line.debit }}</td><td>{{ line.credit }}</td></tr> }</tbody></table>
        <label><input type="checkbox" [ngModel]="submitAssent()" (ngModelChange)="submitAssent.set($event)" /> I reviewed this client’s supplier, evidence, late-arrival dates, exact totals, duplicate warnings and AP posting.</label>
        <button matButton type="button" [disabled]="busy() || !submitAssent()" (click)="submit()">Submit supplier invoice for independent review</button>
      }
    </article>
  }
  @for (row of rows(); track row.submissionId) { <article><h5>{{ row.voucherReference }} · {{ row.supplierInvoiceReference }} · {{ row.state }}</h5>
    <p>{{ row.gross }} {{ row.currency }} · Receipt {{ row.receiptDate }} · Due {{ row.dueDate }} · Late arrival {{ row.lateArrival ? 'Yes' : 'No' }}</p>
    @if (row.duplicateWarning) { <p role="status">Possible same-supplier invoice duplicate requires a retained independent reviewer rationale before approval.</p> }
    @if (row.state === 'SUBMITTED' && row.makerId !== userId()) { <button matButton type="button" [disabled]="busy()" (click)="reviewPreview(row)">Review supplier invoice</button>
      @if (activeReview()?.submissionId === row.submissionId) { <p>{{ activeReview()?.canPost ? 'Ready for approval' : activeReview()?.postingBlock }}</p>
        <table><caption>Exact submitted supplier posting</caption><thead><tr><th>Account</th><th>Description</th><th>Debit</th><th>Credit</th></tr></thead><tbody>@for (line of activeReview()?.manifest?.lines ?? []; track line.lineNumber) { <tr><td>{{ line.accountCode }} · {{ line.accountName }}</td><td>{{ line.description }}</td><td>{{ line.debit }}</td><td>{{ line.credit }}</td></tr> }</tbody></table>
        <label>Independent review reason <textarea [(ngModel)]="reviewReason" maxlength="2000"></textarea></label>
        @if (activeReview()?.duplicateWarning) { <label>Duplicate resolution rationale <textarea [(ngModel)]="duplicateReason" maxlength="2000" required></textarea></label> }
        <button matButton type="button" [disabled]="busy() || !reviewReason.trim() || !activeReview()?.canPost || activeReview()?.duplicateWarning && !duplicateReason.trim()" (click)="review(row, 'APPROVE')">Approve and post supplier invoice</button>
        <button matButton type="button" [disabled]="busy() || !reviewReason.trim()" (click)="review(row, 'RETURN')">Return purchase draft</button>
      }
    }
    @if (row.openItemCreated) { <p>Posted client AP open item · Due {{ row.dueDate }}. No supplier payment is initiated here.</p> }
    @if (row.decision) { <p>Decision {{ row.decision }} · {{ row.decisionReason }} {{ row.duplicateResolutionReason }}</p> }
  </article> }
</section>` })
export class PurchaseInvoiceWorkflow {
  readonly clientId = input.required<string>(); readonly currency = input.required<string>(); readonly periods = input.required<Period[]>();
  private readonly http = inject(HttpClient); private readonly session = inject(SessionService);
  readonly busy = signal(false); readonly error = signal(''); readonly suppliers = signal<ReturnType<typeof decodeCounterparties>['counterparties'] | null>(null);
  readonly draft = signal<PurchaseDraft | null>(null); readonly preview = signal<PurchasePreview | null>(null); readonly rows = signal<PurchaseRow[]>([]); readonly activeReview = signal<PurchaseReview | null>(null);
  readonly draftAssent = signal(false); readonly submitAssent = signal(false); readonly userId = () => this.session.current()?.userId ?? ''; readonly guidPattern = guidPattern;
  periodId = ''; supplierId = ''; voucher = ''; supplierReference = ''; apRoleId = ''; evidence = ''; receiptDate = new Date().toISOString().slice(0, 10);
  documentDate = ''; accountingDate = ''; supplyDate = ''; dueDate = ''; precision = '2'; midpoint = 'AWAY_FROM_ZERO';
  statedNet = ''; statedTax = '0'; statedGross = ''; lines: PurchaseLineInput[] = [{ accountCode: '', description: '', quantity: '1', unitPrice: '', discount: '0' }];
  reviewReason = ''; duplicateReason = ''; private requestId = 0; private operation?: Subscription; private previewRequest: Record<string, unknown> | null = null;
  constructor() { effect(() => { this.clientId(); this.currency(); this.periods(); this.session.invalidation(); untracked(() => { this.operation?.unsubscribe(); ++this.requestId; this.busy.set(false); this.draft.set(null); this.preview.set(null); this.rows.set([]); this.activeReview.set(null); this.reset(); this.loadSuppliers(); this.refresh(); }); }); inject(DestroyRef).onDestroy(() => this.operation?.unsubscribe()); }
  private base(): string { return `/api/ui/accounting/clients/${this.clientId()}`; }
  reset(): void { this.periodId = ''; this.supplierId = ''; this.voucher = ''; this.supplierReference = ''; this.apRoleId = ''; this.evidence = ''; this.documentDate = ''; this.accountingDate = ''; this.supplyDate = ''; this.dueDate = ''; this.statedNet = ''; this.statedTax = '0'; this.statedGross = ''; this.lines = [{ accountCode: '', description: '', quantity: '1', unitPrice: '', discount: '0' }]; this.draftAssent.set(false); this.submitAssent.set(false); }
  addLine(): void { if (this.lines.length < 99) this.lines.push({ accountCode: '', description: '', quantity: '1', unitPrice: '', discount: '0' }); }
  loadSuppliers(): void { const request = ++this.requestId, generation = this.session.invalidation(), client = this.clientId(); this.busy.set(true);
    this.operation = this.http.get<unknown>(`${this.base()}/counterparties`, { params: { role: 'SUPPLIER', page: '0', pageSize: '25' } }).pipe(timeout(15000)).subscribe({ next: value => { if (request !== this.requestId || generation !== this.session.invalidation() || client !== this.clientId()) return; this.busy.set(false); try { this.suppliers.set(decodeCounterparties(value, client, 'SUPPLIER', 0).counterparties); this.refresh(); } catch { this.error.set('Supplier list could not be validated.'); } }, error: () => { if (request === this.requestId) { this.busy.set(false); this.error.set('Client suppliers are unavailable.'); } } }); }
  saveDraft(): void { if (this.busy() || !this.draftAssent() || !guid(this.periodId) || !guid(this.supplierId) || !guid(this.apRoleId) || !datePattern.test(this.receiptDate) || !datePattern.test(this.documentDate) || !datePattern.test(this.accountingDate) || !datePattern.test(this.supplyDate) || !datePattern.test(this.dueDate) || this.lines.length < 1 || this.lines.length > 99 || this.lines.some(l => !amountPattern.test(l.quantity) || !amountPattern.test(l.unitPrice) || !amountPattern.test(l.discount))) return;
    const body = { commandId: crypto.randomUUID(), invoiceId: null, expectedRevision: 0, periodId: this.periodId, supplierId: this.supplierId, voucherReference: this.voucher.trim(), supplierInvoiceReference: this.supplierReference.trim(), receiptDate: this.receiptDate, documentDate: this.documentDate, accountingDate: this.accountingDate, supplyDate: this.supplyDate, dueDate: this.dueDate, currency: this.currency(), policy: { currency: this.currency(), decimalPlaces: Number(this.precision), midpointRule: this.midpoint, residualTreatment: 'REJECT', maximumResidualMinorUnits: 0, roundingAccountCode: '' }, lines: this.lines.map(l => ({ description: l.description.trim(), accountCode: l.accountCode.trim(), quantity: l.quantity, unitPrice: l.unitPrice, discount: l.discount, taxTreatment: 'NONE', taxes: [] })), statedNet: this.statedNet, statedTax: this.statedTax, statedGross: this.statedGross, sourceReceiptId: null, evidenceReference: this.evidence.trim() };
    this.busy.set(true); this.error.set(''); this.operation = this.http.post<PurchaseDraft>(`${this.base()}/purchase-invoice-drafts`, body).pipe(timeout(20000)).subscribe({ next: draft => { this.busy.set(false); this.draft.set(draft); this.refresh(); }, error: () => { this.busy.set(false); this.error.set('Purchase draft could not be confirmed. Refresh supplier invoice history before retrying.'); this.refresh(); } }); }
  private submitBody(): Record<string, unknown> | null { const d = this.draft(); if (!d || !guid(this.apRoleId)) return null; return { commandId: crypto.randomUUID(), expectedDraftRevision: d.revision, payableRoleId: this.apRoleId, previewDigest: '' }; }
  previewDraft(): void { const d = this.draft(), body = this.submitBody(); if (!d || !body || this.busy()) return; this.busy.set(true); this.previewRequest = body;
    this.operation = this.http.post<PurchasePreview>(`${this.base()}/purchase-invoices/${d.invoiceId}/preview`, body).pipe(timeout(20000)).subscribe({ next: p => { this.busy.set(false); this.preview.set(p); this.submitAssent.set(false); }, error: () => { this.busy.set(false); this.error.set('Purchase posting preview failed. Refresh the draft and current AP role.'); this.previewRequest = null; } }); }
  submit(): void { const d = this.draft(), p = this.preview(), b = this.previewRequest; if (!d || !p || !b || this.busy() || !this.submitAssent()) return; this.busy.set(true);
    this.operation = this.http.post(`${this.base()}/purchase-invoices/${d.invoiceId}/submit`, { ...b, previewDigest: p.digest, reviewed: true }).pipe(timeout(20000)).subscribe({ next: () => { this.busy.set(false); this.draft.set(null); this.preview.set(null); this.previewRequest = null; this.reset(); this.refresh(); }, error: () => { this.busy.set(false); this.error.set('Submission outcome is unconfirmed. Refresh supplier invoice history before another action.'); this.refresh(); } }); }
  refresh(): void { if (this.busy()) return; const request = ++this.requestId, generation = this.session.invalidation(), client = this.clientId(); this.busy.set(true);
    this.operation = this.http.get<PurchaseRow[]>(`${this.base()}/purchase-invoices`).pipe(timeout(15000)).subscribe({ next: rows => { if (request !== this.requestId || generation !== this.session.invalidation() || client !== this.clientId()) return; this.rows.set(rows); this.busy.set(false); }, error: () => { if (request === this.requestId) { this.busy.set(false); this.error.set('Supplier invoice history is unavailable.'); } } }); }
  reviewPreview(row: PurchaseRow): void { if (this.busy()) return; this.busy.set(true); const request = ++this.requestId, generation = this.session.invalidation();
    this.operation = this.http.get<PurchaseReview>(`${this.base()}/purchase-invoice-submissions/${row.submissionId}/preview`).pipe(timeout(15000)).subscribe({ next: preview => { if (request !== this.requestId || generation !== this.session.invalidation()) return; this.activeReview.set(preview); this.reviewReason = ''; this.duplicateReason = ''; this.busy.set(false); }, error: () => { if (request === this.requestId) { this.busy.set(false); this.error.set('Purchase invoice review preview is unavailable.'); } } }); }
  review(row: PurchaseRow, decision: 'APPROVE' | 'RETURN'): void { const p = this.activeReview(); if (!p || this.busy()) return; this.busy.set(true);
    this.operation = this.http.post(`${this.base()}/purchase-invoice-reviews`, { commandId: crypto.randomUUID(), submissionId: row.submissionId, decision, reason: this.reviewReason.trim(), duplicateResolutionReason: this.duplicateReason.trim(), previewDigest: p.digest, reviewed: true }).pipe(timeout(20000)).subscribe({ next: () => { this.activeReview.set(null); this.busy.set(false); this.refresh(); }, error: () => { this.busy.set(false); this.error.set('Review outcome is unconfirmed. Refresh supplier invoice history before another action.'); this.activeReview.set(null); this.refresh(); } }); }
}
