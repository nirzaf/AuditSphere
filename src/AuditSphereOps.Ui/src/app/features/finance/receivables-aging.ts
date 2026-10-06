import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { arr, date, dec, guid, int, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

export const decodeReceivablesAging = obj({
  asOfDate: date, dateBasis: text, bucketPolicy: text,
  rows: arr(obj({
    invoiceId: guid, clientId: guid, legalClientName: text, engagementId: nullable(guid), engagementName: nullable(text),
    invoiceType: text, invoiceNumber: text, dueDate: nullable(date), originalAmount: dec,
    appliedReceipts: dec, reversedReceipts: dec, appliedCredits: dec, outstanding: dec, daysOverdue: nullable(int),
    bucket: text, currency: text, paymentTermsStatus: text, financeRecipients: arr(text, 100),
  }), 10000),
  clientCurrencySubtotals: arr(obj({ clientId: nullable(guid), legalClientName: nullable(text), engagementId: nullable(guid),
    engagementName: nullable(text), currency: text, invoiceCount: int, originalAmount: dec,
    appliedReceipts: dec, reversedReceipts: dec, appliedCredits: dec, outstanding: dec }), 10000),
  engagementCurrencySubtotals: arr(obj({ clientId: nullable(guid), legalClientName: nullable(text), engagementId: nullable(guid),
    engagementName: nullable(text), currency: text, invoiceCount: int, originalAmount: dec,
    appliedReceipts: dec, reversedReceipts: dec, appliedCredits: dec, outstanding: dec }), 10000),
  currencySubtotals: arr(obj({ clientId: nullable(guid), legalClientName: nullable(text), engagementId: nullable(guid),
    engagementName: nullable(text), currency: text, invoiceCount: int, originalAmount: dec,
    appliedReceipts: dec, reversedReceipts: dec, appliedCredits: dec, outstanding: dec }), 10000),
});

@Component({
  selector: 'audit-receivables-aging',
  imports: [FormsModule, RouterLink, MatButtonModule, ...SHARED],
  template: `
    <a routerLink="/app/finance">← Back to firm finance</a>
    <audit-page-header title="Firm receivables ageing" eyebrow="Firm billing"
      description="Review collectible advance and final fees by client, engagement and currency. Balances use posted invoices and transactions effective by the selected UTC date." />
    <section class="panel filters" aria-labelledby="report-date-heading">
      <div><h2 id="report-date-heading">Report date</h2>
        <label for="as-of-date">As of (UTC)</label>
        <input id="as-of-date" type="date" [ngModel]="asOf()" (ngModelChange)="changeDate($event)" />
      </div>
      <div class="actions">
        <button matButton="outlined" (click)="report.reload()">Refresh report</button>
        <button matButton="filled" (click)="exportCsv()" [disabled]="exporting() || !report.data()">{{ exporting() ? 'Preparing CSV…' : 'Export CSV' }}</button>
      </div>
    </section>
    <audit-state [loading]="report.loading()" [error]="report.error()" label="firm receivables report" />
    @if (message()) { <p [attr.role]="failed() ? 'alert' : 'status'">{{ message() }}</p> }
    @if (report.data(); as r) {
      <section class="panel policy" aria-label="Ageing policy">
        <p><strong>As of {{ r.asOfDate }}</strong> · {{ r.dateBasis }}</p>
        <p>{{ r.bucketPolicy }}</p>
      <p>Amounts are exact and grouped by currency. Reconciliation is original amount minus allocated receipts plus approved allocation reversals minus issued credits. Unallocated cash stays at billing-account level; zero-balance invoices are shown as settled. There is no separate billing-dispute state, so an outstanding amount remains aged until it is credited or an allocation reversal is approved.</p>
      </section>
      <section class="panel" aria-labelledby="currency-subtotals-heading">
        <h2 id="currency-subtotals-heading">Currency subtotals</h2>
        <div class="table-scroll"><table><caption class="sr-only">Outstanding firm receivables subtotal by currency</caption>
          <thead><tr><th scope="col">Currency</th><th scope="col" class="number">Invoices</th><th scope="col" class="number">Original</th><th scope="col" class="number">Applied receipts</th><th scope="col" class="number">Reversals</th><th scope="col" class="number">Applied credits</th><th scope="col" class="number">Outstanding</th></tr></thead>
          <tbody>@for (s of r.currencySubtotals; track s.currency) { <tr><th scope="row">{{ s.currency }}</th><td class="number">{{ s.invoiceCount }}</td><td class="number">{{ s.originalAmount | money }}</td><td class="number">{{ s.appliedReceipts | money }}</td><td class="number">{{ s.reversedReceipts | money }}</td><td class="number">{{ s.appliedCredits | money }}</td><td class="number"><strong>{{ s.outstanding | money }} {{ s.currency }}</strong></td></tr> }
          @empty { <tr><td colspan="7">No posted fee invoices were due by this date.</td></tr> }</tbody></table></div>
      </section>
      <section class="panel" aria-labelledby="receivables-heading">
        <h2 id="receivables-heading">Invoice detail</h2>
        <div class="table-scroll"><table><caption class="sr-only">Firm fee invoices and due-date ageing at the selected date</caption>
          <thead><tr><th scope="col">Legal client</th><th scope="col">Engagement</th><th scope="col">Type / invoice</th><th scope="col">Due date</th><th scope="col" class="number">Original</th><th scope="col" class="number">Receipts</th><th scope="col" class="number">Reversals</th><th scope="col" class="number">Credits</th><th scope="col" class="number">Outstanding</th><th scope="col">Days overdue</th><th scope="col">Bucket</th><th scope="col">Finance follow-up</th></tr></thead>
          <tbody>@for (row of r.rows; track row.invoiceId) { <tr>
            <th scope="row">{{ row.legalClientName }}</th><td>@if (row.engagementId && row.engagementName) { <a [routerLink]="['/app/engagements', row.engagementId]">{{ row.engagementName }}</a> } @else { Not linked }</td>
            <td>{{ row.invoiceType }} · <a [routerLink]="['/app/practice/invoices', row.invoiceId]">{{ row.invoiceNumber }}</a></td>
            <td>{{ row.dueDate ?? 'Undated · review terms' }}</td><td class="number">{{ row.originalAmount | money }} {{ row.currency }}</td>
            <td class="number">{{ row.appliedReceipts | money }}</td><td class="number">{{ row.reversedReceipts | money }}</td><td class="number">{{ row.appliedCredits | money }}</td>
            <td class="number"><strong>{{ row.outstanding | money }} {{ row.currency }}</strong></td>
            <td>{{ row.daysOverdue ?? '—' }}</td><td><audit-status [value]="row.bucket" /><small>Terms: {{ row.paymentTermsStatus }}</small></td>
            <td>@for (recipient of row.financeRecipients; track recipient) { <div>{{ recipient }}</div> } @empty { <span>Finance contact not recorded</span> }</td>
          </tr> } @empty { <tr><td colspan="12">No posted advance or final fee receivables exist for this report date.</td></tr> }</tbody>
        </table></div>
      </section>
      <section class="panel" aria-labelledby="client-subtotals-heading">
        <h2 id="client-subtotals-heading">Client and currency reconciliation</h2>
        <div class="table-scroll"><table><caption class="sr-only">Receivable totals reconciled by legal client and currency</caption>
          <thead><tr><th scope="col">Legal client</th><th scope="col">Currency</th><th scope="col" class="number">Invoices</th><th scope="col" class="number">Original</th><th scope="col" class="number">Receipts</th><th scope="col" class="number">Reversals</th><th scope="col" class="number">Credits</th><th scope="col" class="number">Outstanding</th></tr></thead>
          <tbody>@for (s of r.clientCurrencySubtotals; track s.clientId + ':' + s.currency) { <tr><th scope="row">{{ s.legalClientName }}</th><td>{{ s.currency }}</td><td class="number">{{ s.invoiceCount }}</td><td class="number">{{ s.originalAmount | money }}</td><td class="number">{{ s.appliedReceipts | money }}</td><td class="number">{{ s.reversedReceipts | money }}</td><td class="number">{{ s.appliedCredits | money }}</td><td class="number">{{ s.outstanding | money }} {{ s.currency }}</td></tr> }
          @empty { <tr><td colspan="8">No client subtotals to reconcile.</td></tr> }</tbody></table></div>
      </section>
      <section class="panel" aria-labelledby="engagement-subtotals-heading">
        <h2 id="engagement-subtotals-heading">Engagement and currency reconciliation</h2>
        <div class="table-scroll"><table><caption class="sr-only">Receivable totals reconciled by engagement and currency</caption>
          <thead><tr><th scope="col">Legal client</th><th scope="col">Engagement</th><th scope="col">Currency</th><th scope="col" class="number">Invoices</th><th scope="col" class="number">Original</th><th scope="col" class="number">Receipts</th><th scope="col" class="number">Reversals</th><th scope="col" class="number">Credits</th><th scope="col" class="number">Outstanding</th></tr></thead>
          <tbody>@for (s of r.engagementCurrencySubtotals; track s.engagementId + ':' + s.currency) { <tr>
            <th scope="row">{{ s.legalClientName }}</th><td>{{ s.engagementName }}</td><td>{{ s.currency }}</td><td class="number">{{ s.invoiceCount }}</td>
            <td class="number">{{ s.originalAmount | money }}</td><td class="number">{{ s.appliedReceipts | money }}</td><td class="number">{{ s.reversedReceipts | money }}</td><td class="number">{{ s.appliedCredits | money }}</td><td class="number">{{ s.outstanding | money }} {{ s.currency }}</td>
          </tr> } @empty { <tr><td colspan="9">No fee invoices are linked to an engagement.</td></tr> }</tbody></table></div>
      </section>
    }
  `,
  styles: `
    .filters { display:flex; flex-wrap:wrap; align-items:end; justify-content:space-between; gap:1rem; }
    .filters > div:first-child { display:grid; gap:.5rem; }
    input[type=date] { min-height:2.75rem; padding:.45rem .65rem; }
    .actions { display:flex; gap:.75rem; flex-wrap:wrap; }
    .policy { border-inline-start:4px solid var(--primary,#205fc5); }
    .policy p { margin:.35rem 0; }
    td, th { vertical-align:top; }
    td small { display:block; margin-top:.25rem; color:var(--text-muted,#566575); }
  `,
})
export class FirmReceivablesAging {
  private readonly api = inject(Api);
  readonly asOf = signal(new Date().toISOString().slice(0, 10));
  readonly report = this.api.resource(() => `/api/ui/finance/receivables-aging?asOf=${encodeURIComponent(this.asOf())}`,
    decodeReceivablesAging, 'Receivables ageing is limited to firm-wide FinanceManager and FinanceReviewer grants.');
  readonly exporting = signal(false);
  readonly message = signal('');
  readonly failed = signal(false);
  changeDate(value: string): void {
    if (/^\d{4}-\d{2}-\d{2}$/.test(value)) { this.message.set(''); this.asOf.set(value); }
  }
  async exportCsv(): Promise<void> {
    if (this.exporting() || !this.report.data()) return;
    const date = this.asOf();
    this.exporting.set(true); this.message.set(''); this.failed.set(false);
    const outcome = await this.api.download('/api/ui/finance/receivables-aging/export', { asOfDate: date }, (m) =>
      this.asOf() === date && m.fileName === `firm-receivables-aging-${date}.csv` &&
      m.contentType.startsWith('text/csv') && m.byteCount <= 8_000_000 &&
      m.headers['x-receivables-as-of'] === date);
    this.exporting.set(false); this.failed.set(!outcome.ok);
    this.message.set(outcome.ok ? `The receivables report for ${date} was downloaded.` : outcome.message);
  }
}
