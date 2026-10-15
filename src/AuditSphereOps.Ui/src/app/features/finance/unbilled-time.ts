import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { arr, bool, date, dec, guid, instant, int, nullable, obj, oneOf, str, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

const invalidQueryMessage = 'Choose a valid work-date range of at most 366 days and a supported page.';
const unavailableValueMessage = 'Unavailable — review captured rate/currency';
const emptyGuid = '00000000-0000-0000-0000-000000000000';

function isoDate(value: unknown, path: string): string {
  const result = date(value, path);
  const parsed = new Date(`${result}T00:00:00.000Z`);
  if (!Number.isFinite(parsed.getTime()) || parsed.toISOString().slice(0, 10) !== result)
    throw new Error(`${path}: expected calendar date`);
  return result;
}

function digitString(value: unknown, path: string): string {
  const result = str(20)(value, path);
  if (!/^(0|[1-9]\d*)$/.test(result)) throw new Error(`${path}: expected invariant digit string`);
  return result;
}

function nonEmptyGuid(value: unknown, path: string): string {
  const result = guid(value, path);
  if (result.toLowerCase() === emptyGuid) throw new Error(`${path}: expected non-empty identifier`);
  return result;
}

const unbilledBucket = oneOf('UNALLOCATED', 'RESERVED', 'INVOICED', 'REVIEW_REQUIRED');
const feeBasis = oneOf('FIXED_FEE_REFERENCE', 'TIME_SOURCE_REFERENCE', 'UNDETERMINED');

const firmUnbilledTimeShape = obj({
  from: isoDate,
  to: isoDate,
  observedAtUtc: instant,
  totalEntries: digitString,
  page: int,
  pageSize: int,
  hasMore: bool,
  rows: arr(obj({
    timeEntryId: nonEmptyGuid,
    revision: int,
    clientId: nullable(nonEmptyGuid),
    clientName: nullable(text),
    engagementId: nullable(nonEmptyGuid),
    engagementLabel: nullable(text),
    workDate: isoDate,
    durationMinutes: int,
    currency: nullable(str(20)),
    standardValue: nullable(dec),
    bucket: unbilledBucket,
    feeBasis,
    invoiceId: nullable(nonEmptyGuid),
    invoiceNumber: nullable(text),
    reason: nullable(text),
  }), 100),
  subtotals: arr(obj({
    currency: nullable(str(20)),
    bucket: unbilledBucket,
    feeBasis,
    entryCount: digitString,
    minutes: digitString,
    standardValue: nullable(dec),
    unavailableValueCount: digitString,
  }), 10000),
  basisNotice: text,
});

export const decodeFirmUnbilledTime = (value: unknown, path: string) => {
  const report = firmUnbilledTimeShape(value, path);
  for (const row of report.rows) {
    if ((row.invoiceId === null) !== (row.invoiceNumber === null) ||
        row.invoiceId !== null && row.clientId === null ||
        row.bucket === 'UNALLOCATED' && row.invoiceId !== null ||
        (row.bucket === 'RESERVED' || row.bucket === 'INVOICED') && row.invoiceId === null ||
        row.revision < 1 || row.durationMinutes < 1)
      throw new Error(`${path}: inconsistent row attribution`);
  }
  if (report.page < 0 || report.page > 10_000 || ![10, 25, 50, 100].includes(report.pageSize))
    throw new Error(`${path}: unsupported page`);
  return report;
};

export type FirmUnbilledTimeReport = ReturnType<typeof decodeFirmUnbilledTime>;
type UnbilledBucket = ReturnType<typeof unbilledBucket>;

const defaultDates = (): { from: string; to: string } => {
  const now = new Date();
  const to = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate()));
  const from = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate() - 29));
  return { from: to.toISOString().slice(0, 10), to: to.toISOString().slice(0, 10) };
};

function isIsoCalendarDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const parsed = new Date(`${value}T00:00:00.000Z`);
  return Number.isFinite(parsed.getTime()) && parsed.toISOString().slice(0, 10) === value;
}

function validRange(from: string, to: string): boolean {
  if (!isIsoCalendarDate(from) || !isIsoCalendarDate(to)) return false;
  const start = Date.parse(`${from}T00:00:00.000Z`);
  const end = Date.parse(`${to}T00:00:00.000Z`);
  const elapsedDays = (end - start) / 86_400_000;
  return elapsedDays >= 0 && elapsedDays < 366;
}

function validOptionalGuid(value: string): boolean {
  if (value === '') return true;
  if (value.toLowerCase() === emptyGuid) return false;
  try { guid(value, 'filter'); return true; } catch { return false; }
}

