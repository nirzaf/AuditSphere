import { Component, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { Subscription, filter, map, timeout } from 'rxjs';
import { bool, decode, guid, obj, str } from './decode';
import { guidPattern } from './contracts';
import { SessionService } from './session';
import { decodeWorkspaceContextChoices, type WorkspaceContextChoicesPage } from './workspace-context-contracts';

type ContextResourceEndpoint = 'accounting/packages' | 'audit/workpapers' | 'audit/populations' | 'findings' | 'reviews' | 'records/archives';
type ContextRoute = { kind: 'engagement'; id: string } | { kind: 'client'; id: string } |
  { kind: 'resource'; endpoint: ContextResourceEndpoint; id: string };
type ContextState = 'none' | 'loading' | 'available' | 'unavailable';

interface WorkspaceContextView {
  clientId: string;
  clientName: string;
  engagementId: string | null;
  service: string | null;
  periodStart: string | null;
  periodEnd: string | null;
  canOpenClient: boolean;
}

interface ChoiceState {
  items: WorkspaceContextChoicesPage['items'];
  nextCursor: string | null;
  loading: boolean;
  deadline: number;
  wallDeadline: number;
  seenIds: string[];
  seenCursors: string[];
}

const engagementContext = obj({
  id: guid,
  clientId: guid,
  clientName: str(2000),
  serviceRoute: str(200),
  serviceProfileId: str(200),
  periodStart: str(100),
  periodEnd: str(100),
  canViewClientProfile: bool,
});
const resourceParent = obj({ id: guid, engagementId: guid });
const choicePageSize = 100;

function serviceLabel(value: string): string {
  const words = value.trim().replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/[_-]+/g, ' ').split(/\s+/).filter(Boolean);
  return words.length ? words.map(word => word.toLowerCase()).map(word => word[0].toUpperCase() + word.slice(1)).join(' ') : 'Engagement';
}

/** Route-derived, read-only context. It never changes the authenticated firm or grants. */
@Component({
  selector: 'audit-workspace-context',
  imports: [RouterLink],
  template: `
    @if (state() !== 'none') {
      <section class="context-bar" aria-label="Current workspace context" aria-live="polite">
        <div class="context-summary">
          <span class="context-caption">Current context</span>
          @if (state() === 'loading') {
            <span role="status">Loading authorized context…</span>
          } @else if (state() === 'unavailable' || !context()) {
            <span role="status">Current context unavailable</span>
          } @else {
            <strong>{{ context()!.clientName }}</strong>
            @if (context()!.engagementId) {
              <span>{{ context()!.service || 'Engagement' }} · {{ context()!.periodStart }} to {{ context()!.periodEnd }}</span>
            } @else {
              <span>Client workspace</span>
            }
          }
        </div>
        @if (context(); as current) {
          <div class="context-actions">
            @if (switchChoices().length) {
              <label>
                <span>Switch engagement</span>
                <select [value]="current.engagementId ?? ''" aria-label="Switch to an authorized engagement"
                  [disabled]="choices()?.loading ?? true" (change)="selectEngagement($event)">
                  @if (!current.engagementId) { <option value="" disabled>Choose an engagement</option> }
                  @for (item of switchChoices(); track item.engagementId) {
                    <option [value]="item.engagementId">{{ serviceLabel(item.serviceRoute) }} · {{ item.periodStart }} to {{ item.periodEnd }}</option>
                  }
                </select>
              </label>
            }
            @if (choices()?.loading) { <span role="status">Loading choices…</span> }
            @if (choices()?.nextCursor && !choices()?.loading) {
              <button type="button" (click)="loadMore()">Load more engagements</button>
            }
            @if (current.canOpenClient) {
              <a [routerLink]="['/app/clients', current.clientId]">Client profile</a>
            }
            <a routerLink="/app">Portfolio</a>
          </div>
        }
        @if (state() === 'unavailable' && routeHasContext()) {
          <button type="button" (click)="retry()">Refresh context</button>
        }
      </section>
    }
  `,
  styles: `
    :host { display: block; }
    .context-bar { display: flex; align-items: center; justify-content: space-between; gap: .75rem 1rem;
      flex-wrap: wrap; padding: .5rem 1rem; border-bottom: 1px solid var(--as-shell-border, #dbe3ef);
      background: var(--as-surface, #fff); color: var(--as-text, #172033); }
    .context-summary, .context-actions { display: flex; align-items: center; gap: .5rem .85rem; flex-wrap: wrap; min-width: 0; }
    .context-caption { color: var(--as-muted, #526078); font-size: .8rem; }
    .context-summary strong { overflow-wrap: anywhere; }
    .context-actions label { display: inline-flex; align-items: center; gap: .4rem; }
    select, button, a { min-height: 2.5rem; }
    select { max-width: min(32rem, 80vw); }
    a { display: inline-flex; align-items: center; }
    :focus-visible { outline: 3px solid #1d4ed8; outline-offset: 2px; }
    @media (max-width: 520px) {
      .context-bar { align-items: flex-start; padding-inline: .75rem; }
      .context-actions, .context-actions label { width: 100%; }
      select { flex: 1; min-width: 0; max-width: 100%; }
    }
  `,
})
export class WorkspaceContext {
  private readonly router = inject(Router);
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private readonly routeUrl = toSignal(this.router.events.pipe(
    filter((event): event is NavigationEnd => event instanceof NavigationEnd),
    map(event => event.urlAfterRedirects),
  ), { initialValue: this.router.url });

