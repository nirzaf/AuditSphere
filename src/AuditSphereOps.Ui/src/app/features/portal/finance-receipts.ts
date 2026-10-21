import {
  afterNextRender,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  Injector,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { arr, bool, DecodeError, guid, instant, nat, nullable, obj, oneOf, str, text } from '../../core/decode';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';

export const portalFinanceReceipt = obj({
  receiptId: guid,
  documentId: nullable(guid),
  kind: oneOf('ADVANCE', 'BALANCE'),
  amount: text,
  appliedAmount: text,
  currency: text,
  reference: text,
  receivedAt: instant,
  downloadUrl: nullable(text),
});

export type PortalFinanceReceipt = ReturnType<typeof portalFinanceReceipt>;

export const portalFinanceReceiptKey = (row: Pick<PortalFinanceReceipt, 'kind' | 'receiptId'>): string =>
  JSON.stringify([row.kind, row.receiptId.toLowerCase()]);

const financeReceiptPage = obj({
  agreementId: guid,
  receipts: arr(portalFinanceReceipt, 50),
  pageSize: nat,
  hasMore: bool,
  nextCursor: nullable(str(256)),
});

export type PortalFinanceReceiptPage = ReturnType<typeof financeReceiptPage>;

export const portalFinanceReceiptPage = (value: unknown, path: string): PortalFinanceReceiptPage => {
  const page = financeReceiptPage(value, path);
  if (
    page.pageSize !== 50 ||
    page.receipts.length > page.pageSize ||
    page.hasMore !== (page.nextCursor !== null) ||
    (page.hasMore && page.receipts.length !== page.pageSize) ||
    new Set(page.receipts.map(portalFinanceReceiptKey)).size !== page.receipts.length ||
    (page.nextCursor !== null && !/^[A-Za-z0-9_-]{1,256}$/.test(page.nextCursor))
  )
    throw new DecodeError('Unsupported payment receipt page.');
  return page;
};

@Component({
  selector: 'audit-portal-finance-receipts',
  imports: [MatButtonModule, ...SHARED],
  template: `
    <audit-state [loading]="ws.loading()" [error]="ws.error() || pageError()" label="payment receipts" />
    @if (page(); as data) {
      <p class="live-page">Showing a live receipt page; this is not a durable snapshot.</p>
      <div class="table-section" tabindex="0" role="region" aria-label="Scrollable payment receipts">
        <h4 #heading [id]="headingId()" tabindex="-1">Payment receipts</h4>
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
            @for (r of data.receipts; track receiptKey(r)) {
              <tr>
                <td>{{ r.kind === 'ADVANCE' ? 'Advance payment' : 'Final settlement' }}</td>
                <td><code>{{ r.reference }}</code></td>
                <td>{{ r.amount }} {{ r.currency }}</td>
                <td>{{ r.appliedAmount }} {{ r.currency }}</td>
                <td>{{ r.receivedAt }}</td>
                <td>
                  @if (r.downloadUrl) {
                    <a matButton [href]="r.downloadUrl" target="_blank" rel="noopener">Download receipt</a>
                  } @else {
                    <span>Retained</span>
                  }
                </td>
              </tr>
            } @empty {
              <tr><td colspan="6">No payment receipts are recorded on this page.</td></tr>
            }
          </tbody>
        </table>
      </div>
    }
    <nav aria-label="Payment receipt pages">
      <button matButton type="button" (click)="refreshFromStart()" [disabled]="ws.loading()">
        Refresh receipts from start
      </button>
      <button matButton type="button" (click)="previous($event)" [disabled]="cursors().length < 2 || ws.loading()">
        Newer receipts
      </button>
      <span aria-live="polite">
        @if (page(); as data) {
          Page {{ cursors().length }} · {{ data.receipts.length }} {{ data.receipts.length === 1 ? 'receipt' : 'receipts' }} shown
        } @else if (ws.loading()) {
          Loading page {{ cursors().length }}
        } @else {
          Receipt page unavailable
        }
      </span>
      <button matButton type="button" (click)="next($event)" [disabled]="!page()?.hasMore || ws.loading() || !!pageError()">
        Older receipts
      </button>
    </nav>
  `,
  styles: `
    :host { display: block; min-width: 0; }
    .table-section { overflow-x: auto; }
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
      th { font-weight: 600; color: var(--mat-sys-on-surface-variant); }
    }
    .live-page, h4:focus { color: var(--mat-sys-on-surface-variant); }
    h4:focus { outline: 2px solid var(--mat-sys-primary); outline-offset: 4px; }
    nav { display: flex; flex-wrap: wrap; align-items: center; gap: 0.5rem; margin-block-start: 0.75rem; }
  `,
})
export class PortalFinanceReceipts {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private destroyed = false;
  private scopeInitialized = false;
  private boundScope = '';
  private boundInlinePage: PortalFinanceReceiptPage | null = null;
  private pendingFocus: { origin: Element | null; generation: number; agreementId: string; cursor: string | null } | null = null;

  readonly agreementId = input.required<string>();
  readonly initialReceipts = input.required<PortalFinanceReceipt[]>();
  readonly initialHasMore = input.required<boolean>();
  readonly initialNextCursor = input.required<string | null>();
  readonly cursors = signal<(string | null)[]>([null], {
    equal: (left, right) => left.length === right.length && left.every((cursor, index) => cursor === right[index]),
  });
  private readonly firstPageRefreshRequested = signal(false);
  private readonly refreshRequestVersion = signal(0);
  private readonly firstPage = signal<PortalFinanceReceiptPage | null>(null);
  private readonly inlinePage = computed<PortalFinanceReceiptPage>(() => ({
    agreementId: this.agreementId(),
    receipts: this.initialReceipts(),
    pageSize: 50,
    hasMore: this.initialHasMore(),
    nextCursor: this.initialNextCursor(),
  }));
  readonly ws = this.api.resource(
    () => {
      const agreementId = this.agreementId();
      const cursor = this.cursors().at(-1);
      this.refreshRequestVersion();
      if (this.session.current()?.staff !== false || cursor === undefined || (cursor === null && !this.firstPageRefreshRequested()))
        return null;
      return `/api/ui/portal/finance/agreements/${encodeURIComponent(agreementId)}/receipts?pageSize=50${
        cursor === null ? '' : `&cursor=${encodeURIComponent(cursor)}`
      }`;
    },
    portalFinanceReceiptPage,
    'Payment receipts are unavailable in your current scope.',
  );
  private readonly currentPageSource = computed(() => {
    if (this.cursors().at(-1) === null) {
      if (this.firstPageRefreshRequested()) return this.ws.data();
      return this.ws.error() ? null : this.firstPage();
    }
    return this.ws.data();
  });
  readonly pageError = computed(() => {
    const page = this.currentPageSource();
    if (!page) return '';
    if (page.agreementId.toLowerCase() !== this.agreementId().toLowerCase())
      return 'Payment receipts are unavailable in your current scope.';
    return page.nextCursor && this.cursors().includes(page.nextCursor)
      ? 'The receipt continuation repeated an earlier page. Refresh receipts from the start.'
      : '';
  });
  readonly page = computed(() =>
    this.session.current()?.staff !== false || this.pageError() ? null : this.currentPageSource(),
  );
  readonly headingId = computed(() => `portal-finance-receipts-${this.agreementId()}`);

  constructor() {
    effect(() => {
      const session = this.session.current();
      const scope = session
        ? JSON.stringify([this.session.invalidation(), session.firmId, session.userId, session.generation, session.staff])
        : '';
      const inline = this.inlinePage();
      const scopeChanged = this.scopeInitialized && scope !== this.boundScope;
      const inlineChanged = inline !== this.boundInlinePage;
      if (!this.scopeInitialized || scopeChanged || inlineChanged) {
        if (!this.scopeInitialized || inlineChanged) this.firstPage.set(inline);
        else if (scopeChanged) this.firstPage.set(null);
        this.cursors.set([null]);
        this.firstPageRefreshRequested.set(false);
        this.pendingFocus = null;
      }
      this.scopeInitialized = true;
      this.boundScope = scope;
      this.boundInlinePage = inline;
    });
    effect(() => {
      if (!this.ws.error()) return;
      this.firstPage.set(null);
      this.pendingFocus = null;
    });
    effect(() => {
      const data = this.ws.data();
      if (!data || !this.firstPageRefreshRequested() || this.cursors().at(-1) !== null ||
          data.agreementId.toLowerCase() !== this.agreementId().toLowerCase()) return;
      this.firstPage.set(data);
      this.firstPageRefreshRequested.set(false);
    });
    effect(() => {
      const data = this.page();
      if (!data) return;
      const pending = this.pendingFocus;
      if (!pending || pending.agreementId !== this.agreementId() || pending.cursor !== this.cursors().at(-1)) return;
      this.pendingFocus = null;
      afterNextRender(() => {
        if (this.destroyed || pending.generation !== this.session.invalidation() ||
            pending.agreementId !== this.agreementId() || pending.cursor !== this.cursors().at(-1)) return;
        const active = this.host.nativeElement.ownerDocument.activeElement;
        if (active === pending.origin || active === this.host.nativeElement.ownerDocument.body)
          this.heading()?.nativeElement.focus();
      }, { injector: this.injector });
    });
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; });
  }

  receiptKey(row: PortalFinanceReceipt): string { return portalFinanceReceiptKey(row); }

  next(event: Event): void {
    const cursor = this.page()?.nextCursor;
    if (this.ws.loading() || !cursor || this.cursors().includes(cursor)) return;
    this.prepareNavigation(event, cursor);
    this.firstPageRefreshRequested.set(false);
    this.cursors.update((items) => [...items, cursor]);
  }

  previous(event: Event): void {
    const target = this.cursors().at(-2);
    if (this.ws.loading() || target === undefined) return;
    this.prepareNavigation(event, target);
    this.firstPageRefreshRequested.set(false);
    this.cursors.update((items) => items.slice(0, -1));
  }

  refreshFromStart(): void {
    if (this.ws.loading()) return;
    this.pendingFocus = null;
    this.cursors.set([null]);
    this.firstPageRefreshRequested.set(true);
    this.refreshRequestVersion.update((version) => version + 1);
  }

  private prepareNavigation(event: Event, cursor: string | null): void {
    this.pendingFocus = {
      origin: event.currentTarget as Element | null,
      generation: this.session.invalidation(),
      agreementId: this.agreementId(),
      cursor,
    };
  }
}
