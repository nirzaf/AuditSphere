import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { arr, bool, date, dec, guid, int, nat, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

const row = obj({ engagementId: guid, label: text, currency: text, budgetMinutes: nat, budgetValue: nullable(dec), actualMinutes: nat, standardValue: nullable(dec),
  actualCost: nullable(dec), billed: nullable(dec), collected: nullable(dec), realizationPercent: nullable(dec), collectionPercent: nullable(dec), profit: nullable(dec),
  marginPercent: nullable(dec), budgetVarianceMinutes: int, costComplete: bool, contractedFee: nullable(dec), contractCurrency: nullable(text),
  lifetimeStandardValue: nullable(dec), contractedFeeLessStandardValue: nullable(dec), nonChargeableMinutes: nat });
const analyticsProjection = obj({ from: date, to: date, engagements: arr(row, 100),
  engagementPageSize: int, hasMoreEngagements: bool, nextEngagementId: nullable(guid),
  departments: arr(obj({ department: text, capacityMinutes: nat, chargeableMinutes: nat, utilizationPercent: nullable(dec), targetPercent: nullable(dec) })),
  milestones: obj({ due: nat, completed: nat, completedOnTime: nat, completedLate: nat, overdue: nat,
    completionPercent: nullable(dec), onTimePercent: nullable(dec) }),
  workload: arr(obj({ assigneeKey: text, assigneeName: text, due: nat, completed: nat, open: nat, overdue: nat })),
  phaseWorkload: arr(obj({ phase: text, label: text, due: nat, completed: nat, open: nat, overdue: nat, completionPercent: nullable(dec) }), 8),
  reviewTurnaround: obj({ decisions: nat, reviewed: nat, changesRequired: nat, measured: nat, excluded: nat, averageHours: nullable(dec) }),
  clientWait: obj({ intervals: nat, completed: nat, open: nat, excluded: nat, averageCompletedHours: nullable(dec),
    averageOpenHours: nullable(dec), available: bool, unavailableReason: nullable(text) }),
  definitions: arr(text, 50) });

export function decodeAnalytics(value: unknown, path: string) {
  const result = analyticsProjection(value, path);
  if (result.engagementPageSize < 1 || result.engagementPageSize > 100 ||
      result.engagements.length > result.engagementPageSize ||
      result.hasMoreEngagements !== (result.nextEngagementId !== null) ||
      (result.hasMoreEngagements && (result.engagements.length !== result.engagementPageSize ||
        result.nextEngagementId?.toLowerCase() !== result.engagements.at(-1)?.engagementId.toLowerCase())))
    throw new Error('Unsupported analytics page or cursor.');
  return result;
}

export type AnalyticsView = 'economics' | 'contract' | 'capacity' | 'operations';

/** Focused views over one unchanged projection; each states the basis of what it shows. */
export const ANALYTICS_VIEWS: readonly { key: AnalyticsView; label: string; basis: string }[] = [
  { key: 'economics', label: 'Engagement economics', basis: 'Standard value and recorded staff cost use approved time in the selected work dates; billed and collected are engagement to date. ' +
    'Billed less recorded staff cost excludes overheads and is not the firm\'s accounting profit.' },
  { key: 'contract', label: 'Contract comparison', basis: 'Agreed contract fee against lifetime approved billable standard value in the contract currency. ' +
    'This lifetime basis is separate from the work-date figures in the other views.' },
  { key: 'capacity', label: 'Capacity and time', basis: 'Approved actual time in the selected work dates against the current approved budget. ' +
    'Department utilization is approved billable time over net capacity; planned allocation is not shown here.' },
  { key: 'operations', label: 'Operational delivery', basis: 'These summaries cover the whole selected period, not the engagement page. ' +
    'Operational indicators do not establish audit quality, professional approval or phase completion.' },
];

export function hours(minutes: number, places = 1): string {
  return (minutes / 60).toLocaleString('en-US', { minimumFractionDigits: places, maximumFractionDigits: places });
}

@Component({
  selector: 'audit-practice-analytics',
  imports: [FormsModule, MatButtonModule, ...SHARED],
  styles: [`
    .analytics-basis, .analytics-scope { color: var(--as-text-muted); max-width: 72ch; }
  `],
  template: `
    <audit-page-header title="Practice analytics" eyebrow="Economics"
      description="Engagement economics, billing realization, budget hours, utilization and operational delivery from approved records, with every formula stated." />
    <form class="inline-form filter-bar" aria-label="Reporting period" (submit)="$event.preventDefault(); apply()">
      <label>From <input type="date" name="from" [(ngModel)]="from" /></label>
      <label>To <input type="date" name="to" [(ngModel)]="to" /></label>
      <button matButton="outlined" type="submit">Show</button>
    </form>
    <nav class="analytics-views" aria-label="Analytics views">
      @for (item of views; track item.key) {
        <button matButton type="button" [attr.aria-pressed]="selected() === item.key" (click)="selected.set(item.key)">{{ item.label }}</button>
      }
    </nav>
    <audit-state [loading]="view.loading()" [error]="view.error()" label="practice analytics" />
    @if (view.data(); as v) {
      @if (selected() !== 'operations') {
        <section class="panel" aria-labelledby="econ-heading">
          <h2 id="econ-heading">{{ current().label }}</h2>
          <p class="analytics-basis">{{ current().basis }}</p>
          <div class="table-scroll"><table>
            <caption>{{ current().label }}, engagement page, work dates {{ v.from }} to {{ v.to }}</caption>
            @switch (selected()) {
              @case ('contract') {
                <thead><tr><th scope="col">Engagement</th><th scope="col">Contract currency</th><th class="number" scope="col">Agreed contract fee</th>
                  <th class="number" scope="col">Lifetime standard value</th><th class="number" scope="col">Contract contribution</th></tr></thead>
                <tbody>@for (r of v.engagements; track r.engagementId) {
                  <tr><th scope="row">{{ r.label }}</th><td>{{ r.contractCurrency ?? 'Unavailable' }}</td>
                    <td class="number">{{ r.contractedFee === null ? 'Unavailable' : (r.contractedFee | money) }}</td>
                    <td class="number">{{ r.lifetimeStandardValue === null ? 'Unavailable' : (r.lifetimeStandardValue | money) }}</td>
                    <td class="number">{{ r.contractedFeeLessStandardValue === null ? 'Unavailable' : (r.contractedFeeLessStandardValue | money) }}</td></tr>
                } @empty { <tr><td colspan="5">No engagements on this page.</td></tr> }</tbody>
              }
              @case ('capacity') {
                <thead><tr><th scope="col">Engagement</th><th class="number" scope="col">Approved budget h</th><th class="number" scope="col">Actual h</th>
                  <th class="number" scope="col">Non-chargeable h</th><th class="number" scope="col">Budget hours remaining / overrun</th></tr></thead>
                <tbody>@for (r of v.engagements; track r.engagementId) {
                  <tr><th scope="row">{{ r.label }}</th><td class="number">{{ hrs(r.budgetMinutes) }}</td><td class="number">{{ hrs(r.actualMinutes) }}</td>
                    <td class="number">{{ hrs(r.nonChargeableMinutes) }}</td><td class="number">{{ hrs(r.budgetVarianceMinutes) }}{{ r.budgetVarianceMinutes < 0 ? ' overrun' : '' }}</td></tr>
                } @empty { <tr><td colspan="5">No engagements on this page.</td></tr> }</tbody>
              }
              @default {
                <thead><tr><th scope="col">Engagement</th><th scope="col">Currency</th><th class="number" scope="col">Standard value</th><th class="number" scope="col">Recorded staff cost</th>
                  <th class="number" scope="col">Billed to date</th><th class="number" scope="col">Collected to date</th><th class="number" scope="col">Billing realization</th><th class="number" scope="col">Collection ratio</th>
                  <th class="number" scope="col">Billed less recorded staff cost</th><th class="number" scope="col">Billed-less-staff-cost margin</th></tr></thead>
                <tbody>@for (r of v.engagements; track r.engagementId) {
                  <tr><th scope="row">{{ r.label }}</th><td>{{ currencyLabel(r.currency) }}</td>
                    <td class="number">{{ r.standardValue | money }}</td><td class="number">{{ r.actualCost | money }}{{ r.costComplete || r.currency === 'MIXED' || r.currency === 'UNAVAILABLE' ? '' : ' (incomplete)' }}</td>
                    <td class="number">{{ r.billed | money }}</td><td class="number">{{ r.collected | money }}</td>
                    <td class="number">{{ pct(r.realizationPercent) }}</td><td class="number">{{ pct(r.collectionPercent) }}</td>
                    <td class="number">{{ r.profit | money }}</td><td class="number">{{ pct(r.marginPercent) }}</td></tr>
                } @empty { <tr><td colspan="10">No engagements on this page.</td></tr> }</tbody>
              }
            }
          </table></div>
          <p class="analytics-scope">This table shows one page of engagements in stable engagement-ID order. Each figure belongs to its engagement; the page is not a firm total.</p>
          <nav aria-label="Engagement economics pages">
            <button matButton (click)="firstPage()" [disabled]="!economicsCursor() || view.loading()">First engagements</button>
            @if (v.hasMoreEngagements && v.nextEngagementId) {
              <button matButton (click)="nextPage(v.nextEngagementId)" [disabled]="view.loading()">Next engagements</button>
            }
          </nav>
        </section>
      }
      @if (selected() === 'capacity') {
        <section class="panel" aria-labelledby="util-heading">
          <h2 id="util-heading">Utilization by department</h2>
          <p class="analytics-scope">Covers the whole selected period, not the engagement page.</p>
          <ul aria-label="Department utilization">@for (d of v.departments; track d.department) {
            <li>{{ d.department }}: {{ pct(d.utilizationPercent) }} of {{ hrs(d.capacityMinutes, 0) }} h capacity (target {{ pct(d.targetPercent) }})</li> }</ul>
        </section>
      }
      @if (selected() === 'operations') {
        <p class="analytics-basis">{{ current().basis }}</p>
        <section class="panel" aria-labelledby="milestone-heading">
          <h2 id="milestone-heading">Task completion and milestone performance</h2>
          <p aria-label="Task completion and milestone performance">{{ v.milestones.completed }} / {{ v.milestones.due }} applicable tasks completed · completion {{ pct(v.milestones.completionPercent) }} ·
            {{ v.milestones.completedOnTime }} on time · {{ v.milestones.completedLate }} late · {{ v.milestones.overdue }} overdue · on-time {{ pct(v.milestones.onTimePercent) }}</p>
        </section>
        <section class="panel" aria-labelledby="workload-heading">
          <h2 id="workload-heading">Task workload by assignee</h2>
          <p>Counts include non-cancelled tasks due in the selected period. Open includes overdue tasks; no-due-date tasks are outside this measure.</p>
          <div class="table-scroll"><table>
            <caption>Due task workload by assignee {{ v.from }} to {{ v.to }}</caption>
            <thead><tr><th scope="col">Assignee</th><th class="number" scope="col">Due</th><th class="number" scope="col">Completed</th><th class="number" scope="col">Open, includes overdue</th><th class="number" scope="col">Overdue</th></tr></thead>
            <tbody>@for (r of v.workload; track r.assigneeKey) {
              <tr><th scope="row">{{ r.assigneeName }}</th><td class="number">{{ r.due }}</td><td class="number">{{ r.completed }}</td><td class="number">{{ r.open }}</td><td class="number">{{ r.overdue }}</td></tr>
            } @empty { <tr><td colspan="5">No due tasks in this period.</td></tr> }</tbody>
          </table></div>
        </section>
        <section class="panel" aria-labelledby="review-turnaround-heading">
          <h2 id="review-turnaround-heading">Procedure review turnaround</h2>
          <p>Average elapsed time from result submission to each review decision created in this period. Includes reviewed and changes-required decisions; this is an operational duration, not review quality or audit clearance.</p>
          <p aria-label="Procedure review turnaround">{{ v.reviewTurnaround.averageHours === null ? 'n/a' : v.reviewTurnaround.averageHours + ' h' }} average across {{ v.reviewTurnaround.measured }} measured decisions ·
            {{ v.reviewTurnaround.reviewed }} reviewed · {{ v.reviewTurnaround.changesRequired }} changes required · {{ v.reviewTurnaround.excluded }} excluded chronology or revision records.</p>
        </section>
        <section class="panel" aria-labelledby="client-wait-heading">
          <h2 id="client-wait-heading">Client response wait</h2>
          @if (!v.clientWait.available) {
            <p role="status">Unavailable: {{ v.clientWait.unavailableReason }}</p>
          } @else {
            <p>The selected period filters confirmed PBC email starts; later client actions are included through now. Reminder emails do not restart the clock, and provider acceptance does not prove inbox delivery.</p>
            <p aria-label="Client response wait">
              {{ v.clientWait.averageCompletedHours === null ? 'n/a' : v.clientWait.averageCompletedHours + ' h' }} average for {{ v.clientWait.completed }} completed intervals ·
              {{ v.clientWait.open }} currently open{{ v.clientWait.averageOpenHours === null ? '' : ' (' + v.clientWait.averageOpenHours + ' h average)' }} ·
              {{ v.clientWait.excluded }} excluded terminal requests without a recorded client response · {{ v.clientWait.intervals }} intervals started in this period.
            </p>
          }
        </section>
        <section class="panel" aria-labelledby="phase-workload-heading">
          <h2 id="phase-workload-heading">Task completion by workflow phase</h2>
          <p>Operational task counts from current workflow bindings. Unbound tasks are shown separately; task completion does not establish phase or audit-gate readiness.</p>
          <div class="table-scroll"><table>
            <caption>Due task completion by workflow phase {{ v.from }} to {{ v.to }}</caption>
            <thead><tr><th scope="col">Phase</th><th class="number" scope="col">Due</th><th class="number" scope="col">Completed</th><th class="number" scope="col">Open</th><th class="number" scope="col">Overdue</th><th class="number" scope="col">Completion</th></tr></thead>
            <tbody>@for (r of v.phaseWorkload; track r.phase) {
              <tr><th scope="row">{{ r.label }}</th><td class="number">{{ r.due }}</td><td class="number">{{ r.completed }}</td><td class="number">{{ r.open }}</td><td class="number">{{ r.overdue }}</td><td class="number">{{ pct(r.completionPercent) }}</td></tr>
            }</tbody>
          </table></div>
        </section>
      }
      <section class="panel" aria-labelledby="defs-heading">
        <h2 id="defs-heading">Definitions</h2>
        <ul>@for (d of v.definitions; track $index) { <li>{{ d }}</li> }</ul>
      </section>
    }
  `,
})
export class PracticeAnalytics {
  private readonly api = inject(Api);
  from = new Date().getFullYear() + '-01-01';
  to = new Date().toISOString().slice(0, 10);
  private readonly range = signal({ from: this.from, to: this.to });
  readonly economicsCursor = signal<string | null>(null);
  readonly views = ANALYTICS_VIEWS;
  readonly selected = signal<AnalyticsView>('economics');
  readonly current = computed(() => ANALYTICS_VIEWS.find(item => item.key === this.selected())!);
  readonly view = this.api.resource(() => {
    const params = new URLSearchParams({ from: this.range().from, to: this.range().to, engagementPageSize: '50' });
    const after = this.economicsCursor();
    if (after) params.set('afterEngagementId', after);
    return `/api/ui/practice/analytics?${params.toString()}`;
  }, decodeAnalytics,
    'Practice analytics require a firm-wide Partner, Administrator, or Finance Manager assignment.');
  readonly hrs = hours;
  apply(): void { this.economicsCursor.set(null); this.range.set({ from: this.from, to: this.to }); }
  firstPage(): void { this.economicsCursor.set(null); }
  nextPage(after: string): void { this.economicsCursor.set(after); }
  currencyLabel(currency: string): string {
    if (currency === 'MIXED') return 'Mixed — financial totals unavailable';
    if (currency === 'UNAVAILABLE') return 'Currency unavailable';
    return currency;
  }
  pct(value: string | null): string {
    if (value === null) return 'n/a';
    const [w, f = ''] = value.split('.');
    const d = f.slice(0, 1).replace(/0$/, '');
    return w + (d ? '.' + d : '') + '%';
  }
}
