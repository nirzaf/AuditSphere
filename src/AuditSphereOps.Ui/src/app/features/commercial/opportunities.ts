import { Component, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
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
  expectedFee: string;
  currency: string;
  periodStart: string;
  periodEnd: string;
  proposalId: string | null;
  revision: string;
}
interface LeadWorkspace {
  id: string;
  name: string;
  status: string;
  opportunities: Opportunity[];
}
interface OpportunityCreateIntent {
  leadId: string;
  requestId: string;
  serviceRoute: string;
  entityScope: string;
  periodStart: string;
  periodEnd: string;
  expectedFee: string;
  currency: string;
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
    !Array.isArray(v['opportunities']) ||
    v['opportunities'].length > 100
  )
    throw new Error('Unsupported lead');
  for (const o of v['opportunities']) {
    if (
      !o ||
      typeof o.id !== 'string' ||
      !guidPattern.test(o.id) ||
      !['serviceRoute', 'entityScope', 'stage', 'currency', 'periodStart', 'periodEnd'].every(
        (k) => typeof o[k] === 'string',
      ) ||
      !exactDecimal(o.expectedFee) ||
      typeof o.revision !== 'string' ||
      !/^\d{1,19}$/.test(o.revision) ||
      (o.proposalId !== null &&
        (typeof o.proposalId !== 'string' || !guidPattern.test(o.proposalId)))
    )
      throw new Error('Unsupported opportunity');
  }
  return v as unknown as LeadWorkspace;
}
@Component({
  selector: 'audit-opportunities',
  imports: [RouterLink, FormsModule, MatButtonModule, MatProgressBarModule],
  template: `
    <a routerLink="/app/practice/leads">Leads</a>
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
      @if (lead.status === 'QUALIFIED') {
        <h2>Record discovery opportunity</h2>
        <p>Commercial discovery does not accept a client or activate professional work.</p>
        <form (ngSubmit)="createOpportunity()">
          <label
            >Service route<input
              name="route"
              [(ngModel)]="draft.serviceRoute"
              required
              maxlength="100"
              [disabled]="busy() || createPending()"
          /></label>
          <label
            >Entity scope<textarea
              name="entity"
              [(ngModel)]="draft.entityScope"
              required
              maxlength="10000"
              [disabled]="busy() || createPending()"
            ></textarea>
          </label>
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
          <label
            >Expected fee<input
              name="fee"
              [(ngModel)]="draft.expectedFee"
              inputmode="decimal"
              required
              [disabled]="busy() || createPending()"
          /></label>
          <label
            >Currency<input
              name="currency"
              [(ngModel)]="draft.currency"
              required
              maxlength="3"
              [disabled]="busy() || createPending()"
          /></label>
          <label
            ><input type="checkbox" name="reviewed" [(ngModel)]="reviewed" [disabled]="busy()" />I
            reviewed these discovery terms.</label
          >
          <button matButton type="submit" [disabled]="!reviewed || busy() || uncertain()">
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
      <h2>Latest 100 opportunities</h2>
      @for (o of lead.opportunities; track o.id) {
        <section>
          <h3>{{ o.serviceRoute }} · {{ o.stage }}</h3>
          <p>{{ o.entityScope }}</p>
          <p>{{ o.periodStart }} to {{ o.periodEnd }} · {{ o.expectedFee }} {{ o.currency }}</p>
          @if (o.proposalId) {
            <a [routerLink]="['/app/practice/proposals', o.proposalId]"
              >Open proposal revision {{ o.revision }}</a
            >
          } @else if (o.stage !== 'WON' && o.stage !== 'LOST') {
            <button matButton [disabled]="busy() || uncertain()" (click)="select(o)">
              Prepare initial proposal
            </button>
          }
        </section>
      } @empty {
        <p>No opportunities recorded for this lead.</p>
      }
      @if (selected(); as o) {
        <h2>Initial proposal for {{ o.serviceRoute }}</h2>
        <p>
          Review the scope and fee above, plus the terms below. This creates a draft requiring
          independent review.
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
            ><input
              type="checkbox"
              name="proposalReviewed"
              [(ngModel)]="proposalReviewed"
              [disabled]="busy()"
            />I
            reviewed the proposal terms.</label
          >
          <button matButton type="submit" [disabled]="!proposalReviewed || busy() || uncertain()">
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
  private id = '';
  private request?: Subscription;
  private write?: Subscription;
  private requestId: string = crypto.randomUUID();
  private proposalRequestId: string = crypto.randomUUID();
  private savedProposalIntent: ProposalCreateIntent | null = null;
  private fence = 0;
  readonly data = signal<LeadWorkspace | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly createPending = signal(false);
  readonly recoverableCreate = signal(false);
  readonly proposalCreatePending = signal(false);
  readonly recoverableProposalCreate = signal(false);
  readonly commandStatus = signal('');
  readonly selected = signal<Opportunity | null>(null);
  draft = {
    serviceRoute: '',
    entityScope: '',
    periodStart: '',
    periodEnd: '',
    expectedFee: '',
    currency: 'QAR',
  };
  reviewed = false;
  proposalReviewed = false;
  profile = '';
  deliverables = '';
  exclusions = '';
  dependencies = '';
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
      this.write?.unsubscribe();
    });
  }
  private reset(): void {
    this.fence++;
    this.request?.unsubscribe();
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
    this.profile = this.deliverables = this.exclusions = this.dependencies = '';
    this.draft = {
      serviceRoute: '',
      entityScope: '',
      periodStart: '',
      periodEnd: '',
      expectedFee: '',
      currency: 'QAR',
    };
    this.requestId = crypto.randomUUID();
    this.proposalRequestId = crypto.randomUUID();
  }
  select(o: Opportunity): void {
    if (this.proposalCreatePending()) return;
    this.selected.set(o);
    this.proposalReviewed = false;
  }
  createOpportunity(): void {
    if (
      !this.data() ||
      !this.reviewed ||
      !exactDecimal(this.draft.expectedFee) ||
      this.busy() ||
      this.uncertain() ||
      this.createPending()
    ) {
      this.commandStatus.set('Review terms and enter an exact decimal fee.');
      return;
    }
    const intent: OpportunityCreateIntent = {
      leadId: this.id,
      requestId: this.requestId,
      ...this.draft,
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
    const expectedFee = text('expectedFee', 100),
      currency = text('currency', 3);
    if (
      !leadId ||
      !guidPattern.test(leadId) ||
      !requestId ||
      !guidPattern.test(requestId) ||
      serviceRoute === null ||
      entityScope === null ||
      periodStart === null ||
      periodEnd === null ||
      !expectedFee ||
      !exactDecimal(expectedFee) ||
      !currency ||
      currency.length !== 3 ||
      v['submissionPending'] !== true
    )
      return null;
    return {
      leadId,
      requestId,
      serviceRoute,
      entityScope,
      periodStart,
      periodEnd,
      expectedFee,
      currency,
      submissionPending: true,
    };
  }
  private intentScope(): string {
    return 'commercial-opportunity-create:' + this.id;
  }
  private currentIntent(): OpportunityCreateIntent {
    return { leadId: this.id, requestId: this.requestId, ...this.draft, submissionPending: true };
  }
  private sameIntent(a: OpportunityCreateIntent, b: OpportunityCreateIntent): boolean {
    return (
      a.leadId === b.leadId &&
      a.requestId === b.requestId &&
      a.serviceRoute === b.serviceRoute &&
      a.entityScope === b.entityScope &&
      a.periodStart === b.periodStart &&
      a.periodEnd === b.periodEnd &&
      a.expectedFee === b.expectedFee &&
      a.currency === b.currency &&
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
      serviceRoute: saved.serviceRoute,
      entityScope: saved.entityScope,
      periodStart: saved.periodStart,
      periodEnd: saved.periodEnd,
      expectedFee: saved.expectedFee,
      currency: saved.currency,
    };
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
        entityScope: intent.entityScope,
        periodStart: intent.periodStart,
        periodEnd: intent.periodEnd,
        expectedFee: intent.expectedFee,
        currency: intent.currency,
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
    if (!o || !this.proposalReviewed || o.proposalId || this.busy() || this.uncertain()) return;
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
      dependencies === null || !fee || !exactDecimal(fee) ||
      !currency || currency.length !== 3 ||
      !periodStart || periodStart.length !== 10 ||
      !periodEnd || periodEnd.length !== 10 ||
      v['submissionPending'] !== true
    ) return null;
    return {
      leadId, opportunityId, requestId, expectedRevision, serviceProfile, scope,
      exclusions, deliverables, dependencies, fee, currency, periodStart, periodEnd,
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
      fee: opportunity.expectedFee,
      currency: opportunity.currency,
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
      this.profile = this.exclusions = this.deliverables = this.dependencies = '';
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
    this.request?.unsubscribe();
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
}