@Component({
  selector: 'audit-firm-unbilled-time',
  imports: [FormsModule, RouterLink, MatButtonModule, ...SHARED],
  template: `
    <section class="report" aria-label="Approved unbilled time report">
      <p>Current observations, not a historical accounting WIP balance.</p>
      <div class="filters">
        <label>From (UTC)<input type="date" [ngModel]="from()" (ngModelChange)="changeFrom($event)" /></label>
        <label>To (UTC)<input type="date" [ngModel]="to()" (ngModelChange)="changeTo($event)" /></label>
        <details class="advanced">
          <summary>Advanced filters</summary>
          <label>Client ID<input type="text" maxlength="36" autocomplete="off" [ngModel]="clientId()" (ngModelChange)="changeClientId($event)" /></label>
          <label>Engagement ID<input type="text" maxlength="36" autocomplete="off" [ngModel]="engagementId()" (ngModelChange)="changeEngagementId($event)" /></label>
        </details>
        <label>Rows per page
          <select [ngModel]="pageSize()" (ngModelChange)="changePageSize($event)">
            @for (size of pageSizes; track size) { <option [ngValue]="size">{{ size }}</option> }
          </select>
        </label>
        <button matButton="filled" [disabled]="loading()" (click)="apply()">{{ loading() ? 'Loading…' : 'Apply' }}</button>
      </div>
      @if (error()) { <p role="alert">{{ error() }}</p> }
      @if (loading()) { <p role="status">Loading approved time observations…</p> }
      @if (report(); as current) {
        <section class="basis" aria-label="Report basis">
          <p>{{ current.basisNotice }}</p>
          <p>Current observations, not a historical accounting WIP balance.</p>
        </section>
        <p class="page-summary">{{ current.totalEntries }} matching entries · Page {{ current.page + 1 }}</p>
        @for (group of groups; track group.bucket) {
          <section class="bucket" [attr.aria-label]="group.label + ' standard values'">
            <h3>{{ group.label }}</h3>
            <div class="table-scroll"><table>
              <caption>{{ group.label }} subtotals for the complete filtered report</caption>
              <thead><tr><th scope="col">Currency</th><th scope="col">Fee basis</th><th scope="col" class="number">Entries</th><th scope="col" class="number">Minutes</th><th scope="col" class="number">Unavailable values</th><th scope="col" class="number">Standard value</th></tr></thead>
              <tbody>
                @for (subtotal of subtotalsFor(group.bucket); track subtotal.currency + ':' + subtotal.feeBasis) {
                  <tr><th scope="row">{{ subtotal.currency ?? 'Currency unavailable' }}</th>
                    <td>{{ feeBasisLabel(subtotal.feeBasis) }}</td><td class="number">{{ subtotal.entryCount }}</td><td class="number">{{ subtotal.minutes }}</td><td class="number">{{ subtotal.unavailableValueCount }}</td>
                    <td class="number">@if (subtotal.standardValue === null) { {{ unavailableValueMessage }} } @else { {{ subtotal.standardValue | money:6 }} {{ subtotal.currency }} }</td></tr>
                } @empty { <tr><td colspan="6">No entries in this classification.</td></tr> }
              </tbody>
            </table></div>
            <div class="table-scroll"><table>
              <caption>{{ group.label }} approved time on this page</caption>
              <thead><tr><th scope="col">Work date</th><th scope="col">Client</th><th scope="col">Engagement</th><th scope="col" class="number">Minutes</th><th scope="col">Currency</th><th scope="col" class="number">Standard value</th><th scope="col">Fee basis</th><th scope="col">Invoice / review</th></tr></thead>
              <tbody>
                @for (row of rowsFor(group.bucket); track row.timeEntryId) {
                  <tr><td>{{ row.workDate }}</td><td>{{ row.clientName ?? 'Client attribution unavailable' }}</td><td>{{ row.engagementLabel ?? 'Not linked' }}</td>
                    <td class="number">{{ row.durationMinutes }}</td><td>{{ row.currency ?? 'Unavailable' }}</td>
                    <td class="number">@if (row.standardValue === null) { {{ unavailableValueMessage }} } @else { {{ row.standardValue | money:6 }} {{ row.currency }} }</td>
                    <td>{{ feeBasisLabel(row.feeBasis) }}</td>
                    <td>@if (row.invoiceId) { <a [routerLink]="['/app/practice/invoices', row.invoiceId]">{{ row.invoiceNumber ?? 'Open invoice' }}</a> }
                      @else if (row.reason) { {{ row.reason }} } @else { — }</td>
                  </tr>
                } @empty { <tr><td colspan="8">No entries on this page.</td></tr> }
              </tbody>
            </table></div>
          </section>
        }
        <nav class="paging" aria-label="Unbilled time report pages">
          <button matButton="outlined" [disabled]="loading() || current.page === 0" (click)="previousPage()">Previous</button>
          <span>Page {{ current.page + 1 }}</span>
          <button matButton="outlined" [disabled]="loading() || !current.hasMore" (click)="nextPage()">Next</button>
        </nav>
      }
    </section>
  `,
  styles: `
    .filters { display:flex; flex-wrap:wrap; align-items:end; gap:1rem; margin-block:1rem; }
    .filters label, .advanced label { display:grid; gap:.35rem; }
    input, select { min-height:2.5rem; padding:.4rem .55rem; }
    .advanced { display:grid; gap:.6rem; }
    .advanced summary { cursor:pointer; font-weight:600; }
    .basis { border-inline-start:4px solid var(--primary,#205fc5); padding-inline-start:.75rem; }
    .basis p { margin:.35rem 0; }
    .bucket { margin-block:1.25rem; }
    .table-scroll { overflow-x:auto; margin-block:.75rem; }
    table { width:100%; border-collapse:collapse; }
    th, td { padding:.5rem; text-align:start; vertical-align:top; border-bottom:1px solid var(--border,#d7dee7); }
    .number { text-align:end; font-variant-numeric:tabular-nums; }
    .paging { display:flex; align-items:center; justify-content:flex-end; gap:.75rem; }
  `,
})
export class FirmUnbilledTime {
  private readonly api = inject(Api);
  private requestGeneration = 0;
  private readonly dates = defaultDates();
  readonly from = signal(this.dates.from);
  readonly to = signal(this.dates.to);
  readonly clientId = signal('');
  readonly engagementId = signal('');
  readonly pageSize = signal(50);
  readonly page = signal(0);
  readonly report = signal<FirmUnbilledTimeReport | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly pageSizes = [10, 25, 50, 100] as const;
  readonly groups = [
    { bucket: 'UNALLOCATED', label: 'Unallocated' },
    { bucket: 'RESERVED', label: 'Reserved' },
    { bucket: 'INVOICED', label: 'Invoiced' },
    { bucket: 'REVIEW_REQUIRED', label: 'Review required' },
  ] as const;
  readonly unavailableValueMessage = unavailableValueMessage;

