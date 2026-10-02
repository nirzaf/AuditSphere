import { Component, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Subscription, timeout } from 'rxjs';
import { EngagementPlanning } from './planning';
import { SessionService } from '../../core/session';
interface Hold {
  kind: string;
  reason: string;
  released: boolean;
  createdAt: string;
  releasedAt: string | null;
}
interface Engagement {
  id: string;
  clientId: string;
  clientName: string;
  serviceRoute: string;
  status: string;
  periodStart: string;
  periodEnd: string;
  generation: string;
  professionalWorkBlocked: boolean;
  canActivate: boolean;
  holds: Hold[];
}
const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export function decodeEngagement(value: unknown): Engagement {
  if (!value || typeof value !== 'object') throw new Error('Invalid engagement');
  const v = value as Record<string, unknown>;
  if (
    !['id', 'clientId'].every((k) => typeof v[k] === 'string' && guid.test(v[k] as string)) ||
    !['clientName', 'serviceRoute', 'status', 'periodStart', 'periodEnd', 'generation'].every(
      (k) => typeof v[k] === 'string',
    ) ||
    !/^[0-9]{1,19}$/.test(v['generation'] as string) ||
    typeof v['canActivate'] !== 'boolean' ||
    typeof v['professionalWorkBlocked'] !== 'boolean' ||
    !Array.isArray(v['holds']) ||
    v['holds'].length > 100
  )
    throw new Error('Invalid engagement');
  for (const h of v['holds']) {
    if (
      !h ||
      typeof h.kind !== 'string' ||
      typeof h.reason !== 'string' ||
      typeof h.released !== 'boolean' ||
      typeof h.createdAt !== 'string' ||
      (h.releasedAt !== null && typeof h.releasedAt !== 'string')
    )
      throw new Error('Invalid hold');
  }
  return v as unknown as Engagement;
}
@Component({
  selector: 'audit-engagement',
  imports: [FormsModule, EngagementPlanning, RouterLink, MatButtonModule, MatProgressBarModule],
  template: `
    <a routerLink="/app">Portfolio</a>
    <h1>Engagement details</h1>
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" aria-label="Loading engagement" />
    }
    @if (error()) {
      <section role="alert">
        <h2>Engagement unavailable</h2>
        <p>{{ error() }}</p>
        <button matButton (click)="load()">Retry</button>
      </section>
    }
    @if (data(); as engagement) {
      <h2>{{ engagement.serviceRoute }} — {{ engagement.clientName }}</h2>
      <p>{{ engagement.status }} · {{ engagement.periodStart }} to {{ engagement.periodEnd }}</p>
      <p>
        Engagement ID: <code>{{ engagement.id }}</code> · Revision {{ engagement.generation }}
      </p>
      @if (engagement.professionalWorkBlocked) {
        <section role="status">
          <h2>Professional work blocked</h2>
          <p>Review the clearance gates before starting professional work.</p>
          <a [routerLink]="[]" fragment="engagement-clearance">Review clearance gates</a>
        </section>
      }
      @if (engagement.canActivate) {
        <section>
          <h2>Partner activation</h2>
          <p>
            Activation requires the current unconditional acceptance for this service and no
            unreleased hold.
          </p>
          <a [routerLink]="['/app/clients', engagement.clientId, 'assessment']"
            >Review acceptance checklist</a
          >
          <label
            ><input type="checkbox" [(ngModel)]="activationReviewed" [disabled]="activating()" /> I
            reviewed this engagement and its acceptance prerequisites.</label
          >
          <button
            matButton
            [disabled]="!activationReviewed || activating() || activationUnknown()"
            (click)="activate()"
          >
            Activate engagement
          </button>
          <p role="status">{{ activationStatus() }}</p>
        </section>
      }
      @defer (on viewport) {
        <audit-engagement-planning [engagementId]="engagement.id" />
      } @placeholder {
        <p>Team and budget</p>
      }
      <h2 id="engagement-clearance">Holds and clearance gates</h2>
      @if (!engagement.holds.length) {
        <p>No holds recorded.</p>
      }
      @for (hold of engagement.holds; track $index) {
        <article>
          <h3>{{ hold.kind }}</h3>
          <p>{{ hold.reason }}</p>
          <p>{{ hold.released ? 'Released' : 'Active' }} · {{ hold.createdAt }}</p>
        </article>
      }
      <nav aria-label="Engagement workflows">
        @for (workflow of workflows; track workflow.path) {
          <a [routerLink]="['/app/engagements', engagement.id, workflow.path]">{{
            workflow.label
          }}</a
          ><br />
        }
      </nav>
    }
  `,
})
export class EngagementDetail {
  readonly workflows = [
    { path: 'audit-plan', label: 'Audit plan' },
    { path: 'audit-fieldwork', label: 'Fieldwork' },
    { path: 'completion', label: 'Completion' },
    { path: 'statements', label: 'Statements' },
    { path: 'tb-intake', label: 'Trial balance intake' },
    { path: 'pbc', label: 'PBC requests' },
  ];
  activationReviewed = false;
  readonly activating = signal(false);
  readonly activationUnknown = signal(false);
  readonly activationStatus = signal('');
  activate(): void {
    if (
      !this.data()?.canActivate ||
      !this.activationReviewed ||
      this.activating() ||
      this.activationUnknown()
    )
      return;
    this.activating.set(true);
    this.activationStatus.set('Checking activation prerequisites…');
    const id = this.id;
    const generation = this.session.invalidation();
    this.http
      .post('/api/ui/engagements/' + id + '/activate', {})
      .pipe(timeout(15000))
      .subscribe({
        next: () => {
          this.activating.set(false);
          if (id !== this.id || generation !== this.session.invalidation()) return;
          this.activationReviewed = false;
          this.activationStatus.set('Engagement activated.');
          this.load();
        },
        error: (failure) => {
          this.activating.set(false);
          if (id !== this.id || generation !== this.session.invalidation()) return;
          if (failure.status >= 400 && failure.status < 500)
            this.activationStatus.set(
              'Activation blocked. Review current acceptance, your Partner scope and unreleased holds.',
            );
          else {
            this.activationUnknown.set(true);
            this.activationStatus.set(
              'Outcome unconfirmed. Refresh and review engagement state before another action.',
            );
          }
          if (failure.status === 401) this.session.clear();
        },
      });
  }
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  private readonly session = inject(SessionService);
  private request?: Subscription;
  private id = '';
  readonly data = signal<Engagement | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  constructor() {
    const routeSubscription = this.route.paramMap.subscribe((p) => {
      this.activationReviewed = false;
      this.activationUnknown.set(false);
      this.activationStatus.set('');
      this.id = p.get('id') ?? '';
      this.load();
    });
    effect(() => {
      this.session.invalidation();
      const staff = this.session.current()?.staff;
      untracked(() => {
        this.request?.unsubscribe();
        this.data.set(null);
        this.activationReviewed = false;
        this.activationUnknown.set(false);
        this.activationStatus.set('');
        if (staff) this.load();
      });
    });
    inject(DestroyRef).onDestroy(() => {
      routeSubscription.unsubscribe();
      this.request?.unsubscribe();
    });
  }
  load(): void {
    this.request?.unsubscribe();
    this.data.set(null);
    this.error.set('');
    this.loading.set(false);
    if (!this.session.current()?.staff) return;
    if (!guid.test(this.id)) {
      this.error.set('The engagement link is invalid.');
      return;
    }
    this.loading.set(true);
    const generation = this.session.invalidation();
    this.request = this.http
      .get<unknown>('/api/ui/engagements/' + this.id)
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (generation !== this.session.invalidation()) return;
          try {
            this.data.set(decodeEngagement(value));
          } catch {
            this.error.set('The server returned an unsupported response.');
          }
          this.loading.set(false);
        },
        error: (failure) => {
          this.loading.set(false);
          this.error.set('Check your access or retry shortly.');
          if (failure.status === 401) this.session.clear();
        },
      });
  }
}
