import {
  afterNextRender,
  Component,
  DestroyRef,
  ElementRef,
  effect,
  inject,
  Injector,
  signal,
  untracked,
  viewChild,
  viewChildren,
} from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import { Drafts } from '../../core/drafts';
import { exactDecimal, guidPattern } from '../../core/contracts';
interface Opportunity {
  id: string;
  serviceRoute: string;
  entityScope: string;
  stage: string;
  expectedFee: string | null;
  currency: string | null;
  periodStart: string;
  periodEnd: string;
  createdAt: string;
  proposalId: string | null;
  revision: string;
  acceptanceClientId: string | null;
  acceptanceProcess: 'CLIENT_ACCEPTANCE' | 'CONTINUANCE';
  acceptanceStatus: string;
  canPrepareCommercial: boolean;
}
type LeadActivityAction =
  | 'LEAD_CREATED'
  | 'LEAD_QUALIFIED'
  | 'OPPORTUNITY_CREATED'
  | 'PROPOSAL_REVISION_CREATED'
  | 'PROPOSAL_REVIEWED'
  | 'PROPOSAL_MARKED_SENT'
  | 'PROPOSAL_ACCEPTED'
  | 'PROPOSAL_DECLINED'
  | 'CLIENT_CONVERSION_LINKED';
interface LeadActivity {
  id: string;
  action: LeadActivityAction;
  occurredAt: string;
  actorName: string;
  actorUserId: string | null;
  isBackfill: boolean;
  fromState: string | null;
  toState: string | null;
  sourceChannel: string | null;
  ownerName: string | null;
  ownerUserId: string | null;
  serviceRoute: string | null;
  opportunityId: string | null;
  proposalId: string | null;
  proposalRevision: string | null;
  clientId: string | null;
}
interface LeadWorkspace {
  id: string;
  name: string;
  status: string;
  source: string;
  ownerUserId: string | null;
  ownerName: string;
  createdAt: string;
  opportunities: Opportunity[];
  opportunitiesHasMore: boolean;
  history: LeadActivity[];
  historyHasMore: boolean;
  hasLegacyHistoryGap: boolean;
  serviceRoutes: { id: string; serviceRoute: string; heading: string; entityScopeTemplate: string }[];
}
type HistoryFocusOrigin = { element: Element; interaction: number };
type OpportunityFocusOrigin = { element: HTMLButtonElement; interaction: number; owner: string };
interface OpportunityCreateIntent {
  leadId: string;
  requestId: string;
  serviceRoute: string;
  prospectiveLegalName: string;
  prospectiveRegistrationNumber: string | null;
  prospectiveJurisdiction: string | null;
  entityScope: string;
  entityScopeConfirmed: boolean;
  entityScopeManuallyEdited: boolean;
  periodStart: string;
  periodEnd: string;
  submissionPending: true;
}
interface ProposalCreateIntent {
  leadId: string;
  opportunityId: string;
  requestId: string;
  expectedRevision: string;
  serviceProfile: string;
  scope: string;
  exclusions: string;
  deliverables: string;
  dependencies: string;
  timeline: string;
  fee: string;
  currency: string;
  periodStart: string;
  periodEnd: string;
  submissionPending: true;
}
export function decodeOpportunities(value: unknown): LeadWorkspace {
  if (!value || typeof value !== 'object') throw new Error('Unsupported lead');
  const v = value as Record<string, unknown>;
  if (
    typeof v['id'] !== 'string' ||
    !guidPattern.test(v['id']) ||
    typeof v['name'] !== 'string' ||
    typeof v['status'] !== 'string' ||
    typeof v['source'] !== 'string' ||
    (v['ownerUserId'] !== null && (typeof v['ownerUserId'] !== 'string' || !guidPattern.test(v['ownerUserId']))) ||
    typeof v['ownerName'] !== 'string' ||
    typeof v['createdAt'] !== 'string' ||
    !Number.isFinite(Date.parse(v['createdAt'])) ||
    !Array.isArray(v['opportunities']) ||
    v['opportunities'].length > 100 ||
    typeof v['opportunitiesHasMore'] !== 'boolean' ||
    (v['opportunitiesHasMore'] && v['opportunities'].length === 0) ||
    !Array.isArray(v['history']) ||
    v['history'].length > 200 ||
    typeof v['historyHasMore'] !== 'boolean' ||
    typeof v['hasLegacyHistoryGap'] !== 'boolean'
  )
    throw new Error('Unsupported lead');
  for (const o of v['opportunities']) {
    if (
      !o ||
      typeof o['id'] !== 'string' ||
      !guidPattern.test(o['id']) ||
      !['serviceRoute', 'entityScope', 'stage', 'periodStart', 'periodEnd', 'acceptanceProcess', 'acceptanceStatus'].every(
        (k) => typeof o[k] === 'string',
      ) ||
      typeof o['createdAt'] !== 'string' ||
      !Number.isFinite(Date.parse(o['createdAt'])) ||
      (o['expectedFee'] !== null && !exactDecimal(o['expectedFee'])) ||
      (o['currency'] !== null && (typeof o['currency'] !== 'string' || !/^[A-Z]{3}$/.test(o['currency']))) ||
      (o['expectedFee'] === null) !== (o['currency'] === null) ||
      !['CLIENT_ACCEPTANCE', 'CONTINUANCE'].includes(o['acceptanceProcess']) ||
      typeof o['canPrepareCommercial'] !== 'boolean' ||
      (o['acceptanceClientId'] !== null && (typeof o['acceptanceClientId'] !== 'string' || !guidPattern.test(o['acceptanceClientId']))) ||
      typeof o['revision'] !== 'string' ||
      !/^\d{1,19}$/.test(o['revision']) ||
      (o['proposalId'] !== null &&
        (typeof o['proposalId'] !== 'string' || !guidPattern.test(o['proposalId'])))
    )
      throw new Error('Unsupported opportunity');
  }
  if (!Array.isArray(v['serviceRoutes']) || v['serviceRoutes'].length > 20 ||
      (v['serviceRoutes'] as Record<string, unknown>[]).some((r) => !r ||
        typeof r['id'] !== 'string' || typeof r['serviceRoute'] !== 'string' || typeof r['heading'] !== 'string' ||
        typeof r['entityScopeTemplate'] !== 'string' || !r['entityScopeTemplate'].includes('{clientName}')))
    throw new Error('Unsupported service routes');
  const actions: readonly string[] = [
    'LEAD_CREATED',
    'LEAD_QUALIFIED',
    'OPPORTUNITY_CREATED',
    'PROPOSAL_REVISION_CREATED',
    'PROPOSAL_REVIEWED',
    'PROPOSAL_MARKED_SENT',
    'PROPOSAL_ACCEPTED',
    'PROPOSAL_DECLINED',
    'CLIENT_CONVERSION_LINKED',
  ];
  for (const event of v['history']) {
    if (
      !event ||
      typeof event.id !== 'string' ||
      !guidPattern.test(event.id) ||
      typeof event.action !== 'string' ||
      !actions.includes(event.action) ||
      typeof event.occurredAt !== 'string' ||
      !Number.isFinite(Date.parse(event.occurredAt)) ||
      typeof event.actorName !== 'string' ||
      (event.actorUserId !== null &&
        (typeof event.actorUserId !== 'string' || !guidPattern.test(event.actorUserId))) ||
      typeof event.isBackfill !== 'boolean' ||
      !['fromState', 'toState', 'sourceChannel', 'ownerName', 'serviceRoute'].every(
        (k) => event[k] === null || typeof event[k] === 'string',
      ) ||
      !['ownerUserId', 'opportunityId', 'proposalId', 'clientId'].every(
        (k) => event[k] === null || (typeof event[k] === 'string' && guidPattern.test(event[k])),
      ) ||
      (event.proposalRevision !== null &&
        (typeof event.proposalRevision !== 'string' || !/^\d{1,19}$/.test(event.proposalRevision)))
    )
      throw new Error('Unsupported lead activity');
  }
  return v as unknown as LeadWorkspace;
}
@Component({
  selector: 'audit-opportunities',
  imports: [RouterLink, FormsModule, MatButtonModule, MatProgressBarModule],
  host: {
    '(document:pointerdown)': 'recordFocusInteraction()',
    '(document:keydown)': 'recordFocusInteraction()',
  },
  template: `
    <a routerLink="/app/practice/leads">Leads</a> · <a routerLink="/app/practice/pipeline">Opportunity pipeline</a>
    <h1>Opportunities</h1>
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" aria-label="Loading opportunities" />
    }
    @if (error()) {
      <p role="alert">{{ error() }}</p>
    }
    <button matButton [disabled]="busy()" (click)="load()">Refresh persisted state</button>
    @if (data(); as lead) {
      <h2>{{ lead.name }}</h2>
      <p>{{ lead.status }}</p>
      <p>Source: {{ lead.source }} · Owner: {{ lead.ownerName }} · Created: {{ lead.createdAt }}</p>
      <section #historySection tabindex="-1" aria-labelledby="lead-history-heading">
        <h2 id="lead-history-heading">Lead history</h2>
        @if (lead.hasLegacyHistoryGap) {
          <p role="note">
            This lead predates retained activity history. Its original source is preserved; older actions and their
            actors could not be reconstructed. Entries marked historical show only known creation details.
          </p>
        }
        @if (lead.history.length === 0) {
          <p>No lead activity is recorded.</p>
        } @else {
          <ol>
            @for (event of lead.history; track event.id) {
              <li>
                <strong>{{ activityLabel(event.action) }}</strong>
                <time [attr.datetime]="event.occurredAt">{{ event.occurredAt }}</time>
                <span> · {{ event.actorName }}</span>
                @if (event.fromState || event.toState) {
                  <span> · {{ event.fromState ?? '—' }} → {{ event.toState ?? '—' }}</span>
                }
                @if (event.sourceChannel) {
                  <span> · Source: {{ event.sourceChannel }}</span>
                }
                @if (event.ownerName) {
                  <span> · Owner: {{ event.ownerName }}</span>
                }
                @if (event.serviceRoute) {
                  <span> · Service: {{ event.serviceRoute }}</span>
                }
                @if (event.proposalRevision) {
                  <span> · Proposal revision {{ event.proposalRevision }}</span>
                }
                @if (event.clientId) {
                  <span> · Client identity: {{ event.clientId }}</span>
                }
                @if (event.isBackfill) {
                  <span> · Historical source details; actor not retained</span>
                }
              </li>
            }
          </ol>
          @if (lead.historyHasMore) {
            <button matButton [disabled]="busy() || loading() || historyLoading()" (click)="loadOlderHistory($event.currentTarget)">
              Load older activity
            </button>
            @if (historyLoading()) {
              <p role="status">Loading older activity…</p>
            }
            @if (historyError()) {
              <p role="alert">{{ historyError() }}</p>
            }
          }
        }
      </section>
      @if (lead.status === 'QUALIFIED') {
        <h2>Record discovery opportunity</h2>
        <p>Discovery records service scope and reporting period. Professional acceptance or continuance must be completed before proposal or quotation work.</p>
        <form (ngSubmit)="createOpportunity()">
          <label>Prospective client legal name
            <input name="prospectiveLegalName" [(ngModel)]="draft.prospectiveLegalName"
              (ngModelChange)="identityChanged()" required maxlength="300"
              [disabled]="busy() || createPending()" />
          </label>
          <label>Registration number (optional)
            <input name="registration" [(ngModel)]="draft.prospectiveRegistrationNumber"
              (ngModelChange)="identityChanged()" maxlength="64"
              [disabled]="busy() || createPending()" />
          </label>
          <label>Jurisdiction (required with registration number)
            <input name="jurisdiction" [(ngModel)]="draft.prospectiveJurisdiction"
              (ngModelChange)="identityChanged()" maxlength="100"
              [disabled]="busy() || createPending()" />
          </label>
          <label for="opportunity-service-route">Service route</label>
          <select id="opportunity-service-route" name="route" [(ngModel)]="draft.serviceRoute" (ngModelChange)="serviceRouteChanged()"
            required [disabled]="busy() || createPending()">
            <option value="" disabled>Select a supported service</option>
            @for (route of lead.serviceRoutes; track route.serviceRoute) {
              <option [value]="route.serviceRoute">{{ route.heading }}</option>
            }
          </select>
          <label
            >Entity scope<textarea
              name="entity"
              [(ngModel)]="draft.entityScope"
              (ngModelChange)="entityScopeChanged()"
              required
              maxlength="10000"
              [disabled]="busy() || createPending()"
            ></textarea>
          </label>
          <label><input type="checkbox" name="scopeConfirmed" [(ngModel)]="draft.entityScopeConfirmed"
              (ngModelChange)="reviewed = false" [disabled]="busy() || createPending()" required />
            I verified the prospective client's identity and confirm this scope. Subsidiaries and additional entities are excluded unless explicitly listed above.</label>
          <label
            >Period start<input
              name="start"
              type="date"
              [(ngModel)]="draft.periodStart"
              required
              [disabled]="busy() || createPending()"
          /></label>
          <label
            >Period end<input
              name="end"
              type="date"
              [(ngModel)]="draft.periodEnd"
              required
              [disabled]="busy() || createPending()"
          /></label>
          <label><input type="checkbox" name="reviewed" [(ngModel)]="reviewed" [disabled]="busy()" />I reviewed the discovery details.</label>
          <button matButton type="submit" [disabled]="!reviewed || !draft.entityScopeConfirmed || busy() || uncertain()">
            Record opportunity
          </button>
        </form>
        @if (createPending() && uncertain() && recoverableCreate()) {
          <p role="alert">
            This saved opportunity request may have completed. Review the unchanged terms and
            resolve it with the same request identity.
          </p>
          <button
            matButton
            [disabled]="!reviewed || busy()"
            (click)="resolveSavedOpportunityRequest()"
          >
            Resolve saved opportunity request
          </button>
        }
      }
      <section #opportunitiesSection tabindex="-1" aria-labelledby="lead-opportunities-heading">
        <h2 id="lead-opportunities-heading">Opportunities</h2>
        @for (o of lead.opportunities; track o.id) {
          <article>
            <h3 #opportunityHeading tabindex="-1">{{ o.serviceRoute }} · {{ o.stage }}</h3>
            <p>{{ o.entityScope }}</p>
            <p>{{ o.periodStart }} to {{ o.periodEnd }}@if (o.expectedFee !== null) { · Historical expected fee {{ o.expectedFee }} {{ o.currency }} }</p>
            <p>Acceptance: {{ o.acceptanceProcess === 'CONTINUANCE' ? 'Continuance' : 'Client acceptance' }} · {{ o.acceptanceStatus }}</p>
            @if (o.proposalId) {
              <a [routerLink]="['/app/practice/proposals', o.proposalId]"
                >Open proposal revision {{ o.revision }}</a
              >
            } @else if (o.stage !== 'WON' && o.stage !== 'LOST') {
              @if (o.canPrepareCommercial) {
                <button matButton [disabled]="busy() || uncertain()" (click)="select(o)">
                Prepare initial proposal
                </button>
              } @else if (o.acceptanceClientId) {
                <a [routerLink]="['/app/clients', o.acceptanceClientId, 'assessment']"
                  [queryParams]="{ opportunityId: o.id }">Complete {{ o.acceptanceProcess === 'CONTINUANCE' ? 'continuance' : 'client acceptance' }}</a>
              }
            }
          </article>
        } @empty {
          <p>No opportunities recorded for this lead.</p>
        }
        @if (lead.opportunitiesHasMore) {
          <button matButton [disabled]="busy() || loading() || opportunitiesLoading()" (click)="loadOlderOpportunities($event.currentTarget)">
            {{ opportunitiesLoading() ? 'Loading older opportunities…' : 'Load older opportunities' }}
          </button>
        }
        @if (opportunitiesError()) {
          <p role="alert">{{ opportunitiesError() }}</p>
        }
        @if (opportunitiesLoading()) {
          <p role="status">Loading older opportunities…</p>
        }
      </section>
      @if (selected(); as o) {
        <h2>Initial proposal for {{ o.serviceRoute }}</h2>
        <p>
          Current unconditional Partner acceptance is recorded for this exact scope. Enter commercial terms below; this creates a draft requiring independent review.
        </p>
        <form (ngSubmit)="createProposal()">
          <label
            >Service profile<input
              name="profile"
              [(ngModel)]="profile"
              (ngModelChange)="proposalReviewed = false"
              [disabled]="busy() || proposalCreatePending()"
              required
              maxlength="100"
          /></label>
          <label
            >Deliverables<textarea
              name="deliverables"
              [(ngModel)]="deliverables"
              (ngModelChange)="proposalReviewed = false"
              [disabled]="busy() || proposalCreatePending()"
              required
              maxlength="10000"
            ></textarea>
          </label>
          <label
            >Exclusions<textarea
              name="exclusions"
              [(ngModel)]="exclusions"
              (ngModelChange)="proposalReviewed = false"
              [disabled]="busy() || proposalCreatePending()"
              maxlength="10000"
            ></textarea>
          </label>
          <label
            >Dependencies<textarea
              name="dependencies"
              [(ngModel)]="dependencies"
              (ngModelChange)="proposalReviewed = false"
              [disabled]="busy() || proposalCreatePending()"
              maxlength="10000"
            ></textarea>
          </label>
          <label
            >Estimated execution timeline (required for a new quotation)<textarea
              name="timeline"
              [(ngModel)]="timeline"
              (ngModelChange)="proposalReviewed = false"
              [disabled]="busy() || proposalCreatePending()"
              maxlength="4000"
            ></textarea>
            <small>Offered to the client on the quotation. Changing it later needs a new proposal revision.</small>
          </label>
          <label>Proposal fee<input name="proposalFee" [(ngModel)]="proposalFee" inputmode="decimal"
              (ngModelChange)="proposalReviewed = false" required [disabled]="busy() || proposalCreatePending()" /></label>
          <label>Currency<input name="proposalCurrency" [(ngModel)]="proposalCurrency" maxlength="3"
              (ngModelChange)="proposalReviewed = false" required [disabled]="busy() || proposalCreatePending()" /></label>
          <label
            ><input
              type="checkbox"
              name="proposalReviewed"
              [(ngModel)]="proposalReviewed"
              [disabled]="busy()"
            />I
            reviewed the proposal terms.</label
          >
          <button matButton type="submit" [disabled]="!proposalReviewed || !exactDecimal(proposalFee) || !validCurrency(proposalCurrency) || busy() || uncertain()">
            Create initial draft
          </button>
        </form>
        @if (proposalCreatePending() && uncertain() && recoverableProposalCreate()) {
          <p role="alert">
            The saved proposal request may have completed. Review the unchanged terms and resolve it
            with the same request identity.
          </p>
          <button
            matButton
            [disabled]="!proposalReviewed || busy()"
            (click)="resolveSavedProposalRequest()"
          >
            Resolve saved proposal request
          </button>
        }
      }
    }
    <p role="status">{{ commandStatus() }}</p>
  `,
})
export class Opportunities {
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly session = inject(SessionService);
  private readonly drafts = inject(Drafts);
  private readonly hostElement = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly renderInjector = inject(Injector);
  private readonly historySection = viewChild<ElementRef<HTMLElement>>('historySection');
  private readonly opportunitiesSection = viewChild<ElementRef<HTMLElement>>('opportunitiesSection');
  private readonly opportunityHeadings = viewChildren<ElementRef<HTMLElement>>('opportunityHeading');
  private focusInteraction = 0;
  private id = '';
  private request?: Subscription;
  private historyRequest?: Subscription;
  private opportunitiesRequest?: Subscription;
  private write?: Subscription;
  private requestId: string = crypto.randomUUID();
  private proposalRequestId: string = crypto.randomUUID();
  private savedProposalIntent: ProposalCreateIntent | null = null;
  private fence = 0;
  readonly data = signal<LeadWorkspace | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly historyLoading = signal(false);
  readonly historyError = signal('');
  readonly opportunitiesLoading = signal(false);
  readonly opportunitiesError = signal('');
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly createPending = signal(false);
  readonly recoverableCreate = signal(false);
  readonly proposalCreatePending = signal(false);
  readonly recoverableProposalCreate = signal(false);
  readonly commandStatus = signal('');
  readonly selected = signal<Opportunity | null>(null);
  draft = {
    prospectiveLegalName: '',
    prospectiveRegistrationNumber: '',
    prospectiveJurisdiction: '',
    serviceRoute: '',
    entityScope: '',
    entityScopeConfirmed: false,
    periodStart: '',
    periodEnd: '',
  };
  reviewed = false;
  proposalReviewed = false;
  profile = '';
  deliverables = '';
  exclusions = '';
  dependencies = '';
  timeline = '';
  proposalFee = '';
  proposalCurrency = '';
  private entityScopeManuallyEdited = false;
  recordFocusInteraction(): void { this.focusInteraction++; }
  private historyFocusOrigin(trigger?: EventTarget | null): HistoryFocusOrigin | null {
    const host = this.hostElement.nativeElement;
    const document = host.ownerDocument;
    if (!(trigger instanceof HTMLButtonElement) || !host.contains(trigger) ||
        (document.activeElement !== trigger && document.activeElement !== document.body)) return null;
    return { element: trigger, interaction: this.focusInteraction };
  }
  private focusHistorySection(origin: HistoryFocusOrigin, owner: number): void {
    afterNextRender(() => {
      const document = this.hostElement.nativeElement.ownerDocument;
      if (owner !== this.fence || origin.interaction !== this.focusInteraction ||
          (document.activeElement !== origin.element && document.activeElement !== document.body)) return;
      this.historySection()?.nativeElement.focus();
    }, { injector: this.renderInjector });
  }
  constructor() {
    const route = this.route.paramMap.subscribe((p) => {
      this.reset();
      this.id = p.get('id') ?? '';
      this.restorePendingIntent();
      this.restorePendingProposalIntent();
      this.load();
    });
    effect(() => {
      this.session.invalidation();
      const staff = this.session.current()?.staff;
      untracked(() => {
        this.reset();
        if (staff) {
          this.restorePendingIntent();
          this.restorePendingProposalIntent();
          this.load();
        }
      });
    });
    inject(DestroyRef).onDestroy(() => {
      this.fence++;
      route.unsubscribe();
      this.request?.unsubscribe();
      this.historyRequest?.unsubscribe();
      this.opportunitiesRequest?.unsubscribe();
      this.write?.unsubscribe();
    });
  }
  private reset(): void {
    this.fence++;
    this.request?.unsubscribe();
    this.historyRequest?.unsubscribe();
    this.opportunitiesRequest?.unsubscribe();
    this.historyLoading.set(false);
    this.historyError.set('');
    this.opportunitiesLoading.set(false);
    this.opportunitiesError.set('');
    this.loading.set(false);
    this.error.set('');
    this.write?.unsubscribe();
    this.data.set(null);
    this.selected.set(null);
    this.busy.set(false);
    this.uncertain.set(false);
    this.createPending.set(false);
    this.recoverableCreate.set(false);
    this.proposalCreatePending.set(false);
    this.recoverableProposalCreate.set(false);
    this.savedProposalIntent = null;
    this.commandStatus.set('');
    this.reviewed = this.proposalReviewed = false;
    this.profile = this.deliverables = this.exclusions = this.dependencies = this.timeline = '';
    this.proposalFee = this.proposalCurrency = '';
    this.entityScopeManuallyEdited = false;
    this.draft = {
      prospectiveLegalName: '',
      prospectiveRegistrationNumber: '',
      prospectiveJurisdiction: '',
      serviceRoute: '',
      entityScope: '',
      entityScopeConfirmed: false,
      periodStart: '',
      periodEnd: '',
    };
    this.requestId = crypto.randomUUID();
    this.proposalRequestId = crypto.randomUUID();
  }
  select(o: Opportunity): void {
    if (this.proposalCreatePending() || !o.canPrepareCommercial) return;
    this.selected.set(o);
    this.proposalReviewed = false;
    this.proposalFee = '';
    this.proposalCurrency = '';
  }
  validCurrency(value: string): boolean { return /^[A-Z]{3}$/.test(value); }
  exactDecimal(value: string): boolean { return exactDecimal(value); }
  private generatedScope(): string {
    const route = this.data()?.serviceRoutes.find((x) => x.serviceRoute === this.draft.serviceRoute);
    return route?.entityScopeTemplate.replaceAll('{clientName}', this.draft.prospectiveLegalName.trim()) ?? '';
  }
  private refreshGeneratedScope(): void {
    if (!this.entityScopeManuallyEdited) this.draft.entityScope = this.generatedScope();
    this.draft.entityScopeConfirmed = false;
    this.reviewed = false;
  }
  serviceRouteChanged(): void {
    this.refreshGeneratedScope();
  }
  identityChanged(): void {
    this.refreshGeneratedScope();
  }
  entityScopeChanged(): void {
    this.entityScopeManuallyEdited = true;
    this.draft.entityScopeConfirmed = false;
    this.reviewed = false;
  }
  activityLabel(action: LeadActivityAction): string {
    return {
      LEAD_CREATED: 'Lead created',
      LEAD_QUALIFIED: 'Lead qualified',
      OPPORTUNITY_CREATED: 'Service interest recorded',
      PROPOSAL_REVISION_CREATED: 'Proposal revision created',
      PROPOSAL_REVIEWED: 'Proposal entered internal review',
      PROPOSAL_MARKED_SENT: 'Proposal marked sent',
      PROPOSAL_ACCEPTED: 'Proposal response accepted',
      PROPOSAL_DECLINED: 'Proposal declined',
      CLIENT_CONVERSION_LINKED: 'Client conversion linked',
    }[action];
  }
  createOpportunity(): void {
    if (
      !this.data() ||
      !this.reviewed ||
      !this.draft.prospectiveLegalName.trim() ||
      !this.draft.serviceRoute ||
      !this.data()!.serviceRoutes.some((route) => route.serviceRoute === this.draft.serviceRoute) ||
      !this.draft.entityScope.trim() ||
      !this.draft.entityScopeConfirmed ||
      (!!this.draft.prospectiveRegistrationNumber.trim() !== !!this.draft.prospectiveJurisdiction.trim()) ||
      this.busy() ||
      this.uncertain() ||
      this.createPending()
    ) {
      this.commandStatus.set('Confirm the client identity, supported service, entity scope and reporting period. Pricing is recorded after acceptance.');
      return;
    }
    const intent: OpportunityCreateIntent = {
      leadId: this.id,
      requestId: this.requestId,
      serviceRoute: this.draft.serviceRoute,
      prospectiveLegalName: this.draft.prospectiveLegalName.trim(),
      prospectiveRegistrationNumber: this.draft.prospectiveRegistrationNumber.trim() || null,
      prospectiveJurisdiction: this.draft.prospectiveJurisdiction.trim() || null,
      entityScope: this.draft.entityScope.trim(),
      entityScopeConfirmed: true,
      entityScopeManuallyEdited: this.entityScopeManuallyEdited,
      periodStart: this.draft.periodStart,
      periodEnd: this.draft.periodEnd,
      submissionPending: true,
    };
    if (!this.persistIntent(intent)) {
      this.commandStatus.set(
        'The opportunity was not sent because this browser could not save its recovery identity.',
      );
      return;
    }
    this.createPending.set(true);
    this.recoverableCreate.set(false);
    this.sendOpportunityIntent(intent);
  }
  resolveSavedOpportunityRequest(): void {
    if (
      !this.data() ||
      this.busy() ||
      !this.reviewed ||
      !this.createPending() ||
      !this.recoverableCreate()
    )
      return;
    const saved = this.drafts.load(this.intentScope(), Opportunities.validIntent);
    if (
      !saved ||
      saved.leadId !== this.id ||
      saved.requestId !== this.requestId ||
      !this.sameIntent(saved, this.currentIntent())
    ) {
      this.commandStatus.set(
        'The saved recovery details could not be verified. No retry was sent; refresh the page or contact an administrator.',
      );
      return;
    }
    this.sendOpportunityIntent(saved);
  }
  private static validIntent(value: unknown): OpportunityCreateIntent | null {
    if (!value || typeof value !== 'object') return null;
    const v = value as Record<string, unknown>;
    const text = (key: string, max: number) =>
      typeof v[key] === 'string' && (v[key] as string).length <= max ? (v[key] as string) : null;
    const leadId = text('leadId', 36),
      requestId = text('requestId', 36);
    const serviceRoute = text('serviceRoute', 100),
      entityScope = text('entityScope', 10000);
    const periodStart = text('periodStart', 10),
      periodEnd = text('periodEnd', 10);
    const prospectiveLegalName = text('prospectiveLegalName', 300),
      prospectiveRegistrationNumber = v['prospectiveRegistrationNumber'] === null ? null : text('prospectiveRegistrationNumber', 64),
      prospectiveJurisdiction = v['prospectiveJurisdiction'] === null ? null : text('prospectiveJurisdiction', 100);
    if (
      !leadId ||
      !guidPattern.test(leadId) ||
      !requestId ||
      !guidPattern.test(requestId) ||
      serviceRoute === null ||
      !prospectiveLegalName?.trim() ||
      (prospectiveRegistrationNumber !== null && !prospectiveRegistrationNumber) ||
      (prospectiveJurisdiction !== null && !prospectiveJurisdiction) ||
      ((prospectiveRegistrationNumber === null) !== (prospectiveJurisdiction === null)) ||
      entityScope === null ||
      v['entityScopeConfirmed'] !== true ||
      typeof v['entityScopeManuallyEdited'] !== 'boolean' ||
      periodStart === null ||
      periodEnd === null ||
      v['submissionPending'] !== true
    )
      return null;
    return {
      leadId,
      requestId,
      serviceRoute,
      prospectiveLegalName,
      prospectiveRegistrationNumber,
      prospectiveJurisdiction,
      entityScope,
      entityScopeConfirmed: true,
      entityScopeManuallyEdited: v['entityScopeManuallyEdited'],
      periodStart,
      periodEnd,
      submissionPending: true,
    };
  }
  private intentScope(): string {
    return 'commercial-opportunity-create:' + this.id;
  }
  private currentIntent(): OpportunityCreateIntent {
    return {
      leadId: this.id,
      requestId: this.requestId,
      serviceRoute: this.draft.serviceRoute,
      prospectiveLegalName: this.draft.prospectiveLegalName.trim(),
      prospectiveRegistrationNumber: this.draft.prospectiveRegistrationNumber.trim() || null,
      prospectiveJurisdiction: this.draft.prospectiveJurisdiction.trim() || null,
      entityScope: this.draft.entityScope.trim(),
      entityScopeConfirmed: this.draft.entityScopeConfirmed,
      entityScopeManuallyEdited: this.entityScopeManuallyEdited,
      periodStart: this.draft.periodStart,
      periodEnd: this.draft.periodEnd,
      submissionPending: true,
    };
  }
  private sameIntent(a: OpportunityCreateIntent, b: OpportunityCreateIntent): boolean {
    return (
      a.leadId === b.leadId &&
      a.requestId === b.requestId &&
      a.serviceRoute === b.serviceRoute &&
      a.prospectiveLegalName === b.prospectiveLegalName &&
      a.prospectiveRegistrationNumber === b.prospectiveRegistrationNumber &&
      a.prospectiveJurisdiction === b.prospectiveJurisdiction &&
      a.entityScope === b.entityScope &&
      a.entityScopeConfirmed === b.entityScopeConfirmed &&
      a.entityScopeManuallyEdited === b.entityScopeManuallyEdited &&
      a.periodStart === b.periodStart &&
      a.periodEnd === b.periodEnd &&
      a.submissionPending === b.submissionPending
    );
  }
  private persistIntent(intent: OpportunityCreateIntent): boolean {
    this.drafts.save(this.intentScope(), intent);
    const saved = this.drafts.load(this.intentScope(), Opportunities.validIntent);
    return !!saved && this.sameIntent(saved, intent);
  }
  private restorePendingIntent(): void {
    const saved = this.drafts.load(this.intentScope(), Opportunities.validIntent);
    if (!saved || saved.leadId !== this.id) return;
    this.requestId = saved.requestId;
    this.draft = {
      prospectiveLegalName: saved.prospectiveLegalName,
      prospectiveRegistrationNumber: saved.prospectiveRegistrationNumber ?? '',
      prospectiveJurisdiction: saved.prospectiveJurisdiction ?? '',
      serviceRoute: saved.serviceRoute,
      entityScope: saved.entityScope,
      entityScopeConfirmed: saved.entityScopeConfirmed,
      periodStart: saved.periodStart,
      periodEnd: saved.periodEnd,
    };
    this.entityScopeManuallyEdited = saved.entityScopeManuallyEdited;
    this.createPending.set(true);
    this.recoverableCreate.set(true);
    this.uncertain.set(true);
    this.commandStatus.set(
      'A saved opportunity request needs resolution. Review its unchanged terms to retry safely.',
    );
  }
  private sendOpportunityIntent(intent: OpportunityCreateIntent): void {
    if (this.busy()) return;
    const fence = this.fence;
    this.busy.set(true);
    this.uncertain.set(false);
    this.commandStatus.set('Saving reviewed opportunity terms…');
    this.write = this.http
      .post<unknown>('/api/ui/leads/' + intent.leadId + '/opportunities', {
        requestId: intent.requestId,
        serviceRoute: intent.serviceRoute,
        prospectiveLegalName: intent.prospectiveLegalName,
        prospectiveRegistrationNumber: intent.prospectiveRegistrationNumber,
        prospectiveJurisdiction: intent.prospectiveJurisdiction,
        entityScope: intent.entityScope,
        entityScopeConfirmed: intent.entityScopeConfirmed,
        periodStart: intent.periodStart,
        periodEnd: intent.periodEnd,
      })
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (fence !== this.fence) return;
          this.busy.set(false);
          const result = value as { id?: unknown };
          if (
            typeof result?.id !== 'string' ||
            result.id.toLowerCase() !== intent.requestId.toLowerCase()
          ) {
            this.uncertain.set(true);
            this.recoverableCreate.set(true);
            this.commandStatus.set(
              'The response did not confirm the saved request identity. Resolve the saved request before continuing.',
            );
            return;
          }
          this.drafts.clear(this.intentScope());
          this.createPending.set(false);
          this.recoverableCreate.set(false);
          this.reviewed = false;
          this.requestId = crypto.randomUUID();
          this.commandStatus.set('Opportunity recorded.');
          this.load();
        },
        error: (failure) => {
          if (fence !== this.fence) return;
          this.busy.set(false);
          if (failure.status >= 400 && failure.status < 500) {
            this.drafts.clear(this.intentScope());
            this.createPending.set(false);
            this.recoverableCreate.set(false);
            this.uncertain.set(false);
            this.reviewed = false;
            this.requestId = crypto.randomUUID();
            this.commandStatus.set(
              'Opportunity request refused. Review current access, terms and existing opportunities.',
            );
          } else {
            this.uncertain.set(true);
            this.recoverableCreate.set(true);
            this.commandStatus.set(
              'Opportunity outcome unconfirmed. Resolve the saved request before another command.',
            );
          }
          if (failure.status === 401) this.session.clear();
        },
      });
  }
  createProposal(): void {
    const o = this.selected();
    if (!o || !o.canPrepareCommercial || !this.proposalReviewed || o.proposalId ||
        !exactDecimal(this.proposalFee) || !this.validCurrency(this.proposalCurrency) || this.busy() || this.uncertain()) return;
    const intent = this.currentProposalIntent(o);
    if (!this.persistProposalIntent(intent)) {
      this.commandStatus.set(
        'The proposal was not sent because this browser could not save its recovery identity.',
      );
      return;
    }
    this.savedProposalIntent = intent;
    this.proposalCreatePending.set(true);
    this.recoverableProposalCreate.set(false);
    this.sendProposalIntent(intent);
  }
  resolveSavedProposalRequest(): void {
    const intent = this.savedProposalIntent;
    const opportunity = this.selected();
    const saved = this.drafts.load(this.proposalIntentScope(), Opportunities.validProposalIntent);
    if (
      !intent ||
      !saved ||
      !opportunity ||
      !this.proposalReviewed ||
      !this.proposalCreatePending() ||
      !this.recoverableProposalCreate() ||
      this.busy() ||
      !this.sameProposalIntent(saved, intent) ||
      !this.sameProposalIntent(this.currentProposalIntent(opportunity), intent) ||
      opportunity.proposalId !== null ||
      opportunity.revision !== intent.expectedRevision
    ) {
      this.commandStatus.set(
        'The saved proposal request no longer matches current reviewed terms and opportunity state. No retry was sent.',
      );
      return;
    }
    this.sendProposalIntent(saved);
  }
  private static validProposalIntent(value: unknown): ProposalCreateIntent | null {
    if (!value || typeof value !== 'object' || Array.isArray(value)) return null;
    const v = value as Record<string, unknown>;
    const text = (key: string, max: number) =>
      typeof v[key] === 'string' && (v[key] as string).length <= max
        ? (v[key] as string)
        : null;
    const leadId = text('leadId', 36);
    const opportunityId = text('opportunityId', 36);
    const requestId = text('requestId', 36);
    const expectedRevision = text('expectedRevision', 19);
    const serviceProfile = text('serviceProfile', 100);
    const scope = text('scope', 10000);
    const exclusions = text('exclusions', 10000);
    const deliverables = text('deliverables', 10000);
    const dependencies = text('dependencies', 10000);
    // An intent stored before the timeline existed has no such key; it was created without one.
    const timeline = v['timeline'] === undefined ? '' : text('timeline', 4000);
    const fee = text('fee', 100);
    const currency = text('currency', 3);
    const periodStart = text('periodStart', 10);
    const periodEnd = text('periodEnd', 10);
    if (
      !leadId || !guidPattern.test(leadId) ||
      !opportunityId || !guidPattern.test(opportunityId) ||
      !requestId || !guidPattern.test(requestId) ||
      !expectedRevision || !/^\d{1,19}$/.test(expectedRevision) ||
      !serviceProfile || scope === null || exclusions === null || !deliverables ||
      dependencies === null || timeline === null || !fee || !exactDecimal(fee) ||
      !currency || currency.length !== 3 ||
      !periodStart || periodStart.length !== 10 ||
      !periodEnd || periodEnd.length !== 10 ||
      v['submissionPending'] !== true
    ) return null;
    return {
      leadId, opportunityId, requestId, expectedRevision, serviceProfile, scope,
      exclusions, deliverables, dependencies, timeline, fee, currency, periodStart, periodEnd,
      submissionPending: true,
    };
  }
  private proposalIntentScope(): string {
    return 'commercial-proposal-create:' + this.id;
  }
  private currentProposalIntent(opportunity: Opportunity): ProposalCreateIntent {
    return {
      leadId: this.id,
      opportunityId: opportunity.id,
      requestId: this.proposalRequestId,
      expectedRevision: opportunity.revision,
      serviceProfile: this.profile,
      scope: opportunity.entityScope,
      exclusions: this.exclusions,
      deliverables: this.deliverables,
      dependencies: this.dependencies,
      timeline: this.timeline,
      fee: this.proposalFee,
      currency: this.proposalCurrency,
      periodStart: opportunity.periodStart,
      periodEnd: opportunity.periodEnd,
      submissionPending: true,
    };
  }
  private sameProposalIntent(a: ProposalCreateIntent, b: ProposalCreateIntent): boolean {
    return a.leadId === b.leadId &&
      a.opportunityId === b.opportunityId &&
      a.requestId === b.requestId &&
      a.expectedRevision === b.expectedRevision &&
      a.serviceProfile === b.serviceProfile &&
      a.scope === b.scope &&
      a.exclusions === b.exclusions &&
      a.deliverables === b.deliverables &&
      a.dependencies === b.dependencies &&
      a.timeline === b.timeline &&
      a.fee === b.fee &&
      a.currency === b.currency &&
      a.periodStart === b.periodStart &&
      a.periodEnd === b.periodEnd &&
      a.submissionPending === b.submissionPending;
  }
  private persistProposalIntent(intent: ProposalCreateIntent): boolean {
    this.drafts.save(this.proposalIntentScope(), intent);
    const saved = this.drafts.load(this.proposalIntentScope(), Opportunities.validProposalIntent);
    return !!saved && this.sameProposalIntent(saved, intent);
  }
  private restorePendingProposalIntent(): void {
    const saved = this.drafts.load(this.proposalIntentScope(), Opportunities.validProposalIntent);
    if (!saved || saved.leadId !== this.id) return;
    this.savedProposalIntent = saved;
    this.proposalRequestId = saved.requestId;
    this.proposalCreatePending.set(true);
    this.uncertain.set(true);
    this.commandStatus.set(
      'A saved proposal request is being checked against current opportunity state.',
    );
  }
  private reconcileProposalIntent(workspace: LeadWorkspace): void {
    const intent = this.savedProposalIntent;
    if (!intent) return;
    const opportunity = workspace.opportunities.find((o) => o.id === intent.opportunityId);
    if (opportunity?.proposalId === intent.requestId) {
      this.clearProposalIntent();
      this.uncertain.set(false);
      this.proposalReviewed = false;
      this.commandStatus.set('Persisted proposal found. Opening it.');
      void this.router.navigate(['/app/practice/proposals', intent.requestId]);
      return;
    }
    if (
      !opportunity ||
      opportunity.proposalId !== null ||
      opportunity.revision !== intent.expectedRevision
    ) {
      this.clearProposalIntent();
      this.selected.set(null);
      this.profile = this.exclusions = this.deliverables = this.dependencies = this.timeline = '';
      this.uncertain.set(false);
      this.commandStatus.set(
        'The opportunity changed while the proposal request was unresolved. No retry was sent; review the current proposal state.',
      );
      return;
    }
    this.selected.set(opportunity);
    this.profile = intent.serviceProfile;
    this.exclusions = intent.exclusions;
    this.deliverables = intent.deliverables;
    this.dependencies = intent.dependencies;
    this.timeline = intent.timeline;
    this.proposalFee = intent.fee;
    this.proposalCurrency = intent.currency;
    this.proposalCreatePending.set(true);
    this.recoverableProposalCreate.set(true);
    this.uncertain.set(true);
    this.commandStatus.set(
      'The unchanged saved proposal terms are ready. Review them and confirm before resolving the request.',
    );
  }
  private clearProposalIntent(): void {
    this.drafts.clear(this.proposalIntentScope());
    this.savedProposalIntent = null;
    this.proposalCreatePending.set(false);
    this.recoverableProposalCreate.set(false);
    this.proposalReviewed = false;
    this.proposalFee = this.proposalCurrency = '';
    this.proposalRequestId = crypto.randomUUID();
  }
  private sendProposalIntent(intent: ProposalCreateIntent): void {
    if (!this.data() || this.busy()) return;
    const fence = this.fence;
    this.busy.set(true);
    this.uncertain.set(false);
    this.commandStatus.set('Saving reviewed proposal terms…');
    this.write = this.http
      .post<unknown>('/api/ui/opportunities/' + intent.opportunityId + '/proposals', {
        requestId: intent.requestId,
        expectedRevision: intent.expectedRevision,
        serviceProfile: intent.serviceProfile,
        scope: intent.scope,
        exclusions: intent.exclusions,
        deliverables: intent.deliverables,
        dependencies: intent.dependencies,
        timeline: intent.timeline,
        fee: intent.fee,
        currency: intent.currency,
        periodStart: intent.periodStart,
        periodEnd: intent.periodEnd,
      })
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (fence !== this.fence) return;
          this.busy.set(false);
          const result = value as { id?: unknown };
          if (
            typeof result?.id !== 'string' ||
            result.id.toLowerCase() !== intent.requestId.toLowerCase()
          ) {
            this.uncertain.set(true);
            this.recoverableProposalCreate.set(true);
            this.commandStatus.set(
              'Proposal outcome unconfirmed. Resolve the saved request before another command.',
            );
            return;
          }
          this.clearProposalIntent();
          this.uncertain.set(false);
          this.commandStatus.set('Proposal draft recorded.');
          void this.router.navigate(['/app/practice/proposals', result.id]);
        },
        error: (failure) => {
          if (fence !== this.fence) return;
          this.busy.set(false);
          if (failure.status >= 400 && failure.status < 500) {
            this.clearProposalIntent();
            this.uncertain.set(false);
            this.commandStatus.set(
              'Proposal request refused. Review current access, terms and opportunity revision.',
            );
          } else {
            this.uncertain.set(true);
            this.recoverableProposalCreate.set(true);
            this.commandStatus.set(
              'Proposal outcome unconfirmed. Resolve the saved request before another command.',
            );
          }
          if (failure.status === 401) this.session.clear();
        },
      });
  }
  load(): void {
    this.fence++;
    this.request?.unsubscribe();
    this.historyRequest?.unsubscribe();
    this.opportunitiesRequest?.unsubscribe();
    this.historyLoading.set(false);
    this.historyError.set('');
    this.opportunitiesLoading.set(false);
    this.opportunitiesError.set('');
    this.loading.set(false);
    this.data.set(null);
    this.error.set('');
    if (!guidPattern.test(this.id) || !this.session.current()?.staff) return;
    const fence = this.fence;
    this.loading.set(true);
    this.request = this.http
      .get<unknown>('/api/ui/leads/' + this.id)
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (fence !== this.fence) return;
          try {
            const v = decodeOpportunities(value);
            this.data.set(v);
            if (!this.draft.prospectiveLegalName.trim() && !this.createPending()) {
              this.draft.prospectiveLegalName = v.name.trim();
              this.refreshGeneratedScope();
            }
            const creationWasPersisted = v.opportunities.some((o) => o.id === this.requestId);
            if (
              this.uncertain() &&
              (creationWasPersisted ||
                v.opportunities.some((o) => o.id === this.selected()?.id && o.proposalId))
            ) {
              if (this.createPending() && creationWasPersisted) {
                this.drafts.clear(this.intentScope());
                this.createPending.set(false);
                this.recoverableCreate.set(false);
              }
              this.uncertain.set(false);
              this.reviewed = this.proposalReviewed = false;
              this.selected.set(null);
              this.requestId = crypto.randomUUID();
              this.commandStatus.set('Persisted result found. Review current state.');
            }
            this.reconcileProposalIntent(v);
          } catch {
            this.error.set('Unsupported opportunity response.');
          }
          this.loading.set(false);
        },
        error: (failure) => {
          if (fence !== this.fence) return;
          this.loading.set(false);
          this.error.set(
            'Opportunities unavailable. Current firm-wide commercial authority is required.',
          );
          if (failure.status === 401) this.session.clear();
        },
      });
  }
  loadOlderHistory(trigger?: EventTarget | null): void {
    const current = this.data();
    const oldest = current?.history[0];
    if (
      !current?.historyHasMore ||
      !oldest ||
      this.historyLoading() ||
      !guidPattern.test(this.id) ||
      !this.session.current()?.staff
    )
      return;
    const fence = this.fence;
    const focusOrigin = this.historyFocusOrigin(trigger);
    const query = `?historyBeforeOccurredAt=${encodeURIComponent(oldest.occurredAt)}&historyBeforeId=${oldest.id}`;
    this.historyLoading.set(true);
    this.historyError.set('');
    this.historyRequest = this.http
      .get<unknown>('/api/ui/leads/' + this.id + query)
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (fence !== this.fence) return;
          let finalHistoryPage = false;
          try {
            const page = decodeOpportunities(value);
            const latest = this.data();
            if (!latest || latest.id !== current.id || page.id !== latest.id || page.history.length === 0) {
              throw new Error('Lead history page does not match the current lead.');
            }
            const known = new Set(latest.history.map((event) => event.id.toLowerCase()));
            const older = page.history.filter((event) => !known.has(event.id.toLowerCase()));
            if (older.length === 0) throw new Error('Lead history page contained no earlier events.');
            const history = [...older, ...latest.history].sort((a, b) => {
              const time = Date.parse(a.occurredAt) - Date.parse(b.occurredAt);
              if (time !== 0) return time;
              const left = a.id.toLowerCase();
              const right = b.id.toLowerCase();
              return left < right ? -1 : left > right ? 1 : 0;
            });
            this.data.set({ ...latest, history, historyHasMore: page.historyHasMore });
            finalHistoryPage = !page.historyHasMore;
          } catch {
            this.historyError.set('Older lead activity could not be read safely. Refresh persisted state and retry.');
          }
          this.historyLoading.set(false);
          if (finalHistoryPage && focusOrigin) this.focusHistorySection(focusOrigin, fence);
        },
        error: (failure) => {
          if (fence !== this.fence) return;
          this.historyLoading.set(false);
          this.historyError.set('Older lead activity is unavailable. Refresh persisted state and retry.');
          if (failure.status === 401) this.session.clear();
        },
      });
  }
  private opportunityOwnerKey(): string {
    const session = this.session.current();
    return `${this.fence}:${this.id}:${session?.firmId ?? ''}:${session?.userId ?? ''}:${session?.generation ?? ''}:${session?.staff ?? false}`;
  }
  private opportunityFocusOrigin(
    trigger: EventTarget | null | undefined,
    owner: string,
  ): OpportunityFocusOrigin | null {
    const host = this.hostElement.nativeElement;
    const document = host.ownerDocument;
    if (!(trigger instanceof HTMLButtonElement) || !host.contains(trigger) ||
        (document.activeElement !== trigger && document.activeElement !== document.body)) return null;
    return { element: trigger, interaction: this.focusInteraction, owner };
  }
  private opportunityFocusIsOwned(origin: OpportunityFocusOrigin): boolean {
    const document = this.hostElement.nativeElement.ownerDocument;
    return origin.owner === this.opportunityOwnerKey() &&
      origin.interaction === this.focusInteraction &&
      (document.activeElement === origin.element || document.activeElement === document.body);
  }
  private focusLoadedOpportunities(
    origin: OpportunityFocusOrigin,
    firstNewIndex: number,
  ): void {
    afterNextRender(() => {
      if (!this.opportunityFocusIsOwned(origin)) return;
      (
        this.opportunityHeadings()[firstNewIndex]?.nativeElement ??
        this.opportunitiesSection()?.nativeElement
      )?.focus();
    }, { injector: this.renderInjector });
  }
  private restoreOpportunityRetryFocus(origin: OpportunityFocusOrigin): void {
    afterNextRender(() => {
      if (
        !this.opportunityFocusIsOwned(origin) ||
        !origin.element.isConnected ||
        !this.hostElement.nativeElement.contains(origin.element) ||
        origin.element.disabled
      ) return;
      origin.element.focus();
    }, { injector: this.renderInjector });
  }
  loadOlderOpportunities(trigger?: EventTarget | null): void {
    const current = this.data();
    const oldest = current?.opportunities.at(-1);
    if (
      !current?.opportunitiesHasMore ||
      !oldest ||
      this.opportunitiesLoading() ||
      this.loading() ||
      !guidPattern.test(this.id) ||
      !this.session.current()?.staff
    )
      return;
    const owner = this.opportunityOwnerKey();
    const focusOrigin = this.opportunityFocusOrigin(trigger, owner);
    const firstNewIndex = current.opportunities.length;
    const query = `?opportunityBeforeCreatedAt=${encodeURIComponent(oldest.createdAt)}&opportunityBeforeId=${oldest.id}`;
    this.opportunitiesLoading.set(true);
    this.opportunitiesError.set('');
    this.opportunitiesRequest = this.http
      .get<unknown>('/api/ui/leads/' + this.id + query)
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (owner !== this.opportunityOwnerKey()) return;
          let opportunityPageLoaded = false;
          let retryPagerAfterDecodeFailure = false;
          try {
            const page = decodeOpportunities(value);
            const latest = this.data();
            if (
              !latest ||
              latest.id !== current.id ||
              page.id !== latest.id ||
              (page.opportunities.length === 0 && page.opportunitiesHasMore)
            )
              throw new Error('Lead opportunity page does not match the current lead.');
            const known = new Set(latest.opportunities.map((opportunity) => opportunity.id.toLowerCase()));
            const older = page.opportunities.filter((opportunity) => {
              const key = opportunity.id.toLowerCase();
              if (known.has(key)) return false;
              known.add(key);
              return true;
            });
            if (older.length === 0 && page.opportunitiesHasMore)
              throw new Error('Lead opportunity page did not advance.');
            this.data.set({
              ...latest,
              opportunities: [...latest.opportunities, ...older],
              opportunitiesHasMore: page.opportunitiesHasMore,
            });
            opportunityPageLoaded = true;
          } catch {
            this.opportunitiesError.set('Older opportunities could not be read safely. Refresh persisted state and retry.');
            retryPagerAfterDecodeFailure = true;
          }
          this.opportunitiesLoading.set(false);
          if (opportunityPageLoaded && focusOrigin)
            this.focusLoadedOpportunities(focusOrigin, firstNewIndex);
          else if (retryPagerAfterDecodeFailure && focusOrigin)
            this.restoreOpportunityRetryFocus(focusOrigin);
        },
        error: (failure) => {
          if (owner !== this.opportunityOwnerKey()) return;
          this.opportunitiesLoading.set(false);
          if (failure.status === 401 || failure.status === 403) {
            this.reset();
            this.error.set('Opportunities unavailable. Current firm-wide commercial authority is required.');
            if (failure.status === 401) this.session.clear();
            return;
          }
          this.opportunitiesError.set('Older opportunities are unavailable. Refresh persisted state and retry.');
          if (focusOrigin) this.restoreOpportunityRetryFocus(focusOrigin);
        },
      });
  }
}
