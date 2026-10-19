import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { map } from 'rxjs';
import { Api } from '../../core/api';
import { arr, bool, decode, dec, guid, instant, nat, obj, str } from '../../core/decode';
import type { Decoder } from '../../core/decode';
import { SHARED } from '../../core/ui';
import { FeeAgreement } from '../commercial/fee-agreement';

export interface FeeAgreementQueueRow {
  agreementId: string;
  clientId: string;
  clientName: string;
  fee: string;
  currency: string;
  createdAt: string;
}
export interface FeeAgreementQueuePage {
  page: number;
  hasMore: boolean;
  rows: FeeAgreementQueueRow[];
}
const feeAgreementQueuePageDecoder: Decoder<FeeAgreementQueuePage> = obj({
  page: nat,
  hasMore: bool,
  rows: arr(obj({
    agreementId: guid,
    clientId: guid,
    clientName: str(300),
    fee: dec,
    currency: str(12),
    createdAt: instant,
  }), 20),
});
export function decodeFeeAgreementQueuePage(value: unknown): FeeAgreementQueuePage {
  return decode(feeAgreementQueuePageDecoder, value);
}

@Component({
  selector: 'audit-fee-settlements',
  imports: [RouterLink, MatButtonModule, ...SHARED],
  template: `
    <a routerLink="/app/finance">← Back to firm ledger</a>
    <audit-page-header title="Fee settlements" eyebrow="Finance"
      description="Review agreed fees, draft invoices, and recorded client payments." />
    <p class="actions">
      <button matButton="outlined" (click)="settlements.reload()" [disabled]="invalidPage()">Refresh fee settlements</button>
    </p>
    @if (invalidPage()) {
      <p role="alert">The page number must be a non-negative whole number.</p>
    } @else {
      <audit-state [loading]="settlements.loading()" [error]="settlements.error()" label="fee settlements" />
      @if (settlements.data(); as result) {
        <section class="panel" aria-labelledby="fee-settlements-list-heading">
          <h2 id="fee-settlements-list-heading">Agreements</h2>
          <div class="table-scroll" role="region" aria-label="Fee settlements table; scroll horizontally if needed" tabindex="0">
            <table>
              <caption>Fee agreements and settlement status</caption>
              <thead>
                <tr>
                  <th scope="col">Client</th>
                  <th scope="col">Agreement</th>
                  <th scope="col" class="number">Agreed fee</th>
                  <th scope="col">Created</th>
                  <th scope="col"><span class="sr-only">Actions</span></th>
                </tr>
              </thead>
              <tbody>
                @for (row of result.rows; track row.agreementId) {
                  <tr>
                    <th scope="row">{{ row.clientName }}</th>
                    <td><code>{{ row.agreementId }}</code></td>
                    <td class="number">{{ row.fee }} {{ row.currency }}</td>
                    <td><time [attr.datetime]="row.createdAt">{{ row.createdAt }}</time></td>
                    <td>
                      <a [routerLink]="['/app/finance/fee-agreements', row.agreementId]"
                        [attr.aria-label]="'Open fee settlement for ' + row.clientName + ' (' + row.agreementId + ')'">Open settlement</a>
                    </td>
                  </tr>
                } @empty {
                  <tr><td colspan="5">No fee agreements are available in this page.</td></tr>
                }
              </tbody>
            </table>
          </div>
          <nav class="pagination" aria-label="Fee settlements pages">
            @if (result.page > 0) {
              <a [routerLink]="['/app/finance/fee-agreements']" [queryParams]="{ page: result.page - 1 }">Previous page</a>
            }
            <span aria-current="page">Page {{ result.page + 1 }}</span>
            @if (result.hasMore) {
              <a [routerLink]="['/app/finance/fee-agreements']" [queryParams]="{ page: result.page + 1 }">Next page</a>
            }
          </nav>
        </section>
      }
    }
  `,
  styles: `
    .actions, .pagination { display:flex; align-items:center; gap:.75rem; flex-wrap:wrap; }
    .pagination { justify-content:space-between; margin-top:1rem; }
    .table-scroll { max-width:100%; overflow-x:auto; overscroll-behavior-inline:contain; }
    table { width:100%; min-width:48rem; border-collapse:collapse; }
    th, td { padding:.65rem .75rem; text-align:start; vertical-align:top; }
    .number { text-align:end; white-space:nowrap; }
    time, code { white-space:nowrap; }
  `,
})
export class FeeSettlements {
  private readonly api = inject(Api);
  private readonly route = inject(ActivatedRoute);
  private readonly pageQuery = toSignal(
    this.route.queryParamMap.pipe(map((params) => params.get('page'))),
    { initialValue: this.route.snapshot.queryParamMap.get('page') },
  );
  readonly page = computed(() => {
    const raw = this.pageQuery();
    if (raw === null) return 0;
    if (!/^(0|[1-9]\d*)$/.test(raw)) return -1;
    const page = Number(raw);
    return Number.isSafeInteger(page) ? page : -1;
  });
  readonly invalidPage = computed(() => this.page() < 0);
  readonly settlements = this.api.resource(
    () => this.invalidPage() ? null : `/api/ui/finance/fee-agreements?page=${this.page()}`,
    (value, path) => {
      const result = feeAgreementQueuePageDecoder(value, path);
      if (result.page !== this.page()) throw new Error('Unexpected fee agreement page');
      return result;
    },
    'Fee settlements are unavailable in the current finance scope.',
    'Current FinanceManager or FinanceReviewer access is required for fee settlements.',
  );
}

@Component({
  selector: 'audit-fee-agreement-route',
  imports: [FeeAgreement],
  template: '<audit-fee-agreement [agreementId]="agreementId()" />',
})
export class FeeAgreementDetailRoute {
  private readonly route = inject(ActivatedRoute);
  readonly agreementId = toSignal(
    this.route.paramMap.pipe(map((params) => params.get('id') ?? '')),
    { initialValue: this.route.snapshot.paramMap.get('id') ?? '' },
  );
}