  changeFrom(value: string): void { this.from.set(value); this.filtersChanged(); }
  changeTo(value: string): void { this.to.set(value); this.filtersChanged(); }
  changeClientId(value: string): void { this.clientId.set(value); this.filtersChanged(); }
  changeEngagementId(value: string): void { this.engagementId.set(value); this.filtersChanged(); }

  changePageSize(value: number): void {
    this.pageSize.set(value);
    this.filtersChanged();
  }

  apply(): Promise<void> {
    this.page.set(0);
    return this.load(0);
  }

  previousPage(): Promise<void> {
    if (this.page() > 0) return this.load(this.page() - 1);
    return Promise.resolve();
  }

  nextPage(): Promise<void> {
    if (this.report()?.hasMore) return this.load(this.page() + 1);
    return Promise.resolve();
  }

  rowsFor(bucket: UnbilledBucket): FirmUnbilledTimeReport['rows'] {
    return (this.report()?.rows ?? []).filter(row => row.bucket === bucket);
  }

  subtotalsFor(bucket: UnbilledBucket): FirmUnbilledTimeReport['subtotals'] {
    return (this.report()?.subtotals ?? []).filter(subtotal => subtotal.bucket === bucket);
  }

  feeBasisLabel(value: FirmUnbilledTimeReport['rows'][number]['feeBasis']): string {
    if (value === 'FIXED_FEE_REFERENCE') return 'Fixed-fee reference — not an additional fee to bill';
    if (value === 'TIME_SOURCE_REFERENCE') return 'Time source reference';
    return 'Undetermined — finance review';
  }

  private filtersChanged(): void {
    this.requestGeneration++;
    this.loading.set(false);
    this.report.set(null);
    this.error.set('');
    this.page.set(0);
  }

  private async load(targetPage: number): Promise<void> {
    const from = this.from();
    const to = this.to();
    const clientId = this.clientId();
    const engagementId = this.engagementId();
    const pageSize = this.pageSize();
    const validPageSize = this.pageSizes.some(size => size === pageSize);
    this.requestGeneration++;
    const generation = this.requestGeneration;
    this.report.set(null);
    this.error.set('');
    if (!validRange(from, to) || !validOptionalGuid(clientId) || !validOptionalGuid(engagementId) ||
        !validPageSize || !Number.isSafeInteger(targetPage) || targetPage < 0 || targetPage > 10_000) {
      this.loading.set(false);
      this.error.set(invalidQueryMessage);
      return;
    }

    const query = new URLSearchParams({ from, to, page: String(targetPage), pageSize: String(pageSize) });
    if (clientId !== '') query.set('clientId', clientId);
    if (engagementId !== '') query.set('engagementId', engagementId);
    this.loading.set(true);
    try {
      const result = await this.api.get(`/api/ui/finance/unbilled-time?${query.toString()}`, decodeFirmUnbilledTime);
      if (generation !== this.requestGeneration) return;
      if (result.from !== from || result.to !== to || result.page !== targetPage || result.pageSize !== pageSize)
        throw new Error('Unsupported response.');
      this.page.set(targetPage);
      this.report.set(result);
    } catch (error) {
      if (generation !== this.requestGeneration) return;
      this.error.set(error instanceof Error && error.message ? error.message : 'Temporarily unavailable. Retry shortly.');
    } finally {
      if (generation === this.requestGeneration) this.loading.set(false);
    }
  }
}
