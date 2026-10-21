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
  snapshot: { version: string; profileId: string; chartVersionId: string; supplierId: string; supplierName: string; voucherReference: string; supplierInvoiceReference: string; normalizedSupplierReference: string; currency: string; receiptDate: string; documentDate: string; accountingDate: string; supplyDate: string; dueDate: string; policy: { currency: string; decimalPlaces: number; midpointRule: string; residualTreatment: string; maximumResidualMinorUnits: number; roundingAccountCode: string }; lines: { lineNumber: number; accountId: string; accountCode: string; accountName: string; description: string; quantity: string; unitPrice: string; discount: string; net: string; tax: string; gross: string }[]; net: string; tax: string; gross: string; statedNet: string; statedTax: string; statedGross: string; lateArrival: boolean; possibleDuplicateCount: number; sourceReceiptId: string | null; sourceReceiptHash: string | null; evidenceReference: string } }
interface PurchaseDraftRow { invoiceId: string; periodId: string; supplierId: string; supplierName: string; voucherReference: string; supplierInvoiceReference: string; revision: string; state: 'DRAFT' | 'RETURNED'; makerId: string }
interface PurchaseDraftPage { clientId: string; page: number; pageSize: number; hasMore: boolean; rows: PurchaseDraftRow[] }
interface PendingDraftSave { clientId: string; actorId: string; body: Record<string, unknown> }
type PendingInvoiceCommand = { kind: 'DRAFT' | 'SUBMIT' | 'REVIEW'; clientId: string; actorId: string; targetId: string | null; body: Record<string, unknown>; savedAt: number };
interface PurchaseCommandOutcome { commandId: string; kind: string; actorUserId: string; intentHash: string; outcome: string; invoiceId: string; draftId: string | null; submissionId: string | null; decisionId: string | null; journalId: string | null; draft: unknown | null; receipt: PurchaseCommandReceipt | null; postingReceipt: PostingReceipt | null }
interface PurchaseCommandReceipt { commandId: string; invoiceId: string; submissionId: string; kind: string; actorUserId: string; intentHash: string; outcome: string; decisionId: string | null }
interface PostingReceipt { commandId: string; clientId: string; journalId: string; actorUserId: string; intentHash: string; status: string }
interface PurchasePreview { invoiceId: string; revision: string; currency: string; net: string; tax: string; gross: string; digest: string; lateArrival: boolean; duplicateWarnings: string[]; manifest: { lines: { lineNumber: number; accountCode: string; accountName: string; description: string; debit: string; credit: string }[] } }
interface PurchaseReview { invoiceId: string; submissionId: string; journalId: string; journalRevision: string; digest: string; canPost: boolean; postingBlock: string | null; duplicateWarning: boolean; manifest: { supplierInvoiceReference: string; voucherReference: string; lateArrival: boolean; duplicateWarnings: string[]; lines: { lineNumber: number; accountCode: string; accountName: string; description: string; debit: string; credit: string }[] } }
interface PurchaseRow { invoiceId: string; supplierId: string; supplierInvoiceReference: string; voucherReference: string; state: string; currency: string; gross: string; dueDate: string; receiptDate: string; lateArrival: boolean; duplicateWarning: boolean; submissionId: string; journalId: string | null; makerId: string; decision: string | null; decisionReason: string | null; duplicateResolutionReason: string | null; openItemCreated: boolean }
const guid = (value: unknown): value is string => typeof value === 'string' && guidPattern.test(value);
const datePattern = /^\d{4}-\d{2}-\d{2}$/;
const amountPattern = /^(?:0|[1-9]\d{0,12})(?:\.\d{1,6})?$/;
const moneyPattern = /^(?:0|[1-9]\d{0,12})\.\d{6}$/;
const pendingLifetime = 4 * 60 * 60 * 1000;

function decodePendingInvoiceCommand(value: unknown, clientId: string, actorId: string): PendingInvoiceCommand | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null;
  const pending = value as PendingInvoiceCommand;
  const commandId = pending.body && typeof pending.body['commandId'] === 'string' ? pending.body['commandId'] as string : '';
  return pending.clientId === clientId && pending.actorId === actorId && ['DRAFT', 'SUBMIT', 'REVIEW'].includes(pending.kind) &&
    (pending.kind === 'DRAFT' ? pending.targetId === null || guid(pending.targetId) : guid(pending.targetId)) &&
    guid(commandId) && Number.isFinite(pending.savedAt) && pending.savedAt <= Date.now() && Date.now() - pending.savedAt <= pendingLifetime &&
    pending.body && typeof pending.body === 'object' && !Array.isArray(pending.body)
    ? pending : null;
}

function decodeCommandOutcome(value: unknown, clientId: string, actorId: string, pending: PendingInvoiceCommand): PurchaseCommandOutcome {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('Invalid command outcome');
  const result = value as PurchaseCommandOutcome;
  const commandId = String(pending.body['commandId'] ?? '');
  if (result.commandId !== commandId || result.kind !== pending.kind || result.actorUserId !== actorId ||
      !/^[a-f0-9]{64}$/.test(result.intentHash) || !guid(result.invoiceId) ||
      (result.draftId !== null && !guid(result.draftId)) || (result.submissionId !== null && !guid(result.submissionId)) ||
      (result.decisionId !== null && !guid(result.decisionId)) || (result.journalId !== null && !guid(result.journalId)))
    throw new Error('Command outcome identity mismatch');
  if (pending.kind === 'DRAFT') {
    if (result.outcome !== 'DRAFT' || !result.draft || !guid(result.draftId!)) throw new Error('Draft outcome is incomplete');
    const expectedInvoice = pending.body['invoiceId'];
    const expectedRevision = BigInt(String(pending.body['expectedRevision'] ?? '0')) + 1n;
    const draft = decodePurchaseDraft(result.draft, clientId, typeof expectedInvoice === 'string' ? expectedInvoice : result.invoiceId);
    if (draft.createdByUserId !== actorId || draft.id !== result.draftId || draft.invoiceId !== result.invoiceId || BigInt(draft.revision) !== expectedRevision)
      throw new Error('Draft outcome does not match the exact request');
  } else {
    const receipt = result.receipt;
    if (!receipt || receipt.commandId !== commandId || receipt.kind !== pending.kind || receipt.actorUserId !== actorId ||
        receipt.invoiceId !== result.invoiceId || receipt.intentHash !== result.intentHash || !guid(receipt.submissionId))
      throw new Error('Purchase receipt identity mismatch');
    if (pending.kind === 'SUBMIT' && (result.outcome !== 'SUBMITTED' || receipt.outcome !== 'SUBMITTED' || result.submissionId !== receipt.submissionId || result.invoiceId !== pending.targetId))
      throw new Error('Submission receipt mismatch');
    if (pending.kind === 'REVIEW') {
      const decision = String(pending.body['decision'] ?? '').toUpperCase();
      const expected = decision === 'RETURN' ? 'RETURNED' : 'POSTED';
      if (!['APPROVE', 'RETURN'].includes(decision) || result.outcome !== expected || receipt.outcome !== expected || result.decisionId !== receipt.decisionId || result.submissionId !== pending.targetId)
        throw new Error('Review decision acknowledgement mismatch');
      if (decision === 'APPROVE') {
        const posting = result.postingReceipt;
        if (!posting || posting.commandId !== commandId || posting.clientId !== clientId || posting.actorUserId !== actorId ||
            posting.journalId !== result.journalId || posting.intentHash !== result.intentHash || posting.status !== 'POSTED')
          throw new Error('Posting receipt is missing or mismatched');
      }
    }
  }
  return result;
}

