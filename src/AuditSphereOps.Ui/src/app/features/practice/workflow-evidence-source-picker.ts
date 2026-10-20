import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { guidPattern } from '../../core/contracts';
import { arr, bool, guid, nat, nullable, obj, oneOf, text, type Decoder } from '../../core/decode';
import { SessionService } from '../../core/session';

export type WorkflowEvidenceSourceKind = 'PBC_ACCEPTED_OR_CLOSED' | 'CURRENT_PROCEDURE_REVIEWED';

const PAGE_SIZE = 25;
const MAX_PAGE = 10000;
const MAX_QUERY_LENGTH = 120;
const kindDecoder = oneOf('PBC_ACCEPTED_OR_CLOSED', 'CURRENT_PROCEDURE_REVIEWED');
const sourceRevision: Decoder<string> = (value, path) => {
  if (typeof value !== 'string' || !/^[1-9][0-9]{0,18}$/.test(value) || BigInt(value) > 9223372036854775807n)
    throw new Error(`${path}: expected canonical positive Int64 revision string`);
  return value;
};
const evidenceSource = obj({ id: guid, label: text, state: text, revision: nullable(sourceRevision) });
const evidenceSourcePage = obj({ engagementId: guid, kind: kindDecoder, page: nat, pageSize: nat,
  hasMore: bool, items: arr(evidenceSource, PAGE_SIZE), selected: nullable(evidenceSource) });
type EvidenceSource = ReturnType<typeof evidenceSource>;
type EvidenceSourcePage = ReturnType<typeof evidenceSourcePage>;

@Component({
  selector: 'audit-workflow-evidence-source-picker',
  imports: [MatButtonModule],
  template: `
    <section class="source-picker" [attr.aria-label]="pickerLabel()">
      <button matButton type="button" [attr.aria-label]="disclosureLabel()"
        [attr.aria-expanded]="opened()" [attr.aria-controls]="pickerId() + '-panel'"
        [disabled]="interactionDisabled()" (click)="toggleDisclosure()">
        @if (opened()) { Close evidence source search } @else { Open evidence source search }
      </button>
      <div [id]="pickerId() + '-panel'" [hidden]="!opened()">
      @if (opened()) {
        <p>Recorded source state is informational; selecting a source does not approve, accept, or review it.</p>
        <form (submit)="search($event)">
          <label [for]="pickerId() + '-query'">{{ searchLabel() }}</label>
          <input [id]="pickerId() + '-query'" type="search" [value]="searchDraft()"
            (input)="setSearchDraft($any($event.target).value)" [attr.maxlength]="maxQueryLength"
            [disabled]="interactionDisabled() || sources.loading()" />
          <button matButton type="submit" [attr.aria-label]="searchActionLabel()"
            [disabled]="interactionDisabled() || sources.loading()">Search sources</button>
        </form>

        @if (selectedId()) {
          @if (sources.data(); as page) {
            @if (page.selected; as selected) {
              <p class="current-selection"><strong>Current selection:</strong> {{ selected.label }} · Recorded state {{ selected.state }}@if (selected.revision !== null) { · Revision {{ selected.revision }} }</p>
            } @else {
              <p class="current-selection"><strong>Current source reference:</strong> <code>{{ selectedId() }}</code></p>
            }
          } @else {
            <p class="current-selection"><strong>Current source reference:</strong> <code>{{ selectedId() }}</code></p>
          }
        }

        @if (sources.loading()) { <p role="status">Loading evidence sources…</p> }
        @if (sources.error()) { <p role="alert">{{ sources.error() }}</p> }
        @if (sources.data(); as result) {
          <ul [attr.aria-label]="resultsLabel()">
            @for (item of result.items; track item.id) {
              <li><button matButton type="button" [disabled]="interactionDisabled() || sources.loading()"
                [attr.aria-pressed]="item.id.toLowerCase() === selectedId()?.toLowerCase()" [attr.aria-label]="selectLabel(item)"
                (click)="select(item)">{{ item.label }} · ID <code>{{ item.id }}</code> · Recorded state {{ item.state }}@if (item.revision !== null) { · Revision {{ item.revision }} }</button></li>
            } @empty {
              <li>No matching sources on this page.</li>
            }
          </ul>
          <nav [attr.aria-label]="pageLabel()">
            <button matButton type="button" [attr.aria-label]="previousPageLabel()" (click)="previousPage()"
              [disabled]="interactionDisabled() || sources.loading() || result.page === 0">Previous sources</button>
            <span aria-live="polite">Page {{ result.page + 1 }}</span>
            <button matButton type="button" [attr.aria-label]="nextPageLabel()" (click)="nextPage()"
              [disabled]="interactionDisabled() || sources.loading() || !result.hasMore || result.page >= maxPage">Next sources</button>
          </nav>
        }
      }
      </div>
    </section>
  `,
  styles: `:host { display: block; }
    form { display: flex; flex-wrap: wrap; align-items: end; gap: .5rem; margin-block: .75rem; }
    form label { display: grid; flex: 1 1 16rem; gap: .35rem; }
    input { min-width: 0; max-width: 100%; min-height: 44px; }
    button { max-width: 100%; min-height: 44px; height: auto; white-space: normal; overflow-wrap: anywhere; text-align: start; }
    ul { padding-inline-start: 1.25rem; }
    li { margin-block: .35rem; overflow-wrap: anywhere; }
    nav { display: flex; flex-wrap: wrap; align-items: center; gap: .75rem; margin-block: .5rem; }
    .current-selection { overflow-wrap: anywhere; }`,
})
export class WorkflowEvidenceSourcePicker {
  readonly engagementId = input.required<string>();
  readonly kind = input.required<WorkflowEvidenceSourceKind>();
  readonly stepTitle = input.required<string>();
  readonly requirementKey = input.required<string>();
  readonly pickerId = input.required<string>();
  readonly selectedId = input<string | null>(null);
  readonly disabled = input(false);
  readonly sourceSelected = output<string>();
  readonly maxQueryLength = MAX_QUERY_LENGTH;
  readonly maxPage = MAX_PAGE;
  readonly opened = signal(false);
  readonly searchDraft = signal('');
  readonly query = signal('');
  readonly page = signal(0);
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  readonly interactionDisabled = computed(() => this.disabled() || this.session.current()?.staff !== true);
  readonly sources = this.api.resource(() => this.url(), (value, path) => this.decodePage(value, path),
    'Evidence sources are unavailable in this engagement scope.');
  private activeContext: string | null = null;
  private requestedContext = '';
  private loadedRequestContext = '';

