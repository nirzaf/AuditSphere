import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { arr, guid, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

export const decodeReviewQueue = arr(obj({ packageId: guid, framework: text, periodStart: text, periodEnd: text, currency: text, packageHash: text,
  managementDecision: text, accountingDecision: text, partnerDecision: text, nextAction: text }), 2000);

@Component({
  selector: 'audit-package-reviews',
  imports: [RouterLink, MatButtonModule, ...SHARED],
  template: `
    <a routerLink="/app/accounting">← Back to accounting workspace</a>
    <audit-page-header title="Financial package reviews" eyebrow="Accounting review"
      description="Only validated packages in the current client and engagement grants are listed. Every stage is bound to the package's exact revision, generation and hash." />
    <button matButton="outlined" (click)="queue.reload(); selected.set([])">Refresh queue</button>
    <audit-state [loading]="queue.loading()" [error]="queue.error()" label="the review queue" />
    @if (queue.data(); as q) {
      <p role="status">{{ q.length }} stages shown · {{ partnerRequired(q) }} partner action required · {{ selected().length }} selected for preview</p>
      <section class="panel" aria-labelledby="queue-heading">
        <h2 id="queue-heading">Action required</h2>
        <p>Selection is optional. Preview only; no package decision is recorded from this screen.</p>
        <p class="actions"><button matButton="filled" (click)="preview(q)" [disabled]="!selected().length">Preview selected</button>
          @if (previewText()) { <span role="status" aria-live="polite">{{ previewText() }}</span> }</p>
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
  readonly queue = this.api.resource(() => '/api/ui/accounting/reviews', decodeReviewQueue, 'This queue requires an authorized internal accounting role.');
  readonly selected = signal<string[]>([]);
  readonly previewText = signal('');
  partnerRequired(q: { nextAction: string }[]): number { return q.filter((x) => x.nextAction === 'PARTNER_APPROVAL_REQUIRED').length; }
  toggleAll(q: { packageId: string }[], e: Event): void { this.selected.set((e.target as HTMLInputElement).checked ? q.map((x) => x.packageId) : []); this.previewText.set(''); }
  toggle(id: string, e: Event): void {
    const on = (e.target as HTMLInputElement).checked;
    this.selected.update((s) => (on ? [...new Set([...s, id])] : s.filter((x) => x !== id))); this.previewText.set('');
  }
  preview(q: { packageId: string; nextAction: string }[]): void {
    const chosen = q.filter((x) => this.selected().includes(x.packageId));
    const blocked = chosen.filter((x) => x.nextAction === 'PARTNER_APPROVAL_REQUIRED').length;
    const eligible = chosen.length - blocked;
    this.previewText.set(blocked === 0 ? `Preview: ${eligible} selected stage${eligible === 1 ? '' : 's'} are eligible for this role. No decision was recorded.`
      : `Preview: ${eligible} eligible, ${blocked} blocked because partner approval requires a Partner or Administrator. No decision was recorded.`);
  }
}
