import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { arr, bool, date, dec, guid, int, nat, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

const row = obj({ engagementId: guid, label: text, currency: text, budgetMinutes: nat, budgetValue: nullable(dec), actualMinutes: nat, standardValue: nullable(dec),
  actualCost: nullable(dec), billed: nullable(dec), collected: nullable(dec), realizationPercent: nullable(dec), collectionPercent: nullable(dec), profit: nullable(dec),
  marginPercent: nullable(dec), budgetVarianceMinutes: int, costComplete: bool, contractedFee: nullable(dec), contractCurrency: nullable(text),
  lifetimeStandardValue: nullable(dec), contractedFeeLessStandardValue: nullable(dec) });
export const decodeAnalytics = obj({ from: date, to: date, engagements: arr(row, 2000),
  departments: arr(obj({ department: text, capacityMinutes: nat, chargeableMinutes: nat, utilizationPercent: nullable(dec), targetPercent: nullable(dec) })),
  milestones: obj({ due: nat, completedOnTime: nat, completedLate: nat, overdue: nat, onTimePercent: nullable(dec) }), definitions: arr(text, 50) });

export function hours(minutes: number, places = 1): string {
  return (minutes / 60).toLocaleString('en-US', { minimumFractionDigits: places, maximumFractionDigits: places });
}

@Component({
  selector: 'audit-practice-analytics',
  imports: [FormsModule, MatButtonModule, ...SHARED],
  template: `
    <audit-page-header title="Practice analytics" eyebrow="Economics"
      description="Engagement profitability, realization, budget variance, utilization and milestone performance from approved records, with every formula stated." />
    <form class="inline-form" aria-label="Reporting period" (submit)="$event.preventDefault(); apply()">
      <label>From <input type="date" name="from" [(ngModel)]="from" /></label>
      <label>To <input type="date" name="to" [(ngModel)]="to" /></label>
      <button matButton="outlined" type="submit">Show</button>
    </form>
    <audit-state [loading]="view.loading()" [error]="view.error()" label="practice analytics" />
    @if (view.data(); as v) {
      <section class="panel" aria-labelledby="econ-heading">
        <h2 id="econ-heading">Engagement economics</h2>
        <div class="table-scroll"><table>
          <caption>Engagement economics {{ v.from }} to {{ v.to }}</caption>
          <thead><tr><th>Engagement</th><th>Currency</th><th class="number">Budget h</th><th class="number">Actual h</th><th class="number">Standard value</th><th class="number">Cost</th>
            <th class="number">Billed</th><th class="number">Collected</th><th class="number">Realization</th><th class="number">Collection</th><th class="number">Profit</th>
            <th class="number">Margin</th><th class="number">Contract fee</th><th class="number">Lifetime standard value</th><th class="number">Fee less standard value</th></tr></thead>
          <tbody>@for (r of v.engagements; track r.engagementId) {
            <tr><th scope="row">{{ r.label }}</th><td>{{ currencyLabel(r.currency) }}</td><td class="number">{{ hrs(r.budgetMinutes) }}</td><td class="number">{{ hrs(r.actualMinutes) }}</td>
              <td class="number">{{ r.standardValue | money }}</td><td class="number">{{ r.actualCost | money }}{{ r.costComplete || r.currency === 'MIXED' || r.currency === 'UNAVAILABLE' ? '' : ' (incomplete)' }}</td>
              <td class="number">{{ r.billed | money }}</td><td class="number">{{ r.collected | money }}</td>
              <td class="number">{{ pct(r.realizationPercent) }}</td><td class="number">{{ pct(r.collectionPercent) }}</td>
              <td class="number">{{ r.profit | money }}</td><td class="number">{{ pct(r.marginPercent) }}</td>
              <td class="number">{{ r.contractedFee === null ? 'Unavailable' : (r.contractedFee | money) }} {{ r.contractCurrency }}</td>
              <td class="number">{{ r.lifetimeStandardValue === null ? 'Unavailable' : (r.lifetimeStandardValue | money) }} {{ r.contractCurrency }}</td>
              <td class="number">{{ r.contractedFeeLessStandardValue === null ? 'Unavailable' : (r.contractedFeeLessStandardValue | money) }} {{ r.contractCurrency }}</td></tr>
          } @empty { <tr><td colspan="15">No engagement activity in this period.</td></tr> }</tbody>
        </table></div>
      </section>
      <section class="panel" aria-labelledby="util-heading">
        <h2 id="util-heading">Utilization by department</h2>
        <ul aria-label="Department utilization">@for (d of v.departments; track d.department) {
          <li>{{ d.department }}: {{ pct(d.utilizationPercent) }} of {{ hrs(d.capacityMinutes, 0) }} h capacity (target {{ pct(d.targetPercent) }})</li> }</ul>
      </section>
      <section class="panel" aria-labelledby="milestone-heading">
        <h2 id="milestone-heading">Milestone performance</h2>
        <p aria-label="Milestone performance">{{ v.milestones.due }} due · {{ v.milestones.completedOnTime }} on time · {{ v.milestones.completedLate }} late ·
          {{ v.milestones.overdue }} overdue · on-time {{ pct(v.milestones.onTimePercent) }}</p>
      </section>
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
  readonly view = this.api.resource(() => `/api/ui/practice/analytics?from=${this.range().from}&to=${this.range().to}`, decodeAnalytics,
    'Practice analytics require a firm-wide Partner, Manager or finance assignment.');
  readonly hrs = hours;
  apply(): void { this.range.set({ from: this.from, to: this.to }); }
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
