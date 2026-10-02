import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, CommandState } from '../../core/api';
import { SHARED } from '../../core/ui';
import { ClosedPeriod, decodeClosedPeriods, decodeMaintenance } from './maintenance';

@Component({
  selector: 'audit-period-restatements',
  imports: [FormsModule, RouterLink, MatButtonModule, ...SHARED],
  template: `
    <a routerLink="/app/accounting">← Back to accounting workspace</a>
    <audit-page-header title="Period restatements" eyebrow="Accounting period maintenance" description="Authorized post-close amendments recorded with immutable lineage; issued packages are never rewritten." />
    <audit-state [loading]="overview.loading()" [error]="overview.error()" label="scoped restatements" />
    @if (overview.data(); as o) {
      <p role="note">A restatement links two validated immutable packages for one closed period. Creation and independent review remain separate actions.</p>
      <p role="status">{{ o.clients.length }} clients in scope · {{ closed().length }} closed periods for client · {{ o.restatements.length }} restatements in scope</p>
      <section class="panel" aria-labelledby="create-heading">
        <h2 id="create-heading">Request a restatement</h2>
        <form class="inline-form" (submit)="$event.preventDefault(); create()">
          <label>Client <select name="client" [(ngModel)]="f.client" (ngModelChange)="loadClient($event)" required><option value="">Select a client</option>
            @for (c of o.clients; track c.id) { <option [value]="c.id">{{ c.name }}</option> }</select></label>
          <label>Closed period <select name="period" [(ngModel)]="f.period" (ngModelChange)="f.original = ''; f.revised = ''" required><option value="">Select a period</option>
            @for (p of closed(); track p.id) { <option [value]="p.id">{{ p.periodCode }} ({{ p.startDate }} to {{ p.endDate }})</option> }</select></label>
          <label>Original package <select name="original" [(ngModel)]="f.original" required><option value="">Select package</option>
            @for (k of packages(); track k.id) { <option [value]="k.id">{{ k.id }} · {{ k.currency }} · {{ k.hashPrefix }}</option> }</select></label>
          <label>Revised package <select name="revised" [(ngModel)]="f.revised" required><option value="">Select package</option>
            @for (k of packages(); track k.id) { <option [value]="k.id">{{ k.id }} · {{ k.currency }} · {{ k.hashPrefix }}</option> }</select></label>
          <label>Revised basis <input name="basis" [(ngModel)]="f.basis" maxlength="200" required /></label>
          <label>Change type <select name="type" [(ngModel)]="f.type" required><option value="">Select change type</option>
            <option value="RECLASSIFIED">Reclassified presentation</option><option value="RESTATED_ERROR">Prior-period error restated</option>
            <option value="POLICY_TRANSITION">Accounting policy transition</option><option value="PROSPECTIVE_ESTIMATE_CHANGE">Prospective estimate change</option></select></label>
          <label>Affected periods (comma-separated codes) <input name="affected" [(ngModel)]="f.affected" maxlength="500" /></label>
          <label>Evidence reference <input name="evidence" [(ngModel)]="f.evidence" maxlength="2000" required /></label>
          <label>Reason <textarea name="reason" [(ngModel)]="f.reason" maxlength="4000" rows="3" required></textarea></label>
          <button matButton="filled" type="submit" [disabled]="cmd.busy()">{{ cmd.busy() ? 'Saving…' : 'Create restatement request' }}</button>
        </form>
        @if (closedError()) { <p role="alert" class="error-text">{{ closedError() }}</p> }
      </section>
      <section class="panel" aria-labelledby="history-heading">
        <h2 id="history-heading">Restatement history</h2>
        <div class="table-scroll"><table><thead><tr><th scope="col">Client</th><th scope="col">Period</th><th scope="col">Basis</th><th scope="col">Status</th><th scope="col">Evidence</th><th scope="col">Action</th></tr></thead>
          <tbody>@for (r of o.restatements; track r.id) {
            <tr><td>{{ r.clientName }}</td><td>{{ r.periodCode }}</td><td>{{ r.revisedBasis }}</td><td><audit-status [value]="r.status" /></td><td><code>{{ r.evidenceReference }}</code></td>
              <td>@if (r.canReview) { <button matButton="filled" (click)="approve(r.id)" [disabled]="cmd.busy()">Approve independently</button> } @else { — }</td></tr>
          } @empty { <tr><td colspan="6">No restatement requests are available in the current scope.</td></tr> }</tbody></table></div>
      </section>
    }
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
})
export class PeriodRestatements {
  private readonly api = inject(Api);
  readonly overview = this.api.resource(() => '/api/ui/accounting/period-maintenance', decodeMaintenance, 'Sign in with an authorized accounting identity to manage period restatements.');
  readonly cmd = new CommandState(this.api);
  readonly closed = signal<ClosedPeriod[]>([]);
  readonly closedError = signal('');
  f = { client: '', period: '', original: '', revised: '', basis: '', type: '', affected: '', evidence: '', reason: '' };
  packages() { return this.closed().find((p) => p.id === this.f.period)?.packages ?? []; }
  async loadClient(clientId: string): Promise<void> {
    this.closed.set([]); this.f.period = ''; this.closedError.set('');
    if (!clientId) return;
    try {
      const periods = await this.api.get(`/api/ui/accounting/period-maintenance/clients/${clientId}`, decodeClosedPeriods);
      if (this.f.client !== clientId) return;
      this.closed.set(periods); this.f.period = periods[0]?.id ?? '';
    } catch (e) { this.closedError.set((e as Error).message); }
  }
  create(): void {
    const f = this.f;
    if (!f.client || !f.period || !f.original || !f.revised || f.original === f.revised) {
      this.cmd.failed.set(true); this.cmd.message.set('Select one client, closed period and two distinct validated packages.'); return;
    }
    void this.cmd.run(`/api/ui/accounting/clients/${f.client}/restatements`, { periodId: f.period, originalPackageId: f.original, revisedPackageId: f.revised,
      revisedBasis: f.basis, reason: f.reason, evidenceReference: f.evidence, changeType: f.type, affectedPeriods: f.affected || null },
      'Restatement request created; an independent reviewer must approve it.').finally(() => this.overview.reload());
  }
  approve(id: string): void { void this.cmd.run(`/api/ui/accounting/restatements/${id}/approve`, {}, 'Restatement independently approved.').finally(() => this.overview.reload()); }
}
