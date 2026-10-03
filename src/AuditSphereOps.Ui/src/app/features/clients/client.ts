import { Component, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Subscription, timeout } from 'rxjs';
import { PortfolioNavigation } from '../portfolio/portfolio-contracts';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';
import { guidPattern } from '../../core/contracts';
import { Client, ClientLocation, clientLocation, decodeClient, clientUtcTime, portalIntentExplanation } from './client-contracts';
export { decodeClient } from './client-contracts';
@Component({
  selector: 'audit-client',
  imports: [
    FormsModule,
    MatFormFieldModule,
    MatInputModule,
    RouterLink,
    MatButtonModule,
    MatProgressBarModule,
    ...SHARED,
  ],
  templateUrl: './client.html',
  styleUrl: './client.scss',
})
export class ClientDetail {
  readonly portfolioNavigation = inject(PortfolioNavigation);
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly utcTime = clientUtcTime;
  readonly portalExplanation = portalIntentExplanation;
  readonly location = signal<ClientLocation | null>(clientLocation(k => this.route.snapshot.queryParamMap.get(k)));
  private viewRevision = 0;
  private routeRevision = 0;
  private destroyed = false;
  private readonly session = inject(SessionService);
  private request?: Subscription;
  private id = '';
  serviceRoute = '';
  serviceProfile = '';
  periodStart = '';
  periodEnd = '';
  readonly saving = signal(false);
  readonly uncertain = signal(false);
  readonly commandStatus = signal('');
  readonly data = signal<Client | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  constructor() {
    const routeSubscription = this.route.paramMap.subscribe((p) => {
      this.resetContact();
      this.routeRevision++;
      this.saving.set(false);
      this.id = (p.get('id') ?? '').toLowerCase();
      this.load();
    });
    const querySubscription = this.route.queryParamMap.subscribe(p => {
      this.location.set(clientLocation(k => p.get(k)));
      this.load();
    });
    effect(() => {
      this.session.invalidation();
      const staff = this.session.current()?.staff;
      untracked(() => {
        this.routeRevision++;
        this.saving.set(false);
        this.request?.unsubscribe();
        this.data.set(null);
        this.resetContact();
        if (staff) this.load();
      });
    });
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.routeRevision++;
      this.viewRevision++;
      routeSubscription.unsubscribe();
      querySubscription.unsubscribe();
      this.request?.unsubscribe();
    });
  }
  private resetContact(): void {
    this.serviceRoute = '';
    this.serviceProfile = '';
    this.periodStart = '';
    this.periodEnd = '';
    this.commandStatus.set('');
    this.uncertain.set(false);
  }
  createEngagement(): void {
    if (!this.data()?.canCreateEngagement || this.saving() || this.uncertain()) return;
    this.saving.set(true);
    this.commandStatus.set('Creating blocked engagement…');
    const id = this.id;
    const owner = this.owner();
    this.http
      .post('/api/ui/clients/' + id + '/engagements', {
        serviceRoute: this.serviceRoute,
        serviceProfile: this.serviceProfile,
        periodStart: this.periodStart,
        periodEnd: this.periodEnd,
      })
      .pipe(timeout(15000))
      .subscribe({
        next: () => {
          if (owner !== this.owner() || this.destroyed) return;
          this.saving.set(false);
          this.resetContact();
          this.commandStatus.set('Engagement created. Partner activation remains required.');
          this.load();
        },
        error: (failure) => {
          if (owner !== this.owner() || this.destroyed) return;
          this.saving.set(false);
          if (failure.status >= 400 && failure.status < 500)
            this.commandStatus.set(
              'Creation refused. Review your scope, service profile and period.',
            );
          else {
            this.uncertain.set(true);
            this.commandStatus.set(
              'Creation outcome unconfirmed. Review current engagements before resubmitting.',
            );
          }
          if (failure.status === 401) this.session.clear();
        },
      });
  }
  page(kind: 'engagement' | 'contact', page: number): void {
    const location = this.location(), data = this.data();
    if (!location || !data || this.saving() || !Number.isSafeInteger(page) || page < 0 || page > 10000) return;
    const total = kind === 'engagement' ? data.metrics.engagements : data.metrics.contacts;
    const size = kind === 'engagement' ? location.engagementPageSize : location.contactPageSize;
    if (page > Math.max(0, Math.ceil(total / size) - 1)) return;
    this.navigate({ ...location, [kind + 'Page']: page });
  }
  size(kind: 'engagement' | 'contact', value: string): void {
    const location = this.location();
    if (!location || this.saving() || !['10', '25', '50'].includes(value)) return;
    this.navigate({ ...location, [kind + 'Page']: 0, [kind + 'PageSize']: Number(value) });
  }
  private navigate(location: ClientLocation): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams: location });
  }
  private owner(): string {
    const s = this.session.current();
    return s?.staff ? `${s.firmId}:${s.userId}:${s.generation}:${this.session.invalidation()}:${this.id}:${this.routeRevision}` : '';
  }
  load(): void {
    const revision = ++this.viewRevision;
    this.request?.unsubscribe();
    this.data.set(null);
    this.error.set('');
    this.loading.set(false);
    if (this.destroyed || !this.session.current()?.staff) return;
    const id = this.id, location = this.location(), owner = this.owner();
    if (!guidPattern.test(id) || !location) {
      this.error.set('The client link or page parameters are invalid.');
      return;
    }
    this.loading.set(true);
    const query = new URLSearchParams(Object.entries(location).map(([k,v]) => [k, String(v)]));
    this.request = this.http.get<unknown>('/api/ui/clients/' + id + '?' + query).pipe(timeout(15000)).subscribe({
      next: (value) => {
        if (revision !== this.viewRevision || owner !== this.owner() || this.destroyed) return;
        try {
          const decoded = decodeClient(value);
          if (decoded.id.toLowerCase() !== id || Object.entries(location).some(([key, n]) => decoded.paging[key as keyof ClientLocation] !== n))
            throw new Error('Wrong client context');
          this.data.set(decoded);
        } catch { this.error.set('The server returned an unsupported response.'); }
        this.loading.set(false);
      },
      error: (failure) => {
        if (revision !== this.viewRevision || owner !== this.owner() || this.destroyed) return;
        this.loading.set(false);
        this.error.set('Check your access or retry shortly.');
        if (failure.status === 401) this.session.clear();
      },
    });
  }
}
