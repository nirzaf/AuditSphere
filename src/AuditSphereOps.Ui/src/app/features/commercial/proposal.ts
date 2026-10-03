import { Component, DestroyRef, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatDialog } from '@angular/material/dialog';
import { UnsavedChangesDialog } from '../../core/unsaved-changes';
import { CommercialFormDraft, textFields } from './commercial-form-draft';
import { firstValueFrom, Subscription, timeout } from 'rxjs';
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
  sentAt: string | null;
  responseAt: string | null;
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
  ownerName: string;
  authorName: string;
  reviewerName: string;
  approvedAt: string | null;
  sentAt: string | null;
  responseAt: string | null;
  supersedesId: string | null;
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
    !['ownerName', 'authorName', 'reviewerName'].every((k) => typeof v[k] === 'string') ||
    !['approvedAt', 'sentAt', 'responseAt', 'supersedesId'].every(
      (k) => v[k] === null || typeof v[k] === 'string',
    ) ||
    (v['supersedesId'] !== null &&
      (typeof v['supersedesId'] !== 'string' || !guidPattern.test(v['supersedesId'] as string))) ||
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
      !['status', 'currency', 'createdAt'].every((k) => typeof h[k] === 'string') ||
      !['sentAt', 'responseAt'].every((k) => h[k] === null || typeof h[k] === 'string')
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
        <dt>Commercial owner</dt>
        <dd>{{ proposal.ownerName }}</dd>
        <dt>Proposal author</dt>
        <dd>{{ proposal.authorName }}</dd>
        <dt>Internal review</dt>
        <dd>
          {{ proposal.reviewerName
          }}{{
            proposal.approvedAt
              ? ' · ' + proposal.approvedAt.slice(0, 16).replace('T', ' ') + ' UTC'
              : ' · Not recorded'
          }}
        </dd>
        <dt>Delivery</dt>
        <dd>
          {{
            proposal.sentAt
              ? 'Recorded sent ' + proposal.sentAt.slice(0, 16).replace('T', ' ') + ' UTC'
              : 'Not recorded as sent'
          }}
        </dd>
        <dt>Client response</dt>
        <dd>
          {{
            proposal.responseAt
              ? proposal.status +
                ' · ' +
                proposal.responseAt.slice(0, 16).replace('T', ' ') +
                ' UTC'
              : 'Not recorded'
          }}
        </dd>
        @if (proposal.supersedesId) {
          <dt>Supersedes</dt>
          <dd>
            <code>{{ proposal.supersedesId }}</code>
          </dd>
        }
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
            (ngModelChange)="reviewed = false"
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
              (ngModelChange)="reviewed = false"
              required
              maxlength="100"
          /></label>
          <label
            >Scope<textarea
              name="scope"
              [(ngModel)]="draft.scope"
              (ngModelChange)="reviewed = false"
              required
              maxlength="10000"
            ></textarea>
          </label>
          <label
            >Exclusions<textarea
              name="exclusions"
              [(ngModel)]="draft.exclusions"
              (ngModelChange)="reviewed = false"
              maxlength="10000"
            ></textarea>
          </label>
          <label
            >Deliverables<textarea
              name="deliverables"
              [(ngModel)]="draft.deliverables"
              (ngModelChange)="reviewed = false"
              required
              maxlength="10000"
            ></textarea>
          </label>
          <label
            >Dependencies<textarea
              name="dependencies"
              [(ngModel)]="draft.dependencies"
              (ngModelChange)="reviewed = false"
              maxlength="10000"
            ></textarea>
          </label>
          <label
            >Fee<input
              name="fee"
              [(ngModel)]="draft.fee"
              (ngModelChange)="reviewed = false"
              inputmode="decimal"
              required
          /></label>
          <label
            >Currency<input
              name="currency"
              [(ngModel)]="draft.currency"
              (ngModelChange)="reviewed = false"
              required
              maxlength="3"
          /></label>
          <label
            >Period start<input
              name="start"
              type="date"
              [(ngModel)]="draft.periodStart"
              (ngModelChange)="reviewed = false"
              required
          /></label>
          <label
            >Period end<input
              name="end"
              type="date"
              [(ngModel)]="draft.periodEnd"
              (ngModelChange)="reviewed = false"
              required
          /></label>
          <button matButton type="submit" [disabled]="!reviewed || busy() || uncertain()">
            Create reviewed revision
          </button>
        </form>
      }
      <p>
        Tab drafts retain only unsubmitted fields, never assent. Recovery requires this current
        proposal revision and session.
      </p>
      <button
        matButton
        [disabled]="busy() || uncertain() || !tabDraft.scope()"
        (click)="saveTabDraft()"
      >
        Save proposal tab draft
      </button>
      <button
        matButton
        [disabled]="busy() || uncertain() || !tabDraft.scope()"
        (click)="recoverTabDraft()"
      >
        Recover proposal tab draft
      </button>
      <h2>Recorded revisions</h2>
      <ul>
        @for (version of proposal.versions; track version.id) {
          <li>
            <a [routerLink]="['/app/practice/proposals', version.id]"
              >Revision {{ version.revision }}</a
            >
            · {{ version.status }} · {{ version.fee }} {{ version.currency }} · created
            {{ version.createdAt }} · sent {{ version.sentAt ?? '—' }} · response
            {{ version.responseAt ?? '—' }}
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
  private readonly dialog = inject(MatDialog);
  private readonly quotationEditor = viewChild(Quotation);
  private readonly documentEditor = viewChild(CommercialDocuments);
  private readonly feeEditor = viewChild(FeeAgreement);
  readonly tabDraft = new CommercialFormDraft(
    () => ({ ...this.draft, reason: this.reason }),
    (v) =>
      textFields(v, {
        serviceProfile: 100,
        scope: 10000,
        exclusions: 10000,
        deliverables: 10000,
        dependencies: 10000,
        fee: 40,
        currency: 3,
        periodStart: 10,
        periodEnd: 10,
        reason: 1000,
      }),
  );
  private editors() {
    return [this.quotationEditor(), this.documentEditor(), this.feeEditor()].filter(
      (x) => x !== undefined,
    );
  }
  saveTabDraft(): boolean {
    if (!this.data() || this.busy() || this.uncertain()) return false;
    const saved = this.tabDraft.save();
    this.commandStatus.set(
      saved
        ? 'Unsubmitted proposal fields saved without review confirmation.'
        : 'Tab draft could not be saved.',
    );
    return saved;
  }
  recoverTabDraft(): void {
    if (!this.data() || this.busy() || this.uncertain()) return;
    const fields = this.tabDraft.recover();
    if (fields) {
      const { reason, ...draft } = fields;
      this.draft = draft;
      this.reason = reason;
      this.reviewed = false;
      this.commandStatus.set(
        'Proposal fields recovered. Review the current revision and inputs again.',
      );
    } else this.commandStatus.set('No compatible proposal tab draft is available.');
  }
  async confirmNavigation(): Promise<boolean> {
    const editors = this.editors();
    if (this.busy() || this.uncertain() || editors.some((e) => e.busy() || e.uncertain()))
      return false;
    const dirty = editors.filter((e) => e.tabDraft.dirty());
    if (!this.tabDraft.dirty() && !dirty.length) return true;
    const id = this.id,
      epoch = this.session.invalidation();
    const choice = await firstValueFrom(this.dialog.open(UnsavedChangesDialog).afterClosed());
    if (id !== this.id || epoch !== this.session.invalidation()) return true;
    if (choice === 'save') {
      const saved =
        (!this.tabDraft.dirty() || this.saveTabDraft()) && dirty.every((e) => e.saveTabDraft());
      if (!saved)
        this.commandStatus.set(
          'One or more drafts could not be saved. Keep this page open to retain the edits.',
        );
      return saved;
    }
    if (choice === 'discard')
      return this.tabDraft.discard() && dirty.every((e) => e.tabDraft.discard());
    return false;
  }
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
    this.tabDraft.reset();
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
          this.tabDraft.submitted();
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
    const preserveDraft = this.tabDraft.dirty();
    const pendingDraft = { ...this.draft };
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
            if (preserveDraft) this.draft = pendingDraft;
            else this.tabDraft.reset();
            this.reviewed = false;
            if (!this.legalName) this.legalName = p.leadName;
            void this.tabDraft.bind(`commercial-proposal:${p.id}`, p);
          } catch {
            this.data.set(null);
            this.error.set('Proposal returned an unsupported response.');
          }
          this.loading.set(false);
        },
        error: (failure) => {
          this.loading.set(false);
          this.data.set(null);
          this.error.set('Proposal unavailable. Current firm-wide commercial access is required.');
          if (failure.status === 401) this.session.clear();
        },
      });
  }
}