  readonly state = signal<ContextState>('none');
  readonly serviceLabel = serviceLabel;
  readonly context = signal<WorkspaceContextView | null>(null);
  readonly choices = signal<ChoiceState | null>(null);
  readonly routeHasContext = signal(false);
  readonly switchChoices = () => {
    const current = this.context();
    const state = this.choices();
    if (!current || !state || state.deadline <= performance.now() || state.wallDeadline <= Date.now()) return [];
    if (!current.engagementId) return state.items.length ? state.items : [];
    const currentId = current.engagementId.toLowerCase();
    const currentChoice = state.items.find(item => item.engagementId.toLowerCase() === currentId) ?? {
      engagementId: current.engagementId, serviceRoute: current.service ?? '',
      periodStart: current.periodStart ?? '', periodEnd: current.periodEnd ?? '', status: 'CURRENT',
    };
    const alternatives = state.items.filter(item => item.engagementId.toLowerCase() !== currentId);
    return alternatives.length || state.nextCursor ? [currentChoice, ...alternatives] : [];
  };

  private activeKey = '';
  private requestSerial = 0;
  private choiceSerial = 0;
  private contextRequest?: Subscription;
  private choiceRequest?: Subscription;
  private expiryTimer?: ReturnType<typeof setTimeout>;

  constructor() {
    const destroy = inject(DestroyRef);
    destroy.onDestroy(() => {
      this.contextRequest?.unsubscribe();
      this.choiceRequest?.unsubscribe();
      if (this.expiryTimer !== undefined) clearTimeout(this.expiryTimer);
    });
    effect(() => {
      const url = this.routeUrl();
      const owner = this.owner();
      untracked(() => this.reconcile(url, owner));
    });
  }

  private owner(): string {
    const session = this.session.current();
    return session?.staff
      ? `${session.firmId}:${session.userId}:${session.generation}:${this.session.invalidation()}`
      : '';
  }

  private path(url: string): string {
    try { return this.router.parseUrl(url).root.children['primary']?.segments.map(segment => segment.path).join('/') ?? ''; }
    catch { return ''; }
  }

