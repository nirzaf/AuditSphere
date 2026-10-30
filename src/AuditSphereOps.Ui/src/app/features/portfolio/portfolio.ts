import { Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { LocationStrategy } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Subscription, timeout } from 'rxjs';
import { toSignal } from '@angular/core/rxjs-interop';
import { form, FormField, maxLength } from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Api } from '../../core/api';
import { guidPattern } from '../../core/contracts';
import { SessionService } from '../../core/session';
import { WorkspaceAvailability } from '../../core/workspace-availability';
import { SHARED } from '../../core/ui';
import { PortfolioProgress } from '../practice/engagement-progress';
import { WorkspaceNavigationIcon } from '../../core/workspace-navigation-icon';
import { PortfolioContextPage, PortfolioNavigation, decodeContextPage, decodePortfolioWorkspace, portfolioLocation, portfolioWindow } from './portfolio-contracts';
export { decodePortfolio } from './portfolio-contracts';
export type { ClientRow, PortfolioPage } from './portfolio-contracts';

interface ChoiceState {
  clientId: string; context: string; loading: boolean; error: string; page: PortfolioContextPage | null;
  deadline: number; wallDeadline: number; first: boolean; seen: string[]; cursors: string[];
}

@Component({
  selector: 'audit-portfolio',
  imports: [RouterLink, FormField, MatButtonModule, MatFormFieldModule, MatInputModule, PortfolioProgress, WorkspaceNavigationIcon, ...SHARED],
  templateUrl: './portfolio.html',
  styleUrl: './portfolio.scss',
})
export class Portfolio {
  private readonly api = inject(Api);
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private readonly availability = inject(WorkspaceAvailability);
  private readonly locationStrategy = inject(LocationStrategy);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly navigation = inject(PortfolioNavigation);
  private readonly params = toSignal(this.route.queryParamMap, { initialValue: this.route.snapshot.queryParamMap });
  readonly location = computed(() => portfolioLocation((k) => this.params().get(k)));
  readonly model = signal({ search: '' });
  readonly fields = form(this.model, (p) => maxLength(p.search, 100));
  private readonly identityFence = signal(false);
  private workspaceRequest = '';
  readonly view = this.api.resource(() => {
    const q = this.location();
    this.workspaceRequest = this.portfolioContext();
    return q && this.workspaceRequest && !this.identityFence() ? `/api/ui/portfolio/workspace?${new URLSearchParams({ search: q.search, page: String(q.page), pageSize: String(q.pageSize) })}` : null;
  }, (v) => {
    const value = decodePortfolioWorkspace(v), q = this.location();
    if (this.identityFence() || !q || !this.workspaceRequest || this.workspaceRequest !== this.portfolioContext() ||
        value.search !== q.search || value.clients.page !== q.page || value.clients.pageSize !== q.pageSize)
      throw new Error('Wrong portfolio context');
    return value;
  }, 'Portfolio is unavailable in your current scope.',
  'No portfolio data is shown without an active AuditSphere grant. Ask a firm administrator to review your access, then retry.',
  () => this.availability.invalidate());
  readonly exporting = signal(false);
  readonly message = signal('');
  readonly failed = signal(false);
  private readonly candidatePage = signal(0);
  private readonly packagePage = signal(0);
  private readonly candidateSize = signal(10);
  private readonly packageSize = signal(10);
  readonly candidates = computed(() => portfolioWindow(this.view.data()?.candidates ?? [], this.candidatePage(), this.candidateSize()));
  readonly packages = computed(() => portfolioWindow(this.view.data()?.packages ?? [], this.packagePage(), this.packageSize()));
  private version = 0;
  private destroyed = false;
  private choiceRequest?: Subscription;
  private choiceTimer?: ReturnType<typeof setTimeout>;
  private choiceSerial = 0;
  private readonly choice = signal<ChoiceState | null>(null);
  constructor() {
    let previousOwner = this.owner();
    const remembered = this.navigation.read();
    if (!this.route.snapshot.queryParamMap.keys.length && remembered)
      void this.router.navigate([], { relativeTo: this.route, queryParams: remembered, replaceUrl: true });
    effect(() => {
      const q = this.location();
      this.session.invalidation();
      this.session.current();
      untracked(() => {
        this.version++;
        this.message.set(''); this.failed.set(false); this.exporting.set(false);
        this.candidatePage.set(0); this.packagePage.set(0);
        const owner = this.owner();
        if (previousOwner && owner !== previousOwner) {
          this.identityFence.set(true); this.model.set({ search: '' }); this.navigation.read();
          if (owner) {
            previousOwner = owner;
            void this.router.navigate([], { relativeTo: this.route, queryParams: {}, replaceUrl: true }).then(() => {
              if (!this.destroyed && owner === this.owner()) this.identityFence.set(false);
            });
          }
          return;
        }
        previousOwner = owner;
        this.model.set({ search: owner ? q?.search ?? '' : '' });
        if (q && owner) this.navigation.remember(q);
      });
    });
    effect(() => {
      const context = this.portfolioContext();
      untracked(() => { if (this.choice() && this.choice()!.context !== context) this.closeChoices(); });
    });
    const onReturn = () => {
      const state = this.choice();
      if (!state || this.choiceFor(state.clientId)) return;
      if (state.loading && state.page && state.context === this.portfolioContext()) this.expireChoices(state.clientId, state.context);
      else this.closeChoices();
    };
    window.addEventListener('focus', onReturn);
    document.addEventListener('visibilitychange', onReturn);
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true; this.version++; this.closeChoices();
      window.removeEventListener('focus', onReturn); document.removeEventListener('visibilitychange', onReturn);
    });
  }
  private owner(): string {
    const s = this.session.current();
    return s?.staff ? `${s.firmId}:${s.userId}:${s.generation}:${this.session.invalidation()}` : '';
  }
  private portfolioContext(): string {
    const owner = this.owner(), q = this.location(), available = this.availability.current();
    const revision = this.availability.revision();
    return owner && q && available && !this.identityFence() ? JSON.stringify([owner, q.search, q.page, q.pageSize, q.selected, revision]) : '';
  }
  workspaceData() {
    return this.workspaceRequest && this.workspaceRequest === this.portfolioContext() ? this.view.data() : null;
  }
  accessCurrent(): boolean { return !!this.portfolioContext(); }
  refreshWorkspace(): void {
    this.closeChoices();
    if (this.portfolioContext() && this.workspaceRequest === this.portfolioContext()) this.view.reload();
    else this.availability.refresh();
  }
  choiceFor(clientId: string): ChoiceState | null {
    const state = this.choice();
    if (!state || state.clientId !== clientId || !state.context || state.context !== this.portfolioContext() ||
        !this.workspaceData()?.clients.items.some(item => item.id === clientId)) return null;
    if (state.page && (performance.now() >= state.deadline || Date.now() >= state.wallDeadline)) return null;
    return state;
  }
  closeChoices(): void {
    ++this.choiceSerial; this.choiceRequest?.unsubscribe(); this.choiceRequest = undefined;
    if (this.choiceTimer !== undefined) clearTimeout(this.choiceTimer);
    this.choiceTimer = undefined; this.choice.set(null);
  }
  openChoices(clientId: string): void {
    if (!this.portfolioContext() || !this.workspaceData()?.clients.items.some(item => item.id === clientId)) return;
    this.closeChoices();
    const state: ChoiceState = { clientId, context: this.portfolioContext(), loading: true, error: '', page: null,
      deadline: 0, wallDeadline: 0, first: true, seen: [], cursors: [] };
    this.choice.set(state);
    this.readChoices(state, null);
  }
  moreChoices(clientId: string): void {
    const state = this.choiceFor(clientId);
    if (!state || state.loading || !state.page?.hasMore || !state.page.nextCursor) return;
    this.readChoices(state, state.page.nextCursor);
  }
  private readChoices(state: ChoiceState, cursor: string | null): void {
    const serial = ++this.choiceSerial, started = performance.now(), startedWall = Date.now();
    const owner = this.owner();
    const size = 25;
    this.choice.set({ ...state, loading: true, error: '' });
    const params = new URLSearchParams({ clientId: state.clientId, pageSize: String(size) });
    if (cursor) params.set('cursor', cursor);
    this.choiceRequest = this.http.get<unknown>(`/api/ui/portfolio/context-choices?${params}`).pipe(timeout(20000)).subscribe({
      next: value => {
        if (serial !== this.choiceSerial) return;
        if (cursor !== null && (performance.now() >= state.deadline || Date.now() >= state.wallDeadline)) {
          if (this.choice()?.context === state.context && this.portfolioContext() === state.context)
            this.failChoices(state.clientId, state.context);
          return;
        }
        if (!this.choiceFor(state.clientId)) return;
        try {
          const page = decodeContextPage(value, state.clientId, size);
          const remaining = page.validForMs - Math.max(performance.now() - started, Date.now() - startedWall);
          const ids = page.items.map(item => item.engagementId.toLowerCase());
          if (remaining <= 0 || ids.some(id => state.seen.includes(id)) ||
              (cursor !== null && (!state.page || state.page.nextCursor !== cursor || state.cursors.includes(cursor) ||
                state.page.canOpenClient !== page.canOpenClient || state.page.clientName !== page.clientName)) ||
              (page.nextCursor !== null && (page.nextCursor === cursor || state.cursors.includes(page.nextCursor))) ||
              state.seen.length + ids.length > 10000 || state.cursors.length >= 1000) throw new Error('Changed context');
          if (this.choiceTimer !== undefined) clearTimeout(this.choiceTimer);
          this.choice.set({ ...state, loading: false, page, first: cursor === null, error: '',
            deadline: performance.now() + remaining, wallDeadline: Date.now() + remaining,
            seen: [...state.seen, ...ids], cursors: cursor ? [...state.cursors, cursor] : state.cursors });
          this.choiceTimer = setTimeout(() => this.expireChoices(state.clientId, state.context), remaining);
        } catch { this.failChoices(state.clientId, state.context); }
      },
      error: (error: unknown) => {
        if (serial !== this.choiceSerial || !owner || owner !== this.owner()) return;
        this.failChoices(state.clientId, state.context);
        if (error instanceof HttpErrorResponse && error.status === 401) this.session.clear();
        else if (error instanceof HttpErrorResponse && error.status === 403) this.availability.invalidate();
      },
    });
  }
  private expireChoices(clientId: string, context: string): void {
    const state = this.choice();
    if (state?.clientId === clientId && state.context === context && state.loading) {
      // Keep an in-flight continuation alive long enough to handle an authoritative denial.
      if (this.choiceTimer !== undefined) clearTimeout(this.choiceTimer);
      this.choiceTimer = undefined;
      this.choice.set({ ...state, page: null, error: 'Destinations expired. Load them again.' });
    } else this.failChoices(clientId, context);
  }
  private failChoices(clientId: string, context: string): void {
    this.closeChoices();
    if (context === this.portfolioContext()) this.choice.set({ clientId, context, loading: false,
      error: 'Destinations are unavailable or changed. Load them again.', page: null, deadline: 0, wallDeadline: 0,
      first: true, seen: [], cursors: [] });
  }
  search(event: Event): void {
    event.preventDefault();
    if (this.fields().invalid()) return;
    this.go(0, this.model().search.trim());
  }
  go(page: number, search = this.location()?.search ?? '', pageSize = this.location()?.pageSize ?? 25): void {
    if (page < 0 || page > 10000) return;
    void this.router.navigate([], { relativeTo: this.route, queryParams: { search, page, pageSize } });
  }
  resizePage(size: unknown): void {
    if (size === '10' || size === '25') this.go(0, this.location()?.search ?? '', Number(size));
  }
  createdAt(value: string): string {
    const date = new Date(value);
    return Number.isFinite(date.getTime()) ? date.toLocaleString(undefined, { dateStyle: 'short', timeStyle: 'short' }) : 'Unsupported timestamp';
  }
  recentPage(kind: 'candidate' | 'package', page: number): void {
    const current = kind === 'candidate' ? this.candidates() : this.packages();
    if (!Number.isSafeInteger(page) || page < 0 || page > Math.max(0, Math.ceil(current.total / current.pageSize) - 1)) return;
    (kind === 'candidate' ? this.candidatePage : this.packagePage).set(page);
  }
  recentSize(kind: 'candidate' | 'package', size: string): void {
    if (size !== '10' && size !== '25') return;
    (kind === 'candidate' ? this.candidateSize : this.packageSize).set(Number(size));
    (kind === 'candidate' ? this.candidatePage : this.packagePage).set(0);
  }
  private choiceRoute(kind: 'client' | 'engagement', target: string): string | null {
    return guidPattern.test(target) ? `/app/${kind === 'client' ? 'clients' : 'engagements'}/${encodeURIComponent(target)}` : null;
  }
  choiceHref(kind: 'client' | 'engagement', target: string): string | null {
    const route = this.choiceRoute(kind, target);
    return route ? this.locationStrategy.prepareExternalUrl(route) : null;
  }
  followChoice(id: string, kind: 'client' | 'engagement', target: string, event: MouseEvent): void {
    const page = this.choiceFor(id)?.page, route = this.choiceRoute(kind, target);
    if (!route || !page || (kind === 'client' ? !page.canOpenClient || target !== id
      : !page.items.some(item => item.engagementId === target))) { event.preventDefault(); return; }
    if (event.type !== 'click' || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
    event.preventDefault();
    const q = this.location();
    if (q) this.navigation.remember({ ...q, selected: id });
    void this.router.navigateByUrl(route);
  }
  async download(): Promise<void> {
    const q = this.location();
    if (!q || !this.workspaceData() || this.exporting()) return;
    const version = this.version, context = this.portfolioContext();
    this.exporting.set(true); this.message.set(''); this.failed.set(false);
    const r = await this.api.download('/api/ui/portfolio/export', { search: q.search }, (m) =>
      !this.destroyed && version === this.version && !!context && context === this.portfolioContext() && !!this.workspaceData() &&
      m.fileName === 'auditsphere-portfolio.csv' && m.contentType.startsWith('text/csv') &&
      m.byteCount <= 8_000_000);
    if (this.destroyed || version !== this.version) return;
    if (!context || context !== this.portfolioContext() || !this.workspaceData()) { this.exporting.set(false); return; }
    this.exporting.set(false); this.failed.set(!r.ok);
    this.message.set(r.ok ? 'Scoped CSV downloaded. The recent-record window is included; this export is not release approval.' : r.message);
  }
}
