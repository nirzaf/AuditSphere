import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { arr, bool, guid, int, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

export const decodeEvidenceQueue = arr(obj({ kind: text, area: text, clientName: text, engagementName: text, periodCode: text, status: text, inputGeneration: nullable(int),
  currentGeneration: nullable(int), linkStatus: text, workpaperId: nullable(guid), reference: text, isStale: bool, isTerminal: bool, evidenceId: nullable(guid) }), 20000);
const PAGE = 25;
const PAGE_SIZES = [10, 25, 50] as const;

@Component({
  selector: 'audit-accounting-evidence',
  imports: [RouterLink, MatButtonModule, ...SHARED],
  template: `
    <a routerLink="/app/accounting">← Back to accounting workspace</a>
    <audit-page-header title="Accounting evidence queue" eyebrow="Accounting evidence"
      description="Only accounting evidence in the authenticated client scope is shown. Reconciliations retain local proofs; valuation and analysis approval requires a reviewed audit-procedure result. A free-text reference alone is not approval evidence." />
    <button matButton="outlined" (click)="rows.reload(); page.set(0)">Refresh queue</button>
    <audit-state [loading]="rows.loading()" [error]="rows.error()" label="scoped accounting evidence" />
    @if (rows.data(); as r) {
      <p role="status">{{ r.length }} evidence records · {{ count(r, 'open') }} awaiting action · {{ count(r, 'stale') }} stale or unverified inputs · {{ count(r, 'reviewed') }} reviewed result linked</p>
      @if (!r.length) { <section class="panel"><h2>No accounting evidence</h2><p>No ECL, inventory, specialist, analytical or journal-risk records are available in the selected scope.</p></section> }
      @else {
        <section class="panel" aria-labelledby="evidence-heading">
          <h2 id="evidence-heading">Account-area evidence</h2>
          <div class="table-scroll"><table>
            <thead><tr><th>Area</th><th>Client</th><th>Engagement</th><th>Period</th><th>Status</th><th>Input generation</th><th>Audit evidence</th><th>Reference</th></tr></thead>
            <tbody>@for (x of pageRows(r); track $index) {
              <tr><td><strong>{{ x.kind }}</strong><small>{{ x.area }}</small>@if (x.kind === 'RECONCILIATION' && x.evidenceId) { <a [routerLink]="['/app/accounting/reconciliations', x.evidenceId]">Inspect reconciliation</a> } @else if (x.evidenceId && ['ECL', 'INVENTORY', 'SPECIALIST', 'ANALYTICAL', 'JOURNAL_RISK'].includes(x.kind)) { <a [routerLink]="['/app/accounting/evidence', x.kind, x.evidenceId]">Inspect {{ x.kind }} evidence</a> }</td><td>{{ x.clientName }}</td><td>{{ x.engagementName }}</td><td>{{ x.periodCode }}</td>
                <td><audit-status [value]="x.status" />{{ x.isStale ? ' · INPUT CHECK REQUIRED' : '' }}</td><td>{{ x.inputGeneration ?? 'Not retained' }} / {{ x.currentGeneration ?? 'Unavailable' }}</td>
                <td><audit-status [value]="x.linkStatus" />@if (x.workpaperId) { <a [routerLink]="['/app/audit/workpapers', x.workpaperId]">Open linked workpaper</a> }</td>
                <td><small>{{ x.reference }}</small></td></tr> }</tbody>
          </table></div>
          <nav aria-label="Pages" class="actions"><button type="button" (click)="page.set(page() - 1)" [disabled]="page() === 0">Previous</button>
            <span>Page {{ page() + 1 }} of {{ pages(r.length) }}</span><button type="button" (click)="page.set(page() + 1)" [disabled]="page() + 1 >= pages(r.length)">Next</button>
            <label>Rows per page <select aria-label="Rows per page" (change)="setPageSize($event)">
              @for (size of pageSizes; track size) { <option [value]="size" [selected]="size === pageSize()">{{ size }}</option> }
            </select></label></nav>
        </section>
      }
    }
  `,
})
export class AccountingEvidence {
  private readonly api = inject(Api);
  readonly rows = this.api.resource(() => '/api/ui/accounting/evidence', decodeEvidenceQueue, 'This queue requires an authorized internal accounting role and an explicit client scope.');
  readonly page = signal(0);
  readonly pageSize = signal(PAGE);
  readonly pageSizes = PAGE_SIZES;
  pages(n: number): number { return Math.max(1, Math.ceil(n / this.pageSize())); }
  pageRows(rows: ReturnType<typeof decodeEvidenceQueue>) { const size = this.pageSize(); return rows.slice(this.page() * size, (this.page() + 1) * size); }
  setPageSize(event: Event): void {
    const value = Number((event.target as HTMLSelectElement).value);
    if ((PAGE_SIZES as readonly number[]).includes(value)) { this.pageSize.set(value); this.page.set(0); }
  }
  count(r: ReturnType<typeof decodeEvidenceQueue>, kind: 'open' | 'stale' | 'reviewed'): number {
    return r.filter((x) => (kind === 'open' ? !x.isTerminal : kind === 'stale' ? x.isStale : x.linkStatus === 'REVIEWED_RESULT')).length;
  }
}
