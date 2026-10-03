import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, routeGuid } from '../../core/api';
import {
  arr,
  bool,
  dec,
  guid,
  instant,
  nat,
  nullable,
  obj,
  oneOf,
  sha256,
  str,
} from '../../core/decode';
import { SHARED } from '../../core/ui';

const reflection = oneOf(
  'UNKNOWN',
  'NOT_REFLECTED',
  'REFLECTED',
  'PARTIALLY_REFLECTED',
  'NOT_APPLICABLE',
);
const row = obj({
  journalNumber: str(32),
  journalRevision: nat,
  layer: str(32),
  technicalStatus: str(40),
  reflectionState: reflection,
  classification: oneOf('ELIGIBLE', 'EXCLUDED', 'BLOCKED'),
  reasonCode: str(100),
  plannedReflectionState: reflection,
  journalId: nullable(guid),
  evidence: str(2000),
  reviewedByUserId: nullable(guid),
  reviewedAt: nullable(instant),
});
const review = obj({
  id: guid,
  clientId: guid,
  engagementId: guid,
  datasetId: guid,
  sourceRevision: nat,
  sourceDigest: str(100),
  periodId: nullable(guid),
  periodCode: str(100),
  periodStart: str(10),
  periodEnd: str(10),
  bookId: nullable(guid),
  bookCode: str(100),
  basis: str(100),
  currency: str(10),
  status: oneOf('Draft', 'Finalized'),
  createdByUserId: guid,
  createdAt: instant,
  resultHash: nullable(sha256),
  retainedDebits: dec,
  retainedCredits: dec,
  retainedAppliedCount: nat,
  membershipDigest: sha256,
  reviewBasis: sha256,
  eligibleCount: nat,
  excludedCount: nat,
  blockedCount: nat,
  blockers: arr(str(2000), 100),
  journals: arr(row, 25),
  page: nat,
  hasMore: bool,
});
export function decodePlanReview(raw: unknown, path = 'response') {
  const v = review(raw, path),
    total = v.eligibleCount + v.excludedCount + v.blockedCount;
  if (
    v.sourceRevision < 1 ||
    total > 1000 ||
    v.page > 40 ||
    v.journals.length !== Math.min(25, Math.max(0, total - v.page * 25)) ||
    v.hasMore !== total > (v.page + 1) * 25 ||
    v.journals.some(
      (r) =>
        r.journalRevision < 1 ||
        (r.classification === 'BLOCKED' && !r.reasonCode) ||
        (r.classification === 'ELIGIBLE' &&
          (r.technicalStatus !== 'Posted' ||
            r.reflectionState !== 'NOT_REFLECTED' ||
            r.plannedReflectionState !== r.reflectionState)),
    ) ||
    (v.status === 'Finalized' && !v.resultHash)
  )
    throw new Error('Unsupported plan context');
  return v;
}
export const decodePlanQueue = obj({
  items: arr(
    obj({
      id: guid,
      datasetId: guid,
      clientName: str(500),
      engagementName: str(500),
      status: oneOf('Draft', 'Finalized'),
      createdAt: instant,
    }),
    25,
  ),
  page: nat,
  hasMore: bool,
});
export function eligibilityReason(code: string) {
  return (
    (
      {
        'journal.not-posted':
          'The exact journal revision is missing or has not been technically posted.',
        'journal.ambiguous-identity':
          'More than one journal matches this logical revision. Resolve its identity before application.',
        'journal.group-only': 'A group-only elimination belongs in the consolidation workflow.',
        'reflection.unresolved':
          'Unknown or partial source reflection blocks application. A reviewer must resolve exact evidence.',
        'reflection.changed-since-plan':
          'Source reflection changed after this plan was created. Review the new decision and create a replacement plan.',
        'reflection.already-in-source':
          'Already included in the source; this journal contributes no additional adjustment.',
        'reflection.not-applicable': 'This journal is not applicable to the selected source.',
        'reflection.unsupported':
          'The current source reflection is unsupported; application is blocked.',
      } as Record<string, string>
    )[code] ??
    (code
      ? 'The server reports an eligibility blocker. Refresh and review its supporting context.'
      : 'Eligible in this exact membership review.')
  );
}

