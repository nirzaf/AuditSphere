import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink, RouterLinkActive } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs';
import { Api } from '../../core/api';
import { arr, bool, dec, guid, nat, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

export const decodeMappings = arr(obj({ id: guid, clientName: text, engagementName: text, periodStart: text, periodEnd: text, version: nat, generation: nat, hasChart: bool, status: text }), 1000);
export const decodeJournals = arr(obj({ id: guid, journalNumber: text, clientName: text, engagementName: text, purpose: text, periodCode: text, currency: nullable(text), status: text, revision: nat }), 1000);
export const decodeDifferences = arr(obj({ id: guid, accountArea: text, differenceType: text, clientName: text, engagementName: text, amount: dec, currency: text, status: text,
  correctionState: nullable(text), proposedJournalId: nullable(guid) }), 1000);
type Mode = 'mappings' | 'journals' | 'differences';
const TITLES: Record<Mode, string> = { mappings: 'COA and accounting mappings', journals: 'Adjustment journals', differences: 'Audit differences' };
const PAGE = 25;
const PAGE_SIZES = [10, 25, 50] as const;

@Component({
  selector: 'audit-accounting-records',
  imports: [RouterLink, RouterLinkActive, ...SHARED],
  template: `
    <a routerLink="/app/accounting">← Back to accounting workspace</a>
    <audit-page-header [title]="title()" eyebrow="Accounting records"
      description="Only records in the authenticated client or exact engagement scope are shown. Detail links re-check authorization before displaying the record." />
    <nav aria-label="Accounting record queues" class="actions">
      <a routerLink="/app/accounting/mappings" routerLinkActive="active" ariaCurrentWhenActive="page">COA and mappings</a>
      <a routerLink="/app/accounting/journals" routerLinkActive="active" ariaCurrentWhenActive="page">Adjustments</a>
      <a routerLink="/app/accounting/adjustment-plans" routerLinkActive="active" ariaCurrentWhenActive="page">Plan eligibility</a>
      <a routerLink="/app/accounting/differences" routerLinkActive="active" ariaCurrentWhenActive="page">Differences</a>
    </nav>
    <button type="button" (click)="reload()">Refresh queue</button>
    @switch (mode()) {
      @case ('mappings') {
        <audit-state [loading]="mappings.loading()" [error]="mappings.error()" label="scoped accounting records" />
        @if (mappings.data(); as rows) {
          <p role="status">{{ rows.length }} mapping versions shown · {{ chartBound(rows) }} chart-bound</p>
          <div class="table-scroll"><table><caption>Mapping versions</caption>
            <thead><tr><th>Client</th><th>Engagement</th><th>Period</th><th>Version</th><th>Chart</th><th>Status</th><th><span class="sr-only">Action</span></th></tr></thead>
            <tbody>@for (m of page(rows); track m.id) {
              <tr><td>{{ m.clientName }}</td><td>{{ m.engagementName }}</td><td>{{ m.periodStart }} to {{ m.periodEnd }}</td><td>v{{ m.version }} · gen {{ m.generation }}</td>
                <td>{{ m.hasChart ? 'Bound' : 'Legacy/unbound' }}</td><td><audit-status [value]="m.status" /></td><td><a [routerLink]="['/app/accounting/mappings', m.id]">Open mapping</a></td></tr>
            } @empty { <tr><td colspan="7">No mapping versions are available in the current scope.</td></tr> }</tbody></table></div>
          <nav aria-label="Pages" class="actions"><button type="button" (click)="prev()" [disabled]="pageIndex() === 0">Previous</button><span>Page {{ pageIndex() + 1 }} of {{ pages(rows.length) }}</span><button type="button" (click)="next(rows.length)" [disabled]="pageIndex() + 1 >= pages(rows.length)">Next</button>
            <label>Rows per page <select aria-label="Rows per page" (change)="setPageSize($event)">
              @for (size of pageSizes; track size) { <option [value]="size" [selected]="size === pageSize()">{{ size }}</option> }
            </select></label></nav>
        }
      }
      @case ('journals') {
        <audit-state [loading]="journals.loading()" [error]="journals.error()" label="scoped accounting records" />
        @if (journals.data(); as rows) {
          <p role="status">{{ rows.length }} journals shown · {{ posted(rows) }} posted</p>
          <div class="table-scroll"><table><caption>Adjustment journals</caption>
            <thead><tr><th>Journal</th><th>Client</th><th>Engagement</th><th>Purpose</th><th>Context</th><th>Status</th><th><span class="sr-only">Action</span></th></tr></thead>
            <tbody>@for (j of page(rows); track j.id) {
              <tr><td><strong>{{ j.journalNumber }}</strong><small>rev {{ j.revision }}</small></td><td>{{ j.clientName }}</td><td>{{ j.engagementName }}</td><td>{{ j.purpose }}</td>
                <td>{{ j.periodCode }} · {{ j.currency ?? 'Currency not recorded' }}</td><td><audit-status [value]="j.status" /></td><td><a [routerLink]="['/app/accounting/journals', j.id]">Open journal</a></td></tr>
            } @empty { <tr><td colspan="7">No adjustment journals are available in the current scope.</td></tr> }</tbody></table></div>
          <nav aria-label="Pages" class="actions"><button type="button" (click)="prev()" [disabled]="pageIndex() === 0">Previous</button><span>Page {{ pageIndex() + 1 }} of {{ pages(rows.length) }}</span><button type="button" (click)="next(rows.length)" [disabled]="pageIndex() + 1 >= pages(rows.length)">Next</button>
            <label>Rows per page <select aria-label="Rows per page" (change)="setPageSize($event)">
              @for (size of pageSizes; track size) { <option [value]="size" [selected]="size === pageSize()">{{ size }}</option> }
            </select></label></nav>
        }
      }
      @case ('differences') {
        <audit-state [loading]="differences.loading()" [error]="differences.error()" label="scoped accounting records" />
        @if (differences.data(); as rows) {
          <p>Differences remain separate from journal posting. A proposed journal link is shown only when one is persisted for the exact difference.</p>
          <p role="status">{{ rows.length }} differences shown · {{ linked(rows) }} linked journals</p>
          <div class="table-scroll"><table><caption>Audit differences</caption>
            <thead><tr><th>Area</th><th>Client</th><th>Engagement</th><th class="number">Amount</th><th>Status</th><th>Correction</th><th><span class="sr-only">Action</span></th></tr></thead>
            <tbody>@for (d of page(rows); track d.id) {
              <tr><td><strong>{{ d.accountArea }}</strong><small>{{ d.differenceType }}</small></td><td>{{ d.clientName }}</td><td>{{ d.engagementName }}</td>
                <td class="number">{{ d.amount | money }} {{ d.currency }}</td><td><audit-status [value]="d.status" /></td><td>{{ d.correctionState ?? 'Not recorded' }}</td>
                <td>@if (d.proposedJournalId) { <a [routerLink]="['/app/accounting/journals', d.proposedJournalId]">Open linked journal</a> } @else { <small>No journal linked</small> }</td></tr>
            } @empty { <tr><td colspan="7">No audit differences are available in the current scope.</td></tr> }</tbody></table></div>
          <nav aria-label="Pages" class="actions"><button type="button" (click)="prev()" [disabled]="pageIndex() === 0">Previous</button><span>Page {{ pageIndex() + 1 }} of {{ pages(rows.length) }}</span><button type="button" (click)="next(rows.length)" [disabled]="pageIndex() + 1 >= pages(rows.length)">Next</button>
            <label>Rows per page <select aria-label="Rows per page" (change)="setPageSize($event)">
              @for (size of pageSizes; track size) { <option [value]="size" [selected]="size === pageSize()">{{ size }}</option> }
            </select></label></nav>
        }
      }
    }
  `,
})
export class AccountingRecords {
  private readonly api = inject(Api);
  readonly mode = toSignal(inject(ActivatedRoute).data.pipe(map((d) => d['mode'] as Mode)), { initialValue: 'mappings' as Mode });
  readonly title = computed(() => TITLES[this.mode()]);
  readonly pageIndex = signal(0);
  readonly pageSize = signal(PAGE);
  readonly pageSizes = PAGE_SIZES;
  readonly mappings = this.api.resource(() => (this.mode() === 'mappings' ? '/api/ui/accounting/mappings' : null), decodeMappings, 'This queue requires an authorized internal accounting role and an explicit client scope.');
  readonly journals = this.api.resource(() => (this.mode() === 'journals' ? '/api/ui/accounting/journals' : null), decodeJournals, 'This queue requires an authorized internal accounting role and an explicit client scope.');
  readonly differences = this.api.resource(() => (this.mode() === 'differences' ? '/api/ui/accounting/differences' : null), decodeDifferences, 'This queue requires an authorized internal accounting role and an explicit client scope.');
  reload(): void { this.pageIndex.set(0); ({ mappings: this.mappings, journals: this.journals, differences: this.differences })[this.mode()].reload(); }
  page<T>(rows: T[]): T[] { const size = this.pageSize(); return rows.slice(this.pageIndex() * size, (this.pageIndex() + 1) * size); }
  pages(n: number): number { return Math.max(1, Math.ceil(n / this.pageSize())); }
  setPageSize(event: Event): void {
    const value = Number((event.target as HTMLSelectElement).value);
    if ((PAGE_SIZES as readonly number[]).includes(value)) { this.pageSize.set(value); this.pageIndex.set(0); }
  }
  prev(): void { this.pageIndex.update((i) => Math.max(0, i - 1)); }
  next(n: number): void { this.pageIndex.update((i) => Math.min(this.pages(n) - 1, i + 1)); }
  chartBound(rows: { hasChart: boolean }[]): number { return rows.filter((r) => r.hasChart).length; }
  posted(rows: { status: string }[]): number { return rows.filter((r) => r.status === 'Posted').length; }
  linked(rows: { proposedJournalId: string | null }[]): number { return rows.filter((r) => r.proposedJournalId).length; }
}