  constructor() {
    effect(() => {
      const nextContext = this.contextKey();
      untracked(() => {
        if (this.activeContext === null) {
          this.activeContext = nextContext;
          return;
        }
        if (this.activeContext === nextContext) return;
        this.activeContext = nextContext;
        this.loadedRequestContext = '';
        this.opened.set(false);
        this.page.set(0);
        this.query.set('');
        this.searchDraft.set('');
      });
    });
  }

  search(event: Event): void {
    event.preventDefault();
    if (this.interactionDisabled()) return;
    const nextQuery = this.searchDraft().trim().slice(0, MAX_QUERY_LENGTH);
    const unchangedFirstPage = nextQuery === this.query() && this.page() === 0;
    this.query.set(nextQuery);
    this.page.set(0);
    if (unchangedFirstPage) this.sources.reload();
  }

  toggleDisclosure(): void {
    if (!this.interactionDisabled()) this.opened.update(value => !value);
  }

  setSearchDraft(value: string): void {
    this.searchDraft.set(value.slice(0, MAX_QUERY_LENGTH));
  }

  previousPage(): void {
    if (this.interactionDisabled() || this.sources.loading() || this.page() === 0) return;
    this.page.update(current => Math.max(0, Math.min(MAX_PAGE, current - 1)));
  }

  nextPage(): void {
    const result = this.sources.data();
    if (this.interactionDisabled() || this.sources.loading() || !result?.hasMore || this.page() >= MAX_PAGE) return;
    this.page.update(current => Math.min(MAX_PAGE, current + 1));
  }

  select(item: EvidenceSource): void {
    const current = this.sources.data();
    if (this.interactionDisabled() || !this.opened() || this.sources.loading() || !current ||
        this.loadedRequestContext !== this.requestContextKey() ||
        current.engagementId.toLowerCase() !== this.engagementId().toLowerCase() || current.kind !== this.kind() ||
        current.page !== this.page() || current.pageSize !== PAGE_SIZE || !current.items.includes(item) ||
        !guidPattern.test(item.id)) return;
    this.sourceSelected.emit(item.id);
  }

