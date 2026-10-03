import { Component, DestroyRef, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Subscription, timeout } from 'rxjs';
import { EngagementPlanning } from './planning';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';
import { guidPattern } from '../../core/contracts';
import { clientUtcTime } from '../clients/client-contracts';
import { PortfolioNavigation } from '../portfolio/portfolio-contracts';
import {
  Engagement,
  EngagementLocation,
  decodeEngagement,
  engagementLocation,
} from './engagement-contracts';
export { decodeEngagement } from './engagement-contracts';

@Component({
  selector: 'audit-engagement',
  imports: [
    FormsModule,
    EngagementPlanning,
    RouterLink,
    MatButtonModule,
    MatProgressBarModule,
    ...SHARED,
  ],
  templateUrl: './engagement.html',
  styleUrl: './engagement.scss',
})
export class EngagementDetail {
  readonly workflows = [
    { path: 'audit-plan', label: 'Audit plan' },
    { path: 'audit-fieldwork', label: 'Fieldwork' },
    { path: 'completion', label: 'Completion' },
    { path: 'statements', label: 'Statements' },
    { path: 'tb-intake', label: 'Trial balance intake' },
    { path: 'general-ledger', label: 'General ledger' },
    { path: 'pbc', label: 'PBC requests' },
  ];
  readonly portfolioNavigation = inject(PortfolioNavigation);
  readonly utcTime = clientUtcTime;
  readonly planning = viewChild(EngagementPlanning);
  readonly location = signal<EngagementLocation | null>(null);
  activationReviewed = false;
  readonly activating = signal(false);
  readonly activationUnknown = signal(false);
  readonly activationStatus = signal('');
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly session = inject(SessionService);
  private request?: Subscription;
  private id = '';
  private routeRevision = 0;
  private viewRevision = 0;
  private destroyed = false;
  readonly data = signal<Engagement | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  constructor() {
    this.location.set(engagementLocation((k) => this.route.snapshot.queryParamMap.get(k)));
    const routeSubscription = this.route.paramMap.subscribe((p) => {
      this.routeRevision++;
      this.clear();
      this.id = (p.get('id') ?? '').toLowerCase();
      this.load();
    });
    const querySubscription = this.route.queryParamMap.subscribe((p) => {
      this.location.set(engagementLocation((k) => p.get(k)));
      this.load();
    });
    effect(() => {
      this.session.invalidation();
      const staff = this.session.current()?.staff;
      untracked(() => {
        this.routeRevision++;
        this.request?.unsubscribe();
        this.clear();
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
      this.clear();
    });
  }
  private clear(): void {
    this.data.set(null);
    this.loading.set(false);
    this.error.set('');
    this.activationReviewed = false;
    this.activating.set(false);
    this.activationUnknown.set(false);
    this.activationStatus.set('');
  }
  private owner(): string {
    const s = this.session.current();
    return s?.staff
      ? `${s.firmId}:${s.userId}:${s.generation}:${this.session.invalidation()}:${this.id}:${this.routeRevision}`
      : '';
  }
  pagingBlocked(): boolean {
    return (
      this.loading() ||
      this.activating() ||
      !!this.planning()?.busy() ||
      !!this.planning()?.uncertain()
    );
  }
  page(page: number): void {
    const location = this.location(),
      data = this.data();
    if (
      !location ||
      !data ||
      this.pagingBlocked() ||
      !Number.isSafeInteger(page) ||
      page < 0 ||
      page >
        Math.min(10000, Math.max(0, Math.ceil(data.holdMetrics.total / location.holdPageSize) - 1))
    )
      return;
    this.navigate({ ...location, holdPage: page });
  }
  size(size: string): void {
    const location = this.location();
    if (location && !this.pagingBlocked() && ['10', '25', '50'].includes(size))
      this.navigate({ holdPage: 0, holdPageSize: Number(size) });
  }
  private navigate(location: EngagementLocation): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams: location });
  }
  workBlocked(engagement: Engagement): boolean {
    return engagement.professionalWorkBlocked || engagement.holdMetrics.active > 0;
  }
  activate(): void {
    if (
      !this.data()?.canActivate ||
      !this.activationReviewed ||
      this.activating() ||
      this.loading() ||
      this.activationUnknown()
    )
      return;
    const owner = this.owner();
    this.activating.set(true);
    this.activationStatus.set('Checking activation prerequisites…');
    this.http
      .post('/api/ui/engagements/' + this.id + '/activate', {})
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (this.destroyed || owner !== this.owner()) return;
          this.activating.set(false);
          if (
            !value ||
            typeof value !== 'object' ||
            !('id' in value) ||
            typeof value.id !== 'string' ||
            !guidPattern.test(value.id)
          ) {
            this.activationUnknown.set(true);
            this.activationReviewed = false;
            this.activationStatus.set(
              'Outcome unconfirmed. Refresh and review engagement state before another action.',
            );
            return;
          }
          this.activationReviewed = false;
          this.activationStatus.set('Engagement activated.');
          this.load();
        },
        error: (failure) => {
          if (this.destroyed || owner !== this.owner()) return;
          this.activating.set(false);
          this.activationReviewed = false;
          if (failure.status >= 400 && failure.status < 500) {
            this.activationStatus.set(
              'Activation blocked. Review current acceptance, your Partner scope and unreleased holds.',
            );
            this.load();
          } else {
            this.activationUnknown.set(true);
            this.activationStatus.set(
              'Outcome unconfirmed. Refresh and review engagement state before another action.',
            );
          }
          if (failure.status === 401) this.session.clear();
        },
      });
  }
  load(): void {
    const revision = ++this.viewRevision;
    this.request?.unsubscribe();
    this.error.set('');
    this.loading.set(false);
    if (this.destroyed || !this.session.current()?.staff) {
      this.data.set(null);
      return;
    }
    const id = this.id,
      location = this.location(),
      owner = this.owner();
    if (!guidPattern.test(id) || !location) {
      this.data.set(null);
      this.error.set('The engagement link or page parameters are invalid.');
      return;
    }
    // Hide the authorized view during revalidation, preserving the same engagement's team/budget editor instance.
    // A refusal, malformed response, identity change or session loss removes it completely.
    this.loading.set(true);
    const query = new URLSearchParams(Object.entries(location).map(([k, v]) => [k, String(v)]));
    this.request = this.http
      .get<unknown>('/api/ui/engagements/' + id + '?' + query)
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (this.destroyed || revision !== this.viewRevision || owner !== this.owner()) return;
          try {
            const v = decodeEngagement(value);
            if (
              v.id.toLowerCase() !== id ||
              v.paging.holdPage !== location.holdPage ||
              v.paging.holdPageSize !== location.holdPageSize
            )
              throw new Error('Wrong engagement context');
            if (v.generation !== this.data()?.generation || !v.canActivate)
              this.activationReviewed = false;
            if (v.status === 'Active') this.activationUnknown.set(false);
            this.data.set(v);
          } catch {
            this.data.set(null);
            this.activationReviewed = false;
            this.error.set('The server returned an unsupported response.');
          }
          this.loading.set(false);
        },
        error: (failure) => {
          if (this.destroyed || revision !== this.viewRevision || owner !== this.owner()) return;
          this.data.set(null);
          this.activationReviewed = false;
          this.loading.set(false);
          this.error.set('Check your access or retry shortly.');
          if (failure.status === 401) this.session.clear();
        },
      });
  }
}
