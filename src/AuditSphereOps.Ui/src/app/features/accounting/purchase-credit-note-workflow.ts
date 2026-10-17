import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import { guidPattern } from '../../core/contracts';
import { decodeCounterparties } from './counterparties-contract';

interface Period { id: string; code: string; status: string }
interface Supplier { id: string; displayName: string; role: string }
interface PurchaseRow { invoiceId: string; supplierId: string; supplierInvoiceReference: string; state: string; currency: string; gross: string }
interface CreditLine { originalLineNumber: string; accountCode: string; amount: string }
interface CreditManifestLine { lineNumber: number; originalLineNumber: number; accountCode: string; accountName: string; description: string; debit: string; credit: string; creditAmount: string }
interface CreditPreview { creditNoteId: string; originalInvoiceId: string | null; currency: string; totalCredit: string; digest: string; originalAmount: string; previouslyCredited: string; remainingCreditLimit: string; duplicateWarnings: string[]; manifest: { lines: CreditManifestLine[] } }
interface CreditReview { submissionId: string; digest: string; canPost: boolean; postingBlock: string | null; duplicateWarning: boolean; manifest: { lines: CreditManifestLine[] } }
interface CreditRow { creditNoteId: string; creditNoteReference: string; originalInvoiceId: string | null; submissionId: string; state: string; currency: string; amount: string; postingDate: string; reason: string; makerId: string; decision: string | null; decisionReason: string | null; duplicateResolutionReason: string | null; unappliedSupplierDebit: boolean }
type CreditCommandKind = 'SUBMIT' | 'REVIEW';
interface CreditCommandReceipt { commandId: string; creditNoteId: string; submissionId: string; kind: CreditCommandKind; actorUserId: string; intentHash: string; outcome: string; decisionId: string | null }
interface PendingCreditCommand { version: 1; clientId: string; actorId: string; kind: CreditCommandKind; commandId: string; body: Record<string, unknown> }
const guid = (value: unknown): value is string => typeof value === 'string' && guidPattern.test(value);
const amountPattern = /^(?:0|[1-9]\d{0,12})(?:\.\d{1,6})?$/;
const pendingCreditKey = (clientId: string) => `auditsphere.purchase-credit-command.v1:${clientId}`;
const isPendingCreditCommand = (value: unknown, clientId: string): value is PendingCreditCommand => {
  if (!value || typeof value !== 'object') return false;
  const pending = value as Partial<PendingCreditCommand>;
  if (pending.version !== 1 || pending.clientId !== clientId || !guid(pending.actorId) || !guid(pending.commandId) ||
      (pending.kind !== 'SUBMIT' && pending.kind !== 'REVIEW') || !pending.body || typeof pending.body !== 'object' ||
      pending.body['commandId'] !== pending.commandId || pending.body['reviewed'] !== true ||
      typeof pending.body['previewDigest'] !== 'string' || !/^[0-9a-f]{64}$/i.test(pending.body['previewDigest'])) return false;
  if (pending.kind === 'SUBMIT') {
    const credit = pending.body['credit'];
    return !!credit && typeof credit === 'object' && guid((credit as { creditNoteId?: unknown }).creditNoteId);
  }
  return guid(pending.body['submissionId']) && (pending.body['decision'] === 'APPROVE' || pending.body['decision'] === 'RETURN') &&
    typeof pending.body['reason'] === 'string' && typeof pending.body['duplicateResolutionReason'] === 'string';
};