@Component({
  selector: 'audit-adjustment-plan-queue',
  imports: [RouterLink, MatButtonModule, ...SHARED],
  template: `
    <a routerLink="/app/accounting/journals">Back to adjustment journals</a>
    <audit-page-header
      title="Adjustment plan review"
      eyebrow="Exact source membership"
      description="Review persisted plans and the current eligibility of their journal revisions."
    />
    <button matButton (click)="ws.reload()" [disabled]="ws.loading()">Refresh plan queue</button>
    <audit-state [loading]="ws.loading()" [error]="ws.error()" label="scoped adjustment plans" />
    @if (ws.data(); as p) {
      <div
        class="table-scroll"
        tabindex="0"
        role="region"
        aria-label="Adjustment plan queue scroll area"
      >
        <table>
          <caption>
            Plans in your current client or exact engagement scope
          </caption>
          <thead>
            <tr>
              <th>Client</th>
              <th>Engagement</th>
              <th>Status</th>
              <th>Created</th>
              <th>Review</th>
            </tr>
          </thead>
          <tbody>
            @for (r of p.items; track r.id) {
              <tr>
                <td>{{ r.clientName }}</td>
                <td>{{ r.engagementName }}</td>
                <td><audit-status [value]="r.status" /></td>
                <td>{{ r.createdAt }} UTC</td>
                <td>
                  <a [routerLink]="['/app/accounting/adjustment-plans', r.id]">Review plan</a>
                </td>
              </tr>
            } @empty {
              <tr>
                <td colspan="5">No adjustment plans are available in your current scope.</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
      <nav aria-label="Adjustment plan pages" class="actions">
        <button matButton (click)="page.set(p.page - 1)" [disabled]="p.page === 0 || ws.loading()">
          Previous plans
        </button>
        <span>Page {{ p.page + 1 }}</span
        ><button matButton (click)="page.set(p.page + 1)" [disabled]="!p.hasMore || ws.loading()">
          Next plans
        </button>
      </nav>
    }
  `,
})
export class AdjustmentPlanQueue {
  readonly page = signal(0);
  readonly ws = inject(Api).resource(
    () => '/api/ui/accounting/adjustment-plans?page=' + this.page(),
    decodePlanQueue,
  );
}

