import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { arr, guid, instant, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

export const portalFinanceContract = obj({
  agreements: arr(
    obj({
      agreementId: guid,
      engagementId: nullable(guid),
      agreedFee: text,
      currency: text,
      advancePercent: text,
      outstandingBalance: text,
      invoices: arr(
        obj({
          invoiceId: guid,
          kind: text,
          invoiceNumber: text,
          status: text,
          amount: text,
          currency: text,
          issuedAt: nullable(instant),
        }),
        50
      ),
      receipts: arr(
        obj({
          receiptId: guid,
          documentId: nullable(guid),
          kind: text,
          amount: text,
          currency: text,
          reference: text,
          receivedAt: instant,
          downloadUrl: nullable(text),
        }),
        50
      ),
    }),
    50
  ),
});

@Component({
  selector: 'audit-portal-finance',
  imports: [MatButtonModule, ...SHARED],
  template: `
    <section class="panel" aria-labelledby="portal-finance-heading">
      <h2 id="portal-finance-heading">Billing and payment receipts</h2>
      <audit-state [loading]="ws.loading()" [error]="ws.error()" label="your financial records" />
      @if (ws.data(); as data) {
        @for (agr of data.agreements; track agr.agreementId) {
          <article class="agreement-card">
            <h3>
              Agreed fee: {{ agr.agreedFee }} {{ agr.currency }}
              <span class="advance-tag">({{ agr.advancePercent }}% advance terms)</span>
            </h3>
            <p class="balance-summary">
              Outstanding balance: <strong>{{ agr.outstandingBalance }} {{ agr.currency }}</strong>
            </p>

            <div class="financial-tables">
              @if (agr.invoices.length > 0) {
                <div class="table-section">
                  <h4>Invoices</h4>
                  <table class="finance-table" aria-label="Fee invoices">
                    <thead>
                      <tr>
                        <th>Type</th>
                        <th>Invoice #</th>
                        <th>Status</th>
                        <th>Amount</th>
                        <th>Issued</th>
                      </tr>
                    </thead>
                    <tbody>
                      @for (inv of agr.invoices; track inv.invoiceId) {
                        <tr>
                          <td>{{ inv.kind === 'ADVANCE' ? 'Advance' : 'Final Balance' }}</td>
                          <td><code>{{ inv.invoiceNumber }}</code></td>
                          <td><audit-status [value]="inv.status" /></td>
                          <td>{{ inv.amount }} {{ inv.currency }}</td>
                          <td>{{ inv.issuedAt ?? '—' }}</td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
              }

              @if (agr.receipts.length > 0) {
                <div class="table-section">
                  <h4>Payment receipts</h4>
                  <table class="finance-table" aria-label="Payment receipts">
                    <thead>
                      <tr>
                        <th>Type</th>
                        <th>Reference</th>
                        <th>Amount</th>
                        <th>Date</th>
                        <th>Receipt Voucher</th>
                      </tr>
                    </thead>
                    <tbody>
                      @for (r of agr.receipts; track r.receiptId) {
                        <tr>
                          <td>{{ r.kind === 'ADVANCE' ? 'Advance payment' : 'Final settlement' }}</td>
                          <td><code>{{ r.reference }}</code></td>
                          <td>{{ r.amount }} {{ r.currency }}</td>
                          <td>{{ r.receivedAt }}</td>
                          <td>
                            @if (r.downloadUrl) {
                              <a matButton [href]="r.downloadUrl" target="_blank" rel="noopener">
                                Download official voucher
                              </a>
                            } @else {
                              <span>Retained</span>
                            }
                          </td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
              }
            </div>
          </article>
        } @empty {
          <p>No billing agreements or invoices are active for your organization.</p>
        }
      }
    </section>
  `,
  styles: `
    .agreement-card {
      margin-block-end: 1.5rem;
      padding: 1rem;
      border: 1px solid var(--mat-sys-outline-variant);
      border-radius: 8px;
    }
    .advance-tag {
      font-size: 0.875rem;
      font-weight: normal;
      color: var(--mat-sys-on-surface-variant);
    }
    .balance-summary {
      font-size: 1.1rem;
      margin-block: 0.5rem 1rem;
    }
    .financial-tables {
      display: flex;
      flex-direction: column;
      gap: 1rem;
    }
    .finance-table {
      width: 100%;
      border-collapse: collapse;
      margin-block-start: 0.5rem;
      th, td {
        padding: 0.5rem;
        text-align: left;
        border-bottom: 1px solid var(--mat-sys-outline-variant);
      }
      th {
        font-weight: 600;
        color: var(--mat-sys-on-surface-variant);
      }
    }
  `,
})
export class PortalFinance {
  private readonly api = inject(Api);
  readonly ws = this.api.resource(() => '/api/ui/portal/finance', portalFinanceContract);
}
