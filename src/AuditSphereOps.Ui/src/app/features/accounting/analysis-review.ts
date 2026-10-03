import { Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, routeGuid } from '../../core/api';
import { arr, bool, dec, guid, instant, nat, nullable, obj, oneOf, sha256, str } from '../../core/decode';
import { SHARED } from '../../core/ui';

const kinds = ['ECL', 'INVENTORY', 'SPECIALIST', 'ANALYTICAL', 'JOURNAL_RISK'] as const;
const amount = obj({ key: str(100), label: str(200), value: nullable(dec), unit: oneOf('REPORTING_CURRENCY', 'RATIO', 'QUANTITY', 'SCORE') });
const detail = obj({ key: str(100), label: str(200), value: str(64000) });
const link = obj({ id: guid, resultId: guid, workpaperId: guid, status: str(100), inputGeneration: nat,
  isCurrentReviewedResult: bool, linkedByUserId: guid, linkedAt: instant });
const source = obj({ kind: oneOf('GENERATION_BOUND', 'UNAVAILABLE', 'TRIAL_BALANCE', 'GENERAL_LEDGER'), id: nullable(guid),
  reconciliationId: nullable(guid), retainedDigest: nullable(sha256), currentDigest: nullable(sha256), currentChecksPass: bool, description: str(2000) });
const view = obj({ id: guid, kind: oneOf(...kinds), clientId: guid, clientName: str(400), engagementId: guid, engagementName: str(200),
  periodId: guid, periodCode: str(100), basis: str(100), currency: str(10), area: str(100), status: str(100), version: nullable(nat),
  inputGeneration: nullable(nat), currentGeneration: nullable(nat), inputsCurrent: bool, source, assumptionsDigest: nullable(sha256),
  replayDigest: nullable(sha256), replayMatchesInputs: nullable(bool), proposedJournalId: nullable(guid), createdByUserId: nullable(guid), createdAt: instant,
  reviewedByUserId: nullable(guid), reviewedAt: nullable(instant), amounts: arr(amount, 40), details: arr(detail, 40), blockers: arr(str(2000), 20),
  links: arr(link, 25), linkCount: nat, page: nat, hasMore: bool, hasCurrentReviewedProcedure: bool, reviewBasis: sha256 });

export function decodeAnalysisReview(raw: unknown, path = 'response') {
  const r = view(raw, path);
  if (!r.amounts.length || r.linkCount > 500 || r.page >= 20 ||
    r.links.length !== Math.min(25, Math.max(0, r.linkCount - r.page * 25)) || r.hasMore !== (r.linkCount > (r.page + 1) * 25) ||
    new Set(r.links.map(x => x.id)).size !== r.links.length || new Set(r.amounts.map(x => x.key)).size !== r.amounts.length ||
    new Set(r.details.map(x => x.key)).size !== r.details.length ||
    (!r.linkCount && r.hasCurrentReviewedProcedure) || (r.links.some(x => x.isCurrentReviewedResult) && !r.hasCurrentReviewedProcedure) ||
    r.links.some(x => x.isCurrentReviewedResult && (x.status !== 'REVIEWED' || x.inputGeneration !== r.currentGeneration)) ||
    (r.inputsCurrent && (!r.source.currentChecksPass || !r.inputGeneration || r.inputGeneration !== r.currentGeneration || r.status === 'STALE')) ||
    (r.source.currentChecksPass && (r.source.kind === 'UNAVAILABLE' || !r.inputGeneration || r.inputGeneration !== r.currentGeneration ||
      (r.source.kind !== 'GENERATION_BOUND' && (!r.source.id || !r.source.retainedDigest || r.source.retainedDigest !== r.source.currentDigest)))) ||
    (r.kind === 'JOURNAL_RISK' && (r.inputGeneration !== null || r.inputsCurrent || r.source.currentChecksPass || r.source.retainedDigest !== null)) ||
    (r.kind === 'ANALYTICAL' && r.replayMatchesInputs === null) ||
    (r.kind !== 'ANALYTICAL' && (r.replayMatchesInputs !== null || r.replayDigest !== null)) ||
    (r.replayMatchesInputs && !r.replayDigest) ||
    (r.inputsCurrent && r.kind === 'ANALYTICAL' && !r.replayMatchesInputs)) throw new Error('Unsupported accounting evidence context');
  return r;
}

