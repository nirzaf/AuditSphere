import { Component, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NavigationStart, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';
import { ClosedPeriod, decodeMaintenance } from './maintenance';
import { MaintenanceAction, decodeMaintenanceContext } from './period-maintenance-contracts';
import { ReviewedPeriodMaintenance } from './period-maintenance-reviewed-state';

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
        <form class="inline-form" (submit)="$event.preventDefault(); prepareCreate()">
          <label>Client <select name="client" [(ngModel)]="f.client" (ngModelChange)="loadClient($event)" required><option value="">Select a client</option>
            @for (c of o.clients; track c.id) { <option [value]="c.id">{{ c.name }}</option> }</select></label>
          <label>Closed period <select name="period" [(ngModel)]="f.period" (ngModelChange)="choosePeriod($event)" required><option value="">Select a period</option>
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
          <button matButton="filled" type="submit" [disabled]="!flow.canPrepare('CREATE_RESTATEMENT')">{{ cmd.busy() ? 'Loading…' : 'Preview restatement request' }}</button>
        </form>
        @if (closedError()) { <p role="alert" class="error-text">{{ closedError() }}</p> }
      </section>
      <section class="panel" aria-labelledby="history-heading">
        <h2 id="history-heading">Restatement history</h2>
        <div class="table-scroll"><table><thead><tr><th scope="col">Client</th><th scope="col">Period</th><th scope="col">Basis</th><th scope="col">Status</th><th scope="col">Evidence</th><th scope="col">Action</th></tr></thead>
          <tbody>@for (r of o.restatements; track r.id) {
            <tr><td>{{ r.clientName }}</td><td>{{ r.periodCode }}</td><td>{{ r.revisedBasis }}</td><td><audit-status [value]="r.status" /></td><td><code>{{ r.evidenceReference }}</code></td>
              <td>@if (r.canReview) { <button matButton="filled" (click)="prepareApproval(r.id)" [disabled]="cmd.busy()">Review independent approval</button> } @else { — }</td></tr>
          } @empty { <tr><td colspan="6">No restatement requests are available in the current scope.</td></tr> }</tbody></table></div>
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
            @if (preview.approvalDetail; as submitted) {
              <dt>Engagement</dt><dd>{{ submitted.engagementId }}</dd>
              <dt>Submitted period</dt><dd>{{ submitted.periodCode }} · {{ submitted.periodStart }} — {{ submitted.periodEnd }} · {{ submitted.currency }}</dd>
              <dt>Original package</dt><dd>{{ submitted.originalPackageId }}</dd>
              <dt>Original package SHA-256</dt><dd><code>{{ submitted.originalPackageHash }}</code></dd>
              <dt>Revised package</dt><dd>{{ submitted.revisedPackageId }}</dd>
              <dt>Revised package SHA-256</dt><dd><code>{{ submitted.revisedPackageHash }}</code></dd>
              <dt>Revised basis</dt><dd>{{ submitted.revisedBasis }}</dd>
              <dt>Treatment</dt><dd>{{ submitted.changeType }}</dd>
              <dt>Affected periods</dt><dd>{{ submitted.affectedPeriods || 'None specified' }}</dd>
              <dt>Reason</dt><dd>{{ submitted.reason }}</dd>
              <dt>Evidence reference</dt><dd>{{ submitted.evidenceReference }}</dd>
              <dt>Current status</dt><dd>{{ submitted.status }}</dd>
              <dt>Submitted by</dt><dd>{{ submitted.createdByUserId }} · {{ submitted.createdAt }}</dd>
            }
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
export class PeriodRestatements {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  readonly overview = this.api.resource(() => '/api/ui/accounting/period-maintenance', decodeMaintenance);
  readonly flow = new ReviewedPeriodMaintenance(['CREATE_RESTATEMENT', 'APPROVE_RESTATEMENT']);
  readonly cmd = this.flow.cmd;
  readonly closed = signal<ClosedPeriod[]>([]);
  readonly closedError = signal('');

  readonly f = this.protect({ client: '', period: '', original: '', revised: '', basis: '', type: '', affected: '', evidence: '', reason: '' });
  private generation = 0;
  private readSequence = 0;
  private destroyed = false;
  private resettingSource = false;
  readonly restatements = signal<ReturnType<typeof decodeMaintenanceContext>['restatements']>([]);
  constructor() {
    const route = inject(Router).events.subscribe(event => { if (event instanceof NavigationStart) this.reset(); });
    effect(() => { this.session.current(); this.session.invalidation(); untracked(() => this.reset()); });
    effect(() => { this.overview.data(); this.overview.loading(); this.overview.error(); untracked(() => {
      ++this.generation; ++this.readSequence; this.flow.invalidate(!!this.flow.receipt() && !this.overview.error()); this.resettingSource = true; this.closed.set([]); this.restatements.set([]); this.f.period = ''; this.f.original = ''; this.f.revised = ''; this.resettingSource = false;
    }); });
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; this.reset(); route.unsubscribe(); });
  }
  private reset(): void {
    ++this.generation; ++this.readSequence; this.flow.invalidate(); this.closed.set([]); this.restatements.set([]); this.closedError.set('');
    this.f.client = ''; this.f.period = ''; this.f.original = ''; this.f.revised = '';
  }
  private protect<T extends object>(fields: T): T {
    return new Proxy(fields, { set: (target, key, value) => {
      if (Reflect.get(target, key) !== value) {
        ++this.generation; if (!this.resettingSource) this.flow.invalidate();
        if (key === 'client') {
          ++this.readSequence; this.closed.set([]); this.restatements.set([]); this.flow.changeClient(String(value)); Reflect.set(target, 'period', ''); Reflect.set(target, 'original', ''); Reflect.set(target, 'revised', '');
        }
        if (key === 'period') { Reflect.set(target, 'original', ''); Reflect.set(target, 'revised', ''); }
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
    this.closed.set([]); this.restatements.set([]); this.f.period = ''; this.f.original = ''; this.f.revised = ''; this.closedError.set(''); this.flow.invalidate();
    if (!this.allowed() || !this.overview.data()?.clients.some(c => c.id === clientId)) return;
    const valid = this.ticket();
    try {
      const context = await this.api.get(`/api/ui/accounting/period-maintenance/clients/${clientId}/context`, decodeMaintenanceContext);
      if (!valid() || sequence !== this.readSequence) return;
      if (context.restatements.some(r => r.clientId !== clientId)) throw new Error('Wrong client context');
      this.closed.set(context.periods); this.restatements.set(context.restatements); this.f.period = context.periods[0]?.id ?? '';
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
  choosePeriod(id: string): void { this.f.period = id; this.f.original = ''; this.f.revised = ''; this.flow.invalidate(); }
  packages() { return this.closed().find(p => p.id === this.f.period)?.packages ?? []; }
  async prepareCreate(): Promise<void> {
    const f = this.f;
    if (!this.allowed() || !this.closed().some(p => p.id === f.period) || !this.packages().some(k => k.id === f.original) ||
      !this.packages().some(k => k.id === f.revised) || f.original === f.revised || !f.basis.trim() || !f.reason.trim() || !f.evidence.trim() || !f.type) return;
    await this.flow.prepare('CREATE_RESTATEMENT', f.period, null, () => ({ restatement: { clientId: this.f.client, periodId: this.f.period,
      originalPackageId: this.f.original, revisedPackageId: this.f.revised, revisedBasis: this.f.basis.trim(), reason: this.f.reason.trim(),
      evidenceReference: this.f.evidence.trim(), changeType: this.f.type, affectedPeriods: this.f.affected.split(',').map(x => x.trim().toUpperCase()).filter(Boolean).filter((x, i, xs) => xs.indexOf(x) === i).sort().join(',') } }));
  }
  async prepareApproval(id: string): Promise<void> {
    const row = this.overview.data()?.restatements.find(r => r.id === id && r.canReview);
    if (!row || !this.allowed() || this.cmd.busy()) return;
    if (this.f.client !== row.clientId || !this.restatements().some(r => r.id === id)) await this.loadClient(row.clientId);
    if (!this.restatements().some(r => r.id === id && r.canReview)) return;
    await this.flow.prepare('APPROVE_RESTATEMENT', null, id, () => ({ restatementId: id }));
  }
}
