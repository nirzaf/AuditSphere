import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, CommandState, routeGuid } from '../../core/api';
import { arr, bool, dec, guid, instant, nat, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

export const decodeInvoice = obj({ id: guid, invoiceNumber: text, currency: nullable(text), subtotal: dec, tax: dec, total: dec, revision: nat, status: text,
  createdAt: instant, postedAt: nullable(instant), outstanding: dec, canAct: bool,
  lines: arr(obj({ description: text, quantity: dec, unitPrice: dec, lineTotal: dec }), 5000),
  allocations: arr(obj({ receiptId: guid, createdAt: instant, amount: dec }), 5000) });

@Component({
  selector: 'audit-invoice',
  imports: [RouterLink, MatButtonModule, ...SHARED],
  template: `
    <nav aria-label="Breadcrumb"><a routerLink="/app">Portfolio</a> / <a routerLink="/app/finance">Firm finance</a> / <span>Invoice</span></nav>
    <audit-page-header title="Practice invoice" eyebrow="Practice billing" description="Client invoice record, line items, and receipt allocations under practice billing controls." />
    <audit-state [loading]="invoice.loading()" [error]="invoice.error()" label="invoice" />
    @if (invoice.data(); as i) {
      <p role="status">Invoice total {{ i.total | money }} · balance outstanding {{ i.outstanding | money }} · {{ i.allocations.length }} receipt allocations</p>
      <section class="panel" aria-labelledby="invoice-heading">
        <p class="eyebrow">Status: <audit-status [value]="i.status" /> (Rev {{ i.revision }})</p>
        <h2 id="invoice-heading">{{ i.invoiceNumber }}</h2>
        <button matButton="outlined" (click)="invoice.reload()" [disabled]="cmd.busy()">Refresh invoice</button>
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
      <section class="panel" aria-labelledby="alloc-heading">
        <h2 id="alloc-heading">Receipt allocations</h2>
        <div class="table-scroll"><table><thead><tr><th>Receipt ID</th><th>Date</th><th class="number">Allocated amount</th></tr></thead>
          <tbody>@for (a of i.allocations; track $index) { <tr><td><code>{{ a.receiptId }}</code></td><td>{{ a.createdAt.slice(0, 16).replace('T', ' ') }}</td><td class="number">{{ a.amount | money }}</td></tr> }
          @empty { <tr><td colspan="3">No payments or receipts allocated to this invoice.</td></tr> }</tbody></table></div>
      </section>
      @if (i.canAct) {
        <section class="panel" aria-labelledby="workflow-heading">
          <h2 id="workflow-heading">Invoice workflow actions</h2>
          @switch (i.status) {
            @case ('REVIEW_REQUIRED') { <button matButton="filled" (click)="act(i.id, 'approve', 'Invoice approved.')" [disabled]="cmd.busy()">Approve invoice</button> }
            @case ('APPROVED') { <button matButton="filled" (click)="act(i.id, 'post', 'Invoice posted and frozen.')" [disabled]="cmd.busy()">Post invoice (freeze & emit ledger event)</button> }
            @case ('POSTED') { <button matButton="filled" (click)="act(i.id, 'send', 'Invoice marked as sent.')" [disabled]="cmd.busy()">Mark sent to client</button> }
          }
        </section>
      }
    }
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
})
export class InvoiceDetail {
  private readonly api = inject(Api);
  readonly id = routeGuid();
  readonly invoice = this.api.resource(() => (this.id() ? `/api/ui/finance/invoices/${this.id()}` : null), decodeInvoice,
    'The requested invoice was not found in the current firm scope.');
  readonly cmd = new CommandState(this.api);
  act(id: string, action: string, ok: string): void { void this.cmd.run(`/api/ui/finance/invoices/${id}/${action}`, {}, ok).finally(() => this.invoice.reload()); }
  /** Exact-decimal line sum (scaled BigInt) so the footer total always equals the rendered lines. */
  linesTotal(lines: ReturnType<typeof decodeInvoice>['lines']): string {
    const places = Math.max(0, ...lines.map((l) => (l.lineTotal.split('.')[1] ?? '').length));
    const total = lines.reduce((acc, l) => {
      const v = l.lineTotal; const neg = v.startsWith('-'); const [w, f = ''] = (neg ? v.slice(1) : v).split('.');
      return acc + (neg ? -1n : 1n) * BigInt(w + f.padEnd(places, '0'));
    }, 0n);
    const neg = total < 0n; const digits = (neg ? -total : total).toString().padStart(places + 1, '0');
    return (neg ? '-' : '') + (places ? digits.slice(0, -places) + '.' + digits.slice(-places) : digits);
  }
}
