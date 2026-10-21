import { Location } from '@angular/common';
import { afterNextRender, Component, computed, DestroyRef, effect, ElementRef, inject, Injector, signal, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { arr, bool, DecodeError, guid, instant, nat, nullable, obj, str, text } from '../../core/decode';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';
import { clientUtcTime } from '../clients/client-contracts';

const activityItem = obj({ key: str(200), at: instant, kind: str(80), label: text, title: text,
  actorLabel: nullable(text), context: obj({ clientLegalName: text, serviceRoute: nullable(text),
    periodStart: nullable(text), periodEnd: nullable(text) }), targetKind: str(40), targetId: guid });
const activityPage = obj({ snapshotAtUtc: instant, pageSize: nat, items: arr(activityItem, 25),
  hasMore: bool, nextCursor: nullable(str(4096)) });
const targetKinds = new Set(['document-review', 'signed-scan', 'bundle', 'pbc-request', 'invoice', 'payment-receipt']);
export const portalActivityPage = (value: unknown, path: string) => {
  const page = activityPage(value, path);
  if (page.pageSize !== 25 || page.hasMore !== !!page.nextCursor || page.hasMore && page.items.length !== 25 ||
      new Set(page.items.map(item => item.key)).size !== page.items.length ||
      page.items.some(item => !targetKinds.has(item.targetKind))) throw new DecodeError('Unsupported client activity page.');
  return page;
};
type ActivityItem = ReturnType<typeof activityItem>;

@Component({ selector: 'audit-portal-activity', imports: [MatButtonModule, RouterLink, ...SHARED], template: `
  <section class="panel" aria-labelledby="portal-activity-heading">
    <h2 #heading id="portal-activity-heading" tabindex="-1">Your client activity</h2>
    <p>Recorded events in your current access scope. An upload, sent notification or recorded receipt does not prove delivery, acceptance or approval of other work.</p>
    <button matButton type="button" (click)="refresh($event)" [disabled]="ws.loading()">Refresh client activity from start</button>
    <audit-state [loading]="ws.loading()" [error]="ws.error() || pageError()" label="your client activity" />
    @if (navigationError()) { <p role="alert">{{ navigationError() }}</p> }
    @if (page(); as data) {
      <p>Event timestamp cutoff: <time [attr.datetime]="data.snapshotAtUtc">{{ utcTime(data.snapshotAtUtc) }}</time>.
        This is live history; current sources and access are checked on each page. Refresh to include later events.</p>
      <ol class="activity-events">
        @for (item of data.items; track item.key) {
          <li><time [attr.datetime]="item.at">{{ utcTime(item.at) }}</time> · {{ item.label }}
            @if (item.actorLabel) { <span> · {{ item.actorLabel }}</span> }
            <span> · </span>
            @if (item.targetKind === 'pbc-request') {
              <a [routerLink]="['/portal/requests', item.targetId]">{{ item.title }}</a>
            } @else { <a [href]="sourceHref(item)" (click)="openSource($event, item)">{{ item.title }}</a> }
            <p>Client: {{ item.context.clientLegalName || 'Not recorded' }}; service: {{ item.context.serviceRoute || 'Not recorded' }};
              period: {{ item.context.periodStart || 'Not recorded' }} to {{ item.context.periodEnd || 'Not recorded' }}</p>
          </li>
        } @empty { <li>No activity events are recorded on this page.</li> }
      </ol>
      <nav aria-label="Client activity pages">
        <button matButton type="button" (click)="previous($event)" [disabled]="cursors().length === 1 || ws.loading()">Newer client activity</button>
        <span aria-live="polite">Page {{ cursors().length }} · {{ data.items.length }} events shown</span>
        <button matButton type="button" (click)="next($event)" [disabled]="!data.hasMore || ws.loading()">Older client activity</button>
      </nav>
    }
  </section>`, styles: `
    :host { display:block; min-width:0 } .activity-events { padding-inline-start:1.5rem }
    li { margin-block:1rem; overflow-wrap:anywhere } li p { margin-block:.3rem }
    nav { display:flex; flex-wrap:wrap; gap:.5rem; align-items:center }
    h2:focus { outline:2px solid var(--mat-sys-primary); outline-offset:4px }
  ` })
export class PortalActivity {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly location = inject(Location);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private destroyed = false;
  private boundScope = '';
  private pendingFocus: { origin: Element | null; generation: number; cursor: string | null } | null = null;
  private readonly cutoff = signal<string | null>(null);
  readonly cursors = signal<(string | null)[]>([null], {
    equal: (left, right) => left.length === right.length && left.every((cursor, index) => cursor === right[index]),
  });
  readonly navigationError = signal('');
  readonly ws = this.api.resource(() => this.session.current()?.staff === false
    ? `/api/ui/portal/activity?pageSize=25${this.cursors().at(-1) ? `&cursor=${encodeURIComponent(this.cursors().at(-1)!)}` : ''}` : null,
  portalActivityPage, 'Client activity is unavailable in your current scope.');
  readonly pageError = computed(() => {
    const data = this.ws.data();
    if (data?.nextCursor && this.cursors().includes(data.nextCursor))
      return 'The activity continuation repeated an earlier page. Refresh from the start.';
    return data && this.cursors().length > 1 && this.cutoff() !== data.snapshotAtUtc
      ? 'The activity continuation changed. Refresh from the start.' : '';
  });
  readonly page = computed(() => this.session.current()?.staff !== false || this.pageError() ? null : this.ws.data());
  readonly utcTime = clientUtcTime;

  constructor() {
    effect(() => {
      const generation = this.session.invalidation(), session = this.session.current();
      const scope = session ? JSON.stringify([generation, session.firmId, session.userId, session.generation, session.staff]) : '';
      if (scope === this.boundScope) return;
      this.boundScope = scope;
      this.cursors.set([null]); this.cutoff.set(null);
      this.pendingFocus = null; this.navigationError.set('');
    });
    effect(() => {
      const data = this.page();
      if (!data) return;
      if (this.cursors().length === 1) this.cutoff.set(data.snapshotAtUtc);
      const pending = this.pendingFocus;
      if (!pending || pending.cursor !== this.cursors().at(-1)) return;
      this.pendingFocus = null;
      afterNextRender(() => {
        if (this.destroyed || pending.generation !== this.session.invalidation() || pending.cursor !== this.cursors().at(-1)) return;
        const active = this.host.nativeElement.ownerDocument.activeElement;
        if (active === pending.origin || active === this.host.nativeElement.ownerDocument.body) this.heading()?.nativeElement.focus();
      }, { injector: this.injector });
    });
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; });
  }
  private prepareNavigation(event: Event, cursor: string | null): void {
    this.navigationError.set('');
    this.pendingFocus = { origin: event.currentTarget as Element | null, generation: this.session.invalidation(), cursor };
  }
  next(event: Event): void {
    const cursor = this.page()?.nextCursor;
    if (this.ws.loading() || !cursor || this.cursors().includes(cursor)) return;
    this.prepareNavigation(event, cursor); this.cursors.update(items => [...items, cursor]);
  }
  previous(event: Event): void {
    if (this.ws.loading() || this.cursors().length < 2) return;
    this.prepareNavigation(event, this.cursors().at(-2)!); this.cursors.update(items => items.slice(0, -1));
  }
  refresh(event: Event): void {
    if (this.ws.loading()) return;
    this.prepareNavigation(event, null); this.cutoff.set(null);
    if (this.cursors().length === 1) this.ws.reload(); else this.cursors.set([null]);
  }
  private fragment(item: ActivityItem): string {
    return item.targetKind === 'document-review' ? `doc-review-${item.targetId}`
      : item.targetKind === 'signed-scan' ? `scan-${item.targetId}`
      : item.targetKind === 'bundle' ? `bundle-${item.targetId}` : 'portal-finance-heading';
  }
  sourceHref(item: ActivityItem): string {
    const target = this.router.parseUrl(this.router.url); target.fragment = this.fragment(item);
    return this.location.prepareExternalUrl(this.router.serializeUrl(target));
  }
  async openSource(event: MouseEvent, item: ActivityItem): Promise<void> {
    if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
    event.preventDefault();
    const current = () => this.page()?.items.some(row => row.key === item.key && row.targetId === item.targetId) === true;
    if (!current()) return;
    const generation = this.session.invalidation();
    const fragment = this.fragment(item);
    const destination = this.router.parseUrl(this.router.url); destination.fragment = fragment;
    try {
      await this.router.navigateByUrl(destination);
      if (this.destroyed || generation !== this.session.invalidation() || !current() || this.router.url !== this.router.serializeUrl(destination)) return;
      const target = this.host.nativeElement.ownerDocument.getElementById(fragment);
      if (target && this.host.nativeElement.closest('audit-client-portal')?.contains(target)) {
        if (!target.hasAttribute('tabindex')) target.setAttribute('tabindex', '-1');
        target.focus(); target.scrollIntoView({ block: 'start' });
      } else this.navigationError.set('The source is not in the currently loaded portal panels. Open the corresponding request or refresh the portal.');
    } catch { if (!this.destroyed && generation === this.session.invalidation()) this.navigationError.set('The source location could not be opened.'); }
  }
}