@Component({ selector: 'audit-client-purchase-credit-notes', imports: [FormsModule, MatButtonModule], template: `<section aria-label="Client supplier credit notes">
  <h4>Supplier credit notes</h4>
  <p>Record a supplier credit against a posted purchase or document an unlinked exception. Linked credits are limited cumulatively to each original untaxed purchase line. Approval creates an unapplied supplier debit; it never initiates a refund or payment. Tax handling stays optional and separately gated.</p>
  <button matButton type="button" [disabled]="busy()" (click)="refresh()">Refresh supplier credit history</button>
  @if (error()) { <p role="alert">{{ error() }}</p> }
  @if (pendingCommand()) { <article aria-live="polite" aria-label="Unconfirmed supplier credit command">
    <h5>Supplier credit outcome needs reconciliation</h5>
    <p>The original {{ pendingCommand()!.kind === 'SUBMIT' ? 'submission' : 'review or return' }} command is retained under its original command ID. An empty lookup does not prove that it was not accepted. Editing or starting another supplier-credit command remains blocked until a matching retained outcome is acknowledged.</p>
    @if (pendingMatchesContext()) {
      <button matButton type="button" [disabled]="busy()" (click)="checkPendingOutcome()">Check retained outcome</button>
      <button matButton type="button" [disabled]="busy()" (click)="retryPendingCommand()">Retry the exact same command</button>
      @if (recoveredReceipt()) { <p role="status">Matching outcome: {{ recoveredReceipt()!.outcome }}. Command {{ recoveredReceipt()!.commandId }}.</p>
        <button matButton type="button" [disabled]="busy()" (click)="acknowledgePendingOutcome()">Acknowledge outcome and continue</button> }
    } @else { <p role="alert">This command belongs to another signed-in actor. Restore that actor's session to check or retry it.</p> }
  </article> }
  @if (pendingStorageBlocked()) { <p role="alert">A retained supplier-credit command exists but could not be validated. New supplier-credit actions are blocked until its outcome is reconciled.</p> }
  @if (!preview()) {
    <form #form="ngForm" (ngSubmit)="form.valid && previewCredit()"><fieldset [disabled]="busy() || !!pendingCommand() || pendingStorageBlocked()"><legend>Prepare supplier credit</legend>
      <button matButton type="button" (click)="loadSuppliers()">Load client suppliers</button>
      <label>Supplier <select name="supplier" [(ngModel)]="supplierId" required><option value="">Choose supplier</option>@for (supplier of suppliers(); track supplier.id) { <option [value]="supplier.id">{{ supplier.displayName }}</option> }</select></label>
      <label>Original purchase <select name="originalInvoice" [(ngModel)]="originalInvoiceId"><option value="">Unlinked exception</option>@for (invoice of purchases(); track invoice.invoiceId) { <option [value]="invoice.invoiceId" [disabled]="invoice.state !== 'POSTED' || invoice.supplierId !== supplierId">{{ invoice.supplierInvoiceReference }} · {{ invoice.gross }} {{ invoice.currency }}</option> }</select></label>
      <button matButton type="button" (click)="loadPurchases()">Load posted purchases</button>
      <label>Supplier credit reference <input name="reference" [(ngModel)]="reference" required maxlength="200" /></label>
      <label>Posting period <select name="period" [(ngModel)]="periodId" required><option value="">Choose period</option>@for (period of periods(); track period.id) { <option [value]="period.id" [disabled]="period.status !== 'OPEN'">{{ period.code }} · {{ period.status }}</option> }</select></label>
      <label>Posting date <input name="postingDate" type="date" [(ngModel)]="postingDate" required /></label>
      <label>Approved AP role ID <input name="apRole" [(ngModel)]="payableRoleId" required [pattern]="guidPattern.source" maxlength="36" /></label>
      <label>Reason <textarea name="reason" [(ngModel)]="reason" required maxlength="2000"></textarea></label>
      @if (!originalInvoiceId) { <label>Unlinked exception rationale <textarea name="exception" [(ngModel)]="unlinkedRationale" required maxlength="2000"></textarea></label> }
      <label>Source basis / evidence reference <textarea name="sourceBasis" [(ngModel)]="sourceBasis" required maxlength="2000"></textarea></label>
      <fieldset><legend>Credit lines</legend>@for (line of lines; track $index) { <div>
        @if (originalInvoiceId) { <label>Original purchase line number <input [name]="'line-' + $index" type="number" min="1" step="1" [(ngModel)]="line.originalLineNumber" required /></label> }
        <label>Expense or asset account code <input [name]="'account-' + $index" [(ngModel)]="line.accountCode" required maxlength="100" /></label>
        <label>Positive credit amount <input [name]="'amount-' + $index" inputmode="decimal" [(ngModel)]="line.amount" required [pattern]="amountPattern.source" /></label>
      </div> }
      <button matButton type="button" [disabled]="busy() || !!pendingCommand() || pendingStorageBlocked() || lines.length >= 99" (click)="addLine()">Add supplier credit line</button></fieldset>
      <label><input type="checkbox" name="prepareAssent" [(ngModel)]="prepareAssent" /> I checked this client, supplier, original purchase or exception, coding, amount and evidence.</label>
      <button matButton type="submit" [disabled]="busy() || !!pendingCommand() || pendingStorageBlocked() || !prepareAssent || form.invalid">Preview supplier credit</button>
    </fieldset></form>
  } @else {
    <article aria-label="Supplier credit preview"><h5>Exact supplier credit posting preview</h5>
      <p>{{ preview()!.totalCredit }} {{ preview()!.currency }} · Original {{ preview()!.originalAmount }} · Previously credited {{ preview()!.previouslyCredited }} · Remaining line limit {{ preview()!.remainingCreditLimit }}</p>
      @for (warning of preview()!.duplicateWarnings; track warning) { <p role="status">{{ warning }}</p> }
      <table><caption>Balanced client journal lines</caption><thead><tr><th>Account</th><th>Description</th><th>Debit</th><th>Credit</th></tr></thead><tbody>@for (line of preview()!.manifest.lines; track line.lineNumber) { <tr><td>{{ line.accountCode }} · {{ line.accountName }}</td><td>{{ line.description }}</td><td>{{ line.debit }}</td><td>{{ line.credit }}</td></tr> }</tbody></table>
      <label><input type="checkbox" name="submitAssent" [(ngModel)]="submitAssent" /> I reviewed the exact supplier credit posting and its cumulative limit.</label>
      <button matButton type="button" [disabled]="busy() || !!pendingCommand() || pendingStorageBlocked() || !submitAssent" (click)="submitCredit()">Submit for independent review</button>
      <button matButton type="button" [disabled]="busy() || !!pendingCommand() || pendingStorageBlocked()" (click)="preview.set(null); creditNoteId = ''">Edit supplier credit</button>
    </article>
  }
  @for (row of rows(); track row.submissionId) {
    <article><h5>{{ row.creditNoteReference }} · {{ row.state }}</h5><p>{{ row.amount }} {{ row.currency }} · {{ row.originalInvoiceId ? 'Linked purchase' : 'Unlinked exception' }} · {{ row.reason }}</p>
      @if (row.unappliedSupplierDebit) { <p role="status">Approved as an unapplied supplier debit. Cash settlement is not initiated here.</p> }
      @if (row.state === 'SUBMITTED' && row.makerId !== userId()) { <button matButton type="button" [disabled]="busy() || !!pendingCommand() || pendingStorageBlocked()" (click)="reviewPreview(row)">Review supplier credit</button>
        @if (activeReview()?.submissionId === row.submissionId) { <p>{{ activeReview()?.canPost ? 'Ready for approval' : activeReview()?.postingBlock }}</p>
          <table><caption>Exact submitted supplier-credit journal</caption><thead><tr><th>Account</th><th>Description</th><th>Debit</th><th>Credit</th></tr></thead><tbody>@for (line of activeReview()?.manifest?.lines ?? []; track line.lineNumber) { <tr><td>{{ line.accountCode }} · {{ line.accountName }}</td><td>{{ line.description }}</td><td>{{ line.debit }}</td><td>{{ line.credit }}</td></tr> }</tbody></table>
          <label>Independent review reason <textarea [(ngModel)]="reviewReason" maxlength="2000"></textarea></label>
          @if (activeReview()?.duplicateWarning) { <label>Duplicate resolution rationale <textarea [(ngModel)]="duplicateReason" maxlength="2000" required></textarea></label> }
          <button matButton type="button" [disabled]="busy() || !!pendingCommand() || pendingStorageBlocked() || !reviewReason.trim() || !activeReview()?.canPost || activeReview()?.duplicateWarning && !duplicateReason.trim()" (click)="review(row, 'APPROVE')">Approve and post supplier credit</button>
          <button matButton type="button" [disabled]="busy() || !!pendingCommand() || pendingStorageBlocked() || !reviewReason.trim()" (click)="review(row, 'RETURN')">Return supplier credit</button>
        }
      }
    </article>
  }
</section>` })
export class PurchaseCreditNoteWorkflow {
  readonly clientId = input.required<string>(); readonly currency = input.required<string>(); readonly periods = input.required<Period[]>();
  readonly suppliers = signal<Supplier[]>([]); readonly purchases = signal<PurchaseRow[]>([]); readonly rows = signal<CreditRow[]>([]);
  readonly preview = signal<CreditPreview | null>(null); readonly activeReview = signal<CreditReview | null>(null); readonly error = signal(''); readonly busy = signal(false);
  readonly pendingCommand = signal<PendingCreditCommand | null>(null); readonly pendingStorageBlocked = signal(false); readonly recoveredReceipt = signal<CreditCommandReceipt | null>(null);
  readonly guidPattern = guidPattern; readonly amountPattern = amountPattern; readonly userId = () => this.session.current()?.userId ?? '';
  supplierId = ''; originalInvoiceId = ''; reference = ''; periodId = ''; postingDate = new Date().toISOString().slice(0, 10); payableRoleId = '';
  reason = ''; unlinkedRationale = ''; sourceBasis = ''; prepareAssent = false; submitAssent = false; reviewReason = ''; duplicateReason = '';
  lines: CreditLine[] = [this.newLine()];
  private creditNoteId = '';
  private readonly http = inject(HttpClient); private readonly session = inject(SessionService); private readonly destroyRef = inject(DestroyRef);
  private operation?: Subscription; private requestId = 0;
  constructor() {
    effect(() => { const client = this.clientId(); this.currency(); this.periods(); untracked(() => { this.reset(); if (guid(client)) this.refresh(); }); });
    effect(() => { const generation = this.session.invalidation(); untracked(() => { void generation; this.reset(); }); });
    this.destroyRef.onDestroy(() => { this.requestId++; this.operation?.unsubscribe(); });
  }
  private base(): string { return `/api/ui/accounting/clients/${this.clientId()}`; }
  private newLine(): CreditLine { return { originalLineNumber: '', accountCode: '', amount: '' }; }
  addLine(): void { if (!this.pendingCommand() && !this.pendingStorageBlocked() && this.lines.length < 99) this.lines = [...this.lines, this.newLine()]; }
  private reset(): void { this.operation?.unsubscribe(); this.requestId++; this.rows.set([]); this.suppliers.set([]); this.purchases.set([]); this.preview.set(null); this.activeReview.set(null); this.error.set(''); this.busy.set(false); this.recoveredReceipt.set(null); this.supplierId = ''; this.originalInvoiceId = ''; this.reference = ''; this.periodId = ''; this.payableRoleId = ''; this.reason = ''; this.unlinkedRationale = ''; this.sourceBasis = ''; this.prepareAssent = false; this.submitAssent = false; this.creditNoteId = ''; this.lines = [this.newLine()]; this.restorePendingCommand(); }
  pendingMatchesContext(): boolean { const pending = this.pendingCommand(); return !!pending && pending.clientId === this.clientId() && pending.actorId === this.userId(); }
  private restorePendingCommand(): void {
    this.pendingCommand.set(null);
    this.pendingStorageBlocked.set(false);
    if (!guid(this.clientId())) return;
    try {
      const raw = globalThis.sessionStorage?.getItem(pendingCreditKey(this.clientId()));
      if (!raw) return;
      const parsed: unknown = JSON.parse(raw);
      if (isPendingCreditCommand(parsed, this.clientId())) this.pendingCommand.set(parsed);
      else { this.pendingStorageBlocked.set(true); this.error.set('A retained supplier-credit command could not be validated. Reconciliation is required before continuing.'); }
    } catch { this.pendingStorageBlocked.set(true); this.error.set('The retained supplier-credit command is unavailable. Reconciliation is required before continuing.'); }
  }
  private retainPendingCommand(pending: PendingCreditCommand): boolean {
    try {
      const storage = globalThis.sessionStorage;
      if (!storage) throw new Error('session storage unavailable');
      storage.setItem(pendingCreditKey(pending.clientId), JSON.stringify(pending));
      if (storage.getItem(pendingCreditKey(pending.clientId)) !== JSON.stringify(pending)) throw new Error('session storage verification failed');
    } catch { this.error.set('This browser could not retain the command safely, so nothing was sent.'); return false; }
    this.pendingCommand.set(pending); this.recoveredReceipt.set(null); return true;
  }
  loadSuppliers(): void { if (this.busy() || this.pendingCommand()) return; const request = ++this.requestId, generation = this.session.invalidation(), client = this.clientId(); this.busy.set(true); this.operation = this.http.get<unknown>(`${this.base()}/counterparties`, { params: { role: 'SUPPLIER', page: '0', pageSize: '25' } }).pipe(timeout(15000)).subscribe({ next: data => { if (request !== this.requestId || generation !== this.session.invalidation() || client !== this.clientId()) return; try { this.suppliers.set(decodeCounterparties(data, client, 'SUPPLIER', 0).counterparties.filter(x => x.role === 'SUPPLIER' || x.role === 'BOTH').map(x => ({ id: x.id, displayName: x.displayName, role: x.role }))); this.busy.set(false); } catch { this.busy.set(false); this.error.set('Supplier list could not be validated.'); } }, error: () => { if (request === this.requestId) { this.busy.set(false); this.error.set('Client supplier list is unavailable.'); } } }); }
  loadPurchases(): void { if (this.busy() || this.pendingCommand()) return; const request = ++this.requestId; this.busy.set(true); this.operation = this.http.get<PurchaseRow[]>(`${this.base()}/purchase-invoices`).pipe(timeout(15000)).subscribe({ next: rows => { if (request !== this.requestId) return; this.purchases.set(rows.filter(x => x.currency === this.currency() && x.state === 'POSTED')); this.busy.set(false); }, error: () => { if (request === this.requestId) { this.busy.set(false); this.error.set('Posted client purchases are unavailable.'); } } }); }
  private body(): object | null { if (!guid(this.supplierId) || !guid(this.periodId) || !guid(this.payableRoleId) || !this.reference.trim() || !this.reason.trim() || !this.sourceBasis.trim() || this.lines.some(x => !amountPattern.test(x.amount) || Number(x.amount) <= 0 || !x.accountCode.trim()) || this.originalInvoiceId && !guid(this.originalInvoiceId)) return null; this.creditNoteId ||= crypto.randomUUID(); return { creditNoteId: this.creditNoteId, creditNoteReference: this.reference.trim(), originalInvoiceId: this.originalInvoiceId || null, supplierId: this.supplierId, periodId: this.periodId, payableRoleId: this.payableRoleId, postingDate: this.postingDate, reason: this.reason.trim(), unlinkedExceptionRationale: this.unlinkedRationale.trim(), sourceReceiptId: null, sourceBasis: this.sourceBasis.trim(), lines: this.lines.map(x => ({ originalLineNumber: x.originalLineNumber ? Number(x.originalLineNumber) : null, accountCode: x.accountCode.trim(), amount: x.amount })) }; }
  previewCredit(): void { const body = this.body(); if (!body || this.busy() || this.pendingCommand() || this.pendingStorageBlocked()) return; this.busy.set(true); this.operation = this.http.post<CreditPreview>(`${this.base()}/purchase-credit-notes/preview`, body).pipe(timeout(20000)).subscribe({ next: preview => { this.busy.set(false); this.preview.set(preview); this.submitAssent = false; this.error.set(''); }, error: () => { this.busy.set(false); this.error.set('Supplier credit preview failed. Check its client, supplier, AP role, period and line limits.'); } }); }
  submitCredit(): void {
    const preview = this.preview(), credit = this.body();
    if (!preview || !credit || this.busy() || this.pendingCommand() || this.pendingStorageBlocked() || !this.submitAssent) return;
    const commandId = crypto.randomUUID();
    const pending: PendingCreditCommand = { version: 1, clientId: this.clientId(), actorId: this.userId(), kind: 'SUBMIT', commandId,
      body: { commandId, credit, previewDigest: preview.digest, reviewed: true } };
    if (!this.retainPendingCommand(pending)) return;
    this.dispatchPending(pending, true);
  }
  private resetForm(): void { this.reference = ''; this.reason = ''; this.unlinkedRationale = ''; this.sourceBasis = ''; this.prepareAssent = false; this.submitAssent = false; this.creditNoteId = ''; this.lines = [this.newLine()]; }
  refresh(): void { if (this.busy()) return; const request = ++this.requestId, generation = this.session.invalidation(), client = this.clientId(); this.busy.set(true); this.operation = this.http.get<CreditRow[]>(`${this.base()}/purchase-credit-notes`).pipe(timeout(15000)).subscribe({ next: rows => { if (request !== this.requestId || generation !== this.session.invalidation() || client !== this.clientId()) return; this.rows.set(rows); this.busy.set(false); }, error: () => { if (request === this.requestId) { this.busy.set(false); this.error.set('Supplier credit history is unavailable.'); } } }); }
  reviewPreview(row: CreditRow): void { if (this.busy() || this.pendingCommand() || this.pendingStorageBlocked()) return; const request = ++this.requestId, generation = this.session.invalidation(); this.busy.set(true); this.operation = this.http.get<CreditReview>(`${this.base()}/purchase-credit-note-submissions/${row.submissionId}/preview`).pipe(timeout(15000)).subscribe({ next: preview => { if (request !== this.requestId || generation !== this.session.invalidation()) return; this.activeReview.set(preview); this.reviewReason = ''; this.duplicateReason = ''; this.busy.set(false); }, error: () => { if (request === this.requestId) { this.busy.set(false); this.error.set('Supplier credit review preview is unavailable.'); } } }); }
  review(row: CreditRow, decision: 'APPROVE' | 'RETURN'): void {
    const preview = this.activeReview();
    if (!preview || this.busy() || this.pendingCommand() || this.pendingStorageBlocked()) return;
    const commandId = crypto.randomUUID();
    const pending: PendingCreditCommand = { version: 1, clientId: this.clientId(), actorId: this.userId(), kind: 'REVIEW', commandId,
      body: { commandId, submissionId: row.submissionId, decision, reason: this.reviewReason.trim(), duplicateResolutionReason: this.duplicateReason.trim(), previewDigest: preview.digest, reviewed: true } };
    if (!this.retainPendingCommand(pending)) return;
    this.dispatchPending(pending, true);
  }
  private matchesReceipt(pending: PendingCreditCommand, value: unknown): value is CreditCommandReceipt {
    if (!value || typeof value !== 'object') return false;
    const receipt = value as Partial<CreditCommandReceipt>;
    if (receipt.commandId !== pending.commandId || receipt.kind !== pending.kind || receipt.actorUserId !== pending.actorId ||
        !guid(receipt.creditNoteId) || !guid(receipt.submissionId) || typeof receipt.intentHash !== 'string' || !/^[0-9a-f]{64}$/i.test(receipt.intentHash)) return false;
    if (pending.kind === 'SUBMIT') {
      const credit = pending.body['credit'] as { creditNoteId?: unknown } | undefined;
      return receipt.creditNoteId === credit?.creditNoteId && receipt.outcome === 'SUBMITTED' && receipt.decisionId === null;
    }
    const decision = pending.body['decision'];
    const expectedOutcome = decision === 'APPROVE' ? 'POSTED' : decision === 'RETURN' ? 'RETURNED' : null;
    return receipt.submissionId === pending.body['submissionId'] && receipt.outcome === expectedOutcome && guid(receipt.decisionId);
  }
  private dispatchPending(pending: PendingCreditCommand, finishOnMatch: boolean): void {
    if (!this.pendingMatchesContext() || pending.commandId !== this.pendingCommand()?.commandId || this.busy()) return;
    this.busy.set(true);
    this.operation = this.http.post<unknown>(`${this.base()}${pending.kind === 'SUBMIT' ? '/purchase-credit-notes' : '/purchase-credit-note-reviews'}`, pending.body)
      .pipe(timeout(20000)).subscribe({
        next: value => {
          this.busy.set(false);
          if (!this.matchesReceipt(pending, value)) { this.error.set('The response did not match the retained supplier-credit command. Its outcome remains unresolved.'); return; }
          this.recoveredReceipt.set(value);
          if (finishOnMatch) this.acknowledgePendingOutcome();
        },
        error: () => { this.busy.set(false); this.error.set('Supplier credit outcome is unconfirmed. Check the retained outcome; an empty result does not clear this fence.'); }
      });
  }
  checkPendingOutcome(): void {
    const pending = this.pendingCommand();
    if (!pending || !this.pendingMatchesContext() || this.busy()) return;
    this.busy.set(true); this.recoveredReceipt.set(null);
    this.operation = this.http.get<unknown>(`${this.base()}/purchase-credit-note-requests/${pending.commandId}`, { params: { kind: pending.kind } })
      .pipe(timeout(15000)).subscribe({
        next: value => { this.busy.set(false); if (!this.matchesReceipt(pending, value)) { this.error.set('The retained response did not match the original command. The unresolved-outcome fence remains.'); return; } this.recoveredReceipt.set(value); this.error.set(''); },
        error: error => { this.busy.set(false); this.error.set(error?.status === 404 ? 'No outcome is recorded yet. The original command remains unresolved; retry only the exact retained command or check again.' : 'The retained outcome could not be checked. The original command remains fenced.'); }
      });
  }
  retryPendingCommand(): void { const pending = this.pendingCommand(); if (pending && this.pendingMatchesContext()) this.dispatchPending(pending, true); }
  acknowledgePendingOutcome(): void {
    const pending = this.pendingCommand(), receipt = this.recoveredReceipt();
    if (!pending || !receipt || !this.pendingMatchesContext() || !this.matchesReceipt(pending, receipt)) return;
    try { globalThis.sessionStorage?.removeItem(pendingCreditKey(pending.clientId)); }
    catch { this.error.set('The matching outcome is verified, but this browser could not clear the local fence. Refresh the page and reconcile again.'); return; }
    this.pendingCommand.set(null); this.recoveredReceipt.set(null); this.error.set('');
    if (pending.kind === 'SUBMIT') { this.preview.set(null); this.resetForm(); }
    else this.activeReview.set(null);
    this.refresh();
  }
}
