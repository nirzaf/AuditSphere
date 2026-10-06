import { Component, effect, inject, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, routeGuid } from '../../core/api';
import { arr, bool, date, dec, guid, instant, nat, nullable, obj, text } from '../../core/decode';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';

const positiveAmount = Validators.pattern(/^\d+(?:\.\d{1,6})?$/);

export const decodeInvoice = obj({
  id: guid, billingAccountId: guid, invoiceNumber: text, currency: nullable(text), subtotal: dec, tax: dec, total: dec, revision: nat, status: text,
  createdAt: instant, postedAt: nullable(instant), outstanding: dec, credited: dec, allocated: dec, canAct: bool,
  lines: arr(obj({ description: text, quantity: dec, unitPrice: dec, lineTotal: dec }), 5000),
  allocations: arr(obj({ id: guid, receiptId: guid, receiptReference: text, currency: text, createdAt: instant,
    amount: dec, appliedAfterReversals: dec, reversed: dec, remainingToReverse: dec, latestReversalRevision: nat,
    canRequestReversal: bool, canReviewReversal: bool,
    reversals: arr(obj({ id: guid, receiptAllocationId: guid, revision: nat, amount: dec, reference: text, reason: text,
      status: text, submittedByUserId: guid, submittedAt: instant, reviewedByUserId: nullable(guid),
      reviewedAt: nullable(instant), reviewReason: nullable(text) }), 10000) }), 5000),
  receipts: arr(obj({ id: guid, reference: text, currency: text, amount: dec, allocated: dec, remaining: dec, receivedAt: instant }), 100),
  receiptsHaveMore: bool,
  creditNotes: arr(obj({ id: guid, noteNumber: text, currency: text, amount: dec, reason: text, createdAt: instant }), 100),
  creditNotesHaveMore: bool,
  paymentTerms: arr(obj({ id: guid, revision: nat, dueDate: date, basis: text, termsDescription: text,
    evidenceReference: text, status: text, submittedByUserId: guid, submittedAt: instant,
    reviewedByUserId: nullable(guid), reviewedAt: nullable(instant), reviewReason: nullable(text) }), 20),
  paymentTermsHaveMore: bool,
  canSubmitPaymentTerms: bool,
  canReviewPaymentTerms: bool,
  canIssueCreditNote: bool,
  canApproveInvoice: bool,
  canPostInvoice: bool,
  canSendInvoice: bool,
});

