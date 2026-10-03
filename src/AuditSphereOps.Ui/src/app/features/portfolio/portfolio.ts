import { Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { form, FormField, maxLength } from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Api } from '../../core/api';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';
import { PortfolioNavigation, decodePortfolioWorkspace, portfolioLocation, portfolioWindow } from './portfolio-contracts';
export { decodePortfolio } from './portfolio-contracts';
export type { ClientRow, PortfolioPage } from './portfolio-contracts';

@Component({
  selector: 'audit-portfolio',
  imports: [RouterLink, FormField, MatButtonModule, MatFormFieldModule, MatInputModule, ...SHARED],
  templateUrl: './portfolio.html',
  styleUrl: './portfolio.scss',
})
export class Portfolio {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly navigation = inject(PortfolioNavigation);
  private readonly params = toSignal(this.route.queryParamMap, { initialValue: this.route.snapshot.queryParamMap });
  readonly location = computed(() => portfolioLocation((k) => this.params().get(k)));
  readonly model = signal({ search: '' });
  readonly fields = form(this.model, (p) => maxLength(p.search, 100));
  private readonly identityFence = signal(false);
  readonly view = this.api.resource(() => {
    const q = this.location();
    return q && !this.identityFence() ? `/api/ui/portfolio/workspace?${new URLSearchParams({ search: q.search, page: String(q.page), pageSize: String(q.pageSize) })}` : null;
  }, (v) => {
    const value = decodePortfolioWorkspace(v), q = this.location();
    if (this.identityFence() || !q || value.search !== q.search || value.clients.page !== q.page || value.clients.pageSize !== q.pageSize)
      throw new Error('Wrong portfolio context');
    return value;
  }, 'Portfolio is unavailable in your current scope.');
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
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; this.version++; });
  }
  private owner(): string {
    const s = this.session.current();
    return s?.staff ? `${s.firmId}:${s.userId}:${s.generation}:${this.session.invalidation()}` : '';
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
  select(id: string): void {
    const q = this.location();
    if (q) this.navigation.remember({ ...q, selected: id });
  }
  async download(): Promise<void> {
    const q = this.location();
    if (!q || !this.view.data() || this.exporting()) return;
    const version = this.version;
    this.exporting.set(true); this.message.set(''); this.failed.set(false);
    const r = await this.api.download('/api/ui/portfolio/export', { search: q.search }, (m) =>
      !this.destroyed && version === this.version && !!this.session.current()?.staff &&
      m.fileName === 'auditsphere-portfolio.csv' && m.contentType.startsWith('text/csv') &&
      m.byteCount <= 8_000_000);
    if (this.destroyed || version !== this.version) return;
    this.exporting.set(false); this.failed.set(!r.ok);
    this.message.set(r.ok ? 'Scoped CSV downloaded. The recent-record window is included; this export is not release approval.' : r.message);
  }
}
