import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { arr, date, dec, DecodeError, guid, instant, int, nat, nullable, obj, oneOf, str, bool } from '../../core/decode';
import { SHARED } from '../../core/ui';

export const INVOICE_STATUSES = ['DRAFT', 'REVIEW_REQUIRED', 'APPROVED', 'POSTED', 'SENT', 'CANCELLED'] as const;
type InvoiceStatus = (typeof INVOICE_STATUSES)[number];
const STATUS_LABELS: Record<InvoiceStatus, string> = {
  DRAFT: 'Draft', REVIEW_REQUIRED: 'Review required', APPROVED: 'Approved', POSTED: 'Posted', SENT: 'Marked sent', CANCELLED: 'Cancelled',
};
const BASIS_LABELS: Record<string, string> = {
  POSTED: 'Posted or marked sent', UNPOSTED: 'Not yet posted (draft, review, approved)', CANCELLED: 'Cancelled',
};
const KIND_LABELS: Record<string, string> = {
  ADVANCE: 'Advance fee milestone', FINAL: 'Balance fee milestone', TIME: 'Approved time', OTHER: 'Other firm billing',
  SOURCE_REVIEW: 'Fee source needs review',
};

const row = obj({
  id: guid, invoiceNumber: str(100), clientId: guid, clientName: str(300), kind: oneOf('ADVANCE', 'FINAL', 'TIME', 'OTHER', 'SOURCE_REVIEW'),
  engagementId: nullable(guid), engagementName: nullable(str(300)), multipleEngagements: bool, currency: str(3),
  status: oneOf(...INVOICE_STATUSES), revision: int, createdAt: instant, postedAt: nullable(instant), sentAt: nullable(instant),
  total: dec, credited: dec, allocated: dec, outstanding: dec, approvedDueDate: nullable(date), paymentTermsPending: bool,
});
export type InvoiceRow = ReturnType<typeof row>;
const total = obj({
  currency: str(3), basis: oneOf('POSTED', 'UNPOSTED', 'CANCELLED'), count: nat, total: dec,
  credited: nullable(dec), allocated: nullable(dec), outstanding: nullable(dec),
});
const page = obj({ items: arr(row, 100), total: nat, page: nat, pageSize: nat, totals: arr(total, 600),
  accessMode: nullable(oneOf('READ_ONLY', 'OPERATIONAL')) });

/** Refuses a page whose totals do not account for the matching invoices or that mixes settlement into unposted value. */
export function decodeInvoicePage(value: unknown, path: string) {
  const result = page(value, path);
  if (result.pageSize < 1 || result.pageSize > 100 || result.items.length > result.pageSize ||
      result.totals.reduce((sum, t) => sum + t.count, 0) !== result.total ||
      result.totals.some(t => [t.credited, t.allocated, t.outstanding].some(v => (v === null) === (t.basis === 'POSTED'))) ||
      result.items.some(item => (item.status === 'SENT') !== (item.sentAt !== null) || item.multipleEngagements && item.engagementId !== null))
    throw new DecodeError('Unsupported invoice page.');
  return result;
}

/** Sign of an exact decimal string without converting it to a binary number. */
function sign(value: string): number {
  if (/^-?0*(\.0*)?$/.test(value)) return 0;
  return value.startsWith('-') ? -1 : 1;
}

interface Filters { clientId: string; clientName: string; status: string; currency: string; number: string; page: number }
const NO_FILTERS: Filters = { clientId: '', clientName: '', status: '', currency: '', number: '', page: 0 };

