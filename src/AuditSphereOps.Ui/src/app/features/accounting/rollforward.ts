import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, CommandState } from '../../core/api';
import { decimalInput } from '../../core/decode';
import { SHARED } from '../../core/ui';
import { ClosedPeriod, decodeClosedPeriods, decodeMaintenance } from './maintenance';

@Component({
  selector: 'audit-period-rollforward',
  imports: [FormsModule, RouterLink, MatButtonModule, ...SHARED],
  template: `
    <a routerLink="/app/accounting">← Back to accounting workspace</a>
    <audit-page-header title="Period roll-forward" eyebrow="Accounting period maintenance" description="New draft reporting period created with explicit opening-balance evidence." />
    <audit-state [loading]="overview.loading()" [error]="overview.error()" label="scoped periods" />
    @if (overview.data(); as o) {
      <p role="note">Roll-forward creates a new draft period and draft books. Prior approvals are not copied; opening balances require explicit source evidence and remain separately reviewable.</p>
      <p role="status">{{ o.clients.length }} clients in scope · {{ closed().length }} closed periods for client · {{ o.periods.length }} periods in scope</p>
      <section class="panel" aria-labelledby="rollforward-heading">
        <h2 id="rollforward-heading">Create next period</h2>
        <form class="inline-form" (submit)="$event.preventDefault(); submit()">
          <label>Client <select name="client" [(ngModel)]="f.client" (ngModelChange)="loadClient($event)" required><option value="">Select a client</option>
            @for (c of o.clients; track c.id) { <option [value]="c.id">{{ c.name }}</option> }</select></label>
          <label>Closed prior period <select name="prior" [(ngModel)]="f.prior" (ngModelChange)="choosePrior($event)" required><option value="">Select a period</option>
            @for (p of closed(); track p.id) { <option [value]="p.id">{{ p.periodCode }} ({{ p.startDate }} to {{ p.endDate }})</option> }</select></label>
          <label>New period code <input name="code" [(ngModel)]="f.code" maxlength="100" required /></label>
          <label>Start date <input type="date" name="start" [(ngModel)]="f.start" required /></label>
          <label>End date <input type="date" name="end" [(ngModel)]="f.end" required /></label>
          <label>Basis <input name="basis" [(ngModel)]="f.basis" maxlength="100" required /></label>
          <label>Currency <input name="currency" [(ngModel)]="f.currency" maxlength="3" required /></label>
          <label>Validated source package (optional) <select name="package" [(ngModel)]="f.package"><option value="">Use external source evidence</option>
            @for (k of prior()?.packages ?? []; track k.id) { <option [value]="k.id">{{ k.id }} · {{ k.currency }} · {{ k.hashPrefix }}</option> }</select></label>
          <label>Prior closing amount <input name="closing" inputmode="decimal" [(ngModel)]="f.closing" required /></label>
          <label>Current opening amount <input name="opening" inputmode="decimal" [(ngModel)]="f.opening" required /></label>
          <label>Source SHA-256 <input name="hash" [(ngModel)]="f.hash" maxlength="64" required /></label>
          <label>Evidence reference <input name="evidence" [(ngModel)]="f.evidence" maxlength="2000" required /></label>
          <label><span><input type="checkbox" name="reviewed" [(ngModel)]="f.reviewed" /> I reviewed the closed prior period, opening amounts and exact source evidence.</span></label>
          <button matButton="filled" type="submit" [disabled]="cmd.busy() || !f.reviewed">{{ cmd.busy() ? 'Saving…' : 'Create draft period' }}</button>
        </form>
        @if (closedError()) { <p role="alert" class="error-text">{{ closedError() }}</p> }
      </section>
      <section class="panel" aria-labelledby="history-heading">
        <h2 id="history-heading">Scoped period history</h2>
        <div class="table-scroll"><table><thead><tr><th scope="col">Client</th><th scope="col">Period</th><th scope="col">Dates</th><th scope="col">Status</th><th scope="col">Revision</th><th scope="col">Prior period</th></tr></thead>
          <tbody>@for (p of o.periods; track p.id) { <tr><td>{{ p.clientName }}</td><td><a [routerLink]="['/app/accounting/periods', p.id]">{{ p.periodCode }}</a></td><td>{{ p.startDate }} — {{ p.endDate }}</td><td><audit-status [value]="p.status" /></td><td>{{ p.revision }}</td><td>{{ p.priorPeriodCode }}</td></tr> }
          @empty { <tr><td colspan="6">No reporting periods are available in the current scope.</td></tr> }</tbody></table></div>
      </section>
    }
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
})
export class PeriodRollforward {
  private readonly api = inject(Api);
  readonly overview = this.api.resource(() => '/api/ui/accounting/period-maintenance', decodeMaintenance, 'Sign in with an authorized accounting identity to roll a client period forward.');
  readonly cmd = new CommandState(this.api);
  readonly closed = signal<ClosedPeriod[]>([]);
  readonly prior = signal<ClosedPeriod | null>(null);
  readonly closedError = signal('');
  f = { client: '', prior: '', code: '', start: '', end: '', basis: 'STATUTORY', currency: 'QAR', package: '', closing: '0', opening: '0', hash: '', evidence: '', reviewed: false };

  async loadClient(clientId: string): Promise<void> {
    this.closed.set([]); this.prior.set(null); this.f.prior = ''; this.f.package = ''; this.closedError.set('');
    if (!clientId) return;
    try {
      const periods = await this.api.get(`/api/ui/accounting/period-maintenance/clients/${clientId}`, decodeClosedPeriods);
      if (this.f.client !== clientId) return;
      this.closed.set(periods);
      if (periods[0]) { this.f.prior = periods[0].id; this.choosePrior(periods[0].id); }
    } catch (e) { this.closedError.set((e as Error).message); }
  }
  choosePrior(id: string): void {
    const p = this.closed().find((x) => x.id === id) ?? null;
    this.prior.set(p); this.f.package = '';
    if (!p) return;
    const year = Number(p.periodCode);
    const nextStart = new Date(p.endDate + 'T00:00:00Z'); nextStart.setUTCDate(nextStart.getUTCDate() + 1);
    this.f = { ...this.f, currency: p.currency, basis: p.basis, code: Number.isInteger(year) && /^\d+$/.test(p.periodCode) ? String(year + 1) : p.periodCode + '-NEXT',
      start: nextStart.toISOString().slice(0, 10), end: `${nextStart.getUTCFullYear()}-12-31` };
  }
  submit(): void {
    const p = this.prior(); const closing = decimalInput(this.f.closing, 2); const opening = decimalInput(this.f.opening, 2);
    if (!this.f.client || !p || closing === null || opening === null || !/^[a-fA-F0-9]{64}$/.test(this.f.hash.trim())) {
      this.cmd.failed.set(true); this.cmd.message.set('Select a closed period and enter valid dates, decimal opening amounts and a 64-character SHA-256.'); return;
    }
    void this.cmd.run(`/api/ui/accounting/clients/${this.f.client}/rollforward`, { priorPeriodId: p.id, revision: String(p.revision), code: this.f.code, start: this.f.start,
      end: this.f.end, basis: this.f.basis, currency: this.f.currency.toUpperCase(), sourceHash: this.f.hash.trim(), priorClosing: closing, currentOpening: opening,
      evidence: this.f.evidence, sourcePackageId: this.f.package || null, reviewed: this.f.reviewed },
      'Draft period and opening bridge created; a reviewer must approve the opening bridge separately.', () => { this.f.reviewed = false; void this.loadClient(this.f.client); })
      .finally(() => this.overview.reload());
  }
}
