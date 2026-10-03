import { Component, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, routeGuid } from '../../core/api';
import { arr, bool, date, dec, guid, instant, nat, nullable, obj, oneOf, sha256, str } from '../../core/decode';
import { SHARED } from '../../core/ui';

const proof = obj({ id: guid, formulaVersion: str(100), sourceTotal: dec, glTotal: dec, itemsSignedTotal: dec,
  residual: dec, isReconciled: bool, itemCount: nat, itemManifestDigest: nullable(sha256), sourceHash: nullable(sha256),
  inputGeneration: nat, createdByUserId: guid, createdAt: instant, matchesCurrentInputs: bool });
const item = obj({ id: guid, stableItemId: str(200), signedAmount: dec, currency: str(10), itemDate: nullable(date),
  reason: str(4000), evidenceReference: str(4000), disposition: str(100), dateBasis: str(100), agingBucket: str(100),
  isCredit: bool, settlementDate: nullable(date), settlementReference: str(4000) });
const review = obj({ id: guid, clientId: guid, clientName: str(400), engagementId: guid, engagementName: str(200),
  periodId: guid, periodCode: str(100), bookId: nullable(guid), bookCode: nullable(str(100)), basis: str(100), currency: str(10),
  area: str(100), accountSelection: str(20000), asOfDate: date, agingBasis: str(100), agingBucketRuleVersion: str(100),
  status: str(100), revision: nat, supersedesReconciliationId: nullable(guid), createdByUserId: guid, createdAt: instant,
  reviewedByUserId: nullable(guid), reviewedAt: nullable(instant), sourceKind: oneOf('UNBOUND', 'TRIAL_BALANCE', 'GENERAL_LEDGER'),
  sourceId: nullable(guid), sourceHash: nullable(sha256), currentSourceHash: nullable(sha256), sourceAvailable: bool,
  inputGeneration: nat, currentGeneration: nullable(nat), isStale: bool, canReuseApprovedEvidence: bool,
  sourceTotal: dec, glTotal: dec, residual: dec, latestProof: nullable(proof), blockers: arr(str(2000), 20),
  items: arr(item, 25), itemCount: nat, page: nat, hasMore: bool, reviewBasis: sha256 });
export function decodeReconciliation(raw: unknown, path = 'response') {
  const r = review(raw, path);
  if (r.revision < 1 || r.inputGeneration < 1 || r.itemCount > 20000 || r.page >= 800 ||
    r.items.length !== Math.min(25, Math.max(0, r.itemCount - r.page * 25)) || r.hasMore !== (r.itemCount > (r.page + 1) * 25) ||
    new Set(r.items.map(x => x.id)).size !== r.items.length ||
    (r.sourceAvailable && (!r.sourceId || !r.currentSourceHash || r.sourceKind === 'UNBOUND')) ||
    (r.latestProof?.matchesCurrentInputs && (r.latestProof.itemCount !== r.itemCount ||
      r.latestProof.sourceHash !== r.sourceHash || r.latestProof.inputGeneration !== r.inputGeneration ||
      r.latestProof.sourceTotal !== r.sourceTotal || r.latestProof.glTotal !== r.glTotal || r.latestProof.residual !== r.residual ||
      !r.sourceAvailable || r.sourceHash !== r.currentSourceHash || r.inputGeneration !== r.currentGeneration)) ||
    (r.latestProof?.isReconciled && !/^-?0+(?:\.0+)?$/.test(r.latestProof.residual)) ||
    (r.canReuseApprovedEvidence && (r.status !== 'APPROVED' || r.isStale || r.blockers.length ||
      !r.latestProof?.matchesCurrentInputs || !r.latestProof.isReconciled || !r.reviewedByUserId || !r.reviewedAt ||
      r.reviewedByUserId === r.createdByUserId || !r.sourceAvailable || r.sourceHash !== r.currentSourceHash ||
      r.inputGeneration !== r.currentGeneration))) throw new Error('Unsupported reconciliation context');
  return r;
}