  private describe(path: string): ContextRoute | null {
    const segments = path.split('/').filter(Boolean);
    if (segments[0] !== 'app') return null;
    if (segments[1] === 'engagements' && segments[2]) return { kind: 'engagement', id: segments[2] };
    // These route components take an engagement ID and read its existing authorized workspace.
    if (segments[1] === 'completion' && segments[2]) return { kind: 'engagement', id: segments[2] };
    if (segments[1] === 'audit' && segments[2] === 'plans' && segments[3])
      return { kind: 'engagement', id: segments[3] };
    if (segments[1] === 'clients' && segments[2]) return { kind: 'client', id: segments[2] };
    if (segments[1] === 'accounting' && segments[2] === 'clients' && segments[3])
      return { kind: 'client', id: segments[3] };
    if (segments[1] === 'audit' && segments[2] === 'workpapers' && segments[3])
      return { kind: 'resource', endpoint: 'audit/workpapers', id: segments[3] };
    if (segments[1] === 'accounting' && segments[2] === 'packages' && segments[3])
      return { kind: 'resource', endpoint: 'accounting/packages', id: segments[3] };
    if (segments[1] === 'audit' && segments[2] === 'populations' && segments[3])
      return { kind: 'resource', endpoint: 'audit/populations', id: segments[3] };
    if (segments[1] === 'findings' && segments[2]) return { kind: 'resource', endpoint: 'findings', id: segments[2] };
    if (segments[1] === 'reviews' && segments[2]) return { kind: 'resource', endpoint: 'reviews', id: segments[2] };
    if (segments[1] === 'records' && segments[2] === 'archives' && segments[3])
      return { kind: 'resource', endpoint: 'records/archives', id: segments[3] };
    return null;
  }

  private reconcile(url: string, owner: string): void {
    const path = this.path(url), target = this.describe(path), key = `${owner}|${path}`;
    if (key === this.activeKey) return;
    this.activeKey = key;
    const serial = ++this.requestSerial;
    ++this.choiceSerial;
    this.contextRequest?.unsubscribe(); this.contextRequest = undefined;
    this.choiceRequest?.unsubscribe(); this.choiceRequest = undefined;
    if (this.expiryTimer !== undefined) clearTimeout(this.expiryTimer);
    this.expiryTimer = undefined;
    this.context.set(null); this.choices.set(null);
    this.routeHasContext.set(target !== null);
    if (!target) { this.state.set('none'); return; }
    if (!guidPattern.test(target.id) || !owner || !this.session.current()?.staff) {
      this.state.set('unavailable'); return;
    }
    this.state.set('loading');
    if (target.kind === 'client') this.readClient(target.id, path, owner, serial);
    else if (target.kind === 'engagement') this.readEngagement(target.id, path, owner, serial);
    else this.resolveParent(target, path, owner, serial);
  }

  private isCurrent(path: string, owner: string, serial: number): boolean {
    return serial === this.requestSerial && path === this.path(this.routeUrl()) && owner === this.owner();
  }

  private readClient(clientId: string, path: string, owner: string, serial: number): void {
    const started = performance.now(), startedWall = Date.now();
    const params = new URLSearchParams({ clientId, pageSize: String(choicePageSize) });
    this.contextRequest = this.http.get<unknown>(`/api/ui/portfolio/context-choices?${params}`)
      .pipe(timeout(20000)).subscribe({
        next: raw => {
          if (!this.isCurrent(path, owner, serial)) return;
          try {
            const page = decodeWorkspaceContextChoices(raw, clientId, choicePageSize);
            this.commit(page, { clientId: page.clientId, clientName: page.clientName, engagementId: null,
              service: null, periodStart: null, periodEnd: null, canOpenClient: page.canOpenClient },
              Math.max(performance.now() - started, Date.now() - startedWall));
          } catch { this.failContext(); }
        },
        error: failure => this.contextFailure(failure, path, owner, serial),
      });
  }

