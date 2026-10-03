import { Component, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Subscription, timeout } from 'rxjs';
import { PortfolioNavigation } from '../portfolio/portfolio-contracts';
import { SessionService } from '../../core/session';
interface Engagement {
  id: string;
  serviceRoute: string;
  status: string;
  periodStart: string;
  periodEnd: string;
  professionalWorkBlocked: boolean;
}
interface Contact {
  id: string;
  name: string;
  email: string;
  role: string;
  primary: boolean;
}
interface Client {
  id: string;
  name: string;
  status: string;
  engagements: Engagement[];
  contacts: Contact[];
  canManageContacts: boolean;
  safetyGeneration: string;
  canCreateEngagement: boolean;
}
const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export function decodeClient(value: unknown): Client {
  if (!value || typeof value !== 'object') throw new Error('Invalid client');
  const v = value as Record<string, unknown>;
  if (
    typeof v['id'] !== 'string' ||
    !guid.test(v['id']) ||
    typeof v['name'] !== 'string' ||
    typeof v['status'] !== 'string' ||
    !Array.isArray(v['engagements']) ||
    v['engagements'].length > 100
  )
    throw new Error('Invalid client');
  for (const e of v['engagements']) {
    if (
      !e ||
      typeof e.id !== 'string' ||
      !guid.test(e.id) ||
      !['serviceRoute', 'status', 'periodStart', 'periodEnd'].every(
        (k) => typeof e[k] === 'string',
      ) ||
      typeof e.professionalWorkBlocked !== 'boolean'
    )
      throw new Error('Invalid engagement');
  }
  if (
    !Array.isArray(v['contacts']) ||
    v['contacts'].length > 100 ||
    typeof v['canCreateEngagement'] !== 'boolean' ||
    typeof v['canManageContacts'] !== 'boolean' ||
    typeof v['safetyGeneration'] !== 'string' ||
    !/^[0-9]{1,19}$/.test(v['safetyGeneration'])
  )
    throw new Error('Invalid contacts');
  for (const c of v['contacts'])
    if (
      !c ||
      typeof c.id !== 'string' ||
      !guid.test(c.id) ||
      !['name', 'email', 'role'].every((k) => typeof c[k] === 'string') ||
      typeof c.primary !== 'boolean'
    )
      throw new Error('Invalid contact');
  return v as unknown as Client;
}
@Component({
  selector: 'audit-client',
  imports: [
    FormsModule,
    MatFormFieldModule,
    MatInputModule,
    RouterLink,
    MatButtonModule,
    MatProgressBarModule,
  ],
  template: `
    <a routerLink="/app" [queryParams]="portfolioNavigation.params()">Portfolio</a>
    <h1>Client profile</h1>
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" aria-label="Loading client" />
    }
    @if (error()) {
      <section role="alert">
        <h2>Client unavailable</h2>
        <p>{{ error() }}</p>
        <button matButton (click)="load()">Retry</button>
      </section>
    }
    @if (data(); as client) {
      <h2>{{ client.name }}</h2>
      <p>{{ client.status }}</p>
      <p>
        Client ID: <code>{{ client.id }}</code>
      </p>
      <a [routerLink]="['/app/clients', client.id, 'assessment']">Acceptance checklist</a>
      <h2>Client contacts</h2>
      @for (contact of client.contacts; track contact.id) {
        <article>
          <h3>{{ contact.name }}</h3>
          <p>{{ contact.email }} · {{ contact.role }}{{ contact.primary ? ' · Primary' : '' }}</p>
        </article>
      } @empty {
        <p>No contacts recorded.</p>
      }
      @if (client.canManageContacts) {
        <form (ngSubmit)="saveContact()">
          <h3>Add contact</h3>
          <mat-form-field
            ><mat-label>Full name</mat-label
            ><input
              matInput
              name="name"
              [(ngModel)]="contactName"
              required
              maxlength="200"
              [disabled]="saving()"
          /></mat-form-field>
          <mat-form-field
            ><mat-label>Email</mat-label
            ><input
              matInput
              name="email"
              [(ngModel)]="contactEmail"
              type="email"
              required
              maxlength="254"
              [disabled]="saving()"
          /></mat-form-field>
          <mat-form-field
            ><mat-label>Role or title</mat-label
            ><input
              matInput
              name="role"
              [(ngModel)]="contactRole"
              required
              maxlength="200"
              [disabled]="saving()"
          /></mat-form-field>
          <label
            ><input
              type="checkbox"
              name="primary"
              [(ngModel)]="contactPrimary"
              [disabled]="saving()"
            />
            Primary contact</label
          >
          <button matButton="filled" type="submit" [disabled]="saving() || uncertain()">
            Save contact
          </button>
          <p role="status">{{ commandStatus() }}</p>
          @if (uncertain()) {
            <p>Refresh and review current contacts before submitting another request.</p>
          }
        </form>
      }
      @if (client.canCreateEngagement) {
        <section>
          <h2>Create engagement</h2>
          <p>
            A new engagement starts blocked until Partner activation against current unconditional
            acceptance.
          </p>
          <form (ngSubmit)="createEngagement()">
            <mat-form-field
              ><mat-label>Service route</mat-label
              ><input
                matInput
                name="serviceRoute"
                [(ngModel)]="serviceRoute"
                maxlength="50"
                required
                [disabled]="saving()"
            /></mat-form-field>
            <mat-form-field
              ><mat-label>Service profile</mat-label
              ><input
                matInput
                name="serviceProfile"
                [(ngModel)]="serviceProfile"
                maxlength="100"
                required
                [disabled]="saving()"
            /></mat-form-field>
            <label
              >Period start
              <input
                type="date"
                name="start"
                [(ngModel)]="periodStart"
                required
                [disabled]="saving()"
            /></label>
            <label
              >Period end
              <input type="date" name="end" [(ngModel)]="periodEnd" required [disabled]="saving()"
            /></label>
            <button matButton="filled" type="submit" [disabled]="saving() || uncertain()">
              Create engagement
            </button>
          </form>
          <p role="status">{{ commandStatus() }}</p>
        </section>
      }
      <h2>Authorized engagements</h2>
      @if (!client.engagements.length) {
        <p>No engagements available in your current scope.</p>
      }
      @for (engagement of client.engagements; track engagement.id) {
        <article>
          <h3>{{ engagement.serviceRoute }}</h3>
          <p>{{ engagement.periodStart }} to {{ engagement.periodEnd }}</p>
          <p>
            {{ engagement.status }} ·
            {{
              engagement.professionalWorkBlocked
                ? 'Professional work blocked'
                : 'Professional work clear'
            }}
          </p>
          <a [routerLink]="['/app/engagements', engagement.id]">Open engagement</a>
        </article>
      }
    }
  `,
})
export class ClientDetail {
  readonly portfolioNavigation = inject(PortfolioNavigation);
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  private readonly session = inject(SessionService);
  private request?: Subscription;
  private id = '';
  serviceRoute = '';
  serviceProfile = '';
  periodStart = '';
  periodEnd = '';
  contactName = '';
  contactEmail = '';
  contactRole = '';
  contactPrimary = false;
  readonly saving = signal(false);
  readonly uncertain = signal(false);
  readonly commandStatus = signal('');
  readonly data = signal<Client | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  constructor() {
    const routeSubscription = this.route.paramMap.subscribe((p) => {
      this.resetContact();
      this.id = p.get('id') ?? '';
      this.load();
    });
    effect(() => {
      this.session.invalidation();
      const staff = this.session.current()?.staff;
      untracked(() => {
        this.request?.unsubscribe();
        this.data.set(null);
        this.resetContact();
        if (staff) this.load();
      });
    });
    inject(DestroyRef).onDestroy(() => {
      routeSubscription.unsubscribe();
      this.request?.unsubscribe();
    });
  }
  private resetContact(): void {
    this.serviceRoute = '';
    this.serviceProfile = '';
    this.periodStart = '';
    this.periodEnd = '';
    this.contactName = '';
    this.contactEmail = '';
    this.contactRole = '';
    this.contactPrimary = false;
    this.commandStatus.set('');
    this.uncertain.set(false);
  }
  createEngagement(): void {
    if (!this.data()?.canCreateEngagement || this.saving() || this.uncertain()) return;
    this.saving.set(true);
    this.commandStatus.set('Creating blocked engagement…');
    const id = this.id;
    const generation = this.session.invalidation();
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
          this.saving.set(false);
          if (id !== this.id || generation !== this.session.invalidation()) return;
          this.resetContact();
          this.commandStatus.set('Engagement created. Partner activation remains required.');
          this.load();
        },
        error: (failure) => {
          this.saving.set(false);
          if (id !== this.id || generation !== this.session.invalidation()) return;
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
  saveContact(): void {
    const client = this.data();
    if (!client?.canManageContacts || this.saving() || this.uncertain()) return;
    this.saving.set(true);
    this.commandStatus.set('Saving contact…');
    const generation = this.session.invalidation();
    const id = this.id;
    this.http
      .post('/api/ui/clients/' + id + '/contacts', {
        name: this.contactName,
        email: this.contactEmail,
        role: this.contactRole,
        primary: this.contactPrimary,
        safetyGeneration: client.safetyGeneration,
      })
      .pipe(timeout(15000))
      .subscribe({
        next: () => {
          this.saving.set(false);
          if (generation !== this.session.invalidation() || id !== this.id) return;
          this.resetContact();
          this.commandStatus.set('Contact recorded.');
          this.load();
        },
        error: (failure) => {
          this.saving.set(false);
          if (generation !== this.session.invalidation() || id !== this.id) return;
          if (failure.status >= 400 && failure.status < 500)
            this.commandStatus.set(
              'Contact was not saved. Check the fields, current revision and access.',
            );
          else {
            this.uncertain.set(true);
            this.commandStatus.set(
              'The outcome could not be confirmed. Do not resubmit until you review current contacts.',
            );
          }
          if (failure.status === 401) this.session.clear();
        },
      });
  }
  load(): void {
    this.request?.unsubscribe();
    this.data.set(null);
    this.error.set('');
    this.loading.set(false);
    if (!this.session.current()?.staff) return;
    if (!guid.test(this.id)) {
      this.error.set('The client link is invalid.');
      return;
    }
    this.loading.set(true);
    const generation = this.session.invalidation();
    this.request = this.http
      .get<unknown>('/api/ui/clients/' + this.id)
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (generation !== this.session.invalidation()) return;
          try {
            this.data.set(decodeClient(value));
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
