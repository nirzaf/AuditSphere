import { DOCUMENT } from '@angular/common';
import {
  afterNextRender,
  Component,
  DestroyRef,
  effect,
  inject,
  Injector,
  signal,
  untracked,
} from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import { Drafts } from '../../core/drafts';
interface Lead {
  id: string;
  name: string;
  source: string;
  primaryContactName: string | null;
  primaryContactEmail: string | null;
  status: string;
  createdAt: string;
  /** LEAD_INGESTION (stage 1) until a proposal exists; PROPOSAL_OR_LATER once one does. */
  lifecycleStage?: string | null;
  ownerUserId?: string | null;
  ownerName?: string | null;
  /** UNASSIGNED, ACTIVE, DISABLED or MISSING; a recorded owner is never replaced. */
  ownerState?: string | null;
}
/** Complete counts over every lead matching the same search and filters, not only this page. */
interface LeadSummary {
  matching: number;
  awaitingQualification: number;
  qualified: number;
}
interface LeadPage {
  items: Lead[];
  total: number;
  page: number;
  pageSize: number;
  summary?: LeadSummary | null;
}
export const LEAD_STATUSES = ['NEW', 'QUALIFIED', 'UNQUALIFIED', 'LOST'] as const;
const OWNER_STATES = ['UNASSIGNED', 'ACTIVE', 'DISABLED', 'MISSING'];
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
interface LeadDraft {
  name: string;
  source: string;
  contactName: string;
  contactEmail: string;
  requestId: string;
  submissionPending: boolean;
}
export function decodeLeads(value: unknown): LeadPage {
  if (!value || typeof value !== 'object') throw new Error('Invalid leads');
  const v = value as Record<string, unknown>;
  if (
    !Array.isArray(v['items']) ||
    !['total', 'page', 'pageSize'].every((k) => Number.isSafeInteger(v[k]) && Number(v[k]) >= 0) ||
    Number(v['page']) > 10000 ||
    Number(v['pageSize']) < 1 ||
    Number(v['pageSize']) > 100 ||
    v['items'].length > Number(v['pageSize'])
  )
    throw new Error('Invalid pagination');
  for (const l of v['items'])
    if (
      !l ||
      typeof l.id !== 'string' ||
      !UUID.test(l.id) ||
      !['name', 'source', 'status', 'createdAt'].every((k) => typeof l[k] === 'string') ||
      !['primaryContactName', 'primaryContactEmail'].every(
        (k) => l[k] === null || typeof l[k] === 'string',
      ) ||
      !(l.ownerUserId == null || (typeof l.ownerUserId === 'string' && UUID.test(l.ownerUserId))) ||
      !(l.ownerName == null || typeof l.ownerName === 'string') ||
      !(l.ownerState == null || OWNER_STATES.includes(l.ownerState))
    )
      throw new Error('Invalid lead');
  const s = v['summary'];
  if (s != null) {
    if (typeof s !== 'object') throw new Error('Invalid lead summary');
    const counts = s as Record<string, unknown>;
    if (
      !['matching', 'awaitingQualification', 'qualified'].every(
        (k) => Number.isSafeInteger(counts[k]) && Number(counts[k]) >= 0,
      ) ||
      counts['matching'] !== v['total'] ||
      Number(counts['awaitingQualification']) + Number(counts['qualified']) > Number(counts['matching'])
    )
      throw new Error('Invalid lead summary');
  }
  return v as unknown as LeadPage;
}
/** Lead channels from the owner-approved set (STE 4.1.1). The server stores the same canonical values. */
export const LEAD_CHANNELS = ['Phone', 'WhatsApp', 'Email', 'Web Form', 'Referral', 'In-person'] as const;

/** True only for a channel in the approved set. Matching is exact: the server normalises case, the browser sends a chosen option. */
export function isLeadChannel(value: string): boolean {
  return (LEAD_CHANNELS as readonly string[]).includes(value);
}