  private resolveParent(target: Extract<ContextRoute, { kind: 'resource' }>, path: string, owner: string, serial: number): void {
    this.contextRequest = this.http.get<unknown>(`/api/ui/${target.endpoint}/${encodeURIComponent(target.id)}`)
      .pipe(timeout(20000)).subscribe({
        next: raw => {
          if (!this.isCurrent(path, owner, serial)) return;
          try {
            const parent = decode(resourceParent, raw);
            if (parent.id.toLowerCase() !== target.id.toLowerCase()) throw new Error('Wrong resource identity');
            this.readEngagement(parent.engagementId, path, owner, serial);
          } catch { this.failContext(); }
        },
        error: failure => this.contextFailure(failure, path, owner, serial),
      });
  }

  private readEngagement(engagementId: string, path: string, owner: string, serial: number): void {
    const params = new URLSearchParams({ holdPage: '0', holdPageSize: '10' });
    this.contextRequest = this.http.get<unknown>(`/api/ui/engagements/${encodeURIComponent(engagementId)}?${params}`)
      .pipe(timeout(20000)).subscribe({
        next: raw => {
          if (!this.isCurrent(path, owner, serial)) return;
          try {
            const engagement = decode(engagementContext, raw);
            if (engagement.id.toLowerCase() !== engagementId.toLowerCase()) throw new Error('Wrong engagement identity');
            this.readEngagementChoices(engagement, path, owner, serial);
          } catch { this.failContext(); }
        },
        error: failure => this.contextFailure(failure, path, owner, serial),
      });
  }

  private readEngagementChoices(engagement: ReturnType<typeof engagementContext>, path: string, owner: string, serial: number): void {
    const started = performance.now(), startedWall = Date.now();
    const params = new URLSearchParams({ clientId: engagement.clientId, pageSize: String(choicePageSize) });
    this.choiceRequest = this.http.get<unknown>(`/api/ui/portfolio/context-choices?${params}`)
      .pipe(timeout(20000)).subscribe({
        next: raw => {
          if (!this.isCurrent(path, owner, serial)) return;
          try {
            const page = decodeWorkspaceContextChoices(raw, engagement.clientId, choicePageSize);
            const current = page.items.find(item => item.engagementId.toLowerCase() === engagement.id.toLowerCase());
            if (page.clientName !== engagement.clientName || page.canOpenClient !== engagement.canViewClientProfile ||
                current && (current.serviceRoute !== engagement.serviceRoute || current.periodStart !== engagement.periodStart || current.periodEnd !== engagement.periodEnd))
              throw new Error('Changed engagement context');
            this.commit(page, {
              clientId: engagement.clientId, clientName: engagement.clientName, engagementId: engagement.id,
              service: serviceLabel(engagement.serviceRoute || engagement.serviceProfileId),
              periodStart: engagement.periodStart, periodEnd: engagement.periodEnd,
              canOpenClient: engagement.canViewClientProfile,
            }, Math.max(performance.now() - started, Date.now() - startedWall));
          } catch { this.failContext(); }
        },
        error: failure => this.contextFailure(failure, path, owner, serial),
      });
  }

  private commit(page: WorkspaceContextChoicesPage, context: WorkspaceContextView, elapsedMs: number): void {
    if (context.clientId.toLowerCase() !== page.clientId.toLowerCase()) { this.failContext(); return; }
    const remaining = page.validForMs - elapsedMs;
    if (remaining <= 0) { this.failContext(); return; }
    const now = performance.now(), wall = Date.now();
    this.context.set(context);
    this.choices.set({ items: page.items, nextCursor: page.nextCursor, loading: false,
      deadline: now + remaining, wallDeadline: wall + remaining,
      seenIds: page.items.map(item => item.engagementId.toLowerCase()), seenCursors: [] });
    this.state.set('available');
    this.expiryTimer = setTimeout(() => this.expireContext(), remaining);
  }

  private expireContext(): void {
    ++this.requestSerial;
    ++this.choiceSerial;
    this.contextRequest?.unsubscribe(); this.contextRequest = undefined;
    this.choiceRequest?.unsubscribe(); this.choiceRequest = undefined;
    if (this.expiryTimer !== undefined) clearTimeout(this.expiryTimer);
    this.expiryTimer = undefined;
    this.context.set(null); this.choices.set(null); this.state.set('unavailable');
  }