export function decodePurchaseDraft(value: unknown, clientId: string, invoiceId?: string): PurchaseDraft {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('Invalid purchase draft');
  const draft = value as PurchaseDraft, snapshot = draft.snapshot, policy = snapshot?.policy;
  if (draft.clientId !== clientId || (invoiceId && draft.invoiceId !== invoiceId) ||
      ![draft.id, draft.invoiceId, draft.clientId, draft.periodId, draft.supplierId, draft.createdByUserId].every(guid) ||
      typeof draft.revision !== 'string' || !/^[1-9]\d{0,18}$/.test(draft.revision) || draft.state !== 'DRAFT' ||
      typeof draft.snapshotHash !== 'string' || !/^[a-f0-9]{64}$/.test(draft.snapshotHash) || !snapshot ||
      snapshot.version !== 'client-purchase-invoice-v1' || snapshot.supplierId !== draft.supplierId ||
      snapshot.voucherReference !== draft.voucherReference || snapshot.supplierInvoiceReference !== draft.supplierInvoiceReference ||
      ![snapshot.profileId, snapshot.chartVersionId].every(guid) || !snapshot.supplierName?.trim() ||
      !/^[A-Z]{3}$/.test(snapshot.currency) || policy?.currency !== snapshot.currency ||
      !Number.isInteger(policy?.decimalPlaces) || policy.decimalPlaces < 0 || policy.decimalPlaces > 6 ||
      !['TO_EVEN', 'AWAY_FROM_ZERO'].includes(policy.midpointRule) ||
      !['REJECT', 'EXPLICIT_ACCOUNT'].includes(policy.residualTreatment) ||
      !Number.isInteger(policy.maximumResidualMinorUnits) || typeof policy.roundingAccountCode !== 'string' ||
      ![snapshot.receiptDate, snapshot.documentDate, snapshot.accountingDate, snapshot.supplyDate, snapshot.dueDate].every(x => typeof x === 'string' && datePattern.test(x)) ||
      ![snapshot.net, snapshot.tax, snapshot.gross, snapshot.statedNet, snapshot.statedTax, snapshot.statedGross].every(x => typeof x === 'string' && moneyPattern.test(x)) ||
      typeof snapshot.lateArrival !== 'boolean' || !Number.isInteger(snapshot.possibleDuplicateCount) || snapshot.possibleDuplicateCount < 0 ||
      (snapshot.sourceReceiptId !== null && !guid(snapshot.sourceReceiptId)) ||
      (snapshot.sourceReceiptHash !== null && (typeof snapshot.sourceReceiptHash !== 'string' || !/^[a-f0-9]{64}$/.test(snapshot.sourceReceiptHash))) ||
      ((snapshot.sourceReceiptId === null) !== (snapshot.sourceReceiptHash === null)) ||
      !Array.isArray(snapshot.lines) || snapshot.lines.length < 1 || snapshot.lines.length > 99) throw new Error('Invalid scoped purchase draft');
  snapshot.lines.forEach((line, index) => {
    if (line.lineNumber !== index + 1 || !guid(line.accountId) ||
        ![line.accountCode, line.accountName, line.description].every(x => typeof x === 'string' && x.trim()) ||
        ![line.quantity, line.unitPrice, line.discount].every(x => typeof x === 'string' && amountPattern.test(x)) ||
        ![line.net, line.tax, line.gross].every(x => typeof x === 'string' && moneyPattern.test(x))) throw new Error('Invalid retained purchase line');
  });
  return draft;
}

export function decodePurchaseDraftPage(value: unknown, clientId: string, page: number, pageSize = 25): PurchaseDraftPage {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('Invalid purchase draft page');
  const result = value as PurchaseDraftPage;
  if (result.clientId !== clientId || result.page !== page || result.pageSize !== pageSize || typeof result.hasMore !== 'boolean' ||
      !Array.isArray(result.rows) || result.rows.length > pageSize) throw new Error('Invalid scoped purchase draft page');
  const seen = new Set<string>();
  for (const row of result.rows) {
    if (!row || !guid(row.invoiceId) || !guid(row.periodId) || !guid(row.supplierId) || !guid(row.makerId) ||
        !row.supplierName?.trim() || !row.voucherReference?.trim() || !row.supplierInvoiceReference?.trim() ||
        !/^[1-9]\d{0,18}$/.test(row.revision) || !['DRAFT', 'RETURNED'].includes(row.state) || seen.has(row.invoiceId))
      throw new Error('Invalid resumable purchase draft');
    seen.add(row.invoiceId);
  }
  return result;
}

