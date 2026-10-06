import { Component, effect, inject, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, routeGuid } from '../../core/api';
import { arr, bool, dec, guid, instant, nat, nullable, obj, text } from '../../core/decode';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';

const positiveAmount = Validators.pattern(/^\d+(?:\.\d{1,6})?$/);

export const decodeInvoice = obj({
  id: guid, billingAccountId: guid, invoiceNumber: text, currency: nullable(text), subtotal: dec, tax: dec, total: dec, revision: nat, status: text,
  createdAt: instant, postedAt: nullable(instant), outstanding: dec, credited: dec, allocated: dec, canAct: bool,
  lines: arr(obj({ description: text, quantity: dec, unitPrice: dec, lineTotal: dec }), 5000),
  allocations: arr(obj({ receiptId: guid, createdAt: instant, amount: dec }), 5000),
  receipts: arr(obj({ id: guid, reference: text, currency: text, amount: dec, allocated: dec, remaining: dec, receivedAt: instant }), 100),
  receiptsHaveMore: bool,
  creditNotes: arr(obj({ id: guid, noteNumber: text, currency: text, amount: dec, reason: text, createdAt: instant }), 100),
  creditNotesHaveMore: bool,
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
      <p>The last command response was lost. Check the refreshed receipt reference, allocation balance or credit note number before preparing another command.</p>
      @if (!reconciliationLoaded()) { <button matButton="outlined" (click)="reconcile()" [disabled]="busy()">Refresh persisted billing state</button> }
      @else { <p role="status">Persisted state was refreshed. Matching records remain in the history above; the unresolved form stays disabled until you clear it and prepare a fresh reviewed action.</p>
        <button matButton="outlined" (click)="clearUnresolved()" [disabled]="busy()">Clear unresolved billing draft</button> }
    </section> }
    <audit-command-message [message]="message()" [failed]="failed()" />
  `,
  styles: `form { display: grid; gap: .65rem; max-width: 42rem; margin-block: 1rem 1.5rem; } input, select, textarea { max-width: 34rem; width: 100%; min-height: 2.75rem; } textarea { min-height: 5rem; } .check { display: flex; align-items: flex-start; gap: .65rem; } .check input { width: 1.2rem; min-height: 1.2rem; } h3 { margin-block: 1.5rem .5rem; } small, .muted { color: var(--text-muted, #4c5968); }`,
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
