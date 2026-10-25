import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { arr, bool, date, dec, DecodeError, Decoder, guid, instant, nat, nullable, obj, oneOf, str, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

export const PIPELINE_STAGES = ['DISCOVERY', 'PROPOSAL', 'NEGOTIATION', 'WON', 'LOST'] as const;
export type PipelineStageName = (typeof PIPELINE_STAGES)[number];
const STAGE_LABELS: Record<PipelineStageName, string> = {
  DISCOVERY: 'Discovery', PROPOSAL: 'Proposal', NEGOTIATION: 'Negotiation', WON: 'Won', LOST: 'Lost',
};
const PROPOSAL_STATUS_LABELS: Record<string, string> = {
  DRAFT: 'Draft', INTERNAL_REVIEW: 'In internal review', SENT: 'Sent', ACCEPTED: 'Accepted', DECLINED: 'Declined', SUPERSEDED: 'Superseded',
};
const currencyCode: Decoder<string> = (v, p) => {
  if (typeof v === 'string' && /^[A-Z]{3}$/.test(v)) return v;
  throw new DecodeError(`${p}: expected currency code`);
};
const card = obj({
  id: guid, leadId: guid, leadName: str(200), clientId: nullable(guid), clientName: nullable(str(300)), serviceRoute: str(100),
  periodStart: date, periodEnd: date, expectedFee: dec, currency: currencyCode, probability: nullable(dec),
  ownerUserId: nullable(guid), ownerName: nullable(str(200)), ownerState: oneOf('UNASSIGNED', 'ACTIVE', 'DISABLED', 'MISSING'),
  nextAction: nullable(text), createdAt: instant, proposalId: nullable(guid), proposalRevision: nullable(str(20)),
  proposalStatus: nullable(oneOf('DRAFT', 'INTERNAL_REVIEW', 'SENT', 'ACCEPTED', 'DECLINED', 'SUPERSEDED')), proposalRevisions: nat,
});
export type PipelineCard = ReturnType<typeof card>;
const stage = obj({
  stage: oneOf(...PIPELINE_STAGES), count: nat, totals: arr(obj({ currency: currencyCode, count: nat, expectedFee: dec }), 200),
  unavailableCurrencyCount: nat, items: arr(card, 50), hasMore: bool,
});
export type PipelineStage = ReturnType<typeof stage>;
const pipeline = obj({ stages: arr(stage, PIPELINE_STAGES.length), pageSize: nat });

/** Refuses a board whose cards, counts and totals do not describe one coherent cohort. */
export function decodePipeline(value: unknown, path: string) {
  const result = pipeline(value, path);
  if (result.pageSize < 1 || result.pageSize > 50 || result.stages.some(s =>
      s.items.length > result.pageSize || s.items.length > s.count || (s.hasMore && s.items.length !== result.pageSize) ||
      s.totals.reduce((sum, t) => sum + t.count, 0) + s.unavailableCurrencyCount !== s.count ||
      new Set(s.totals.map(t => t.currency)).size !== s.totals.length))
    throw new DecodeError('Unsupported pipeline page.');
  return result;
}

interface Filters { owner: string; service: string; currency: string }
const NO_FILTERS: Filters = { owner: '', service: '', currency: '' };

@Component({
  selector: 'audit-commercial-pipeline',
  imports: [FormsModule, RouterLink, MatButtonModule, ...SHARED],
  styles: [`
    .pipeline-filters { display: flex; flex-wrap: wrap; gap: 12px; align-items: end; }
    .pipeline-scope, .pipeline-window { color: var(--as-text-muted); }
    .pipeline-board { display: grid; grid-template-columns: repeat(5, minmax(220px, 1fr)); gap: 12px; overflow-x: auto; align-items: start; }
    .pipeline-stage { border: 1px solid var(--as-control-border); border-radius: var(--as-radius-md); padding: 12px; min-width: 0; }
    .pipeline-stage h2 { display: flex; justify-content: space-between; gap: 8px; margin: 0 0 8px; font-size: 1rem; }
    .pipeline-totals, .pipeline-cards { list-style: none; margin: 0; padding: 0; display: grid; gap: 8px; }
    .pipeline-card { border: 1px solid var(--as-control-border); border-radius: var(--as-radius-md); padding: 8px; overflow-wrap: anywhere; }
    .pipeline-card h3 { margin: 0 0 4px; font-size: 0.95rem; }
    .pipeline-card p { margin: 2px 0; }
    @media (max-width: 720px) { .pipeline-board { grid-template-columns: 1fr; } }
  `],
  template: `
    <audit-page-header title="Opportunity pipeline" eyebrow="Commercial"
      description="Opportunities grouped by their current stage. This view is read-only: stages change only through proposals, client responses and conversion." />
    <form class="pipeline-filters" aria-label="Filter opportunities" (submit)="$event.preventDefault(); apply()">
      <label>Owner
        <select name="owner" [(ngModel)]="draft.owner">
          <option value="">Any owner</option><option value="me">My opportunities</option><option value="unassigned">Unassigned</option>
        </select>
      </label>
      <label>Service route <input name="service" [(ngModel)]="draft.service" maxlength="100" /></label>
      <label>Currency <input name="currency" [(ngModel)]="draft.currency" maxlength="3" size="4" /></label>
      <button matButton="outlined" type="submit">Apply filters</button>
      <button matButton type="button" (click)="clearFilters()" [disabled]="!hasFilters()">Clear filters</button>
    </form>
    <p class="pipeline-scope">Counts and expected-fee totals cover every opportunity matching the filters. Each currency is totalled
      separately; nothing is converted or weighted by probability. Cards load one page at a time.</p>
    <audit-state [loading]="board.loading()" [error]="board.error()" label="opportunity pipeline" />
    @if (moreError()) { <p role="alert">{{ moreError() }}</p> }
    @if (columns(); as stages) {
      <div class="pipeline-board">
        @for (column of stages; track column.stage) {
          <section class="pipeline-stage" [attr.aria-labelledby]="'pipeline-' + column.stage">
            <h2 [id]="'pipeline-' + column.stage"><span>{{ stageLabel(column.stage) }}</span><span>{{ column.count }}</span></h2>
            <ul class="pipeline-totals" [attr.aria-label]="stageLabel(column.stage) + ' expected fees by currency'">
              @for (total of column.totals; track total.currency) {
                <li>{{ total.expectedFee | money }} {{ total.currency }} · {{ total.count }} {{ total.count === 1 ? 'opportunity' : 'opportunities' }}</li>
              } @empty { <li>No opportunities</li> }
              @if (column.unavailableCurrencyCount > 0) { <li>{{ column.unavailableCurrencyCount }} with an unavailable currency, not totalled</li> }
            </ul>
            @if (column.count > 0) { <p class="pipeline-window">Showing {{ column.items.length }} of {{ column.count }}</p> }
            <ul class="pipeline-cards" [attr.aria-label]="stageLabel(column.stage) + ' opportunities'">
              @for (item of column.items; track item.id) {
                <li><article class="pipeline-card">
                  <h3><a [routerLink]="['/app/practice/leads', item.leadId]">{{ item.leadName }}</a></h3>
                  <p>{{ item.serviceRoute }} · {{ item.periodStart }} to {{ item.periodEnd }}</p>
                  @if (item.clientName) { <p>Client: {{ item.clientName }}</p> }
                  <p>Expected fee {{ item.expectedFee | money }} {{ item.currency }}@if (item.probability !== null) { · recorded probability {{ plain(item.probability) }}% }</p>
                  <p>Owner: {{ ownerLabel(item) }}</p>
                  @if (item.nextAction) { <p>Next action: {{ item.nextAction }}</p> }
                  @if (item.proposalId) {
                    <p><a [routerLink]="['/app/practice/proposals', item.proposalId]">Proposal revision {{ item.proposalRevision }}</a>
                      · {{ proposalLabel(item.proposalStatus) }}@if (item.proposalRevisions > 1) { · {{ item.proposalRevisions }} revisions }</p>
                  } @else { <p>No proposal yet</p> }
                </article></li>
              }
            </ul>
            @if (column.hasMore) {
              <button matButton type="button" (click)="loadMore(column)" [disabled]="loadingMore() !== null">
                {{ loadingMore() === column.stage ? 'Loading…' : 'Show more ' + stageLabel(column.stage).toLowerCase() }}</button>
            }
          </section>
        }
      </div>
    }
  `,
})
export class CommercialPipeline {
  private readonly api = inject(Api);
  draft: Filters = { ...NO_FILTERS };
  private readonly filters = signal<Filters>({ ...NO_FILTERS });
  readonly board = this.api.resource(() => {
    const params = new URLSearchParams();
    const { owner, service, currency } = this.filters();
    if (owner) params.set('owner', owner);
    if (service) params.set('service', service);
    if (currency) params.set('currency', currency);
    const query = params.toString();
    return '/api/ui/pipeline' + (query ? '?' + query : '');
  }, decodePipeline, 'The pipeline requires a firm-wide Administrator, Partner, Manager or Relationship Manager assignment.');
  /** Extra pages belong to the board observation they continue; a reload or filter change discards them. */
  private readonly more = signal<{ board: unknown; pages: Partial<Record<PipelineStageName, { items: PipelineCard[]; hasMore: boolean }>> }>({ board: null, pages: {} });
  readonly loadingMore = signal<PipelineStageName | null>(null);
  readonly moreError = signal('');
  readonly columns = computed(() => {
    const data = this.board.data();
    if (!data) return null;
    const extra = this.more().board === data ? this.more().pages : {};
    return data.stages.map(column => {
      const page = extra[column.stage];
      return page ? { ...column, items: [...column.items, ...page.items], hasMore: page.hasMore } : column;
    });
  });
  readonly hasFilters = computed(() => Object.values(this.filters()).some(Boolean));

  apply(): void {
    this.moreError.set('');
    this.filters.set({ owner: this.draft.owner, service: this.draft.service.trim(), currency: this.draft.currency.trim().toUpperCase() });
  }
  clearFilters(): void { this.draft = { ...NO_FILTERS }; this.apply(); }
  stageLabel(value: PipelineStageName): string { return STAGE_LABELS[value]; }
  proposalLabel(value: string | null): string { return value ? PROPOSAL_STATUS_LABELS[value] ?? value : ''; }
  plain(value: string): string { return value.includes('.') ? value.replace(/\.?0+$/, '') : value; }
  ownerLabel(item: PipelineCard): string {
    switch (item.ownerState) {
      case 'UNASSIGNED': return 'Unassigned';
      case 'DISABLED': return `${item.ownerName ?? 'Unknown owner'} (disabled)`;
      case 'MISSING': return 'Unknown owner';
      default: return item.ownerName ?? 'Unknown owner';
    }
  }

  async loadMore(column: PipelineStage): Promise<void> {
    const board = this.board.data();
    const last = column.items.at(-1);
    if (!board || !last || this.loadingMore()) return;
    this.loadingMore.set(column.stage);
    this.moreError.set('');
    const params = new URLSearchParams({ stage: column.stage, beforeCreatedAt: last.createdAt, beforeId: last.id });
    const { owner, service, currency } = this.filters();
    if (owner) params.set('owner', owner);
    if (service) params.set('service', service);
    if (currency) params.set('currency', currency);
    try {
      const next = await this.api.get('/api/ui/pipeline?' + params.toString(), decodePipeline);
      const page = next.stages.length === 1 && next.stages[0].stage === column.stage ? next.stages[0] : null;
      if (this.board.data() !== board) return;
      if (!page) throw new Error('Unsupported response.');
      const pages = this.more().board === board ? this.more().pages : {};
      const known = new Set(column.items.map(item => item.id));
      this.more.set({ board, pages: { ...pages, [column.stage]: {
        items: [...(pages[column.stage]?.items ?? []), ...page.items.filter(item => !known.has(item.id))], hasMore: page.hasMore } } });
    } catch (error) {
      // A continuation that no longer matches the filtered stage is refused; reload rather than mix two observations.
      this.moreError.set((error instanceof Error ? error.message : 'Temporarily unavailable.') + ' The board was reloaded.');
      this.board.reload();
    } finally {
      this.loadingMore.set(null);
    }
  }
}