@Component({ selector: 'audit-analysis-review', imports: [RouterLink, MatButtonModule, ...SHARED], template: `
    <a routerLink="/app/accounting/evidence">← Back to accounting evidence</a>
    <audit-page-header title="Analysis evidence" eyebrow="Source-linked accounting review"
      description="Inspect retained inputs, methods, rationale and human review separately from current source verification. This read-only view does not calculate, approve or change client books." />
    @if (!kind() || !id()) { <p role="alert">This accounting evidence identity is unsupported.</p> }
    <button matButton="outlined" (click)="data.reload()">Refresh current checks</button>
    <audit-state [loading]="data.loading()" [error]="data.error()" label="scoped analysis evidence" />
    @if (data.data(); as r) {
      <section class="panel"><h2>Exact reporting context</h2><p>{{ r.kind }} · {{ r.area }} · {{ r.clientName }} · {{ r.engagementName }} · {{ r.periodCode }} · {{ r.basis }} · {{ r.currency }}</p>
        <p>Retained status <audit-status [value]="r.status" /> @if (r.version) { · revision {{ r.version }} }</p>
        <p>Current input checks <audit-status [value]="r.inputsCurrent ? 'CURRENT_INPUTS' : 'BLOCKED'" /> ·
          Reviewed procedure <audit-status [value]="r.hasCurrentReviewedProcedure ? 'CURRENT_REVIEWED_RESULT' : 'REVIEW_REQUIRED'" /></p>
        <p>A retained approval alone does not establish current source or procedure verification.</p></section>
      @if (r.blockers.length) { <section class="panel" role="status"><h2>Current verification blockers</h2><ul>@for (b of r.blockers; track b) { <li>{{ b }}</li> }</ul></section> }
      <section class="panel"><h2>Source and input provenance</h2><p>{{ r.source.description }}</p>
        <dl><dt>Provenance type</dt><dd>{{ r.source.kind }}</dd><dt>Input generation</dt><dd>{{ r.inputGeneration ?? 'Not retained' }} retained / {{ r.currentGeneration ?? 'Unavailable' }} current</dd>
          <dt>Retained exact source digest</dt><dd>{{ r.source.retainedDigest ?? 'Not retained' }}</dd><dt>Current exact source digest</dt><dd>{{ r.source.currentDigest ?? 'Unavailable' }}</dd>
          <dt>Declared assumptions digest</dt><dd>{{ r.assumptionsDigest ?? 'Not retained' }}</dd></dl><p>The assumptions digest is retained evidence identity; this inspection does not independently verify external assumptions.</p>
        <div class="actions">@if (r.source.reconciliationId) { <a [routerLink]="['/app/accounting/reconciliations', r.source.reconciliationId]">Inspect exact reconciliation</a> }
          @if (r.source.id) { <a [routerLink]="[r.source.kind === 'TRIAL_BALANCE' ? '/app/accounting/sources' : '/app/accounting/gl-sources', r.source.id]">Inspect exact source</a> }
          @if (r.proposedJournalId) { <a [routerLink]="['/app/accounting/journals', r.proposedJournalId]">Inspect proposed adjustment</a> }</div>
      </section>
      <section class="panel"><h2>Retained amounts and inputs</h2><p>Exact decimal values remain historical evidence when current checks are blocked. Missing values remain unavailable.</p>
        <dl>@for (a of r.amounts; track a.key) { <dt>{{ a.label }}</dt><dd>{{ a.value ?? 'Unavailable' }} {{ a.unit === 'REPORTING_CURRENCY' ? r.currency : a.unit }}</dd> }</dl></section>
      <section class="panel"><h2>Methodology, evidence and rationale</h2><dl>@for (d of r.details; track d.key) { <dt>{{ d.label }}</dt><dd>{{ d.value || 'Not retained' }}</dd> }</dl>
        @if (r.kind === 'ANALYTICAL') { <h3>Retained replay snapshot</h3><p>{{ r.replayMatchesInputs ? 'Digest and typed inputs match' : 'Digest or typed inputs do not match' }}</p><p>{{ r.replayDigest ?? 'Not retained' }}</p> }</section>
      <section class="panel"><h2>Audit procedure links</h2><p role="status">{{ r.linkCount }} complete links · page {{ r.page + 1 }}</p>
        @if (!r.linkCount) { <p>No submitted audit-procedure result is linked. A free-text reference alone is not reviewed audit evidence.</p> } @else {
          <div class="table-scroll"><table><thead><tr><th>Result</th><th>Retained state</th><th>Current review</th><th>Link evidence</th></tr></thead><tbody>
            @for (l of r.links; track l.id) { <tr><td><a [routerLink]="['/app/audit/workpapers', l.workpaperId]">Inspect linked workpaper</a><small>{{ l.resultId }}</small></td>
              <td>{{ l.status }} · generation {{ l.inputGeneration }}</td><td>{{ l.isCurrentReviewedResult ? 'Independently reviewed; current generation' : 'Current review not verified' }}</td>
              <td>{{ l.linkedByUserId }}<small>{{ l.linkedAt }}</small></td></tr> }</tbody></table></div>
          <nav class="actions" aria-label="Audit procedure link pages"><button matButton="outlined" (click)="page.set(r.page - 1)" [disabled]="!r.page">Previous</button>
            <button matButton="outlined" (click)="page.set(r.page + 1)" [disabled]="!r.hasMore">Next</button></nav> }
      </section>
      <section class="panel"><h2>Retained preparation and review</h2><dl><dt>Prepared by</dt><dd>{{ r.createdByUserId ?? 'Not retained' }}</dd><dt>Prepared at</dt><dd>{{ r.createdAt }}</dd>
        <dt>Reviewed by</dt><dd>{{ r.reviewedByUserId ?? 'Not reviewed' }}</dd><dt>Reviewed at</dt><dd>{{ r.reviewedAt ?? 'Not reviewed' }}</dd></dl><p>Inspection basis: {{ r.reviewBasis }}</p></section>
    }
  `, styles: [`.panel { overflow-wrap: anywhere; } dd { margin-inline-start: 0; white-space: pre-wrap; } dt { font-weight: 600; margin-block-start: .5rem; }`] })
export class AnalysisReview {
  private readonly api = inject(Api);
  private readonly route = inject(ActivatedRoute);
  private readonly params = toSignal(this.route.paramMap, { initialValue: this.route.snapshot?.paramMap });
  readonly id = routeGuid();
  readonly kind = computed(() => { const value = this.params()?.get('kind') ?? ''; return kinds.find(x => x === value) ?? null; });
  readonly page = signal(0);
  readonly data = this.api.resource(() => this.id() && this.kind() ? '/api/ui/accounting/evidence/' + this.kind() + '/' + this.id() + '?page=' + this.page() : null,
    (raw, path) => { const r = decodeAnalysisReview(raw, path); if (r.id !== this.id() || r.kind !== this.kind() || r.page !== this.page()) throw new Error('Unsupported accounting evidence target'); return r; },
    'This accounting evidence is unavailable in the current scope.');
  constructor() { effect(() => { this.id(); this.kind(); this.page.set(0); }); }
}
