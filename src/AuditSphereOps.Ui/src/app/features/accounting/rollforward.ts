import { Component, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NavigationStart, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { SessionService } from '../../core/session';
import { decimalInput } from '../../core/decode';
import { SHARED } from '../../core/ui';
import { ClosedPeriod, decodeMaintenance } from './maintenance';
import { MaintenanceAction, decodeMaintenanceContext } from './period-maintenance-contracts';
import { ReviewedPeriodMaintenance } from './period-maintenance-reviewed-state';

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
        <form class="inline-form" (submit)="$event.preventDefault(); prepare()">
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
          <button matButton="filled" type="submit" [disabled]="!flow.canPrepare('ROLL_FORWARD')">{{ cmd.busy() ? 'Loading…' : 'Preview roll-forward' }}</button>
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

    @if (flow.preview(); as preview) {
      <section class="panel" aria-label="Exact maintenance preview">
        <h2>Review exact maintenance effect</h2>
        <p>{{ preview.summary }}</p>
        <dl>
          <dt>Client</dt><dd>{{ clientName(preview.state.clientId) }}</dd>
          <dt>Source period</dt><dd>{{ periodName(preview.state.periodId) }}</dd>
          @if (preview.canonicalInput.rollForward; as input) {
            <dt>New period</dt><dd>{{ input.periodCode }} · {{ input.startDate }} — {{ input.endDate }}</dd>
            <dt>Basis and currency</dt><dd>{{ input.basis }} · {{ input.currency }}</dd>
            <dt>Source</dt><dd>{{ input.sourcePackageId || 'External closing evidence' }}</dd>
            <dt>Source SHA-256</dt><dd><code>{{ input.sourceHash }}</code></dd>
            <dt>Prior closing amount</dt><dd>{{ input.priorClosingAmount }} {{ input.currency }}</dd>
            <dt>Current opening amount</dt><dd>{{ input.currentOpeningAmount }} {{ input.currency }}</dd>
            <dt>Evidence reference</dt><dd>{{ input.evidenceReference }}</dd>
            <dt>Effect</dt><dd>Create draft period, draft books and opening bridge. Prior approvals are not copied; opening approval remains separate.</dd>
          }
          @if (preview.canonicalInput.restatement; as input) {
            <dt>Original package</dt><dd>{{ packageName(input.originalPackageId) }}</dd>
            <dt>Revised package</dt><dd>{{ packageName(input.revisedPackageId) }}</dd>
            <dt>Revised basis</dt><dd>{{ input.revisedBasis }}</dd>
            <dt>Change type</dt><dd>{{ input.changeType }}</dd>
            <dt>Affected periods</dt><dd>{{ input.affectedPeriods || 'None specified' }}</dd>
            <dt>Reason</dt><dd>{{ input.reason }}</dd>
            <dt>Evidence reference</dt><dd>{{ input.evidenceReference }}</dd>
            <dt>Effect</dt><dd>Submit immutable package lineage for independent review. Issued packages remain unchanged.</dd>
          }
          @if (preview.canonicalInput.restatementId; as id) {
            <dt>Restatement</dt><dd>{{ id }}</dd>
            <dt>Effect</dt><dd>Independently approve the submitted restatement. Original and revised packages remain immutable.</dd>
          }
          <dt>Reviewed source fingerprint</dt><dd><code>{{ preview.state.reviewBasis }}</code></dd>
          <dt>Exact request fingerprint</dt><dd><code>{{ preview.requestHash }}</code></dd>
        </dl>
        @for (blocker of preview.state.blockers; track blocker) { <p role="alert">{{ blocker }}</p> }
        <label><input type="checkbox" [ngModel]="flow.reviewed()" (ngModelChange)="flow.reviewed.set($event)" [disabled]="cmd.busy() || !preview.state.canProceed" /> I reviewed this exact preview, source evidence and effect.</label>
        <button matButton="filled" (click)="execute()" [disabled]="cmd.busy() || !flow.reviewed() || !preview.state.canProceed">{{ preview.state.action === 'ROLL_FORWARD' ? 'Create draft period' : preview.state.action === 'CREATE_RESTATEMENT' ? 'Create restatement request' : 'Approve independently' }}</button>
      </section>
    }
    @if (cmd.uncertain()) {
      <section class="panel" aria-label="Exact request recovery">
        <p role="alert">Command outcome is not confirmed. Matching history is not a receipt. Look up the exact retained request before any further submission.</p>
        @for (pending of flow.pending(); track pending.action) {
          <p>Client {{ clientName(pending.clientId) }} · {{ pending.action }} · request {{ pending.reference.requestId }} · <code>{{ pending.reference.requestHash }}</code></p>
          <button matButton="outlined" (click)="reconcile(pending.action)" [disabled]="cmd.busy() || pending.clientId !== f.client">Look up exact receipt</button>
          @if (flow.absent().includes(pending.action)) {
            <p role="note">No receipt was found at the lookup time. The request identity is retained. Re-enter the exact intent and prepare a fresh preview of this same request before explicitly submitting it again.</p>
          }
        }
      </section>
    }
    @if (flow.receipt(); as receipt) {
      <section class="panel" aria-label="Verified maintenance receipt">
        <h2>Verified command receipt</h2>
        <p>{{ receipt.action }} · {{ receipt.result.status }} · {{ receipt.createdAt }}</p>
        <p>Request {{ receipt.requestId }} · <code>{{ receipt.requestHash }}</code></p>
        <p>Actor {{ receipt.actorId }} · session {{ receipt.actorEpoch }}</p>
        <a [routerLink]="['/app/accounting/periods', receipt.result.periodId]">Open resulting period</a>
        @if (receipt.result.restatementId) { <p>Restatement {{ receipt.result.restatementId }}</p> }
      </section>
    }
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
  styles: ['dd, code { overflow-wrap: anywhere; }'],
})
export class PeriodRollforward {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  readonly overview = this.api.resource(() => '/api/ui/accounting/period-maintenance', decodeMaintenance);
  readonly flow = new ReviewedPeriodMaintenance(['ROLL_FORWARD']);
  readonly cmd = this.flow.cmd;
  readonly closed = signal<ClosedPeriod[]>([]);
  readonly closedError = signal('');
  readonly prior = signal<ClosedPeriod | null>(null);
  readonly f = this.protect({ client: '', prior: '', code: '', start: '', end: '', basis: 'STATUTORY', currency: 'QAR', package: '', closing: '0', opening: '0', hash: '', evidence: '' });
  private generation = 0;
  private readSequence = 0;
  private destroyed = false;
  private resettingSource = false;
  readonly restatements = signal<ReturnType<typeof decodeMaintenanceContext>['restatements']>([]);
  constructor() {
    const route = inject(Router).events.subscribe(event => { if (event instanceof NavigationStart) this.reset(); });
    effect(() => { this.session.current(); this.session.invalidation(); untracked(() => this.reset()); });
    effect(() => { this.overview.data(); this.overview.loading(); this.overview.error(); untracked(() => {
      ++this.generation; ++this.readSequence; this.flow.invalidate(!!this.flow.receipt() && !this.overview.error()); this.resettingSource = true; this.closed.set([]); this.restatements.set([]); this.f.prior = ''; this.f.package = ''; this.prior.set(null); this.resettingSource = false;
    }); });
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; this.reset(); route.unsubscribe(); });
  }
  private reset(): void {
    ++this.generation; ++this.readSequence; this.flow.invalidate(); this.closed.set([]); this.restatements.set([]); this.closedError.set('');
    this.f.client = ''; this.f.prior = ''; this.f.package = ''; this.prior.set(null);
  }
  private protect<T extends object>(fields: T): T {
    return new Proxy(fields, { set: (target, key, value) => {
      if (Reflect.get(target, key) !== value) {
        ++this.generation; if (!this.resettingSource) this.flow.invalidate();
        if (key === 'client') {
          ++this.readSequence; this.closed.set([]); this.restatements.set([]); this.flow.changeClient(String(value)); this.prior.set(null); Reflect.set(target, 'prior', ''); Reflect.set(target, 'package', '');
        }
        if (key === 'prior') { this.prior.set(null); Reflect.set(target, 'package', ''); }
      }
      return Reflect.set(target, key, value);
    } });
  }
  private ticket() {
    const generation = this.generation, epoch = this.session.invalidation(), actor = this.session.current();
    return () => !this.destroyed && generation === this.generation && epoch === this.session.invalidation() && actor === this.session.current();
  }
  private allowed(): boolean { return !this.destroyed && !!this.session.current()?.staff && !!this.overview.data() && !this.overview.loading() && !this.overview.error(); }
  async loadClient(clientId: string): Promise<void> {
    ++this.generation; this.f.client = clientId; const sequence = ++this.readSequence;
    this.closed.set([]); this.restatements.set([]); this.f.prior = ''; this.f.package = ''; this.prior.set(null); this.closedError.set(''); this.flow.invalidate();
    if (!this.allowed() || !this.overview.data()?.clients.some(c => c.id === clientId)) return;
    const valid = this.ticket();
    try {
      const context = await this.api.get(`/api/ui/accounting/period-maintenance/clients/${clientId}/context`, decodeMaintenanceContext);
      if (!valid() || sequence !== this.readSequence) return;
      if (context.restatements.some(r => r.clientId !== clientId)) throw new Error('Wrong client context');
      this.closed.set(context.periods); this.restatements.set(context.restatements); if (context.periods[0]) this.choosePrior(context.periods[0].id);
      this.flow.restorePending(clientId);
    } catch (error) { if (valid() && sequence === this.readSequence) this.closedError.set(error instanceof Error ? error.message : 'Closed periods unavailable.'); }
  }
  clientName(id: string): string { return this.overview.data()?.clients.find(c => c.id === id)?.name ?? id; }
  periodName(id: string): string { const p = this.closed().find(p => p.id === id); return p ? `${p.periodCode} (${p.startDate} — ${p.endDate})` : id; }
  packageName(id: string): string { const k = this.closed().flatMap(p => p.packages).find(k => k.id === id); return k ? `${id} · ${k.currency} · hash prefix ${k.hashPrefix}` : id; }
  async reconcile(action: MaintenanceAction): Promise<void> {
    if (!this.allowed()) return;
    await this.flow.reconcile(action);
    if (this.flow.receipt()) this.overview.reload();
  }
  async execute(): Promise<void> {
    if (!this.allowed()) return;
    await this.flow.execute();
    if (this.flow.receipt()) this.overview.reload();
  }
  choosePrior(id: string): void {
    this.flow.invalidate(); this.f.prior = id;
    const p = this.closed().find((x) => x.id === id) ?? null;
    this.prior.set(p); this.f.package = '';
    if (!p) return;
    const year = Number(p.periodCode);
    const nextStart = new Date(p.endDate + 'T00:00:00Z'); nextStart.setUTCDate(nextStart.getUTCDate() + 1);
    Object.assign(this.f, { currency: p.currency, basis: p.basis, code: Number.isInteger(year) && /^\d+$/.test(p.periodCode) ? String(year + 1) : p.periodCode + '-NEXT',
      start: nextStart.toISOString().slice(0, 10), end: `${nextStart.getUTCFullYear()}-12-31` });
  }
  async prepare(): Promise<void> {
    const p = this.prior(), f = this.f, closing = decimalInput(f.closing, 6), opening = decimalInput(f.opening, 6);
    if (!this.allowed() || !p || p.id !== f.prior || !this.closed().includes(p) || !f.code.trim() || !f.basis.trim() || !f.evidence.trim() ||
      !/^\d{4}-\d{2}-\d{2}$/.test(f.start) || !/^\d{4}-\d{2}-\d{2}$/.test(f.end) || closing === null || opening === null ||
      !/^[a-fA-F0-9]{64}$/.test(f.hash.trim()) || (f.package && !p.packages.some(k => k.id === f.package))) return;
    await this.flow.prepare('ROLL_FORWARD', p.id, null, () => ({ rollForward: { clientId: this.f.client, priorPeriodId: this.f.prior,
      periodCode: this.f.code.trim(), startDate: this.f.start, endDate: this.f.end, basis: this.f.basis.trim(), currency: this.f.currency.trim().toUpperCase(),
      sourceHash: this.f.hash.trim().toLowerCase(), priorClosingAmount: decimalInput(this.f.closing, 6), currentOpeningAmount: decimalInput(this.f.opening, 6),
      evidenceReference: this.f.evidence.trim(), sourcePackageId: this.f.package || null, expectedPriorRevision: this.prior()?.revision } }));
  }
}