@Component({
  selector: 'audit-firm-invoices',
  imports: [FormsModule, RouterLink, MatButtonModule, ...SHARED],
  styles: [`
    .invoice-filters { display: flex; flex-wrap: wrap; gap: 12px; align-items: end; }
    .invoice-scope { color: var(--as-text-muted); max-width: 80ch; }
    .link-button { background: none; border: 0; padding: 0; color: inherit; text-decoration: underline; cursor: pointer; font: inherit; text-align: start; }
    td small { display: block; color: var(--as-text-muted); }
  `],
  template: `
    <audit-page-header title="Firm invoices" eyebrow="Billing"
      description="Find firm invoices and open the invoice record. This list is read-only; approval, posting, receipts, credits, terms and corrections stay on each invoice." />
    <form class="invoice-filters" aria-label="Filter invoices" (submit)="$event.preventDefault(); apply()">
      <label>Invoice number <input name="number" [(ngModel)]="draft.number" maxlength="50" /></label>
      <label>Status
        <select name="status" [(ngModel)]="draft.status">
          <option value="">Any status</option>
          @for (s of statuses; track s) { <option [value]="s">{{ statusLabel(s) }}</option> }
        </select>
      </label>
      <label>Currency <input name="currency" [(ngModel)]="draft.currency" maxlength="3" size="4" /></label>
      <button matButton="outlined" type="submit">Apply filters</button>
      <button matButton type="button" (click)="clearFilters()" [disabled]="!hasFilters()">Clear filters</button>
    </form>
    @if (filters().clientId) {
      <p>Client: {{ filters().clientName }} <button matButton type="button" (click)="clearClient()">Show all clients</button></p>
    }
    <audit-state [loading]="view.loading()" [error]="view.error()" label="firm invoices" />
    @if (view.data(); as v) {
      @if (v.accessMode === 'READ_ONLY') { <p role="status">Read-only — Administrator oversight. Invoice operations require their own finance authority.</p> }
      <section class="panel" aria-labelledby="invoice-totals-heading">
        <h2 id="invoice-totals-heading">Totals by currency</h2>
        <p class="invoice-scope">Complete for all {{ v.total }} invoices matching the filters, not only this page. Currencies are never combined.
          Outstanding is today's balance after issued credit notes and receipt allocations less approved reversals; pending corrections are excluded.
          For a balance at a past date use <a routerLink="/app/finance/receivables-aging">Receivables ageing</a>.</p>
        <div class="table-scroll"><table>
          <caption>Invoice totals by currency and posting basis</caption>
          <thead><tr><th scope="col">Currency</th><th scope="col">Basis</th><th class="number" scope="col">Invoices</th><th class="number" scope="col">Invoice total</th>
            <th class="number" scope="col">Credited</th><th class="number" scope="col">Allocated</th><th class="number" scope="col">Outstanding</th></tr></thead>
          <tbody>@for (t of v.totals; track t.currency + t.basis) {
            <tr><th scope="row">{{ t.currency }}</th><td>{{ basisLabel(t.basis) }}</td><td class="number">{{ t.count }}</td><td class="number">{{ t.total | money }}</td>
              <td class="number">{{ t.credited === null ? 'Not applicable' : (t.credited | money) }}</td>
              <td class="number">{{ t.allocated === null ? 'Not applicable' : (t.allocated | money) }}</td>
              <td class="number">{{ t.outstanding === null ? 'Not receivable' : (t.outstanding | money) }}</td></tr>
          } @empty { <tr><td colspan="7">No invoices match these filters.</td></tr> }</tbody>
        </table></div>
      </section>
      <section class="panel" aria-labelledby="invoice-list-heading">
        <h2 id="invoice-list-heading">Invoices</h2>
        <div class="table-scroll"><table>
          <caption>Firm invoices, newest first, page {{ v.page + 1 }}</caption>
          <thead><tr><th scope="col">Invoice</th><th scope="col">Client</th><th scope="col">Source</th><th scope="col">Status</th><th scope="col">Currency</th>
            <th class="number" scope="col">Total</th><th class="number" scope="col">Credited</th><th class="number" scope="col">Allocated</th>
            <th class="number" scope="col">Outstanding</th><th scope="col">Approved due date</th></tr></thead>
          <tbody>@for (r of v.items; track r.id) {
            <tr><th scope="row"><a [routerLink]="['/app/practice/invoices', r.id]">{{ r.invoiceNumber }}</a></th>
              <td><button type="button" class="link-button" (click)="filterClient(r)" [attr.aria-label]="'Show only invoices for ' + r.clientName">{{ r.clientName }}</button></td>
              <td>{{ kindLabel(r.kind) }}<small>{{ r.multipleEngagements ? 'Multiple engagements' : r.engagementName ?? 'No single engagement' }}</small></td>
              <td>{{ statusLabel(r.status) }}
                @if (r.status === 'SENT') { <small>Recorded as sent by staff; no dispatch or delivery evidence.</small> }
                @if (settlement(r); as s) { <small>{{ s }}</small> }</td>
              <td>{{ r.currency }}</td><td class="number">{{ r.total | money }}</td><td class="number">{{ r.credited | money }}</td>
              <td class="number">{{ r.allocated | money }}</td>
              <td class="number">{{ r.status === 'POSTED' || r.status === 'SENT' ? (r.outstanding | money) : 'Not receivable' }}</td>
              <td>{{ r.approvedDueDate ?? 'No approved terms' }}@if (r.paymentTermsPending) { <small>Terms revision pending review</small> }</td></tr>
          } @empty { <tr><td colspan="10">No invoices on this page.</td></tr> }</tbody>
        </table></div>
        <nav aria-label="Invoice pages">
          <button matButton type="button" (click)="goTo(v.page - 1)" [disabled]="v.page === 0 || view.loading()">Previous</button>
          <span>Showing {{ v.items.length }} of {{ v.total }}</span>
          <button matButton type="button" (click)="goTo(v.page + 1)" [disabled]="(v.page + 1) * v.pageSize >= v.total || view.loading()">Next</button>
        </nav>
      </section>
    }
  `,
})
export class FirmInvoices {
  private readonly api = inject(Api);
  readonly statuses = INVOICE_STATUSES;
  draft = { status: '', currency: '', number: '' };
  readonly filters = signal<Filters>({ ...NO_FILTERS });
  readonly view = this.api.resource(() => {
    const { clientId, status, currency, number, page: index } = this.filters();
    const params = new URLSearchParams();
    if (clientId) params.set('clientId', clientId);
    if (status) params.set('status', status);
    if (currency) params.set('currency', currency);
    if (number) params.set('number', number);
    if (index > 0) params.set('page', String(index));
    const query = params.toString();
    return '/api/ui/finance/invoices' + (query ? '?' + query : '');
  }, decodeInvoicePage, 'The invoice list requires a firm-wide Finance Manager or Finance Reviewer assignment.');
  readonly hasFilters = computed(() => {
    const f = this.filters();
    return Boolean(f.clientId || f.status || f.currency || f.number);
  });