  pickerLabel(): string {
    return `Evidence source picker for ${this.stepTitle()} — ${this.requirementKey()}`;
  }

  disclosureLabel(): string {
    const noun = this.kind() === 'PBC_ACCEPTED_OR_CLOSED' ? 'request' : 'procedure';
    const action = this.opened() ? 'Close' : 'Open';
    return `${action} evidence source search for ${noun} — ${this.stepTitle()} — ${this.requirementKey()}`;
  }

  searchLabel(): string {
    const noun = this.kind() === 'PBC_ACCEPTED_OR_CLOSED' ? 'request' : 'procedure';
    return `Find evidence source for ${noun} — ${this.stepTitle()} — ${this.requirementKey()}`;
  }

  searchActionLabel(): string {
    const noun = this.kind() === 'PBC_ACCEPTED_OR_CLOSED' ? 'request' : 'procedure';
    return `Search evidence sources for ${noun} — ${this.stepTitle()} — ${this.requirementKey()}`;
  }

  pageLabel(): string {
    return `Evidence source pages for ${this.stepTitle()} — ${this.requirementKey()}`;
  }

  previousPageLabel(): string {
    return `Previous evidence sources — ${this.stepTitle()} — ${this.requirementKey()}`;
  }

  nextPageLabel(): string {
    return `Next evidence sources — ${this.stepTitle()} — ${this.requirementKey()}`;
  }

  resultsLabel(): string {
    return `Evidence source results for ${this.stepTitle()} — ${this.requirementKey()}`;
  }

  selectLabel(item: EvidenceSource): string {
    const noun = this.kind() === 'PBC_ACCEPTED_OR_CLOSED' ? 'request' : 'procedure';
    return `Select evidence source for ${noun}: ${item.label} (${item.id}) — ${this.stepTitle()} — ${this.requirementKey()}`;
  }

  private url(): string | null {
    const engagementId = this.engagementId();
    const selectedId = this.validSelectedId();
    if (!this.opened() || this.activeContext !== this.contextKey() || this.interactionDisabled() ||
        !guidPattern.test(engagementId) || this.page() > MAX_PAGE) {
      this.requestedContext = '';
      return null;
    }
    this.requestedContext = this.requestContextKey();
    const params = new URLSearchParams({ kind: this.kind(), query: this.query(), page: String(this.page()), pageSize: String(PAGE_SIZE) });
    if (selectedId) params.set('selectedId', selectedId);
    return `/api/ui/engagements/${engagementId}/workflow-evidence-sources?${params.toString()}`;
  }

  private validSelectedId(): string | null {
    const selectedId = this.selectedId();
    return selectedId && guidPattern.test(selectedId) ? selectedId : null;
  }

  private decodePage(value: unknown, path: string): EvidenceSourcePage {
    const result = evidenceSourcePage(value, path);
    const selectedId = this.validSelectedId();
    const requestContext = this.requestContextKey();
    if (!this.requestedContext || this.requestedContext !== requestContext || this.activeContext !== this.contextKey() ||
        result.engagementId.toLowerCase() !== this.engagementId().toLowerCase() || result.kind !== this.kind() || result.page !== this.page() ||
        result.pageSize !== PAGE_SIZE || result.items.length > result.pageSize ||
        result.selected !== null && result.selected.id.toLowerCase() !== selectedId?.toLowerCase() || selectedId === null && result.selected !== null ||
        new Set(result.items.map(item => item.id.toLowerCase())).size !== result.items.length)
      throw new Error('Evidence source response context changed.');
    this.loadedRequestContext = requestContext;
    return result;
  }

  private contextKey(): string {
    const session = this.session.current();
    return JSON.stringify([this.engagementId(), this.kind(), session?.userId ?? null, session?.firmId ?? null,
      session?.generation ?? null, session?.staff ?? null, this.session.invalidation()]);
  }

  private requestContextKey(): string {
    return JSON.stringify([this.contextKey(), this.query(), this.page(), this.validSelectedId()]);
  }
}