@Component({ selector: 'audit-client-purchase-invoices', imports: [FormsModule, MatButtonModule], styleUrl: './purchase-workflow-forms.scss', template: `<section aria-label="Client purchase invoices">
  <h4>Client supplier invoices</h4>
  <p>Capture the supplier document against this client’s own expense or asset accounts and AP control. Untaxed purchases work without the optional tax module; tax-bearing purchases remain separately gated.</p>
  <button matButton type="button" [disabled]="busy() || draftsBusy()" (click)="refresh()">Refresh supplier invoice history</button>
  @if (recoveryNotice()) { <p role="status">{{ recoveryNotice() }}</p> }
  @if (error()) { <p role="alert">{{ error() }}</p> }
  @if (suppliers() !== null && !supplierBusy() && !supplierError() && !supplierBookkeepingActive()) { <p role="status">Bookkeeping service is inactive. Saved purchase history and recovery remain available; new purchase preparation is blocked.</p> }
  @if (draftsError()) { <p role="alert">{{ draftsError() }}</p> }
  @if (pendingCommand() || pendingRecoveryBlocked()) {
    <section aria-label="Unresolved purchase command">
      @if (pendingCommand(); as pending) { <p role="status">The {{ pending.kind }} command {{ pending.body['commandId'] }} is still unresolved. Editing and new purchase actions are locked until its exact outcome is found or the identical request is retried.</p>
        <button matButton type="button" [disabled]="busy()" (click)="reconcilePendingOutcome()">Check saved command outcome</button>
        <button matButton type="button" [disabled]="busy()" (click)="retryPendingOutcome()">Retry exact saved command</button>
      } @else { <p role="alert">A saved purchase command reference could not be safely read. No new purchase command is allowed in this tab.</p> }
    </section>
  }
  <section aria-label="Saved purchase drafts"><h5>Saved drafts and returned work</h5>
    @if (draftsBusy()) { <p role="status">Loading saved purchase drafts…</p> }
    @if (!draftsBusy() && draftRows().length === 0) { <p>No resumable purchase drafts on this page.</p> }
    @for (row of draftRows(); track row.invoiceId) { <article>
      <h6>{{ row.voucherReference }} · {{ row.supplierInvoiceReference }} · Revision {{ row.revision }} · {{ row.state }}</h6>
      <p>{{ row.supplierName }} · Period {{ row.periodId }} · Maker {{ row.makerId }}</p>
      @if (row.makerId === userId()) {
        <button matButton type="button" [disabled]="busy() || hasPendingCommand()" (click)="resumeDraft(row)">{{ row.state === 'RETURNED' ? 'Rework returned purchase draft' : 'Resume purchase draft' }}</button>
      } @else { <p>Only the original maker can revise this draft.</p> }
    </article> }
    <button matButton type="button" [disabled]="draftsBusy() || draftPage() === 0" (click)="loadDrafts(draftPage() - 1)">Previous draft page</button>
    <span>Draft page {{ draftPage() + 1 }}</span>
    <button matButton type="button" [disabled]="draftsBusy() || !draftHasMore()" (click)="loadDrafts(draftPage() + 1)">Next draft page</button>
  </section>
  @if (editing()) {
    <form class="purchase-invoice-entry" #form="ngForm" (ngSubmit)="form.valid && saveDraft()"><fieldset [disabled]="busy() || hasPendingCommand()">
      <legend>{{ draft() ? 'Revise saved supplier document' : 'Capture supplier document' }}</legend>
      @if (returnReason()) { <p role="status">Returned for correction: {{ returnReason() }}</p> }
      <button matButton type="button" [disabled]="supplierBusy()" (click)="loadSuppliers(supplierPage())">Refresh client suppliers</button>
      @if (supplierError()) { <p role="alert">{{ supplierError() }}</p> }
      @if (supplierBusy()) { <p role="status">Loading client suppliers…</p> }
      @if (suppliers() !== null && supplierTotal() === 0 && !supplierBusy()) { <p>No suppliers are available for this client.</p> }
      <label>Supplier <select name="supplier" [ngModel]="supplierId" (ngModelChange)="selectSupplier($event)" required><option value="">Choose supplier</option>@for (supplier of supplierOptions(); track supplier.id) { <option [value]="supplier.id">{{ supplier.displayName }}</option> }</select></label>
      <nav aria-label="Purchase invoice supplier pages">
        <button matButton type="button" [disabled]="supplierBusy() || supplierPage() === 0" (click)="loadSuppliers(supplierPage() - 1)">Previous suppliers</button>
        <span>Supplier page {{ supplierPage() + 1 }} of {{ supplierPageCount() }} · {{ supplierTotal() }} suppliers · {{ supplierPageSize }} per page</span>
        <button matButton type="button" [disabled]="supplierBusy() || !supplierHasNext()" (click)="loadSuppliers(supplierPage() + 1)">Next suppliers</button>
      </nav>
      <label>Internal voucher reference <input name="voucher" [(ngModel)]="voucher" (ngModelChange)="draftChanged()" [readonly]="!!draft()" required maxlength="100" /></label>
      <label>Supplier invoice number <input name="supplierInvoice" [(ngModel)]="supplierReference" (ngModelChange)="draftChanged()" required maxlength="200" /></label>
      <label>Posting period <select name="period" [(ngModel)]="periodId" (ngModelChange)="draftChanged()" required><option value="">Choose period</option>@for (period of periods(); track period.id) { <option [value]="period.id" [disabled]="period.status !== 'OPEN'">{{ period.code }} · {{ period.status }}</option> }</select></label>
      <label>Receipt date <input name="receiptDate" type="date" [(ngModel)]="receiptDate" (ngModelChange)="draftChanged()" required /></label>
      <label>Supplier document date <input name="documentDate" type="date" [(ngModel)]="documentDate" (ngModelChange)="draftChanged()" required /></label>
      <label>Accounting date <input name="accountingDate" type="date" [(ngModel)]="accountingDate" (ngModelChange)="draftChanged()" required /></label>
      <label>Supply / tax date <input name="supplyDate" type="date" [(ngModel)]="supplyDate" (ngModelChange)="draftChanged()" required /></label>
      <label>Due date <input name="dueDate" type="date" [(ngModel)]="dueDate" (ngModelChange)="draftChanged()" required /></label>
      <label>Approved AP role ID <input name="apRole" [(ngModel)]="apRoleId" (ngModelChange)="draftChanged()" required [pattern]="guidPattern.source" maxlength="36" /></label>
      <label>Supplier evidence reference <textarea name="evidence" [(ngModel)]="evidence" (ngModelChange)="draftChanged()" required maxlength="2000"></textarea></label>
      <fieldset><legend>Purchase lines</legend>@for (line of lines; track $index) { <div>
        <label>Expense or asset account code <input [name]="'account-' + $index" [(ngModel)]="line.accountCode" (ngModelChange)="draftChanged()" required maxlength="100" /></label>
        <label>Description <input [name]="'description-' + $index" [(ngModel)]="line.description" (ngModelChange)="draftChanged()" required maxlength="2000" /></label>
        <label>Quantity <input [name]="'quantity-' + $index" inputmode="decimal" [(ngModel)]="line.quantity" (ngModelChange)="draftChanged()" required /></label>
        <label>Unit price <input [name]="'price-' + $index" inputmode="decimal" [(ngModel)]="line.unitPrice" (ngModelChange)="draftChanged()" required /></label>
        <label>Discount <input [name]="'discount-' + $index" inputmode="decimal" [(ngModel)]="line.discount" (ngModelChange)="draftChanged()" required /></label>
      </div> }
      <button matButton type="button" [disabled]="lines.length >= 99 || hasPendingCommand()" (click)="addLine()">Add purchase line</button></fieldset>
      <label>Supplier-stated net <input name="statedNet" inputmode="decimal" [(ngModel)]="statedNet" (ngModelChange)="draftChanged()" required /></label>
      <label>Supplier-stated tax <input name="statedTax" inputmode="decimal" [(ngModel)]="statedTax" (ngModelChange)="draftChanged()" required /></label>
      <label>Supplier-stated gross <input name="statedGross" inputmode="decimal" [(ngModel)]="statedGross" (ngModelChange)="draftChanged()" required /></label>
      <p>Totals must match the exact untaxed calculation. Unsupported tax treatment or a material discrepancy blocks posting. Receipt and document dates remain separate, including late-arriving evidence.</p>
      <label><input type="checkbox" [ngModel]="draftAssent()" (ngModelChange)="draftAssent.set($event)" name="draftAssent" /> I checked this supplier, client, document dates, coding, supplier-stated totals and evidence reference.</label>
      <button matButton type="submit" [disabled]="!canUseSupplier() || !draftAssent() || busy() || hasPendingCommand() || form.invalid">{{ draft() ? 'Save purchase draft revision' : 'Save client purchase draft' }}</button>
      @if (revisionConflict() && draft()) { <p role="alert">The saved revision changed before this edit was accepted.</p><button matButton type="button" [disabled]="busy()" (click)="reloadCurrentDraft()">Reload the latest saved revision</button> }
      @if (draft()) { <button matButton type="button" [disabled]="busy() || hasPendingCommand()" (click)="cancelEdit()">Cancel draft revision</button> }
      @if (!draft()) { <button matButton type="button" [disabled]="busy() || hasPendingCommand()" (click)="startNewDraft()">Clear new purchase form</button> }
    </fieldset></form>
  }
  @if (!editing() && draft(); as d) {
    <article aria-label="Retained purchase draft"><h5>{{ d.voucherReference }} · {{ d.supplierInvoiceReference }}</h5>
      <p>{{ d.snapshot.supplierName }} · Draft revision {{ d.revision }} · {{ d.snapshot.gross }} {{ currency() }}</p>
      <p>Receipt {{ d.snapshot.receiptDate }} · Document {{ d.snapshot.documentDate }} · Accounting {{ d.snapshot.accountingDate }} · Supply {{ d.snapshot.supplyDate }} · Due {{ d.snapshot.dueDate }}</p>
      <p>Supplier evidence: {{ d.snapshot.evidenceReference }}</p>
      @if (returnReason()) { <p role="status">Returned for correction: {{ returnReason() }}</p> }
      @if (d.snapshot.lateArrival) { <p role="status">Late-arriving supplier document: receipt date is after the supplier document date.</p> }
      @if (d.snapshot.possibleDuplicateCount) { <p role="status">Possible same-supplier invoice duplicate warning. A reviewer must record a reason to resolve a legitimate duplicate.</p> }
      <table><caption>Captured client purchase lines</caption><thead><tr><th>Account</th><th>Description</th><th>Net</th><th>Tax</th><th>Gross</th></tr></thead><tbody>@for (line of d.snapshot.lines; track line.lineNumber) { <tr><td>{{ line.accountCode }} · {{ line.accountName }}</td><td>{{ line.description }}</td><td>{{ line.net }}</td><td>{{ line.tax }}</td><td>{{ line.gross }}</td></tr> }</tbody></table>
      <label>Approved AP role ID for preview <input name="previewApRoleId" [(ngModel)]="apRoleId" (ngModelChange)="previewRoleChanged()" [disabled]="busy()" required [pattern]="guidPattern.source" maxlength="36" /></label>
      @if (!validApRoleId()) { <p role="status">Choose the approved payable control role before requesting a posting preview.</p> }
      <button matButton type="button" [disabled]="busy() || d.createdByUserId !== userId()" (click)="editDraft()">Revise saved purchase draft</button>
      <button matButton type="button" [disabled]="busy() || !canUseSupplier() || !validApRoleId()" (click)="previewDraft()">Preview AP posting</button>
      <button matButton type="button" [disabled]="busy()" (click)="startNewDraft()">Start a separate purchase draft</button>
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
    @if (row.state === 'RETURNED' && row.makerId === userId()) { <button matButton type="button" [disabled]="busy() || hasPendingCommand()" (click)="reworkReturnedDraft(row)">Rework returned purchase draft</button> }
    @if (row.state === 'RETURNED' && row.makerId !== userId()) { <p>Only the original maker can revise this returned purchase draft.</p> }
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
  readonly supplierPageSize = 25;
  readonly clientId = input.required<string>(); readonly currency = input.required<string>(); readonly periods = input.required<Period[]>();
  private readonly http = inject(HttpClient); private readonly session = inject(SessionService);
  readonly busy = signal(false); readonly error = signal(''); readonly recoveryNotice = signal(''); readonly suppliers = signal<ReturnType<typeof decodeCounterparties>['counterparties'] | null>(null);
  readonly supplierBusy = signal(false); readonly supplierError = signal(''); readonly supplierPage = signal(0); readonly supplierTotal = signal(0); readonly supplierBookkeepingActive = signal(false);
  readonly selectedSupplier = signal<{ id: string; displayName: string } | null>(null);
  readonly draft = signal<PurchaseDraft | null>(null); readonly preview = signal<PurchasePreview | null>(null); readonly rows = signal<PurchaseRow[]>([]); readonly activeReview = signal<PurchaseReview | null>(null);
  readonly editing = signal(true); readonly returnReason = signal<string | null>(null); readonly revisionConflict = signal(false); readonly formDirty = signal(false);
  readonly draftRows = signal<PurchaseDraftRow[]>([]); readonly draftsBusy = signal(false); readonly draftsError = signal('');
  readonly draftPage = signal(0); readonly draftHasMore = signal(false); readonly pendingDraftSave = signal<PendingDraftSave | null>(null);
  readonly pendingSubmit = signal<PendingInvoiceCommand | null>(null); readonly pendingReview = signal<PendingInvoiceCommand | null>(null);
  readonly pendingRecoveryBlocked = signal(false);
  readonly draftAssent = signal(false); readonly submitAssent = signal(false); readonly userId = () => this.session.current()?.userId ?? ''; readonly guidPattern = guidPattern;
  periodId = ''; supplierId = ''; voucher = ''; supplierReference = ''; apRoleId = ''; evidence = ''; sourceReceiptId: string | null = null; receiptDate = new Date().toISOString().slice(0, 10);
  documentDate = ''; accountingDate = ''; supplyDate = ''; dueDate = ''; precision = '2'; midpoint = 'AWAY_FROM_ZERO';
  statedNet = ''; statedTax = '0'; statedGross = ''; lines: PurchaseLineInput[] = [{ accountCode: '', description: '', quantity: '1', unitPrice: '', discount: '0' }];
  reviewReason = ''; duplicateReason = ''; private requestId = 0; private operation?: Subscription; private previewRequest: Record<string, unknown> | null = null;
  private draftRequestId = 0; private draftOperation?: Subscription; private supplierRequestId = 0; private supplierOperation?: Subscription; private supplierLoaded = false; private supplierScope = '';
  constructor() {
    effect(() => { this.clientId(); this.currency(); this.session.invalidation(); this.session.current(); untracked(() => {
      this.operation?.unsubscribe(); this.draftOperation?.unsubscribe(); this.supplierOperation?.unsubscribe(); ++this.requestId; ++this.draftRequestId; ++this.supplierRequestId;
      this.busy.set(false); this.draftsBusy.set(false); this.draft.set(null); this.preview.set(null); this.rows.set([]); this.activeReview.set(null);
      this.draftRows.set([]); this.draftPage.set(0); this.draftHasMore.set(false); this.draftsError.set(''); this.suppliers.set(null); this.selectedSupplier.set(null); this.supplierPage.set(0); this.supplierTotal.set(0); this.supplierBusy.set(false); this.supplierError.set(''); this.supplierBookkeepingActive.set(false); this.supplierScope = ''; this.supplierLoaded = false; this.pendingDraftSave.set(null);
      this.pendingSubmit.set(null); this.pendingReview.set(null); this.pendingRecoveryBlocked.set(false);
      this.returnReason.set(null); this.revisionConflict.set(false); this.editing.set(true); this.reset();
      this.restorePendingCommand();
      this.loadSuppliers(); this.loadDrafts(0); this.refresh();
    }); });
    const destroyRef = inject(DestroyRef);
      destroyRef.onDestroy(() => { this.operation?.unsubscribe(); this.draftOperation?.unsubscribe(); this.supplierOperation?.unsubscribe(); });
  }
  private base(): string { return `/api/ui/accounting/clients/${this.clientId()}`; }
  private setError(message: string): void { this.error.set(message); this.recoveryNotice.set(''); }
  pendingCommand(): PendingInvoiceCommand | null { return this.pendingDraftSave() ? { ...this.pendingDraftSave()!, kind: 'DRAFT', targetId: typeof this.pendingDraftSave()!.body['invoiceId'] === 'string' ? this.pendingDraftSave()!.body['invoiceId'] as string : null, savedAt: Date.now() } : this.pendingSubmit() ?? this.pendingReview(); }
  hasPendingCommand(): boolean { return this.pendingRecoveryBlocked() || this.pendingDraftSave() !== null || this.pendingSubmit() !== null || this.pendingReview() !== null; }
  validApRoleId(): boolean { return guid(this.apRoleId); }
  private pendingStorageKey(): string {
    const session = this.session.current();
    return `auditsphere.purchase-invoice-command-v1:${session?.firmId ?? 'none'}:${session?.userId ?? 'none'}:${session?.generation ?? 'none'}:${this.clientId()}`;
  }
  private savePendingCommand(kind: PendingInvoiceCommand['kind'], body: Record<string, unknown>, targetId: string | null = null): PendingInvoiceCommand | null {
    const pending: PendingInvoiceCommand = { kind, clientId: this.clientId(), actorId: this.userId(), targetId, body, savedAt: Date.now() };
    if (!guid(pending.clientId) || !guid(pending.actorId) || !guid(body['commandId'])) return null;
    try { sessionStorage.setItem(this.pendingStorageKey(), JSON.stringify(pending)); }
    catch { return null; }
    if (kind === 'DRAFT') this.pendingDraftSave.set({ clientId: pending.clientId, actorId: pending.actorId, body });
    else if (kind === 'SUBMIT') this.pendingSubmit.set(pending);
    else this.pendingReview.set(pending);
    return pending;
  }
  private restorePendingCommand(): void {
    if (!guid(this.clientId()) || !guid(this.userId())) return;
    let raw: string | null;
    try { raw = sessionStorage.getItem(this.pendingStorageKey()); }
    catch { this.pendingRecoveryBlocked.set(true); return; }
    if (!raw) return;
    try {
      const parsed = decodePendingInvoiceCommand(JSON.parse(raw), this.clientId(), this.userId());
      if (!parsed) throw new Error('Invalid pending purchase command');
      if (parsed.kind === 'DRAFT') this.pendingDraftSave.set({ clientId: parsed.clientId, actorId: parsed.actorId, body: parsed.body });
      else if (parsed.kind === 'SUBMIT') this.pendingSubmit.set(parsed);
      else this.pendingReview.set(parsed);
      this.setError(`The saved ${parsed.kind} request remains unresolved. Check its server outcome before retrying or editing.`);
    } catch { this.pendingRecoveryBlocked.set(true); }
  }
  private clearPendingCommand(pending: PendingInvoiceCommand): boolean {
    try { sessionStorage.removeItem(this.pendingStorageKey()); }
    catch { this.setError('The command outcome was found, but its tab recovery record could not be cleared. Reopen and reconcile this command before new work.'); return false; }
    if (pending.kind === 'DRAFT') this.pendingDraftSave.set(null);
    else if (pending.kind === 'SUBMIT') this.pendingSubmit.set(null);
    else this.pendingReview.set(null);
    return true;
  }
  reset(): void { this.periodId = ''; this.supplierId = ''; this.selectedSupplier.set(null); this.voucher = ''; this.supplierReference = ''; this.apRoleId = ''; this.evidence = ''; this.sourceReceiptId = null; this.documentDate = ''; this.accountingDate = ''; this.supplyDate = ''; this.dueDate = ''; this.precision = '2'; this.midpoint = 'AWAY_FROM_ZERO'; this.statedNet = ''; this.statedTax = '0'; this.statedGross = ''; this.lines = [{ accountCode: '', description: '', quantity: '1', unitPrice: '', discount: '0' }]; this.draftAssent.set(false); this.submitAssent.set(false); this.formDirty.set(false); }
  draftChanged(): void { this.formDirty.set(true); this.draftAssent.set(false); this.preview.set(null); this.previewRequest = null; this.submitAssent.set(false); this.revisionConflict.set(false); }
  addLine(): void { if (this.lines.length < 99) { this.lines.push({ accountCode: '', description: '', quantity: '1', unitPrice: '', discount: '0' }); this.draftChanged(); } }
  supplierOptions(): { id: string; displayName: string }[] {
    const current = (this.suppliers() ?? []).map(item => ({ id: item.id, displayName: item.displayName })), selected = this.selectedSupplier();
    return selected && !current.some(item => item.id === selected.id) ? [...current, selected] : current;
  }
  supplierPageCount(): number { return Math.max(1, Math.ceil(this.supplierTotal() / this.supplierPageSize)); }
  supplierHasNext(): boolean { return this.supplierPage() + 1 < this.supplierPageCount(); }
  private supplierContext(): string { const actor = this.session.current(); return [actor?.firmId, actor?.userId, actor?.generation, this.session.invalidation(), this.clientId()].join(':'); }
  canUseSupplier(): boolean { return this.supplierBookkeepingActive() && !this.supplierBusy() && !this.supplierError() && this.supplierScope === this.supplierContext() && this.supplierOptions().some(item => item.id === this.supplierId); }
  private invalidateSupplierPreparation(): void { this.preview.set(null); this.previewRequest = null; this.draftAssent.set(false); this.submitAssent.set(false); }
  selectSupplier(id: string): void {
    if (id === this.supplierId) return;
    this.supplierId = id;
    const selected = this.suppliers()?.find(item => item.id === id);
    this.selectedSupplier.set(selected ? { id: selected.id, displayName: selected.displayName } : null);
    this.draftChanged();
  }
  loadSuppliers(page = 0): void {
    const lastPage = Math.max(0, Math.ceil(this.supplierTotal() / this.supplierPageSize) - 1);
    if (!Number.isInteger(page) || page < 0 || page > 1_000 || (page > lastPage && (this.suppliers() !== null || page !== 0))) return;
    const request = ++this.supplierRequestId, generation = this.session.invalidation(), client = this.clientId(), scope = this.supplierContext();
    this.supplierOperation?.unsubscribe(); this.supplierBusy.set(true); this.supplierError.set(''); this.supplierBookkeepingActive.set(false);
    this.supplierOperation = this.http.get<unknown>(`${this.base()}/counterparties`, { params: { role: 'SUPPLIER', page: String(page), pageSize: String(this.supplierPageSize) } }).pipe(timeout(15000)).subscribe({
      next: value => {
        if (request !== this.supplierRequestId || generation !== this.session.invalidation() || client !== this.clientId() || scope !== this.supplierContext()) return;
        this.supplierBusy.set(false);
        try {
          const result = decodeCounterparties(value, client, 'SUPPLIER', page);
          if (page * this.supplierPageSize >= result.total && page !== 0) throw new Error('Supplier page is beyond the current result set');
          const items = result.counterparties.filter(item => item.role === 'SUPPLIER' || item.role === 'BOTH');
          this.suppliers.set(items); this.supplierPage.set(page); this.supplierTotal.set(result.total);
          this.supplierBookkeepingActive.set(result.bookkeepingActive); this.supplierScope = scope;
          if (!result.bookkeepingActive) this.invalidateSupplierPreparation();
          const selected = items.find(item => item.id === this.supplierId);
          if (selected) this.selectedSupplier.set({ id: selected.id, displayName: selected.displayName });
          if (page === 0 && !this.supplierLoaded) { this.supplierLoaded = true; this.refresh(); }
        } catch { this.invalidateSupplierPreparation(); this.supplierError.set('Client suppliers could not be validated. Refresh before preparing another purchase.'); }
      },
      error: () => { if (request === this.supplierRequestId && generation === this.session.invalidation() && client === this.clientId() && scope === this.supplierContext()) { this.supplierBusy.set(false); this.invalidateSupplierPreparation(); this.supplierError.set('Client suppliers are unavailable; the displayed page was not replaced. Refresh before preparing another purchase.'); } }
    });
  }
  loadDrafts(page = 0): void {
    if (page < 0 || page > 1_000) return;
    const client = this.clientId(), generation = this.session.invalidation(), request = ++this.draftRequestId;
    this.draftsBusy.set(true); this.draftsError.set(''); this.draftOperation?.unsubscribe();
    this.draftOperation = this.http.get<unknown>(`${this.base()}/purchase-invoice-drafts`, { params: { page: String(page), pageSize: '25' } }).pipe(timeout(15000)).subscribe({
      next: value => {
        if (request !== this.draftRequestId || generation !== this.session.invalidation() || client !== this.clientId()) return;
        this.draftsBusy.set(false);
        try { const result = decodePurchaseDraftPage(value, client, page); this.draftRows.set(result.rows); this.draftPage.set(result.page); this.draftHasMore.set(result.hasMore); }
        catch { this.draftsError.set('Saved purchase drafts could not be validated.'); }
      },
      error: failure => {
        if (request !== this.draftRequestId || generation !== this.session.invalidation()) return;
        this.draftsBusy.set(false); this.draftsError.set('Saved purchase drafts are unavailable.');
        if (failure.status === 401) this.session.clear();
      }
    });
  }
  resumeDraft(row: PurchaseDraftRow): void {
    if (row.makerId !== this.userId() || !['DRAFT', 'RETURNED'].includes(row.state) || this.hasPendingCommand()) return;
    if (this.formDirty()) { this.setError('Clear or save the current purchase form before opening another draft.'); return; }
    const returned = row.state === 'RETURNED' ? this.rows().find(x => x.invoiceId === row.invoiceId && x.state === 'RETURNED') : undefined;
    this.loadDraft(row.invoiceId, row.revision, returned?.decisionReason ?? null);
  }
  reworkReturnedDraft(row: PurchaseRow): void {
    if (row.state !== 'RETURNED' || row.makerId !== this.userId() || this.hasPendingCommand()) return;
    if (this.formDirty()) { this.setError('Clear or save the current purchase form before opening returned work.'); return; }
    this.loadDraft(row.invoiceId, undefined, row.decisionReason ?? 'Returned for correction.');
  }
  private loadDraft(invoiceId: string, expectedRevision?: string, returnReason: string | null | undefined = null): void {
    if (this.busy() || !guid(invoiceId)) return;
    const client = this.clientId(), generation = this.session.invalidation(), request = ++this.requestId;
    this.busy.set(true); this.setError(''); this.operation = this.http.get<unknown>(`${this.base()}/purchase-invoice-drafts/${invoiceId}`).pipe(timeout(15000)).subscribe({
      next: value => {
        if (request !== this.requestId || generation !== this.session.invalidation() || client !== this.clientId()) return;
        this.busy.set(false);
        try {
          const draft = decodePurchaseDraft(value, client, invoiceId);
          if (draft.createdByUserId !== this.userId() || (expectedRevision && draft.revision !== expectedRevision)) throw new Error('Stale or foreign draft');
          this.draft.set(draft); this.selectedSupplier.set({ id: draft.supplierId, displayName: draft.snapshot.supplierName }); this.populateDraft(draft); this.returnReason.set(returnReason ?? null);
          this.editing.set(true); this.revisionConflict.set(false); this.preview.set(null); this.previewRequest = null;
          this.loadDrafts(this.draftPage());
        } catch {
          this.setError('The saved purchase draft changed or could not be validated. Refresh the draft list before reopening it.');
          this.loadDrafts(0);
        }
      },
      error: failure => {
        if (request !== this.requestId || generation !== this.session.invalidation()) return;
        this.busy.set(false); this.setError('Saved purchase draft is unavailable. Refresh the list before another attempt.');
        if (failure.status === 401) this.session.clear();
      }
    });
  }
  private populateDraft(draft: PurchaseDraft, preserveApRole = false): void {
    const snapshot = draft.snapshot;
    this.periodId = draft.periodId; this.supplierId = draft.supplierId; this.voucher = draft.voucherReference;
    this.supplierReference = draft.supplierInvoiceReference; if (!preserveApRole) this.apRoleId = ''; this.evidence = snapshot.evidenceReference;
    this.sourceReceiptId = snapshot.sourceReceiptId; this.receiptDate = snapshot.receiptDate; this.documentDate = snapshot.documentDate;
    this.accountingDate = snapshot.accountingDate; this.supplyDate = snapshot.supplyDate; this.dueDate = snapshot.dueDate;
    this.precision = String(snapshot.policy.decimalPlaces); this.midpoint = snapshot.policy.midpointRule;
    this.statedNet = snapshot.statedNet; this.statedTax = snapshot.statedTax; this.statedGross = snapshot.statedGross;
    this.lines = snapshot.lines.map(line => ({ accountCode: line.accountCode, description: line.description, quantity: line.quantity, unitPrice: line.unitPrice, discount: line.discount }));
    this.draftAssent.set(false); this.submitAssent.set(false); this.formDirty.set(false);
  }
  editDraft(): void {
    const draft = this.draft();
    if (!draft || draft.createdByUserId !== this.userId() || this.busy() || this.hasPendingCommand()) return;
    this.populateDraft(draft); this.editing.set(true); this.preview.set(null); this.previewRequest = null; this.revisionConflict.set(false);
  }
  cancelEdit(): void { if (!this.draft() || this.busy() || this.hasPendingCommand()) return; this.editing.set(false); this.revisionConflict.set(false); this.preview.set(null); this.previewRequest = null; this.submitAssent.set(false); }
  startNewDraft(): void {
    if (this.busy() || this.hasPendingCommand()) return;
    this.draft.set(null); this.returnReason.set(null); this.revisionConflict.set(false); this.preview.set(null); this.previewRequest = null;
    this.reset(); this.editing.set(true);
  }
  reloadCurrentDraft(): void {
    const draft = this.draft(); if (!draft || this.busy() || this.hasPendingCommand()) return;
    this.loadDraft(draft.invoiceId, undefined, this.returnReason());
  }
  saveDraft(): void {
    const current = this.draft();
    if (this.busy() || this.hasPendingCommand() || !this.canUseSupplier() || !this.draftAssent() || !guid(this.periodId) || !guid(this.supplierId) || !guid(this.apRoleId) ||
        !datePattern.test(this.receiptDate) || !datePattern.test(this.documentDate) || !datePattern.test(this.accountingDate) || !datePattern.test(this.supplyDate) || !datePattern.test(this.dueDate) ||
        !/^[0-6]$/.test(this.precision) || !['TO_EVEN', 'AWAY_FROM_ZERO'].includes(this.midpoint) || this.lines.length < 1 || this.lines.length > 99 ||
        this.lines.some(l => !amountPattern.test(l.quantity) || !amountPattern.test(l.unitPrice) || !amountPattern.test(l.discount)) ||
        (current && current.createdByUserId !== this.userId())) return;
    const body = { commandId: crypto.randomUUID(), invoiceId: current?.invoiceId ?? null, expectedRevision: current?.revision ?? '0',
      periodId: this.periodId, supplierId: this.supplierId, voucherReference: this.voucher.trim(), supplierInvoiceReference: this.supplierReference.trim(),
      receiptDate: this.receiptDate, documentDate: this.documentDate, accountingDate: this.accountingDate, supplyDate: this.supplyDate, dueDate: this.dueDate,
      currency: this.currency(), policy: { currency: this.currency(), decimalPlaces: Number(this.precision), midpointRule: this.midpoint,
        residualTreatment: 'REJECT', maximumResidualMinorUnits: 0, roundingAccountCode: '' },
      lines: this.lines.map(l => ({ description: l.description.trim(), accountCode: l.accountCode.trim(), quantity: l.quantity, unitPrice: l.unitPrice, discount: l.discount, taxTreatment: 'NONE', taxes: [] })),
      statedNet: this.statedNet, statedTax: this.statedTax, statedGross: this.statedGross, sourceReceiptId: this.sourceReceiptId, evidenceReference: this.evidence.trim() };
    const pending = this.savePendingCommand('DRAFT', body, current?.invoiceId ?? null);
    if (!pending) { this.setError('A tab-scoped exact recovery record could not be saved; the purchase draft was not sent.'); return; }
    this.sendPendingDraftSave();
  }
  retryDraftSave(): void { this.sendPendingDraftSave(); }
  private sendPendingDraftSave(): void {
    const pending = this.pendingDraftSave();
    if (!pending || this.busy() || pending.clientId !== this.clientId() || pending.actorId !== this.userId()) return;
    const generation = this.session.invalidation(), request = ++this.requestId;
    this.busy.set(true); this.setError(''); this.operation = this.http.post<unknown>(`${this.base()}/purchase-invoice-drafts`, pending.body).pipe(timeout(20000)).subscribe({
      next: value => this.acceptDraftSave(value, pending, request, generation),
      error: failure => {
        if (request !== this.requestId || generation !== this.session.invalidation()) return;
        this.busy.set(false);
        if (failure.status === 0 || failure.status >= 500) {
          this.setError('Purchase draft outcome is unconfirmed. Retry the exact saved request before editing or creating another draft.');
        } else {
          if (failure.status === 0 || failure.status >= 500 || failure.status === 409) {
            this.setError('Purchase draft outcome is unconfirmed. Check the exact command outcome before retrying the saved request.');
            this.reconcilePendingOutcome();
          } else {
            const saved = this.pendingCommand();
            if (saved) this.clearPendingCommand(saved);
            this.draftAssent.set(false); this.preview.set(null); this.previewRequest = null;
            this.revisionConflict.set(false);
            this.setError('Purchase draft was refused. Review the current maker, period, supplier and accounting data before starting a new request.');
            this.loadDrafts(0);
          }
        }
        if (failure.status === 401) this.session.clear();
      }
    });
  }
  private acceptDraftSave(value: unknown, pending: PendingDraftSave, request: number, generation: number): void {
    if (request !== this.requestId || generation !== this.session.invalidation() || pending.clientId !== this.clientId()) return;
    this.busy.set(false);
    try {
      const expectedInvoice = pending.body['invoiceId'], expectedRevision = String(pending.body['expectedRevision'] ?? '0');
      const draft = decodePurchaseDraft(value, pending.clientId, typeof expectedInvoice === 'string' ? expectedInvoice : undefined);
      if (draft.createdByUserId !== pending.actorId || BigInt(draft.revision) !== BigInt(expectedRevision) + 1n) throw new Error('Draft response identity mismatch');
      const saved = this.pendingCommand();
      if (!saved || !this.clearPendingCommand(saved)) return;
      this.draft.set(draft); this.populateDraft(draft, true); this.returnReason.set(null);
      this.editing.set(false); this.revisionConflict.set(false); this.setError(''); this.loadDrafts(0); this.refresh();
    } catch {
      this.setError('The saved purchase response did not match this exact revision. Retry the unchanged request to recover its outcome.');
      this.reconcilePendingOutcome();
    }
  }
  reconcilePendingOutcome(): void {
    const pending = this.pendingCommand();
    if (!pending || this.busy() || pending.clientId !== this.clientId() || pending.actorId !== this.userId()) return;
    const commandId = String(pending.body['commandId'] ?? '');
    if (!guid(commandId)) { this.pendingRecoveryBlocked.set(true); return; }
    const request = ++this.requestId, generation = this.session.invalidation(), client = this.clientId();
    this.busy.set(true); this.setError('');
    this.operation = this.http.get<unknown>(`${this.base()}/purchase-invoice-requests/${commandId}`, { params: { kind: pending.kind } }).pipe(timeout(15000)).subscribe({
      next: value => {
        if (request !== this.requestId || generation !== this.session.invalidation() || client !== this.clientId()) return;
        this.busy.set(false);
        try {
          const outcome = decodeCommandOutcome(value, client, pending.actorId, pending);
          if (!this.clearPendingCommand(pending)) return;
          if (pending.kind === 'DRAFT') {
            const draft = outcome.draft as PurchaseDraft;
            this.draft.set(draft); this.populateDraft(draft, true); this.returnReason.set(null); this.editing.set(false);
            this.preview.set(null); this.previewRequest = null; this.loadDrafts(0); this.refresh();
            this.recoveryNotice.set('The exact saved purchase draft was recovered from its retained command.');
          } else if (pending.kind === 'SUBMIT') {
            this.draft.set(null); this.preview.set(null); this.previewRequest = null; this.reset(); this.editing.set(true);
            this.loadDrafts(0); this.refresh(); this.recoveryNotice.set('The exact supplier-invoice submission was confirmed.');
          } else {
            this.activeReview.set(null); this.loadDrafts(0); this.refresh();
            this.recoveryNotice.set(outcome.outcome === 'RETURNED' ? 'The exact purchase return decision was acknowledged.' : 'The exact approval was confirmed by its existing posting receipt.');
          }
        } catch {
          this.setError('The saved command outcome did not match the retained actor, client, command or target. Keep the request locked and reconcile again.');
        }
      },
      error: failure => {
        if (request !== this.requestId || generation !== this.session.invalidation()) return;
        this.busy.set(false);
        this.setError(failure.status === 404
          ? 'No committed receipt is visible yet. The command remains unresolved; retry only the exact saved request.'
          : 'The command outcome could not be verified. Its exact request remains locked and recoverable.');
        if (failure.status === 401) this.session.clear();
      }
    });
  }
  retryPendingOutcome(): void {
    const pending = this.pendingCommand();
    if (!pending || this.busy() || pending.clientId !== this.clientId() || pending.actorId !== this.userId() || this.pendingRecoveryBlocked()) return;
    const commandId = String(pending.body['commandId'] ?? '');
    if (!guid(commandId)) { this.pendingRecoveryBlocked.set(true); return; }
    let url: string;
    if (pending.kind === 'DRAFT') url = `${this.base()}/purchase-invoice-drafts`;
    else if (pending.kind === 'SUBMIT' && guid(pending.targetId)) url = `${this.base()}/purchase-invoices/${pending.targetId}/submit`;
    else if (pending.kind === 'SUBMIT') { this.pendingRecoveryBlocked.set(true); return; }
    else url = `${this.base()}/purchase-invoice-reviews`;
    const request = ++this.requestId, generation = this.session.invalidation();
    this.busy.set(true); this.setError('');
    this.operation = this.http.post<unknown>(url, pending.body).pipe(timeout(20000)).subscribe({
      next: () => { if (request !== this.requestId || generation !== this.session.invalidation()) return; this.busy.set(false); this.reconcilePendingOutcome(); },
      error: failure => {
        if (request !== this.requestId || generation !== this.session.invalidation()) return;
        this.busy.set(false);
        if (failure.status === 401) this.session.clear();
        this.setError('The exact saved request did not return a confirmed response. Checking its retained command outcome; the original body remains locked.');
        this.reconcilePendingOutcome();
      }
    });
  }
  private submitBody(): Record<string, unknown> | null { const d = this.draft(); if (!d || !guid(this.apRoleId)) return null; return { commandId: crypto.randomUUID(), expectedDraftRevision: d.revision, payableRoleId: this.apRoleId, previewDigest: '' }; }
  previewRoleChanged(): void { this.preview.set(null); this.previewRequest = null; this.submitAssent.set(false); }
  previewDraft(): void { if (!this.canUseSupplier() || this.busy() || this.hasPendingCommand()) return; const d = this.draft(), body = this.submitBody(); if (!d || !body || d.supplierId !== this.supplierId) return; this.preview.set(null); this.submitAssent.set(false); this.busy.set(true); this.previewRequest = body;
    this.operation = this.http.post<PurchasePreview>(`${this.base()}/purchase-invoices/${d.invoiceId}/preview`, body).pipe(timeout(20000)).subscribe({ next: p => { this.busy.set(false); this.preview.set(p); this.submitAssent.set(false); this.setError(''); }, error: () => { this.busy.set(false); this.setError('Purchase posting preview failed. Refresh the draft and current AP role.'); this.previewRequest = null; this.submitAssent.set(false); } }); }
  submit(): void { const d = this.draft(), p = this.preview(), b = this.previewRequest; if (!d || !p || !b || this.busy() || this.hasPendingCommand() || !this.submitAssent()) return;
    const body = { ...b, previewDigest: p.digest, reviewed: true };
    if (!this.savePendingCommand('SUBMIT', body, d.invoiceId)) { this.setError('A tab-scoped exact recovery record could not be saved; submission was not sent.'); return; }
    this.retryPendingOutcome(); }
  refresh(): void { if (this.busy()) return; const request = ++this.requestId, generation = this.session.invalidation(), client = this.clientId(); this.busy.set(true);
    this.operation = this.http.get<PurchaseRow[]>(`${this.base()}/purchase-invoices`).pipe(timeout(15000)).subscribe({ next: rows => { if (request !== this.requestId || generation !== this.session.invalidation() || client !== this.clientId()) return; this.rows.set(rows); this.busy.set(false); }, error: () => { if (request === this.requestId) { this.busy.set(false); this.setError('Supplier invoice history is unavailable.'); } } }); }
  reviewPreview(row: PurchaseRow): void { if (this.busy() || this.hasPendingCommand()) return; this.busy.set(true); const request = ++this.requestId, generation = this.session.invalidation();
    this.operation = this.http.get<PurchaseReview>(`${this.base()}/purchase-invoice-submissions/${row.submissionId}/preview`).pipe(timeout(15000)).subscribe({ next: preview => { if (request !== this.requestId || generation !== this.session.invalidation()) return; this.activeReview.set(preview); this.reviewReason = ''; this.duplicateReason = ''; this.busy.set(false); }, error: () => { if (request === this.requestId) { this.busy.set(false); this.setError('Purchase invoice review preview is unavailable.'); } } }); }
  review(row: PurchaseRow, decision: 'APPROVE' | 'RETURN'): void { const p = this.activeReview(); if (!p || this.busy() || this.hasPendingCommand()) return;
    const body = { commandId: crypto.randomUUID(), submissionId: row.submissionId, decision, reason: this.reviewReason.trim(), duplicateResolutionReason: this.duplicateReason.trim(), previewDigest: p.digest, reviewed: true };
    if (!this.savePendingCommand('REVIEW', body, row.submissionId)) { this.setError('A tab-scoped exact recovery record could not be saved; the review decision was not sent.'); return; }
    this.retryPendingOutcome(); }
}