@Component({
  selector: 'audit-leads',
  imports: [
    RouterLink,
    FormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressBarModule,
  ],
  host: {
    '(document:pointerdown)': 'recordFocusInteraction()',
    '(document:keydown)': 'recordFocusInteraction()',
  },
  styles: `
    .lead-create {
      margin: 0.5rem 0 1rem;
      border: 1px solid var(--as-control-border);
      border-radius: var(--as-radius-md);
      padding: 0.5rem 1rem;
    }
    .lead-create > summary {
      cursor: pointer;
      font-weight: 600;
    }
    .lead-summary-scope {
      color: var(--as-text-muted);
    }
  `,
  template: `
    <h1 id="leads-page-heading" tabindex="-1">Practice leads</h1>
    <a routerLink="/app/practice/commercial-settings">Commercial settings</a>
    <p>Commercial qualification remains separate from client acceptance and credit decisions.</p>
    <form class="lead-filters" (ngSubmit)="load()" aria-label="Lead search and filters">
      <mat-form-field appearance="outline" subscriptSizing="dynamic"
        ><mat-label>Search lead name</mat-label
        ><input matInput name="search" [(ngModel)]="search" maxlength="100" /></mat-form-field
      ><mat-form-field appearance="outline" subscriptSizing="dynamic"
        ><mat-label>Status</mat-label
        ><select
          matNativeControl
          name="statusFilter"
          aria-label="Filter by lead status"
          [(ngModel)]="statusFilter"
          (ngModelChange)="load()"
        >
          <option value="">All statuses</option>
          @for (status of statuses; track status) {
            <option [value]="status">{{ statusLabel(status) }}</option>
          }
        </select></mat-form-field
      ><mat-form-field appearance="outline" subscriptSizing="dynamic"
        ><mat-label>Channel</mat-label
        ><select
          matNativeControl
          name="sourceFilter"
          aria-label="Filter by lead channel"
          [(ngModel)]="sourceFilter"
          (ngModelChange)="load()"
        >
          <option value="">All channels</option>
          @for (channel of channels; track channel) {
            <option [value]="channel">{{ channel }}</option>
          }
        </select></mat-form-field
      ><mat-form-field appearance="outline" subscriptSizing="dynamic"
        ><mat-label>Owner</mat-label
        ><select
          matNativeControl
          name="ownerFilter"
          aria-label="Filter by lead owner"
          [(ngModel)]="ownerFilter"
          (ngModelChange)="load()"
        >
          <option value="">Any owner</option>
          <option value="me">My leads</option>
          <option value="unassigned">Unassigned</option>
        </select></mat-form-field
      ><button matButton type="submit">Search</button
      ><button matButton type="button" [disabled]="!hasFilters()" (click)="clearFilters()">
        Clear filters
      </button>
    </form>
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" aria-label="Loading leads" />
    }
    @if (error()) {
      <p role="alert">{{ error() }}</p>
      <button matButton (click)="load()">Retry</button>
    }
    @if (data(); as leads) {
      <details class="lead-create" [open]="createOpen() || createPending()" (toggle)="onCreateToggle($event)">
        <summary>Record a new lead</summary>
        <form (ngSubmit)="create()">
          <mat-form-field appearance="outline" subscriptSizing="dynamic"
            ><mat-label>Lead name</mat-label
            ><input
              matInput
              name="name"
              [(ngModel)]="name"
              (ngModelChange)="touchDraft()"
              required
              maxlength="200"
              [disabled]="busy() || createPending()"
          /></mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic"
            ><mat-label>Channel</mat-label
            ><select
              matNativeControl
              name="source"
              aria-label="Lead channel"
              [(ngModel)]="source"
              (ngModelChange)="touchDraft()"
              required
              [disabled]="busy() || createPending()"
            >
              <option value="" disabled>Choose a channel</option>
              @for (channel of channels; track channel) {
                <option [value]="channel">{{ channel }}</option>
              }
            </select></mat-form-field
          >
          <mat-form-field appearance="outline" subscriptSizing="dynamic"
            ><mat-label>Contact name</mat-label
            ><input
              matInput
              name="contactName"
              [(ngModel)]="contactName"
              (ngModelChange)="touchDraft()"
              maxlength="200"
              [disabled]="busy() || createPending()"
          /></mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic"
            ><mat-label>Contact email</mat-label
            ><input
              matInput
              type="email"
              name="contactEmail"
              [(ngModel)]="contactEmail"
              (ngModelChange)="touchDraft()"
              maxlength="254"
              [disabled]="busy() || createPending()"
          /></mat-form-field>
          <button
            matButton="filled"
            type="submit"
            [disabled]="busy() || uncertain() || createPending()"
          >
            Record lead
          </button>
        </form>
        @if (createPending() && uncertain() && recoverableLeadCreate()) {
          <p role="alert">
            This saved lead request may have completed. Resolve it with the same request before
            starting another lead.
          </p>
          <button matButton (click)="resolveSavedLeadRequest()" [disabled]="busy()">
            Resolve saved lead request
          </button>
        }
      </details>
      <section class="portfolio-metrics" aria-label="Lead summary">
        @if (leads.summary; as summary) {
          <article>
            <h2>Matching leads</h2>
            <strong>{{ summary.matching }}</strong>
          </article>
          <article>
            <h2>New · awaiting qualification</h2>
            <strong>{{ summary.awaitingQualification }}</strong>
          </article>
          <article>
            <h2>Qualified</h2>
            <strong>{{ summary.qualified }}</strong>
          </article>
        } @else {
          <article>
            <h2>Leads in this search</h2>
            <strong>{{ leads.total }}</strong>
          </article>
          <article>
            <h2>New on this page</h2>
            <strong>{{ newOnPage(leads) }}</strong>
          </article>
        }
      </section>
      <p class="lead-summary-scope">
        {{
          leads.summary
            ? 'Counts cover every lead matching the current search and filters, not only this page.'
            : 'The lead total covers the whole search; "New on this page" counts only the loaded page.'
        }}
      </p>
      <p role="status">
        {{ leads.total }} leads in this search
        <button matButton (click)="load()" [disabled]="loading() || busy()">Refresh leads</button>
      </p>
      <div
        class="table-scroll"
        role="region"
        aria-label="Commercial leads; scroll horizontally to see all columns"
        tabindex="0"
      >
        <table>
          <caption>
            Commercial leads
          </caption>
          <thead>
            <tr>
              <th>Name</th>
              <th>Source</th>
              <th>Contact</th>
              <th>Owner</th>
              <th>Recorded</th>
              <th>Status</th>
              <th>Lifecycle</th>
              <th>Action</th>
            </tr>
          </thead>
          <tbody>
            @for (lead of leads.items; track lead.id) {
              <tr>
                <td>
                  <a [routerLink]="['/app/practice/leads', lead.id]">{{ lead.name }}</a>
                </td>
                <td>{{ lead.source }}</td>
                <td>{{ lead.primaryContactName }} · {{ lead.primaryContactEmail }}</td>
                <td>{{ ownerLabel(lead) }}</td>
                <td>{{ lead.createdAt.slice(0, 10) }}</td>
                <td>{{ statusLabel(lead.status) }}</td>
                <td>{{ lead.lifecycleStage === 'LEAD_INGESTION' ? 'Stage 1 · Lead ingestion' : lead.lifecycleStage === 'PROPOSAL_OR_LATER' ? 'Proposal and later stages' : '—' }}</td>
                <td>
                  @if (lead.status === 'NEW') {
                    <button
                      matButton
                      [disabled]="busy() || uncertain()"
                      (click)="startQualification(lead, $event.currentTarget)"
                    >
                      Qualify {{ lead.name }}
                    </button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
      @if (!leads.items.length) {
        <p>{{ hasFilters() ? 'No leads match this search and these filters.' : 'No leads match this search.' }}</p>
      }
      <nav aria-label="Lead pages">
        <button matButton [disabled]="leads.page === 0" (click)="load(leads.page - 1)">
          Previous</button
        ><span>Page {{ leads.page + 1 }}</span
        ><button
          matButton
          [disabled]="(leads.page + 1) * leads.pageSize >= leads.total"
          (click)="load(leads.page + 1)"
        >
          Next
        </button>
      </nav>
      @if (qualification(); as lead) {
        <section role="alert">
          <h2>Qualify {{ lead.name }}?</h2>
          <p>Record ID: {{ lead.id }}</p>
          <p>This does not grant professional acceptance or client access.</p>
          <button matButton [disabled]="busy() || uncertain()" (click)="qualify(lead.id, $event.currentTarget)">
            Confirm qualification</button
          ><button matButton [disabled]="busy()" (click)="cancelQualification()">Cancel</button>
        </section>
      }
    }
    <p role="status">{{ commandStatus() }}</p>
  `,
})
export class Leads {
  private readonly document = inject(DOCUMENT);
  private readonly renderInjector = inject(Injector);
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private request?: Subscription;
  private qualificationRequest?: Subscription;
  private destroyed = false;
  search = '';
  statusFilter = '';
  sourceFilter = '';
  ownerFilter = '';
  name = '';
  source = '';
  readonly channels = LEAD_CHANNELS;
  readonly statuses = LEAD_STATUSES;
  contactName = '';
  contactEmail = '';
  private readonly drafts = inject(Drafts);
  private draftTimer?: ReturnType<typeof setTimeout>;
  private static validDraft(value: unknown): LeadDraft | null {
    if (!value || typeof value !== 'object') return null;
    const v = value as Record<string, unknown>;
    const str = (key: string, max: number) =>
      typeof v[key] === 'string' && (v[key] as string).length <= max ? (v[key] as string) : null;
    const name = str('name', 200),
      source = str('source', 200);
    const contactName = str('contactName', 200),
      contactEmail = str('contactEmail', 254);
    // A draft saved before channels were enforced carries free text; it is discarded rather than shown with no option.
    if (
      name === null ||
      source === null ||
      !isLeadChannel(source) ||
      contactName === null ||
      contactEmail === null
    )
      return null;
    const hasRequestId = Object.hasOwn(v, 'requestId');
    const hasPending = Object.hasOwn(v, 'submissionPending');
    if (hasRequestId !== hasPending) return null;
    // Drafts written before request identities existed remain recoverable as edits.
    if (!hasRequestId)
      return {
        name,
        source,
        contactName,
        contactEmail,
        requestId: crypto.randomUUID(),
        submissionPending: false,
      };
    if (
      typeof v['requestId'] !== 'string' ||
      !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(
        v['requestId'],
      ) ||
      typeof v['submissionPending'] !== 'boolean'
    )
      return null;
    return {
      name,
      source,
      contactName,
      contactEmail,
      requestId: v['requestId'],
      submissionPending: v['submissionPending'],
    };
  }
  touchDraft(): void {
    if (this.createPending()) return;
    clearTimeout(this.draftTimer);
    this.draftTimer = setTimeout(() => {
      if (!this.persistDraft(this.currentDraft(false)))
        this.commandStatus.set(
          'Draft could not be saved in this browser. Keep this page open until the request is submitted.',
        );
    }, 800);
  }
  readonly data = signal<LeadPage | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly createPending = signal(false);
  readonly recoverableLeadCreate = signal(false);
  readonly commandStatus = signal('');
  readonly qualification = signal<Lead | null>(null);
  /** The creation panel opens for an empty lead list or a saved draft; the user may open or close it at any time. */
  readonly createOpen = signal(false);
  private qualificationTrigger: HTMLButtonElement | null = null;
  private focusInteraction = 0;
  private createRequestId: string = crypto.randomUUID();
  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.draftTimer));
    effect(() => {
      this.session.invalidation();
      const staff = this.session.current()?.staff;
      untracked(() => {
        this.request?.unsubscribe();
        this.qualificationRequest?.unsubscribe();
        this.busy.set(false);
        this.data.set(null);
        this.search = this.name = this.source = this.contactName = this.contactEmail = '';
        this.statusFilter = this.sourceFilter = this.ownerFilter = '';
        this.createOpen.set(false);
        this.qualificationTrigger = null;
        this.qualification.set(null);
        this.commandStatus.set('');
        this.uncertain.set(false);
        this.createPending.set(false);
        this.recoverableLeadCreate.set(false);
        this.createRequestId = crypto.randomUUID();
        if (staff) {
          const saved = this.drafts.load('commercial-lead-draft', Leads.validDraft);
          if (saved) {
            this.name = saved.name;
            this.source = saved.source;
            this.contactName = saved.contactName;
            this.contactEmail = saved.contactEmail;
            this.createRequestId = saved.requestId;
            this.createPending.set(saved.submissionPending);
            this.uncertain.set(saved.submissionPending);
            this.createOpen.set(
              saved.submissionPending || !!(saved.name || saved.contactName || saved.contactEmail),
            );
            this.recoverableLeadCreate.set(saved.submissionPending);
            if (saved.submissionPending)
              this.commandStatus.set(
                'A saved lead request needs resolution. The original request identity is ready to retry safely.',
              );
          }
          this.load();
        }
      });
    });
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; this.request?.unsubscribe(); this.qualificationRequest?.unsubscribe(); });
  }
  create(): void {
    if (!this.data() || this.busy() || this.uncertain() || this.createPending()) return;
    clearTimeout(this.draftTimer);
    const draft = this.currentDraft(true);
    if (!this.persistDraft(draft)) {
      this.commandStatus.set(
        'The lead request was not sent because this browser could not save its recovery identity.',
      );
      return;
    }
    this.createPending.set(true);
    this.recoverableLeadCreate.set(false);
    this.sendLeadCreate(draft);
  }
  resolveSavedLeadRequest(): void {
    if (!this.data() || this.busy() || !this.createPending() || !this.recoverableLeadCreate())
      return;
    const saved = this.drafts.load('commercial-lead-draft', Leads.validDraft);
    if (
      !saved?.submissionPending ||
      saved.requestId !== this.createRequestId ||
      saved.name !== this.name ||
      saved.source !== this.source ||
      saved.contactName !== this.contactName ||
      saved.contactEmail !== this.contactEmail
    ) {
      this.commandStatus.set(
        'The saved recovery details could not be verified. No retry was sent; refresh the page or contact an administrator.',
      );
      return;
    }
    this.sendLeadCreate(saved);
  }
  private currentDraft(submissionPending: boolean): LeadDraft {
    return {
      name: this.name,
      source: this.source,
      contactName: this.contactName,
      contactEmail: this.contactEmail,
      requestId: this.createRequestId,
      submissionPending,
    };
  }
  private persistDraft(draft: LeadDraft): boolean {
    this.drafts.save('commercial-lead-draft', draft);
    const saved = this.drafts.load('commercial-lead-draft', Leads.validDraft);
    return (
      !!saved &&
      saved.name === draft.name &&
      saved.source === draft.source &&
      saved.contactName === draft.contactName &&
      saved.contactEmail === draft.contactEmail &&
      saved.requestId === draft.requestId &&
      saved.submissionPending === draft.submissionPending
    );
  }
  private sendLeadCreate(draft: LeadDraft): void {
    if (this.busy()) return;
    const generation = this.session.invalidation();
    this.busy.set(true);
    this.uncertain.set(false);
    this.commandStatus.set('Saving lead…');
    this.http
      .post<unknown>('/api/ui/leads', {
        requestId: draft.requestId,
        name: draft.name,
        source: draft.source,
        contactName: draft.contactName || null,
        contactEmail: draft.contactEmail || null,
      })
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          this.busy.set(false);
          if (generation !== this.session.invalidation()) return;
          const id =
            value && typeof value === 'object' ? (value as Record<string, unknown>)['id'] : null;
          if (typeof id !== 'string' || id.toLowerCase() !== draft.requestId.toLowerCase()) {
            this.uncertain.set(true);
            this.recoverableLeadCreate.set(true);
            this.commandStatus.set(
              'The response did not confirm the saved request identity. Resolve the saved request before continuing.',
            );
            return;
          }
          this.drafts.clear('commercial-lead-draft');
          this.name = this.source = this.contactName = this.contactEmail = '';
          this.createRequestId = crypto.randomUUID();
          this.createPending.set(false);
          this.recoverableLeadCreate.set(false);
          this.uncertain.set(false);
          this.commandStatus.set('Lead recorded.');
          this.load();
        },
        error: (failure) => {
          this.busy.set(false);
          if (generation !== this.session.invalidation()) return;
          if (failure.status >= 400 && failure.status < 500) {
            this.drafts.clear('commercial-lead-draft');
            this.createPending.set(false);
            this.recoverableLeadCreate.set(false);
            this.uncertain.set(false);
            this.createRequestId = crypto.randomUUID();
            this.commandStatus.set(
              failure.status === 409
                ? 'The request identity conflicts with another saved lead request. Review current leads before trying again.'
                : 'Lead request refused. Check current access, required fields and existing leads.',
            );
            this.touchDraft();
          } else {
            this.uncertain.set(true);
            this.recoverableLeadCreate.set(true);
            this.commandStatus.set(
              'Lead outcome unconfirmed. Resolve the saved request to safely confirm or recover it.',
            );
          }
          if (failure.status === 401) this.session.clear();
        },
      });
  }
  statusLabel(status: string): string {
    return (
      { NEW: 'New', QUALIFIED: 'Qualified', UNQUALIFIED: 'Unqualified', LOST: 'Lost' }[status] ?? status
    );
  }
  ownerLabel(lead: Lead): string {
    switch (lead.ownerState) {
      case 'UNASSIGNED':
        return 'Unassigned';
      case 'ACTIVE':
        return lead.ownerName ?? 'Unknown owner';
      case 'DISABLED':
        return `${lead.ownerName ?? 'Unknown owner'} (disabled)`;
      case 'MISSING':
        return 'Unknown owner';
      default:
        return '—';
    }
  }
  newOnPage(leads: LeadPage): number {
    return leads.items.filter((l) => l.status === 'NEW').length;
  }
  hasFilters(): boolean {
    return !!(this.statusFilter || this.sourceFilter || this.ownerFilter);
  }
  clearFilters(): void {
    this.statusFilter = this.sourceFilter = this.ownerFilter = '';
    this.load();
  }
  onCreateToggle(event: Event): void {
    if (event.target instanceof HTMLDetailsElement) this.createOpen.set(event.target.open);
  }
  startQualification(lead: Lead, trigger: EventTarget | null): void {
    this.qualificationTrigger = trigger instanceof HTMLButtonElement ? trigger : null;
    this.qualification.set(lead);
  }
  cancelQualification(): void {
    const trigger = this.qualificationTrigger;
    this.qualificationTrigger = null;
    this.qualification.set(null);
    if (trigger?.isConnected) trigger.focus();
  }
  recordFocusInteraction(): void {
    this.focusInteraction++;
  }
  qualify(id: string, trigger: EventTarget | null = null): void {
    if (this.qualification()?.id !== id || !this.data()?.items.some(lead => lead.id === id && lead.status === 'NEW')) return;
    this.command('/' + id + '/qualify', {}, trigger instanceof HTMLButtonElement ? trigger : null);
  }
  private command(path: string, body: object, qualificationTrigger: HTMLButtonElement | null = null): void {
    if (this.destroyed || !this.session.current()?.staff || !this.data() || this.busy() || this.uncertain()) return;
    const generation = this.session.invalidation();
    const focusOrigin = qualificationTrigger
      ? { element: qualificationTrigger, interaction: this.focusInteraction }
      : null;
    this.busy.set(true);
    this.commandStatus.set('Saving commercial change…');
    this.qualificationRequest = this.http
      .post('/api/ui/leads' + path, body)
      .pipe(timeout(15000))
      .subscribe({
        next: () => {
          if (this.destroyed || generation !== this.session.invalidation()) return;
          this.busy.set(false);
          this.name = this.source = this.contactName = this.contactEmail = '';
          this.drafts.clear('commercial-lead-draft');
          this.qualificationTrigger = null;
          this.qualification.set(null);
          this.commandStatus.set('Commercial change recorded.');
          this.load();
          if (focusOrigin) {
            afterNextRender(() => {
              const active = this.document.activeElement;
              if (this.destroyed || generation !== this.session.invalidation() ||
                  focusOrigin.interaction !== this.focusInteraction ||
                  (active !== focusOrigin.element && active !== this.document.body)) return;
              this.document.getElementById('leads-page-heading')?.focus();
            }, { injector: this.renderInjector });
          }
        },
        error: (failure) => {
          if (this.destroyed || generation !== this.session.invalidation()) return;
          this.busy.set(false);
          if (failure.status >= 400 && failure.status < 500)
            this.commandStatus.set(
              'Change refused. Check current access, required fields and existing leads.',
            );
          else {
            this.uncertain.set(true);
            this.commandStatus.set(
              'Outcome unconfirmed. Review current leads before resubmitting.',
            );
          }
          if ([401, 403, 404].includes(failure.status)) {
            this.data.set(null); this.qualificationTrigger = null; this.qualification.set(null);
            this.error.set('Lead information is unavailable in your current scope.');
          }
          if (failure.status === 401) this.session.clear();
        },
      });
  }
  load(page = 0): void {
    this.qualificationTrigger = null;
    this.qualification.set(null);
    this.request?.unsubscribe();
    this.data.set(null);
    this.error.set('');
    if (!this.session.current()?.staff) return;
    const generation = this.session.invalidation();
    this.loading.set(true);
    const params: Record<string, string | number> = { search: this.search, page };
    if (this.statusFilter) params['status'] = this.statusFilter;
    if (this.sourceFilter) params['source'] = this.sourceFilter;
    if (this.ownerFilter) params['owner'] = this.ownerFilter;
    const unfiltered = !this.search.trim() && !this.hasFilters();
    this.request = this.http
      .get<unknown>('/api/ui/leads', { params })
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (generation !== this.session.invalidation()) return;
          try {
            const leads = decodeLeads(value);
            this.data.set(leads);
            if (unfiltered && leads.total === 0) this.createOpen.set(true);
          } catch {
            this.error.set('Leads returned an unsupported response.');
          }
          this.loading.set(false);
        },
        error: (failure) => {
          this.loading.set(false);
          this.error.set(
            'Leads unavailable. A current firm-wide commercial assignment is required.',
          );
          if (failure.status === 401) this.session.clear();
        },
      });
  }
}