  private failContext(): void {
    ++this.requestSerial;
    ++this.choiceSerial;
    this.contextRequest?.unsubscribe(); this.contextRequest = undefined;
    this.choiceRequest?.unsubscribe(); this.choiceRequest = undefined;
    if (this.expiryTimer !== undefined) clearTimeout(this.expiryTimer);
    this.expiryTimer = undefined;
    this.context.set(null); this.choices.set(null); this.state.set('unavailable');
  }

  private contextFailure(failure: unknown, path: string, owner: string, serial: number): void {
    if (!this.isCurrent(path, owner, serial)) return;
    if (failure instanceof HttpErrorResponse && failure.status === 401) this.session.clear();
    this.failContext();
  }

  retry(): void {
    this.activeKey = '';
    this.reconcile(this.routeUrl(), this.owner());
  }

  loadMore(): void {
    const state = this.choices(), context = this.context();
    if (!state || !context || state.loading || !state.nextCursor || state.deadline <= performance.now() || state.wallDeadline <= Date.now()) {
      if (state && (state.deadline <= performance.now() || state.wallDeadline <= Date.now())) this.expireContext();
      return;
    }
    const serial = ++this.choiceSerial, routeSerial = this.requestSerial, path = this.path(this.routeUrl()), owner = this.owner();
    const cursor = state.nextCursor, started = performance.now(), startedWall = Date.now();
    this.choices.set({ ...state, loading: true });
    const params = new URLSearchParams({ clientId: context.clientId, pageSize: String(choicePageSize), cursor });
    this.choiceRequest = this.http.get<unknown>(`/api/ui/portfolio/context-choices?${params}`)
      .pipe(timeout(20000)).subscribe({
        next: raw => {
          if (serial !== this.choiceSerial || !this.isCurrent(path, owner, routeSerial)) return;
          try {
            const page = decodeWorkspaceContextChoices(raw, context.clientId, choicePageSize);
            const elapsed = Math.max(performance.now() - started, Date.now() - startedWall);
            const remaining = page.validForMs - elapsed;
            const deadline = Math.min(state.deadline, performance.now() + remaining);
            const wallDeadline = Math.min(state.wallDeadline, Date.now() + remaining);
            const ids = page.items.map(item => item.engagementId.toLowerCase());
            if (remaining <= 0 || performance.now() >= state.deadline || Date.now() >= state.wallDeadline ||
                page.clientName !== context.clientName || page.canOpenClient !== context.canOpenClient ||
                state.seenCursors.includes(cursor) || ids.some(id => state.seenIds.includes(id)) ||
                page.nextCursor !== null && (page.nextCursor === cursor || state.seenCursors.includes(page.nextCursor)))
              throw new Error('Changed context choices');
            this.choices.set({ items: [...state.items, ...page.items], nextCursor: page.nextCursor, loading: false,
              deadline, wallDeadline, seenIds: [...state.seenIds, ...ids], seenCursors: [...state.seenCursors, cursor] });
          } catch { this.failContext(); }
        },
        error: failure => {
          if (serial !== this.choiceSerial || !this.isCurrent(path, owner, routeSerial)) return;
          if (failure instanceof HttpErrorResponse && failure.status === 401) this.session.clear();
          this.failContext();
        },
      });
  }

  selectEngagement(event: Event): void {
    const select = event.target as HTMLSelectElement;
    const state = this.choices(), current = this.context();
    if (state && (state.deadline <= performance.now() || state.wallDeadline <= Date.now())) {
      this.expireContext();
      select.value = '';
      return;
    }
    const target = state?.items.find(item => item.engagementId === select.value);
    select.value = current?.engagementId ?? '';
    if (!current || !target || target.engagementId.toLowerCase() === current.engagementId?.toLowerCase()) return;
    // Route guards own the transition. The displayed context changes only after NavigationEnd.
    void this.router.navigateByUrl(`/app/engagements/${encodeURIComponent(target.engagementId)}`);
  }
}
