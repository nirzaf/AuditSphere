import { Component, effect, inject, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { arr, guid, obj, text } from '../../core/decode';
import { SessionService } from '../../core/session';
import { TabDrafts } from '../../core/tab-drafts';
import { SHARED } from '../../core/ui';

export const decodeReviewQueue = arr(obj({ packageId: guid, framework: text, periodStart: text, periodEnd: text, currency: text, packageHash: text,
  managementDecision: text, accountingDecision: text, partnerDecision: text, nextAction: text }), 2000);

// This non-authoritative ID selection is pinned to its draft schema. Recovery separately filters it against a fresh authorized queue.
const selectionScope = { entity: 'accounting/package-review-selection', baseRevision: '22bf59ad8353c8c0a5729323c669339cb5a1a0adf8b7968e2a62f9cbb63db46f' };
function decodeSelection(raw: unknown): string[] | null {
  if (!Array.isArray(raw) || raw.length > 2000) return null;
  const ids = new Set<string>();
  for (const value of raw) {
    if (typeof value !== 'string' || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value) || ids.has(value)) return null;
    ids.add(value);
  }
  return [...ids];
}

@Component({
  selector: 'audit-package-reviews',
  imports: [RouterLink, MatButtonModule, ...SHARED],
  template: `
    <a routerLink="/app/accounting">← Back to accounting workspace</a>
    <audit-page-header title="Financial package reviews" eyebrow="Accounting review"
      description="Only validated packages in the current client and engagement grants are listed. Every stage is bound to the package's exact revision, generation and hash." />
    <button matButton="outlined" (click)="refreshQueue()">Refresh queue</button>
    <audit-state [loading]="queue.loading()" [error]="queue.error()" label="the review queue" />
    @if (queue.data(); as q) {
      <p role="status">{{ q.length }} stages shown · {{ partnerRequired(q) }} partner action required · {{ selected().length }} selected for preview</p>
      <section class="panel" aria-labelledby="queue-heading">
        <h2 id="queue-heading">Action required</h2>
        <p>Selection is optional. Preview only; no package decision is recorded from this screen.</p>
        <p class="actions"><button matButton="filled" (click)="preview(q)" [disabled]="!selected().length">Preview selected</button>
          <button matButton="outlined" (click)="saveSelection(q)" [disabled]="!selected().length">Save selection in this tab</button>
          <button matButton="outlined" (click)="restoreSelection(q)">Restore saved selection</button>
          <button matButton="outlined" (click)="discardSelection()">Discard saved selection</button>
          @if (previewText()) { <span role="status" aria-live="polite">{{ previewText() }}</span> }</p>
        @if (selectionDraftMessage()) { <p role="status" aria-live="polite">{{ selectionDraftMessage() }}</p> }
        <div class="table-scroll"><table>
          <thead><tr><th scope="col"><label><input type="checkbox" [checked]="q.length > 0 && selected().length === q.length" (change)="toggleAll(q, $event)" /> <span class="sr-only">Select all visible packages</span></label></th>
            <th scope="col">Package</th><th scope="col">Period</th><th scope="col">Management</th><th scope="col">Accounting</th><th scope="col">Partner</th><th scope="col">Next action</th><th scope="col"><span class="sr-only">Action</span></th></tr></thead>
          <tbody>@for (x of q; track x.packageId) {
            <tr><td><input type="checkbox" [checked]="selected().includes(x.packageId)" (change)="toggle(x.packageId, $event)" [attr.aria-label]="'Select package ' + x.packageId" /></td>
              <td><code>{{ x.packageId }}</code><small>{{ x.framework }} · {{ x.currency }}</small></td><td>{{ x.periodStart }} to {{ x.periodEnd }}</td>
              <td><audit-status [value]="x.managementDecision" /></td><td><audit-status [value]="x.accountingDecision" /></td><td><audit-status [value]="x.partnerDecision" /></td>
              <td><strong>{{ x.nextAction }}</strong></td><td><a [routerLink]="['/app/accounting/packages', x.packageId]">Open package</a></td></tr>
          } @empty { <tr><td colspan="8">No current package review stage is waiting in your authorized scope.</td></tr> }</tbody>
        </table></div>
      </section>
    }
  `,
})
export class PackageReviews {
  private readonly api = inject(Api);
  private readonly drafts = inject(TabDrafts);
  private readonly session = inject(SessionService);
  private observedInvalidation = this.session.invalidation();
  readonly queue = this.api.resource(() => '/api/ui/accounting/reviews', decodeReviewQueue, 'This queue requires an authorized internal accounting role.');
  readonly selected = signal<string[]>([]);
  readonly previewText = signal('');
  readonly selectionDraftMessage = signal('');
  constructor() {
    effect(() => {
      const current = this.session.invalidation();
      if (current === this.observedInvalidation) return;
      this.observedInvalidation = current;
      untracked(() => {
        this.selected.set([]);
        this.previewText.set('');
        this.selectionDraftMessage.set('');
      });
    });
  }
  partnerRequired(q: { nextAction: string }[]): number { return q.filter((x) => x.nextAction === 'PARTNER_APPROVAL_REQUIRED').length; }
  refreshQueue(): void { this.queue.reload(); this.selected.set([]); this.previewText.set(''); this.selectionDraftMessage.set(''); }
  toggleAll(q: { packageId: string }[], e: Event): void {
    this.selected.set((e.target as HTMLInputElement).checked ? q.map((x) => x.packageId) : []);
    this.previewText.set('');
    this.selectionDraftMessage.set('');
  }
  toggle(id: string, e: Event): void {
    const on = (e.target as HTMLInputElement).checked;
    this.selected.update((s) => (on ? [...new Set([...s, id])] : s.filter((x) => x !== id))); this.previewText.set('');
    this.selectionDraftMessage.set('');
  }
  saveSelection(q: { packageId: string }[]): void {
    const available = new Set(q.map((x) => x.packageId));
    const selected = this.selected().filter((id) => available.has(id));
    const saved = selected.length > 0 && this.drafts.save(selectionScope, selected, decodeSelection);
    this.selectionDraftMessage.set(saved
      ? 'Selection saved in this tab for up to four hours. Only IDs in the freshly loaded authorized queue can be restored.'
      : 'The selection could not be saved. Keep this page open and retry if needed.');
  }
  restoreSelection(q: { packageId: string }[]): void {
    const saved = this.drafts.read(selectionScope, decodeSelection);
    if (saved.state !== 'ready') {
      this.selectionDraftMessage.set(saved.state === 'unavailable' ? 'Tab storage is unavailable; no selection was restored.' : 'No current saved selection is available.');
      return;
    }
    const available = new Set(q.map((x) => x.packageId));
    const restored = saved.draft.value.filter((id) => available.has(id));
    const dropped = saved.draft.value.length - restored.length;
    this.selected.set(restored);
    this.previewText.set('');
    if (restored.length === 0) {
      this.drafts.clear(selectionScope.entity);
      this.selectionDraftMessage.set('The saved packages are no longer in the current authorized queue; the stale selection was discarded.');
    } else {
      if (dropped > 0) this.drafts.save(selectionScope, restored, decodeSelection);
      this.selectionDraftMessage.set(dropped === 0
        ? `Restored ${restored.length} package${restored.length === 1 ? '' : 's'} from this tab's saved selection.`
        : `Restored ${restored.length} current package${restored.length === 1 ? '' : 's'}; discarded ${dropped} no longer available.`);
    }
  }
  discardSelection(): void {
    this.drafts.clear(selectionScope.entity);
    this.selected.set([]);
    this.previewText.set('');
    this.selectionDraftMessage.set('Saved selection discarded.');
  }
  preview(q: { packageId: string; nextAction: string }[]): void {
    const chosen = q.filter((x) => this.selected().includes(x.packageId));
    const blocked = chosen.filter((x) => x.nextAction === 'PARTNER_APPROVAL_REQUIRED').length;
    const eligible = chosen.length - blocked;
    this.previewText.set(blocked === 0 ? `Preview: ${eligible} selected stage${eligible === 1 ? '' : 's'} are eligible for this role. No decision was recorded.`
      : `Preview: ${eligible} eligible, ${blocked} blocked because partner approval requires a Partner or Administrator. No decision was recorded.`);
  }
}