@Component({
  selector: 'audit-invoice',
  imports: [ReactiveFormsModule, RouterLink, MatButtonModule, ...SHARED],
  template: `
    <nav aria-label="Breadcrumb"><a routerLink="/app">Portfolio</a> / <a routerLink="/app/finance">Firm finance</a> / <span>Invoice</span></nav>
    <audit-page-header title="Practice invoice" eyebrow="Practice billing" description="Client invoice record, line items, receipts and credit notes under firm finance controls." />
    <audit-state [loading]="invoice.loading()" [error]="invoice.error()" label="invoice" />
    @if (invoice.data(); as i) {
      <p role="status">Invoice total {{ i.total | money }} · credited {{ i.credited | money }} · allocated {{ i.allocated | money }} · balance outstanding {{ i.outstanding | money }}</p>
      <section class="panel" aria-labelledby="invoice-heading">
        <p class="eyebrow">Status: <audit-status [value]="i.status" /> (Rev {{ i.revision }})</p>
        <h2 id="invoice-heading">{{ i.invoiceNumber }}</h2>
        <button matButton="outlined" (click)="refresh()" [disabled]="busy()">Refresh invoice</button>
        <dl class="facts"><dt>Invoice ID</dt><dd><code>{{ i.id }}</code></dd><dt>Currency</dt><dd>{{ i.currency ?? 'Default' }}</dd><dt>Subtotal</dt><dd>{{ i.subtotal | money }}</dd>
          <dt>Tax</dt><dd>{{ i.tax | money }}</dd><dt>Total amount</dt><dd><strong>{{ i.total | money }}</strong></dd><dt>Balance outstanding</dt><dd><strong>{{ i.outstanding | money }}</strong></dd>
          <dt>Created</dt><dd>{{ i.createdAt.slice(0, 16).replace('T', ' ') }} UTC</dd>@if (i.postedAt) { <dt>Posted</dt><dd>{{ i.postedAt.slice(0, 16).replace('T', ' ') }} UTC</dd> }</dl>
      </section>
      <section class="panel" aria-labelledby="payment-terms-heading">
        <h2 id="payment-terms-heading">Contractual payment terms</h2>
        <p>Due dates are recorded from contractual evidence and take effect only after an independent FinanceReviewer approves the revision. Invoices with no approved terms stay visibly undated; a pending or rejected change does not replace an earlier approved date.</p>
        <div class="table-scroll"><table><caption class="sr-only">Reviewed payment terms revision history</caption>
          <thead><tr><th scope="col">Revision</th><th scope="col">Due date</th><th scope="col">Basis and terms</th><th scope="col">Evidence reference</th><th scope="col">State</th><th scope="col">Review decision</th></tr></thead>
          <tbody>@for (t of i.paymentTerms; track t.id) { <tr><th scope="row">{{ t.revision }}</th><td>{{ t.dueDate }}</td><td>{{ t.basis }} · {{ t.termsDescription }}</td><td>{{ t.evidenceReference }}</td>
            <td><audit-status [value]="t.status" /></td><td>{{ t.reviewReason ?? 'Awaiting independent review' }}@if (t.reviewedAt) { · {{ t.reviewedAt.slice(0, 10) }} }</td></tr> }
          @empty { <tr><td colspan="6">No reviewed payment terms are recorded. This invoice will be marked undated until a supported due date is reviewed.</td></tr> }</tbody>
        </table></div>
        @if (i.paymentTermsHaveMore) { <p>Showing the latest 20 term revisions. Earlier immutable revisions remain retained in the billing history.</p> }
        @if (i.canSubmitPaymentTerms) {
          <h3>Propose a payment-terms revision</h3>
          <form [formGroup]="termsForm" (ngSubmit)="submitTerms(i)">
            <label for="terms-due-date">Contractual due date</label><input id="terms-due-date" type="date" formControlName="dueDate" />
            @if (termsForm.controls.dueDate.touched && termsForm.controls.dueDate.invalid) { <p role="alert">Select the date required by the contract.</p> }
            <label for="terms-basis">Terms evidence type</label><select id="terms-basis" formControlName="basis"><option value="CONTRACTUAL_DUE_DATE">Explicit contractual due date</option><option value="REVIEWED_TERMS_SNAPSHOT">Reviewed payment-terms snapshot</option></select>
            <label for="terms-description">Terms description</label><input id="terms-description" formControlName="termsDescription" maxlength="300" placeholder="For example, NET 30 from invoice date" />
            @if (termsForm.controls.termsDescription.touched && termsForm.controls.termsDescription.invalid) { <p role="alert">Enter a bounded description of the agreed terms.</p> }
            <label for="terms-evidence">Evidence reference</label><input id="terms-evidence" formControlName="evidenceReference" maxlength="500" placeholder="Engagement-letter revision or approved records ID" />
            <small>Enter a stored document or records reference. Do not paste a URL, preauthenticated link, password or token.</small>
            @if (termsForm.controls.evidenceReference.touched && termsForm.controls.evidenceReference.invalid) { <p role="alert">Enter a document reference only, without URL or query-string data.</p> }
            <p>Review: submit due date {{ termsForm.controls.dueDate.value || '—' }} under {{ termsForm.controls.basis.value }} using evidence “{{ termsForm.controls.evidenceReference.value || '—' }}”. The due date is not active until a separate reviewer approves it.</p>
            <label class="check"><input type="checkbox" formControlName="reviewed" /> I checked the due date against the referenced contractual evidence.</label>
            <button matButton="filled" type="submit" [disabled]="busy() || uncertain() || termsForm.invalid">Submit terms for independent review</button>
          </form>
        }
        @if (i.canReviewPaymentTerms) {
          @if (i.paymentTerms[0]; as t) {
            <h3>Independent terms review · Revision {{ t.revision }}</h3>
            <p>Review due date {{ t.dueDate }}, basis {{ t.basis }}, terms “{{ t.termsDescription }}” and evidence {{ t.evidenceReference }}.</p>
            <form [formGroup]="termsReviewForm">
              <label for="terms-review-reason">Review reason</label><textarea id="terms-review-reason" formControlName="reason" maxlength="1000" rows="3"></textarea>
              @if (termsReviewForm.controls.reason.touched && termsReviewForm.controls.reason.invalid) { <p role="alert">Enter the reason for this decision.</p> }
              <label class="check"><input type="checkbox" formControlName="reviewed" /> I independently checked this payment-terms revision and its evidence.</label>
              <div class="actions"><button matButton="filled" (click)="reviewTerms(t.id, true)" [disabled]="busy() || uncertain() || termsReviewForm.invalid">Approve terms</button>
                <button matButton="outlined" (click)="reviewTerms(t.id, false)" [disabled]="busy() || uncertain() || termsReviewForm.invalid">Reject terms</button></div>
            </form>
          }
        } @else if (i.paymentTerms[0]?.status === 'PENDING_REVIEW') { <p>A separate FinanceReviewer must decide this revision. The submitter cannot approve it.</p> }
      </section>
      <section class="panel" aria-labelledby="lines-heading">
        <h2 id="lines-heading">Invoice line items</h2>
        <div class="table-scroll"><table><caption class="sr-only">Invoice lines</caption>
          <thead><tr><th scope="col">Description</th><th scope="col" class="number">Quantity</th><th scope="col" class="number">Unit price</th><th scope="col" class="number">Total</th></tr></thead>
          <tbody>@for (l of i.lines; track $index) { <tr><td>{{ l.description }}</td><td class="number">{{ l.quantity | money }}</td><td class="number">{{ l.unitPrice | money }}</td><td class="number">{{ l.lineTotal | money }}</td></tr> }
          @empty { <tr><td colspan="4">No line items recorded on this invoice.</td></tr> }</tbody>
          <tfoot><tr><td colspan="3" class="number">Total</td><td class="number">{{ linesTotal(i.lines) | money }}</td></tr></tfoot></table></div>
      </section>
      <section class="panel" aria-labelledby="receipts-heading">
        <h2 id="receipts-heading">Payments and receipt allocations</h2>
        <div class="table-scroll"><table><caption class="sr-only">Recent receipts for this client billing account</caption><thead><tr>
          <th scope="col">Transaction reference</th><th scope="col">Received</th><th scope="col">Currency</th><th scope="col" class="number">Receipt amount</th><th scope="col" class="number">Allocated</th><th scope="col" class="number">Available</th>
        </tr></thead><tbody>
          @for (r of receiptHistory(); track r.id) { <tr><td>{{ r.reference }}</td><td>{{ r.receivedAt.slice(0, 16).replace('T', ' ') }} UTC</td><td>{{ r.currency }}</td><td class="number">{{ r.amount | money }}</td><td class="number">{{ r.allocated | money }}</td><td class="number">{{ r.remaining | money }}</td></tr> }
          @empty { <tr><td colspan="6">No receipts are recorded for this client billing account.</td></tr> }
        </tbody></table></div>
        @if (receiptHistoryHaveMore()) { <button matButton="outlined" (click)="loadOlderReceipts()" [disabled]="receiptHistoryBusy() || busy() || uncertain()">{{ receiptHistoryBusy() ? 'Loading older receipts…' : 'Load older receipts' }}</button> }
        @if (receiptHistoryMessage()) { <p role="alert">{{ receiptHistoryMessage() }}</p> }
        <h3>Invoice allocation history</h3>
        <p>An approved reversal unapplies part of a receipt allocation and makes that amount available for reallocation. The report uses the independent review date; pending or rejected requests do not change balances.</p>
        <div class="table-scroll"><table><caption class="sr-only">Receipt allocations and independently reviewed reversals for this invoice</caption>
          <thead><tr><th scope="col">Receipt reference</th><th scope="col">Allocated</th><th scope="col" class="number">Original amount</th><th scope="col" class="number">Applied after reversals</th><th scope="col" class="number">Reversed</th><th scope="col">Reversal history and action</th></tr></thead>
          <tbody>@for (a of i.allocations; track a.id) { <tr>
            <th scope="row">{{ a.receiptReference }}<small>{{ a.createdAt.slice(0, 10) }} · {{ a.currency }}</small></th>
            <td>{{ a.createdAt.slice(0, 16).replace('T', ' ') }} UTC</td><td class="number">{{ a.amount | money }} {{ a.currency }}</td>
            <td class="number">{{ a.appliedAfterReversals | money }} {{ a.currency }}</td><td class="number">{{ a.reversed | money }} {{ a.currency }}</td>
            <td>
              @for (r of a.reversals; track r.id) {
                <div class="reversal-history"><strong>Revision {{ r.revision }} · <audit-status [value]="r.status" /></strong>
                  <span>{{ r.amount | money }} {{ a.currency }} · {{ r.reason }} · evidence {{ r.reference }}</span>
                  <small>{{ r.reviewReason ?? 'Awaiting independent review' }}@if (r.reviewedAt) { · reviewed {{ r.reviewedAt.slice(0, 10) }} }</small>
                  @if (a.canReviewReversal && r.status === 'PENDING_REVIEW') {
                    <div class="actions"><button matButton="filled" (click)="reviewAllocationReversal(r.id, true)" [disabled]="busy() || uncertain() || reversalReviewForm.invalid">Approve reversal</button>
                      <button matButton="outlined" (click)="reviewAllocationReversal(r.id, false)" [disabled]="busy() || uncertain() || reversalReviewForm.invalid">Reject reversal</button></div>
                  }
                </div>
              } @empty { <span>No reversals recorded.</span> }
              @if (a.canRequestReversal && +a.remainingToReverse > 0) {
                <button matButton="outlined" (click)="prepareReversal(a)" [disabled]="busy() || uncertain()">Request reversal (up to {{ a.remainingToReverse | money }} {{ a.currency }})</button>
              }
            </td>
          </tr> } @empty { <tr><td colspan="6">No receipt allocations have been applied to this invoice.</td></tr> }</tbody>
        </table></div>
        @if (selectedAllocation(); as allocation) {
          <h3>Request allocation reversal · {{ allocation.receiptReference }}</h3>
          <form [formGroup]="reversalForm" (ngSubmit)="requestAllocationReversal(allocation)">
            <label for="reversal-amount">Amount to unapply ({{ allocation.currency }})</label>
            <input id="reversal-amount" inputmode="decimal" autocomplete="off" formControlName="amount" />
            @if (reversalForm.controls.amount.touched && reversalForm.controls.amount.invalid) { <p role="alert">Enter a positive amount with no more than six decimal places.</p> }
            <label for="reversal-reference">Bank correction or supporting document reference</label>
            <input id="reversal-reference" formControlName="reference" maxlength="200" autocomplete="off" />
            @if (reversalForm.controls.reference.touched && reversalForm.controls.reference.invalid) { <p role="alert">Enter a bounded evidence reference without URL or query-string data.</p> }
            <label for="reversal-reason">Reason</label><textarea id="reversal-reason" formControlName="reason" maxlength="1000" rows="3"></textarea>
            @if (reversalForm.controls.reason.touched && reversalForm.controls.reason.invalid) { <p role="alert">Enter a reason up to 1,000 characters.</p> }
            <p>Review: request that {{ reversalForm.controls.amount.value || '—' }} {{ allocation.currency }} be unapplied from {{ allocation.receiptReference }}. A separate FinanceReviewer must decide before the invoice balance changes.</p>
            <label class="check"><input type="checkbox" formControlName="reviewed" /> I checked the amount and evidence reference.</label>
            <div class="actions"><button matButton="filled" type="submit" [disabled]="busy() || uncertain() || reversalForm.invalid">Submit reversal for independent review</button>
              <button matButton="outlined" type="button" (click)="selectedAllocation.set(null)">Cancel</button></div>
          </form>
        }
        @if (hasPendingAllocationReversal(i)) {
          <form [formGroup]="reversalReviewForm">
            <label for="reversal-review-reason">Independent reviewer decision reason</label><textarea id="reversal-review-reason" formControlName="reason" maxlength="1000" rows="3"></textarea>
            @if (reversalReviewForm.controls.reason.touched && reversalReviewForm.controls.reason.invalid) { <p role="alert">Enter a decision reason before approving or rejecting.</p> }
            <label class="check"><input type="checkbox" formControlName="reviewed" /> I independently verified the reversal and evidence.</label>
          </form>
        }
        @if (i.canAct) {
          <h3>Record a payment</h3>
          <p>Record the bank or cheque reference first. Then allocate available funds to posted invoices in this billing account.</p>
          <form [formGroup]="receiptForm" (ngSubmit)="recordReceipt(i)">
            <label for="receipt-amount">Payment amount ({{ i.currency ?? 'billing currency' }})</label>
            <input id="receipt-amount" inputmode="decimal" autocomplete="off" formControlName="amount" aria-describedby="receipt-amount-help" />
            <small id="receipt-amount-help">Enter a positive amount with up to six decimal places.</small>
            @if (receiptForm.controls.amount.touched && receiptForm.controls.amount.invalid) { <p role="alert">Enter a positive amount with no more than six decimal places.</p> }
            <label for="receipt-reference">Bank or cheque transaction reference</label>
            <input id="receipt-reference" formControlName="reference" maxlength="200" autocomplete="off" />
            @if (receiptForm.controls.reference.touched && receiptForm.controls.reference.invalid) { <p role="alert">Enter a transaction reference up to 200 characters.</p> }
            <p>Review: record {{ receiptForm.controls.amount.value || '—' }} {{ i.currency ?? '' }} against {{ i.invoiceNumber }}'s billing account, reference “{{ receiptForm.controls.reference.value || '—' }}”.</p>
            <label class="check"><input type="checkbox" formControlName="reviewed" /> I reviewed the payment amount and transaction reference.</label>
            <button matButton="filled" type="submit" [disabled]="busy() || uncertain() || receiptForm.invalid">Record receipt</button>
          </form>
          @if (i.status === 'POSTED' || i.status === 'SENT') {
            <h3>Allocate a recorded receipt</h3>
            @if (receiptsWithBalance().length) {
              <form [formGroup]="allocationForm" (ngSubmit)="allocate(i)">
                <label for="allocation-receipt">Receipt and available balance</label>
                <select id="allocation-receipt" formControlName="receiptId" (change)="selectReceipt($event)">
                  <option value="">Select a receipt</option>
                  @for (r of receiptsWithBalance(); track r.id) { <option [value]="r.id">{{ r.reference }} — {{ r.remaining | money }} {{ r.currency }} available</option> }
                </select>
                <label for="allocation-amount">Amount to allocate ({{ i.currency ?? 'billing currency' }})</label>
                <input id="allocation-amount" inputmode="decimal" autocomplete="off" formControlName="amount" />
                @if (allocationForm.controls.amount.touched && allocationForm.controls.amount.invalid) { <p role="alert">Enter a positive amount with no more than six decimal places.</p> }
                <p>Review: apply {{ allocationForm.controls.amount.value || '—' }} to invoice {{ i.invoiceNumber }}. The server checks both remaining balances.</p>
                <label class="check"><input type="checkbox" formControlName="reviewed" /> I reviewed this receipt, invoice and allocation amount.</label>
                <button matButton="filled" type="submit" [disabled]="busy() || uncertain() || allocationForm.invalid">Allocate receipt</button>
              </form>
            } @else { <p>No unallocated receipts are available for this billing account.</p> }
          } @else { <p>Receipt allocation is available after the invoice is posted.</p> }
        }
      </section>
      <section class="panel" aria-labelledby="credits-heading">
        <h2 id="credits-heading">Credit notes</h2>
        <div class="table-scroll"><table><caption class="sr-only">Issued credit notes for this invoice</caption><thead><tr><th scope="col">Note number</th><th scope="col">Issued</th><th scope="col">Reason</th><th scope="col" class="number">Amount</th></tr></thead>
          <tbody>@for (c of creditHistory(); track c.id) { <tr><td>{{ c.noteNumber }}</td><td>{{ c.createdAt.slice(0, 16).replace('T', ' ') }} UTC</td><td>{{ c.reason }}</td><td class="number">{{ c.amount | money }} {{ c.currency }}</td></tr> }
          @empty { <tr><td colspan="4">No credit notes have been issued for this invoice.</td></tr> }</tbody></table></div>
        @if (creditHistoryHaveMore()) { <button matButton="outlined" (click)="loadOlderCreditNotes()" [disabled]="creditHistoryBusy() || busy() || uncertain()">{{ creditHistoryBusy() ? 'Loading older credit notes…' : 'Load older credit notes' }}</button> }
        @if (creditHistoryMessage()) { <p role="alert">{{ creditHistoryMessage() }}</p> }
        @if (i.canIssueCreditNote && (i.status === 'POSTED' || i.status === 'SENT')) {
          <h3>Issue a credit note</h3>
          <form [formGroup]="creditForm" (ngSubmit)="issueCredit(i)">
            <label for="credit-note-number">Credit note number</label><input id="credit-note-number" formControlName="noteNumber" maxlength="64" autocomplete="off" />
            @if (creditForm.controls.noteNumber.touched && creditForm.controls.noteNumber.invalid) { <p role="alert">Enter a credit note number up to 64 characters.</p> }
            <label for="credit-amount">Credit amount ({{ i.currency ?? 'billing currency' }})</label><input id="credit-amount" inputmode="decimal" autocomplete="off" formControlName="amount" />
            @if (creditForm.controls.amount.touched && creditForm.controls.amount.invalid) { <p role="alert">Enter a positive amount with no more than six decimal places.</p> }
            <label for="credit-reason">Reason</label><textarea id="credit-reason" formControlName="reason" maxlength="1000" rows="3"></textarea>
            @if (creditForm.controls.reason.touched && creditForm.controls.reason.invalid) { <p role="alert">Enter a reason up to 1,000 characters.</p> }
            <p>Review: issue {{ creditForm.controls.amount.value || '—' }} {{ i.currency ?? '' }} against {{ i.invoiceNumber }}. Credits cannot exceed its remaining value.</p>
            <label class="check"><input type="checkbox" formControlName="reviewed" /> I reviewed the credit note number, amount and reason.</label>
            <button matButton="filled" type="submit" [disabled]="busy() || uncertain() || creditForm.invalid">Issue credit note</button>
          </form>
        }
      </section>
      @if (i.canAct) {
        <section class="panel" aria-labelledby="workflow-heading">
          <h2 id="workflow-heading">Invoice workflow actions</h2>
          @switch (i.status) {
            @case ('REVIEW_REQUIRED') {
              @if (i.canApproveInvoice) { <button matButton="filled" (click)="act(i.id, 'approve', 'Invoice approved.')" [disabled]="busy() || uncertain()">Approve invoice</button> }
              @else { <p>Approval requires a separate authorized FinanceReviewer.</p> }
            }
            @case ('APPROVED') {
              @if (i.canPostInvoice) { <button matButton="filled" (click)="act(i.id, 'post', 'Invoice posted and frozen.')" [disabled]="busy() || uncertain()">Post invoice (freeze & emit ledger event)</button> }
              @else { <p>Posting requires an authorized FinanceManager and an approved firm finance profile in the invoice currency.</p> }
            }
            @case ('POSTED') {
              @if (i.canSendInvoice) { <button matButton="filled" (click)="act(i.id, 'send', 'Invoice marked as sent.')" [disabled]="busy() || uncertain()">Mark sent to client</button> }
            }
          }
        </section>
      }
    }
    @if (uncertain()) { <section class="panel" aria-labelledby="uncertain-heading"><h2 id="uncertain-heading">Verify the saved billing state</h2>
      <p>The last command response was lost. Refresh the invoice and billing history to confirm the saved status or record before preparing another action.</p>
      @if (!reconciliationLoaded()) { <button matButton="outlined" (click)="reconcile()" [disabled]="busy()">Refresh persisted billing state</button> }
      @else { <p role="status">Persisted state was refreshed. Matching records remain in the history above; the unresolved form stays disabled until you clear it and prepare a fresh reviewed action.</p>
        <button matButton="outlined" (click)="clearUnresolved()" [disabled]="busy()">Clear unresolved billing draft</button> }
    </section> }
    <audit-command-message [message]="message()" [failed]="failed()" />
  `,
  styles: `form { display: grid; gap: .65rem; max-width: 42rem; margin-block: 1rem 1.5rem; } input, select, textarea { max-width: 34rem; width: 100%; min-height: 2.75rem; } textarea { min-height: 5rem; } .check { display: flex; align-items: flex-start; gap: .65rem; } .check input { width: 1.2rem; min-height: 1.2rem; } h3 { margin-block: 1.5rem .5rem; } small, .muted { color: var(--text-muted, #4c5968); } .actions { display:flex; gap:.75rem; flex-wrap:wrap; } .reversal-history { display:grid; gap:.2rem; margin-block:.5rem; }`,
})
export class InvoiceDetail {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly fb = inject(FormBuilder).nonNullable;
  readonly id = routeGuid();
  readonly invoice = this.api.resource(() => this.id() ? `/api/ui/finance/invoices/${this.id()}` : null, decodeInvoice,
    'The requested invoice was not found in the current firm scope.');
  readonly receiptHistory = signal<ReturnType<typeof decodeInvoice>['receipts']>([]);
  readonly receiptHistoryHaveMore = signal(false);
  readonly receiptHistoryBusy = signal(false);
  readonly receiptHistoryMessage = signal('');
  readonly creditHistory = signal<ReturnType<typeof decodeInvoice>['creditNotes']>([]);
  readonly creditHistoryHaveMore = signal(false);
  readonly creditHistoryBusy = signal(false);
  readonly creditHistoryMessage = signal('');
  private historyVisit = 0;
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly reconciliationLoaded = signal(false);
  readonly message = signal('');
  readonly failed = signal(false);
  readonly receiptForm = this.fb.group({ amount: ['', [Validators.required, positiveAmount]], reference: ['', [Validators.required, Validators.maxLength(200)]], reviewed: [false, Validators.requiredTrue] });
  readonly allocationForm = this.fb.group({ receiptId: ['', Validators.required], amount: ['', [Validators.required, positiveAmount]], reviewed: [false, Validators.requiredTrue] });
  readonly creditForm = this.fb.group({ noteNumber: ['', [Validators.required, Validators.maxLength(64)]], amount: ['', [Validators.required, positiveAmount]], reason: ['', [Validators.required, Validators.maxLength(1000)]], reviewed: [false, Validators.requiredTrue] });
  readonly termsForm = this.fb.group({ dueDate: ['', Validators.required], basis: ['CONTRACTUAL_DUE_DATE', Validators.required],
    termsDescription: ['', [Validators.required, Validators.maxLength(300)]],
    evidenceReference: ['', [Validators.required, Validators.maxLength(500), Validators.pattern(/^(?!https?:\/\/)[^?#]+$/i)]],
    reviewed: [false, Validators.requiredTrue] });
  readonly termsReviewForm = this.fb.group({ reason: ['', [Validators.required, Validators.maxLength(1000)]], reviewed: [false, Validators.requiredTrue] });
  readonly reversalForm = this.fb.group({ amount: ['', [Validators.required, positiveAmount]],
    reference: ['', [Validators.required, Validators.maxLength(200), Validators.pattern(/^(?!https?:\/\/)[^?#]+$/i)]],
    reason: ['', [Validators.required, Validators.maxLength(1000)]], reviewed: [false, Validators.requiredTrue] });
  readonly reversalReviewForm = this.fb.group({ reason: ['', [Validators.required, Validators.maxLength(1000)]], reviewed: [false, Validators.requiredTrue] });
  readonly selectedAllocation = signal<ReturnType<typeof decodeInvoice>['allocations'][number] | null>(null);

  constructor() {
    effect(() => {
      const current = this.invoice.data();
      untracked(() => {
        this.historyVisit++;
        this.receiptHistory.set(current?.receipts ?? []);
        this.receiptHistoryHaveMore.set(current?.receiptsHaveMore ?? false);
        this.receiptHistoryBusy.set(false);
        this.receiptHistoryMessage.set('');
        this.creditHistory.set(current?.creditNotes ?? []);
        this.creditHistoryHaveMore.set(current?.creditNotesHaveMore ?? false);
        this.creditHistoryBusy.set(false);
        this.creditHistoryMessage.set('');
        this.selectedAllocation.set(null);
      });
    });
  }

  receiptsWithBalance(): ReturnType<typeof decodeInvoice>['receipts'] {
    return this.receiptHistory().filter((r) => this.isPositive(r.remaining));
  }

  selectReceipt(event: Event): void {
    const id = (event.target as HTMLSelectElement).value;
    const receipt = this.receiptHistory().find((x) => x.id === id);
    this.allocationForm.patchValue({ amount: receipt?.remaining ?? '', reviewed: false });
  }

  async loadOlderReceipts(): Promise<void> {
    const last = this.receiptHistory().at(-1);
    const invoiceId = this.id();
    if (!invoiceId || !last || !this.receiptHistoryHaveMore() || this.receiptHistoryBusy() || this.busy() || this.uncertain()) return;
    const visit = this.historyVisit;
    const generation = this.session.invalidation();
    this.receiptHistoryBusy.set(true);
    this.receiptHistoryMessage.set('');
    try {
      const page = await this.api.get(`/api/ui/finance/invoices/${invoiceId}?receiptBefore=${encodeURIComponent(last.receivedAt)}&receiptBeforeId=${last.id}`, decodeInvoice);
      if (visit !== this.historyVisit || generation !== this.session.invalidation() || invoiceId !== this.id()) return;
      this.receiptHistory.update((items) => this.appendPage(items, page.receipts));
      this.receiptHistoryHaveMore.set(page.receiptsHaveMore);
    } catch {
      if (visit === this.historyVisit && generation === this.session.invalidation() && invoiceId === this.id())
        this.receiptHistoryMessage.set('Older receipts could not be loaded. Try again shortly.');
    } finally {
      if (visit === this.historyVisit) this.receiptHistoryBusy.set(false);
    }
  }

  async loadOlderCreditNotes(): Promise<void> {
    const last = this.creditHistory().at(-1);
    const invoiceId = this.id();
    if (!invoiceId || !last || !this.creditHistoryHaveMore() || this.creditHistoryBusy() || this.busy() || this.uncertain()) return;
    const visit = this.historyVisit;
    const generation = this.session.invalidation();
    this.creditHistoryBusy.set(true);
    this.creditHistoryMessage.set('');
    try {
      const page = await this.api.get(`/api/ui/finance/invoices/${invoiceId}?creditBefore=${encodeURIComponent(last.createdAt)}&creditBeforeId=${last.id}`, decodeInvoice);
      if (visit !== this.historyVisit || generation !== this.session.invalidation() || invoiceId !== this.id()) return;
      this.creditHistory.update((items) => this.appendPage(items, page.creditNotes));
      this.creditHistoryHaveMore.set(page.creditNotesHaveMore);
    } catch {
      if (visit === this.historyVisit && generation === this.session.invalidation() && invoiceId === this.id())
        this.creditHistoryMessage.set('Older credit notes could not be loaded. Try again shortly.');
    } finally {
      if (visit === this.historyVisit) this.creditHistoryBusy.set(false);
    }
  }

  private appendPage<T extends { id: string }>(current: T[], page: T[]): T[] {
    const seen = new Set(current.map((item) => item.id));
    return [...current, ...page.filter((item) => !seen.has(item.id))];
  }

  async recordReceipt(i: ReturnType<typeof decodeInvoice>): Promise<void> {
    if (this.receiptForm.invalid || this.busy() || this.uncertain()) { this.receiptForm.markAllAsTouched(); return; }
    const input = this.receiptForm.getRawValue();
    await this.run(`/api/ui/finance/billing-accounts/${i.billingAccountId}/receipts`, input,
      'Receipt recorded. Refresh or allocate it to a posted invoice when ready.', () => this.receiptForm.reset({ amount: '', reference: '', reviewed: false }));
  }

  async allocate(i: ReturnType<typeof decodeInvoice>): Promise<void> {
    if (this.allocationForm.invalid || this.busy() || this.uncertain()) { this.allocationForm.markAllAsTouched(); return; }
    const input = this.allocationForm.getRawValue();
    await this.run(`/api/ui/finance/receipts/${input.receiptId}/allocations`, { invoiceId: i.id, amount: input.amount, reviewed: input.reviewed },
      'Receipt allocation recorded against this invoice.', () => this.allocationForm.reset({ receiptId: '', amount: '', reviewed: false }));
  }

  async issueCredit(i: ReturnType<typeof decodeInvoice>): Promise<void> {
    if (this.creditForm.invalid || this.busy() || this.uncertain()) { this.creditForm.markAllAsTouched(); return; }
    await this.run(`/api/ui/finance/invoices/${i.id}/credit-notes`, this.creditForm.getRawValue(),
      'Credit note issued. The invoice balance has been refreshed.', () => this.creditForm.reset({ noteNumber: '', amount: '', reason: '', reviewed: false }));
  }

  async submitTerms(i: ReturnType<typeof decodeInvoice>): Promise<void> {
    if (this.termsForm.invalid || this.busy() || this.uncertain()) { this.termsForm.markAllAsTouched(); return; }
    const terms = this.termsForm.getRawValue();
    await this.run(`/api/ui/finance/invoices/${i.id}/payment-terms`,
      { ...terms, expectedRevision: i.paymentTerms[0]?.revision ?? 0 },
      'Payment terms submitted. They become effective only after independent review.',
      () => this.termsForm.reset({ dueDate: '', basis: 'CONTRACTUAL_DUE_DATE', termsDescription: '', evidenceReference: '', reviewed: false }));
  }

  async reviewTerms(revisionId: string, approve: boolean): Promise<void> {
    if (this.termsReviewForm.invalid || this.busy() || this.uncertain()) { this.termsReviewForm.markAllAsTouched(); return; }
    const { reason, reviewed } = this.termsReviewForm.getRawValue();
    await this.run(`/api/ui/finance/invoice-payment-terms/${revisionId}/review`, { approve, reason, reviewed },
      approve ? 'Payment terms approved and effective from the review date.' : 'Payment terms rejected; the previous approved terms remain in effect.',
      () => this.termsReviewForm.reset({ reason: '', reviewed: false }));
  }

  prepareReversal(allocation: ReturnType<typeof decodeInvoice>['allocations'][number]): void {
    if (this.busy() || this.uncertain() || !allocation.canRequestReversal) return;
    this.selectedAllocation.set(allocation);
    this.reversalForm.reset({ amount: allocation.remainingToReverse, reference: '', reason: '', reviewed: false });
  }

  hasPendingAllocationReversal(i: ReturnType<typeof decodeInvoice>): boolean {
    return i.allocations.some((allocation) => allocation.canReviewReversal &&
      allocation.reversals.some((reversal) => reversal.status === 'PENDING_REVIEW'));
  }

  async requestAllocationReversal(allocation: ReturnType<typeof decodeInvoice>['allocations'][number]): Promise<void> {
    if (this.reversalForm.invalid || this.busy() || this.uncertain() || !allocation.canRequestReversal) {
      this.reversalForm.markAllAsTouched(); return;
    }
    const request = this.reversalForm.getRawValue();
    await this.run(`/api/ui/finance/allocations/${allocation.id}/reversals`,
      { ...request, expectedRevision: allocation.latestReversalRevision },
      'Allocation reversal submitted. It takes effect only after a different FinanceReviewer approves it.',
      () => { this.reversalForm.reset({ amount: '', reference: '', reason: '', reviewed: false }); this.selectedAllocation.set(null); });
  }

  async reviewAllocationReversal(id: string, approve: boolean): Promise<void> {
    if (this.reversalReviewForm.invalid || this.busy() || this.uncertain()) { this.reversalReviewForm.markAllAsTouched(); return; }
    const { reason, reviewed } = this.reversalReviewForm.getRawValue();
    await this.run(`/api/ui/finance/allocation-reversals/${id}/review`, { approve, reason, reviewed },
      approve ? 'Receipt allocation reversed from the invoice; the approved amount is available for reallocation.' : 'Allocation reversal rejected; the original application remains in place.',
      () => this.reversalReviewForm.reset({ reason: '', reviewed: false }));
  }

  async act(id: string, action: string, ok: string): Promise<void> {
    await this.run(`/api/ui/finance/invoices/${id}/${action}`, {}, ok);
  }

  async refresh(): Promise<void> {
    if (this.busy()) return;
    this.invoice.reload();
  }

  async reconcile(): Promise<void> {
    const id = this.id();
    if (!id || this.busy()) return;
    const generation = this.session.invalidation();
    this.busy.set(true);
    try {
      await this.api.get(`/api/ui/finance/invoices/${id}`, decodeInvoice);
      if (generation !== this.session.invalidation()) return;
      this.invoice.reload();
      this.reconciliationLoaded.set(true);
      this.failed.set(false);
      this.message.set('Persisted billing state refreshed. Review the current record before preparing another action.');
    } catch {
      if (generation !== this.session.invalidation()) return;
      this.failed.set(true);
      this.message.set('Billing state could not be verified. Keep this request unresolved and retry the refresh.');
    } finally { this.busy.set(false); }
  }

  clearUnresolved(): void {
    if (!this.reconciliationLoaded() || this.busy()) return;
    this.receiptForm.reset({ amount: '', reference: '', reviewed: false });
    this.allocationForm.reset({ receiptId: '', amount: '', reviewed: false });
    this.creditForm.reset({ noteNumber: '', amount: '', reason: '', reviewed: false });
    this.termsForm.reset({ dueDate: '', basis: 'CONTRACTUAL_DUE_DATE', termsDescription: '', evidenceReference: '', reviewed: false });
    this.termsReviewForm.reset({ reason: '', reviewed: false });
    this.reversalForm.reset({ amount: '', reference: '', reason: '', reviewed: false });
    this.reversalReviewForm.reset({ reason: '', reviewed: false });
    this.selectedAllocation.set(null);
    this.uncertain.set(false);
    this.reconciliationLoaded.set(false);
    this.failed.set(false);
    this.message.set('Unresolved fields cleared. Review the current billing records before preparing a new action.');
  }

  private async run(url: string, body: unknown, success: string, afterSuccess?: () => void): Promise<void> {
    if (this.busy() || this.uncertain()) return;
    const generation = this.session.invalidation();
    this.busy.set(true); this.failed.set(false); this.message.set('');
    try {
      const result = await this.api.command(url, body);
      if (generation !== this.session.invalidation()) return;
      if (!result.ok) {
        this.failed.set(true); this.message.set(result.message);
        if (result.unknown) { this.uncertain.set(true); this.reconciliationLoaded.set(false); }
        return;
      }
      afterSuccess?.();
      this.message.set(success);
      this.invoice.reload();
    } finally { this.busy.set(false); }
  }

  private sum(values: string[]): string {
    const places = Math.max(0, ...values.map((v) => (v.split('.')[1] ?? '').length));
    const total = values.reduce((acc, v) => {
      const neg = v.startsWith('-'); const [whole, fraction = ''] = (neg ? v.slice(1) : v).split('.');
      return acc + (neg ? -1n : 1n) * BigInt(whole + fraction.padEnd(places, '0'));
    }, 0n);
    const neg = total < 0n; const digits = (neg ? -total : total).toString().padStart(places + 1, '0');
    return (neg ? '-' : '') + (places ? `${digits.slice(0, -places)}.${digits.slice(-places)}` : digits);
  }

  private isPositive(value: string): boolean {
    const [whole, fraction = ''] = value.split('.');
    return BigInt(whole + fraction.padEnd(6, '0')) > 0n;
  }

  /** Exact-decimal line sum (scaled BigInt) so the footer total always equals the rendered lines. */
  linesTotal(lines: ReturnType<typeof decodeInvoice>['lines']): string { return this.sum(lines.map((line) => line.lineTotal)); }
}