  apply(): void {
    this.filters.update(f => ({ ...f, status: this.draft.status, currency: this.draft.currency.trim().toUpperCase(),
      number: this.draft.number.trim(), page: 0 }));
  }
  clearFilters(): void { this.draft = { status: '', currency: '', number: '' }; this.filters.set({ ...NO_FILTERS }); }
  filterClient(item: InvoiceRow): void { this.filters.update(f => ({ ...f, clientId: item.clientId, clientName: item.clientName, page: 0 })); }
  clearClient(): void { this.filters.update(f => ({ ...f, clientId: '', clientName: '', page: 0 })); }
  goTo(index: number): void { if (index >= 0) this.filters.update(f => ({ ...f, page: index })); }
  statusLabel(value: string): string { return STATUS_LABELS[value as InvoiceStatus] ?? value; }
  basisLabel(value: string): string { return BASIS_LABELS[value] ?? value; }
  kindLabel(value: string): string { return KIND_LABELS[value] ?? value; }
  /** Derived settlement display for posted invoices; it is not a persisted state. */
  settlement(item: InvoiceRow): string | null {
    if (item.status !== 'POSTED' && item.status !== 'SENT') return null;
    const outstanding = sign(item.outstanding);
    if (outstanding < 0) return 'Applied beyond total — review';
    if (outstanding === 0) return 'No balance outstanding';
    return sign(item.allocated) > 0 || sign(item.credited) > 0 ? 'Partially settled' : 'Nothing applied yet';
  }
}
