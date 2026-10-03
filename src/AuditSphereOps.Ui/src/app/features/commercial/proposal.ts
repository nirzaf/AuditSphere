import { Component, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import { Quotation } from './quotation';
import { CommercialDocuments } from './documents';
import { FeeAgreement } from './fee-agreement';
import { exactDecimal, guidPattern } from '../../core/contracts';
interface History {
  id: string;
  revision: string;
  status: string;
  fee: string;
  currency: string;
  createdAt: string;
}
interface Proposal {
  id: string;
  opportunityId: string;
  leadName: string;
  serviceRoute: string;
  entityScope: string;
  stage: string;
  revision: string;
  status: string;
  serviceProfile: string;
  scope: string;
  exclusions: string;
  deliverables: string;
  dependencies: string;
  fee: string;
  currency: string;
  periodStart: string;
  periodEnd: string;
  responseReason: string | null;
  clientId: string | null;
  canApprove: boolean;
  versions: History[];
}
export function decodeProposal(value: unknown): Proposal {
  if (!value || typeof value !== 'object') throw new Error('Invalid proposal');
  const v = value as Record<string, unknown>;
  if (
    !['id', 'opportunityId'].every(
      (k) => typeof v[k] === 'string' && guidPattern.test(v[k] as string),
    ) ||
    ![
      'leadName',
      'serviceRoute',
      'entityScope',
      'stage',
      'revision',
      'status',
      'serviceProfile',
      'scope',
      'exclusions',
      'deliverables',
      'dependencies',
      'currency',
      'periodStart',
      'periodEnd',
    ].every((k) => typeof v[k] === 'string') ||
    !/^\d{1,19}$/.test(v['revision'] as string) ||
    !exactDecimal(v['fee']) ||
    typeof v['canApprove'] !== 'boolean' ||
    (v['clientId'] !== null &&
      (typeof v['clientId'] !== 'string' || !guidPattern.test(v['clientId']))) ||
    (v['responseReason'] !== null && typeof v['responseReason'] !== 'string') ||
    !Array.isArray(v['versions']) ||
    v['versions'].length > 100
  )
    throw new Error('Invalid proposal');
  for (const h of v['versions'])
    if (
      !h ||
      typeof h.id !== 'string' ||
      !guidPattern.test(h.id) ||
      typeof h.revision !== 'string' ||
      !/^\d{1,19}$/.test(h.revision) ||
      !exactDecimal(h.fee) ||
      !['status', 'currency', 'createdAt'].every((k) => typeof h[k] === 'string')
    )
      throw new Error('Invalid version');
  return v as unknown as Proposal;
}
@Component({
  selector: 'audit-proposal',
  imports: [
    FeeAgreement,
    CommercialDocuments,
    Quotation,
    RouterLink,
    FormsModule,
    MatButtonModule,
    MatProgressBarModule,
  ],
  template: `
    <a routerLink="/app/practice/leads">Leads</a>
    <h1>Proposal</h1>
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" aria-label="Loading proposal" />
    }
    @if (error()) {
      <p role="alert">{{ error() }}</p>
      <button matButton (click)="load()">Retry</button>
    }
    @if (data(); as proposal) {
      <h2>{{ proposal.leadName }}</h2>
      <p>{{ proposal.status }} · Revision {{ proposal.revision }}</p>
      <dl>
        <dt>Proposal ID</dt>
        <dd>
          <code>{{ proposal.id }}</code>
        </dd>
        <dt>Service</dt>
        <dd>{{ proposal.serviceRoute }} · {{ proposal.serviceProfile }}</dd>
        <dt>Entity scope</dt>
        <dd>{{ proposal.entityScope }}</dd>
        <dt>Opportunity stage</dt>
        <dd>{{ proposal.stage }}</dd>
        <dt>Period</dt>
        <dd>{{ proposal.periodStart }} to {{ proposal.periodEnd }}</dd>
        <dt>Fee</dt>
        <dd>{{ proposal.fee }} {{ proposal.currency }}</dd>
        <dt>Scope</dt>
        <dd>{{ proposal.scope }}</dd>
        <dt>Exclusions</dt>
        <dd>{{ proposal.exclusions }}</dd>
        <dt>Deliverables</dt>
        <dd>{{ proposal.deliverables }}</dd>
        <dt>Dependencies</dt>
        <dd>{{ proposal.dependencies }}</dd>
        <dt>Response note</dt>
        <dd>{{ proposal.responseReason ?? 'Not recorded' }}</dd>
      </dl>
      @defer (on viewport) {
        <audit-quotation [proposalId]="proposal.id" (changed)="load()" />
      } @placeholder {
        <p>Quotation pricing and approvals</p>
      }
      @defer (on viewport) {
        <audit-commercial-documents [proposalId]="proposal.id" />
      } @placeholder {
        <p>Commercial document generation and downloads</p>
      }
      <section id="proposal-fee-agreement" aria-label="Fee agreement and billing milestones">
        @defer (on viewport) {
          <audit-fee-agreement [proposalId]="proposal.id" />
        } @placeholder {
          <p>Fee agreement and billing milestones</p>
        }
      </section>
      <h2>Commercial workflow</h2>
      <p>
        Marking sent records status only. No email is sent here. A commercial acceptance does not
        activate professional work.
      </p>
      <label
        ><input type="checkbox" [(ngModel)]="reviewed" [disabled]="busy()" /> I reviewed this
        revision and confirm the selected commercial action.</label
      >
      @if (proposal.status === 'DRAFT') {
        @if (proposal.canApprove) {
          <button
            matButton
            [disabled]="!reviewed || busy() || uncertain()"
            (click)="action('review')"
          >
            Submit for independent internal review
          </button>
        } @else {
          <p>An independent commercial reviewer must review your proposal.</p>
        }
      }
      @if (proposal.status === 'INTERNAL_REVIEW') {
        <button matButton [disabled]="!reviewed || busy() || uncertain()" (click)="action('sent')">
          Mark as sent
        </button>
      }
      @if (proposal.status === 'SENT') {
        <label
          >Client response reason<textarea
            [(ngModel)]="reason"
            maxlength="1000"
            [disabled]="busy()"
          ></textarea>
        </label>
        <button
          matButton
          [disabled]="!reviewed || busy() || uncertain()"
          (click)="action('response', { decision: 'ACCEPTED', reason })"
        >
          Record client acceptance
        </button>
        <button
          matButton
          [disabled]="!reviewed || busy() || uncertain() || !reason"
          (click)="action('response', { decision: 'DECLINED', reason })"
        >
          Record client decline
        </button>
      }
      @if (proposal.status === 'ACCEPTED' && !proposal.clientId) {
        <a [routerLink]="['/app/practice/proposals', id, 'client-conversion']"
          >Review prospect-to-client conversion</a
        >
      }
      @if (proposal.clientId) {
        <a [routerLink]="['/app/clients', proposal.clientId]"
          >Open client for professional acceptance</a
        >
      }
      @if (
        proposal.stage !== 'WON' &&
        proposal.stage !== 'LOST' &&
        proposal.status !== 'ACCEPTED' &&
        !proposal.clientId
      ) {
        <h2>Create a revised proposal</h2>
        <p>
          This creates a new draft and supersedes the current revision. Independent review is
          required again.
        </p>
        <form (ngSubmit)="revise()">
          <label
            >Service profile<input
              name="profile"
              [(ngModel)]="draft.serviceProfile"
              required
              maxlength="100"
          /></label>
          <label
            >Scope<textarea
              name="scope"
              [(ngModel)]="draft.scope"
              required
              maxlength="10000"
            ></textarea>
          </label>
          <label
            >Exclusions<textarea
              name="exclusions"
              [(ngModel)]="draft.exclusions"
              maxlength="10000"
            ></textarea>
          </label>
          <label
            >Deliverables<textarea
              name="deliverables"
              [(ngModel)]="draft.deliverables"
              required
              maxlength="10000"
            ></textarea>
          </label>
          <label
            >Dependencies<textarea
              name="dependencies"
              [(ngModel)]="draft.dependencies"
              maxlength="10000"
            ></textarea>
          </label>
          <label
            >Fee<input name="fee" [(ngModel)]="draft.fee" inputmode="decimal" required
          /></label>
          <label
            >Currency<input name="currency" [(ngModel)]="draft.currency" required maxlength="3"
          /></label>
          <label
            >Period start<input name="start" type="date" [(ngModel)]="draft.periodStart" required
          /></label>
          <label
            >Period end<input name="end" type="date" [(ngModel)]="draft.periodEnd" required
          /></label>
          <button matButton type="submit" [disabled]="!reviewed || busy() || uncertain()">
            Create reviewed revision
          </button>
        </form>
      }
      <h2>Recorded revisions</h2>
      <ul>
        @for (version of proposal.versions; track version.id) {
          <li>
            <a [routerLink]="['/app/practice/proposals', version.id]"
              >Revision {{ version.revision }}</a
            >
            · {{ version.status }} · {{ version.fee }} {{ version.currency }} ·
            {{ version.createdAt }}
          </li>
        }
      </ul>
    }
    <p role="status">{{ commandStatus() }}</p>
  `,
})
export class ProposalDetail {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly session = inject(SessionService);
  private id = '';
  private request?: Subscription;
  readonly data = signal<Proposal | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly commandStatus = signal('');
  reviewed = false;
  reason = '';
  legalName = '';
  draft = {
    serviceProfile: '',
    scope: '',
    exclusions: '',
    deliverables: '',
    dependencies: '',
    fee: '',
    currency: '',
    periodStart: '',
    periodEnd: '',
  };
  constructor() {
    const route = this.route.paramMap.subscribe((p) => {
      this.reset();
      this.id = p.get('id') ?? '';
      this.load();
    });
    effect(() => {
      this.session.invalidation();
      const staff = this.session.current()?.staff;
      untracked(() => {
        this.reset();
        if (staff) this.load();
      });
    });
    inject(DestroyRef).onDestroy(() => {
      route.unsubscribe();
      this.request?.unsubscribe();
    });
  }
  private reset(): void {
    this.request?.unsubscribe();
    this.data.set(null);
    this.reviewed = false;
    this.reason = '';
    this.legalName = '';
    this.commandStatus.set('');
    this.uncertain.set(false);
  }
  revise(): void {
    const proposal = this.data();
    if (!proposal || !this.reviewed || this.busy() || this.uncertain()) return;
    if (!exactDecimal(this.draft.fee)) {
      this.commandStatus.set('Enter an exact decimal fee.');
      return;
    }
    const id = this.id;
    const generation = this.session.invalidation();
    this.busy.set(true);
    this.http
      .post<unknown>('/api/ui/opportunities/' + proposal.opportunityId + '/proposals', {
        ...this.draft,
        expectedRevision: proposal.revision,
      })
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          this.busy.set(false);
          if (id !== this.id || generation !== this.session.invalidation()) return;
          const result = value as { id?: unknown };
          if (typeof result?.id !== 'string' || !guidPattern.test(result.id)) {
            this.uncertain.set(true);
            this.commandStatus.set('Outcome unconfirmed. Review persisted revisions.');
            return;
          }
          this.router.navigate(['/app/practice/proposals', result.id]);
        },
        error: (failure) => {
          this.busy.set(false);
          if (id !== this.id || generation !== this.session.invalidation()) return;
          this.uncertain.set(!(failure.status >= 400 && failure.status < 500));
          this.commandStatus.set(
            this.uncertain()
              ? 'Outcome unconfirmed. Review persisted revisions before retrying.'
              : 'Revision refused. Reload and check the reviewed terms and current revision.',
          );
          if (failure.status === 401) this.session.clear();
        },
      });
  }
  action(path: string, body: object = {}): void {
    if (!this.data() || !this.reviewed || this.busy() || this.uncertain()) return;
    const id = this.id;
    const generation = this.session.invalidation();
    this.busy.set(true);
    this.commandStatus.set('Saving commercial action…');
    this.http
      .post('/api/ui/proposals/' + id + '/' + path, body)
      .pipe(timeout(15000))
      .subscribe({
        next: () => {
          this.busy.set(false);
          if (id !== this.id || generation !== this.session.invalidation()) return;
          this.reviewed = false;
          this.commandStatus.set('Commercial action recorded.');
          this.load();
        },
        error: (failure) => {
          this.busy.set(false);
          if (id !== this.id || generation !== this.session.invalidation()) return;
          if (failure.status >= 400 && failure.status < 500)
            this.commandStatus.set(
              'Action refused. Check current scope, revision, independent review and quotation approval.',
            );
          else {
            this.uncertain.set(true);
            this.commandStatus.set(
              'Outcome unconfirmed. Refresh and review the persisted proposal before another action.',
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
    const generation = this.session.invalidation();
    this.loading.set(true);
    this.request = this.http
      .get<unknown>('/api/ui/proposals/' + this.id)
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (generation !== this.session.invalidation()) return;
          try {
            const p = decodeProposal(value);
            this.data.set(p);
            this.draft = {
              serviceProfile: p.serviceProfile,
              scope: p.scope,
              exclusions: p.exclusions,
              deliverables: p.deliverables,
              dependencies: p.dependencies,
              fee: p.fee,
              currency: p.currency,
              periodStart: p.periodStart,
              periodEnd: p.periodEnd,
            };
            if (!this.legalName) this.legalName = p.leadName;
          } catch {
            this.error.set('Proposal returned an unsupported response.');
          }
          this.loading.set(false);
        },
        error: (failure) => {
          this.loading.set(false);
          this.error.set('Proposal unavailable. Current firm-wide commercial access is required.');
          if (failure.status === 401) this.session.clear();
        },
      });
  }
}