@Component({
  selector: 'audit-adjustment-plan-review',
  imports: [RouterLink, MatButtonModule, ...SHARED],
  template: `
    <a routerLink="/app/accounting/adjustment-plans">Back to adjustment plan queue</a>
    <audit-page-header
      title="Adjustment plan eligibility"
      eyebrow="Current applicability and retained evidence"
      description="Review the exact source, planned journal revisions and current reflection decisions."
    />
    <button matButton (click)="ws.reload()" [disabled]="ws.loading()">
      Refresh eligibility review
    </button>
    <audit-state [loading]="ws.loading()" [error]="ws.error()" label="current adjustment plan" />
    @if (ws.data(); as v) {
      <section class="panel" aria-label="Exact adjustment plan context">
        <h2>Plan context <audit-status [value]="v.status" /></h2>
        <dl>
          <dt>Plan identity</dt>
          <dd>
            <code>{{ v.id }}</code>
          </dd>
          <dt>Source / revision</dt>
          <dd>
            <code>{{ v.datasetId }}</code> · {{ v.sourceRevision }}
          </dd>
          <dt>Source SHA-256</dt>
          <dd>
            <code>{{ v.sourceDigest }}</code>
          </dd>
          <dt>Period</dt>
          <dd>{{ v.periodCode || 'Unbound' }} · {{ v.periodStart }} to {{ v.periodEnd }}</dd>
          <dt>Book / basis / currency</dt>
          <dd>{{ v.bookCode || 'No explicit book' }} · {{ v.basis }} · {{ v.currency }}</dd>
          <dt>Created by</dt>
          <dd>
            <code>{{ v.createdByUserId }}</code> · {{ v.createdAt }} UTC
          </dd>
          <dt>Current membership digest</dt>
          <dd>
            <code>{{ v.membershipDigest }}</code>
          </dd>
        </dl>
        <p>
          This review is read-only. Source books, journal technical approval, plan calculation and
          financial-package release are separate operations.
        </p>
      </section>
      <section class="panel" aria-label="Current membership eligibility">
        <h2>Current membership eligibility</h2>
        <p role="status">
          {{ v.eligibleCount }} eligible · {{ v.excludedCount }} excluded ·
          {{ v.blockedCount }} blocked
        </p>
        @if (v.blockers.length) {
          <div role="alert">
            <h3>Application blockers</h3>
            <ul>
              @for (b of v.blockers; track b) {
                <li>{{ b }}</li>
              }
            </ul>
          </div>
        } @else {
          <p>
            No blockers were found in this bounded review. A new application still requires its own
            current server validation and explicit authorization.
          </p>
        }
      </section>
      @if (v.status === 'Finalized') {
        <section class="panel" aria-label="Retained plan calculation">
          <h2>Retained plan calculation</h2>
          <dl>
            <dt>Calculation SHA-256</dt>
            <dd>
              <code>{{ v.resultHash }}</code>
            </dd>
            <dt>Retained debits</dt>
            <dd class="number">{{ v.retainedDebits | money: 6 }} {{ v.currency }}</dd>
            <dt>Retained credits</dt>
            <dd class="number">{{ v.retainedCredits | money: 6 }} {{ v.currency }}</dd>
            <dt>Retained applied journals</dt>
            <dd>{{ v.retainedAppliedCount }}</dd>
          </dl>
          <p>
            These are persisted calculation results. They do not prove current package eligibility,
            release or external posting.
          </p>
        </section>
      }
      <section class="panel" aria-label="Plan journal membership">
        <h2>Planned and current journal evidence</h2>
        <div
          class="table-scroll"
          tabindex="0"
          role="region"
          aria-label="Plan membership scroll area"
        >
          <table>
            <caption>
              Exact selected journal revisions
            </caption>
            <thead>
              <tr>
                <th>Journal / revision</th>
                <th>Layer</th>
                <th>Technical state</th>
                <th>Planned reflection</th>
                <th>Current reflection</th>
                <th>Eligibility / reason</th>
                <th>Reviewer evidence</th>
              </tr>
            </thead>
            <tbody>
              @for (
                r of v.journals;
                track r.journalNumber + ':' + r.journalRevision + ':' + r.layer
              ) {
                <tr>
                  <td>
                    @if (r.journalId) {
                      <a [routerLink]="['/app/accounting/journals', r.journalId]"
                        >{{ r.journalNumber }} · {{ r.journalRevision }}</a
                      >
                    } @else {
                      {{ r.journalNumber }} · {{ r.journalRevision }}
                    }
                  </td>
                  <td>{{ r.layer }}</td>
                  <td><audit-status [value]="r.technicalStatus" /></td>
                  <td>{{ r.plannedReflectionState }}</td>
                  <td>{{ r.reflectionState }}</td>
                  <td>
                    <audit-status [value]="r.classification" />
                    <p>{{ reason(r.reasonCode) }}</p>
                  </td>
                  <td>
                    {{ r.evidence || 'No current reviewer evidence' }}
                    @if (r.reviewedByUserId) {
                      <p>
                        <code>{{ r.reviewedByUserId }}</code> · {{ r.reviewedAt }} UTC
                      </p>
                    }
                  </td>
                </tr>
              } @empty {
                <tr>
                  <td colspan="7">No journals were selected on this membership page.</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <nav aria-label="Plan membership pages" class="actions">
          <button
            matButton
            (click)="page.set(v.page - 1)"
            [disabled]="v.page === 0 || ws.loading()"
          >
            Previous membership
          </button>
          <span>Page {{ v.page + 1 }}</span
          ><button matButton (click)="page.set(v.page + 1)" [disabled]="!v.hasMore || ws.loading()">
            Next membership
          </button>
        </nav>
      </section>
    }
  `,
})
export class AdjustmentPlanReview {
  private readonly id = routeGuid();
  readonly page = signal(0);
  readonly ws = inject(Api).resource(
    () =>
      this.id() ? `/api/ui/accounting/adjustment-plans/${this.id()}?page=${this.page()}` : null,
    decodePlanReview,
  );
  readonly reason = eligibilityReason;
}