@Component({ selector: 'audit-reconciliation-review', imports: [RouterLink, MatButtonModule, ...SHARED],
  template: `
    <a routerLink="/app/accounting/evidence">← Back to accounting evidence</a>
    <audit-page-header title="Reconciliation evidence" eyebrow="Source-linked accounting review"
      description="Inspect retained amounts and the complete item proof against the current exact source. This inspection does not calculate, approve or change client books." />
    <button matButton="outlined" (click)="data.reload()">Refresh current eligibility</button>
    <audit-state [loading]="data.loading()" [error]="data.error()" label="scoped reconciliation evidence" />
    @if (data.data(); as r) {
      <section class="panel" aria-labelledby="context"><h2 id="context">Exact reporting context</h2>
        <p>{{ r.clientName }} · {{ r.engagementName }} · {{ r.periodCode }} · {{ r.bookCode ?? 'No reporting book' }} · {{ r.basis }} · {{ r.currency }}</p>
        <p>{{ r.area }} · as of {{ r.asOfDate }} · revision {{ r.revision }} · accounts {{ r.accountSelection }}</p>
        <p>Retained status <audit-status [value]="r.status" /> · Current eligibility <audit-status [value]="r.canReuseApprovedEvidence ? 'CURRENT_APPROVED' : r.isStale ? 'STALE' : 'REVIEW_REQUIRED'" /></p>
        @if (r.supersedesReconciliationId) { <a [routerLink]="['/app/accounting/reconciliations', r.supersedesReconciliationId]">Inspect prior reconciliation revision</a> }
      </section>
      @if (r.blockers.length) { <section class="panel" role="status"><h2>Evidence reuse blocked</h2><ul>@for (b of r.blockers; track b) { <li>{{ b }}</li> }</ul></section> }
      <section class="panel"><h2>Retained source and current source</h2>
        <dl><dt>Source kind</dt><dd>{{ r.sourceKind }}</dd><dt>Retained source digest</dt><dd class="digest">{{ r.sourceHash ?? 'Not retained' }}</dd>
          <dt>Current exact source digest</dt><dd class="digest">{{ r.currentSourceHash ?? 'Unavailable' }}</dd>
          <dt>Input generation</dt><dd>{{ r.inputGeneration }} retained / {{ r.currentGeneration ?? 'Unavailable' }} current</dd>
          <dt>Selected ageing policy</dt><dd>{{ r.agingBasis || 'Not applicable' }} · {{ r.agingBucketRuleVersion || 'Not applicable' }}</dd></dl>
        @if (r.sourceId) { <a [routerLink]="[r.sourceKind === 'TRIAL_BALANCE' ? '/app/accounting/sources' : '/app/accounting/gl-sources', r.sourceId]">Inspect exact source</a> }
      </section>
      <section class="panel"><h2>Retained reconciliation amounts</h2><p>These historical values remain visible when reuse is blocked.</p>
        <dl><dt>Source total</dt><dd>{{ r.sourceTotal }} {{ r.currency }}</dd><dt>General ledger total</dt><dd>{{ r.glTotal }} {{ r.currency }}</dd>
          <dt>Retained residual</dt><dd>{{ r.residual }} {{ r.currency }}</dd></dl>
      </section>
      <section class="panel"><h2>Latest retained item proof</h2>
        @if (r.latestProof; as p) { <p>{{ p.formulaVersion }} · {{ p.itemCount }} complete items · {{ p.matchesCurrentInputs ? 'Matches current inputs' : 'Does not match current inputs' }}</p>
          <dl><dt>Complete items signed total</dt><dd>{{ p.itemsSignedTotal }} {{ r.currency }}</dd><dt>Proof residual</dt><dd>{{ p.residual }} {{ r.currency }}</dd>
            <dt>Item manifest digest</dt><dd class="digest">{{ p.itemManifestDigest ?? 'Not retained' }}</dd><dt>Calculated by</dt><dd>{{ p.createdByUserId }}</dd><dt>Calculated at</dt><dd>{{ p.createdAt }}</dd></dl>
        } @else { <p>No calculated proof is retained. No zero result or approval is inferred.</p> }
      </section>
      <section class="panel"><h2>Reconciling items</h2><p role="status">{{ r.itemCount }} complete items · page {{ r.page + 1 }}</p>
        @if (!r.itemCount) { <p>No reconciling items are retained.</p> } @else {
          <div class="table-scroll"><table><thead><tr><th>Item</th><th>Signed amount</th><th>Date / ageing</th><th>Reason and evidence</th><th>Disposition / settlement</th></tr></thead>
            <tbody>@for (x of r.items; track x.id) { <tr><td>{{ x.stableItemId }}{{ x.isCredit ? ' · Credit' : '' }}</td><td>{{ x.signedAmount }} {{ x.currency }}</td>
              <td>{{ x.itemDate ?? 'Not retained' }}<small>{{ x.dateBasis }} · {{ x.agingBucket }}</small></td><td>{{ x.reason }}<small>{{ x.evidenceReference }}</small></td>
              <td>{{ x.disposition }}<small>{{ x.settlementDate ?? '' }} {{ x.settlementReference }}</small></td></tr> }</tbody></table></div>
          <nav class="actions" aria-label="Reconciling item pages"><button matButton="outlined" (click)="page.set(r.page - 1)" [disabled]="!r.page">Previous</button>
            <button matButton="outlined" (click)="page.set(r.page + 1)" [disabled]="!r.hasMore">Next</button></nav>
        }
      </section>
      <section class="panel"><h2>Retained preparation and review</h2><dl><dt>Prepared by</dt><dd>{{ r.createdByUserId }}</dd><dt>Prepared at</dt><dd>{{ r.createdAt }}</dd>
        <dt>Reviewed by</dt><dd>{{ r.reviewedByUserId ?? 'Not reviewed' }}</dd><dt>Reviewed at</dt><dd>{{ r.reviewedAt ?? 'Not reviewed' }}</dd></dl>
        <p class="digest">Inspection basis: {{ r.reviewBasis }}</p></section>
    }
  `, styles: [`.digest, .panel { overflow-wrap: anywhere; } dd { margin-inline-start: 0; } dt { font-weight: 600; margin-block-start: .5rem; }`],
})
export class ReconciliationReview {
  private readonly api = inject(Api);
  readonly id = routeGuid();
  readonly page = signal(0);
  readonly data = this.api.resource(() => { const id = this.id(); return id ? '/api/ui/accounting/reconciliations/' + id + '?page=' + this.page() : null; },
    (raw, path) => { const r = decodeReconciliation(raw, path); if (r.id !== this.id() || r.page !== this.page()) throw new Error('Unsupported reconciliation target'); return r; },
    'This reconciliation is unavailable in the current scope.');
  constructor() { effect(() => { this.id(); this.page.set(0); }); }
}
