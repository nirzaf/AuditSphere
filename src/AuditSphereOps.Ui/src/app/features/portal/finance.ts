import {
  afterNextRender,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  Injector,
  signal,
  viewChild,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { arr, bool, date, DecodeError, guid, instant, nat, nullable, obj, str, text } from '../../core/decode';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';

const invoice = obj({
  invoiceId: guid,
  kind: text,
  invoiceNumber: text,
  status: text,
  amount: text,
  currency: text,
  postedAt: nullable(instant),
  sentAt: nullable(instant),
  deliveryStatus: text,
  collectionStatus: text,
  cashApplied: text,
  creditApplied: text,
  outstandingAmount: text,
  dueDate: nullable(date),
  paymentTermsStatus: text,
  agingStatus: text,
});

const receipt = obj({
  receiptId: guid,
  documentId: nullable(guid),
  kind: text,
  amount: text,
  appliedAmount: text,
  currency: text,
  reference: text,
  receivedAt: instant,
  downloadUrl: nullable(text),
});

// Per-agreement invoice and receipt summaries remain bounded to 50 on this wire contract.
// arr rejects overflow; the client never truncates these histories silently.
const agreement = obj({
  agreementId: guid,
  engagementId: nullable(guid),
  agreedFee: text,
  currency: text,
  advancePercent: text,
  outstandingBalance: text,
  invoices: arr(invoice, 50),
  receipts: arr(receipt, 50),
});

const financePage = obj({
  asOfDateUtc: date,
  agreements: arr(agreement, 50),
  pageSize: nat,
  hasMore: bool,
  nextCursor: nullable(str(256)),
});

export const portalFinanceContract = (value: unknown, path: string) => {
  const page = financePage(value, path);
  if (
    page.pageSize !== 50 ||
    page.agreements.length > page.pageSize ||
    new Set(page.agreements.map((row) => row.agreementId.toLowerCase())).size !== page.agreements.length ||
    page.hasMore !== (page.nextCursor !== null) ||
    (page.hasMore && page.agreements.length !== page.pageSize) ||
    (page.nextCursor !== null && !/^[A-Za-z0-9_-]{1,256}$/.test(page.nextCursor))
  )
    throw new DecodeError('Unsupported client finance page.');
  return page;
};

@Component({
  selector: 'audit-portal-finance',
  imports: [MatButtonModule, ...SHARED],
  template: `
    <section class="panel" aria-labelledby="portal-finance-heading">
      <h2 #heading id="portal-finance-heading" tabindex="-1">Billing and payment receipts</h2>
      <audit-state [loading]="ws.loading()" [error]="ws.error() || pageError()" label="your financial records" />
      <nav aria-label="Client finance pages">
        <button matButton type="button" (click)="refreshFromStart()" [attr.aria-disabled]="ws.loading()">
          Refresh finance from start
        </button>
        <button matButton type="button" (click)="previous($event)" [disabled]="cursors().length < 2 || ws.loading()">
          Newer agreements
        </button>
        <span aria-live="polite">
          @if (page(); as data) {
            Page {{ cursors().length }} · {{ data.agreements.length }}
            {{ data.agreements.length === 1 ? 'agreement' : 'agreements' }} shown
          } @else if (ws.loading()) {
            Loading page {{ cursors().length }}
          }
        </span>
        <button matButton type="button" (click)="next($event)" [disabled]="!page()?.hasMore || ws.loading()">
          Older agreements
        </button>
      </nav>
      @if (page(); as data) {
        <p class="aging-as-of">Invoice aging as of {{ data.asOfDateUtc }} UTC. Lifecycle, cash collection and aging are separate states; invoice-specific provider delivery is shown only when recorded.</p>
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
                <div class="table-section" tabindex="0" role="region" aria-label="Scrollable fee invoices">
                  <h4>Invoices</h4>
                  <table class="finance-table" aria-label="Fee invoices">
                    <thead>
                      <tr>
                        <th>Type</th>
                        <th>Invoice #</th>
                        <th>Lifecycle</th>
                        <th>Collection</th>
                        <th>Aging</th>
                        <th>Amount</th>
                        <th>Cash applied</th>
                        <th>Credit applied</th>
                        <th>Outstanding</th>
                        <th>Approved due date</th>
                        <th>Posted</th>
                        <th>Sent</th>
                        <th>Provider delivery</th>
                      </tr>
                    </thead>
                    <tbody>
                      @for (inv of agr.invoices; track inv.invoiceId) {
                        <tr>
                          <td>{{ inv.kind === 'ADVANCE' ? 'Advance' : 'Final Balance' }}</td>
                          <td><code>{{ inv.invoiceNumber }}</code></td>
                          <td><audit-status [value]="inv.status" /></td>
                          <td><audit-status [value]="inv.collectionStatus" /></td>
                          <td><audit-status [value]="inv.agingStatus" /></td>
                          <td>{{ inv.amount }} {{ inv.currency }}</td>
                          <td>{{ inv.cashApplied }} {{ inv.currency }}</td>
                          <td>{{ inv.creditApplied }} {{ inv.currency }}</td>
                          <td>{{ inv.outstandingAmount }} {{ inv.currency }}</td>
                          <td>
                            {{ inv.dueDate ?? '—' }}
                            @if (inv.paymentTermsStatus !== 'APPROVED' && inv.paymentTermsStatus !== 'NOT_CONFIGURED') {
                              <small>Terms {{ inv.paymentTermsStatus }}</small>
                            }
                          </td>
                          <td>{{ inv.postedAt ?? '—' }}</td>
                          <td>{{ inv.sentAt ?? '—' }}</td>
                          <td><audit-status [value]="inv.deliveryStatus" /></td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
              }

              @if (agr.receipts.length > 0) {
                <div class="table-section" tabindex="0" role="region" aria-label="Scrollable payment receipts">
                  <h4>Payment receipts</h4>
                  <p>Currently applied amounts reflect independently approved allocation reversals. Pending or rejected reversals do not change applied cash; the original receipt voucher remains unchanged.</p>
                  <table class="finance-table" aria-label="Payment receipts">
                    <thead>
                      <tr>
                        <th>Type</th>
                        <th>Reference</th>
                        <th>Original allocation</th>
                        <th>Currently applied</th>
                        <th>Date</th>
                        <th>Receipt document</th>
                      </tr>
                    </thead>
                    <tbody>
                      @for (r of agr.receipts; track r.receiptId) {
                        <tr>
                          <td>{{ r.kind === 'ADVANCE' ? 'Advance payment' : 'Final settlement' }}</td>
                          <td><code>{{ r.reference }}</code></td>
                          <td>{{ r.amount }} {{ r.currency }}</td>
                          <td>{{ r.appliedAmount }} {{ r.currency }}</td>
                          <td>{{ r.receivedAt }}</td>
                          <td>
                            @if (r.downloadUrl) {
                              <a matButton [href]="r.downloadUrl" target="_blank" rel="noopener">
                                Download receipt
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
      width: max-content;
      min-width: 100%;
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
    .table-section {
      overflow-x: auto;
    }
    .aging-as-of {
      color: var(--mat-sys-on-surface-variant);
    }
    small {
      display: block;
      color: var(--mat-sys-on-surface-variant);
    }
    nav {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.5rem;
      margin-block-end: 1rem;
    }
    h2:focus {
      outline: 2px solid var(--mat-sys-primary);
      outline-offset: 4px;
    }
  `,
})
export class PortalFinance {
  private readonly api = inject(Api);
  readonly session = inject(SessionService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private destroyed = false;
  private boundScope = '';
  private pendingFocus: { origin: Element | null; generation: number; cursor: string | null } | null = null;
  readonly cursors = signal<(string | null)[]>([null], {
    equal: (left, right) => left.length === right.length && left.every((cursor, index) => cursor === right[index]),
  });
  readonly ws = this.api.resource(
    () => {
      if (this.session.current()?.staff !== false) return null;
      const cursor = this.cursors().at(-1);
      return `/api/ui/portal/finance?pageSize=50${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`;
    },
    portalFinanceContract,
    'Finance records are unavailable in your current scope.',
  );
  readonly pageError = computed(() => {
    const nextCursor = this.ws.data()?.nextCursor;
    return nextCursor && this.cursors().includes(nextCursor)
      ? 'The finance continuation repeated an earlier page. Refresh from the start.'
      : '';
  });
  readonly page = computed(() =>
    this.session.current()?.staff !== false || this.pageError() ? null : this.ws.data(),
  );

  constructor() {
    effect(() => {
      const generation = this.session.invalidation();
      const session = this.session.current();
      const scope = session
        ? JSON.stringify([generation, session.firmId, session.userId, session.generation, session.staff])
        : '';
      if (scope === this.boundScope) return;
      this.boundScope = scope;
      this.cursors.set([null]);
      this.pendingFocus = null;
    });
    effect(() => {
      const data = this.page();
      if (!data) return;
      const pending = this.pendingFocus;
      if (!pending || pending.cursor !== this.cursors().at(-1)) return;
      this.pendingFocus = null;
      afterNextRender(() => {
        if (
          this.destroyed ||
          pending.generation !== this.session.invalidation() ||
          pending.cursor !== this.cursors().at(-1)
        )
          return;
        const active = this.host.nativeElement.ownerDocument.activeElement;
        if (active === pending.origin || active === this.host.nativeElement.ownerDocument.body)
          this.heading()?.nativeElement.focus();
      }, { injector: this.injector });
    });
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
    });
  }

  next(event: Event): void {
    const cursor = this.page()?.nextCursor;
    if (this.ws.loading() || !cursor || this.cursors().includes(cursor)) return;
    this.prepareNavigation(event, cursor);
    this.cursors.update((items) => [...items, cursor]);
  }

  previous(event: Event): void {
    const target = this.cursors().at(-2);
    if (this.ws.loading() || target === undefined) return;
    this.prepareNavigation(event, target);
    this.cursors.update((items) => items.slice(0, -1));
  }

  refreshFromStart(): void {
    if (this.ws.loading()) return;
    this.pendingFocus = null;
    if (this.cursors().length > 1) this.cursors.set([null]);
    else this.ws.reload();
  }

  private prepareNavigation(event: Event, cursor: string | null): void {
    this.pendingFocus = {
      origin: event.currentTarget as Element | null,
      generation: this.session.invalidation(),
      cursor,
    };
  }
}
