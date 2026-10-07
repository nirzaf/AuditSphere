import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, CommandState, routeGuid } from '../../core/api';
import { Drafts } from '../../core/drafts';
import { arr, bool, dec, guid, instant, int, nat, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

export const decodeAdvanced = obj({
  scope: obj({ id: guid, groupName: text, version: int, groupRevision: int, method: text, reportingCurrency: text, status: text, approvedComponentCount: nat, approvedReviewedJournalCount: nat }),
  schedules: arr(obj({ id: guid, status: text, createdByUserId: guid, approvedByUserId: nullable(guid), createdAt: instant, approvedAt: nullable(instant), inputDigest: text }), 500),
  executions: arr(obj({ id: guid, status: text, createdByUserId: guid, approvedByUserId: nullable(guid), createdAt: instant, approvedAt: nullable(instant),
    comparativeSignedTotal: dec, currentSignedTotal: dec, outputDigest: text }), 500),
  canPrepare: bool, canReview: bool, suggestedSourceManifestJson: text });

@Component({
  selector: 'audit-advanced-consolidation',
  imports: [FormsModule, RouterLink, MatButtonModule, ...SHARED],
  template: `
    <nav aria-label="Consolidation location"><a routerLink="/app/consolidation">Consolidation</a> / <span>Advanced workflow</span></nav>
    <audit-page-header title="Advanced consolidation workflow" eyebrow="Group accounting" description="Exact approved group perimeter, schedule inputs, and consolidation execution state for this scope." />
    <audit-state [loading]="ws.loading()" [error]="ws.error()" label="the advanced consolidation workflow" />
    @if (workspace(); as w) {
      <p class="actions"><a routerLink="/app/consolidation">Group workspace</a><audit-status [value]="w.scope.status" /></p>
      <p role="status">{{ w.schedules.length }} schedules · {{ approvedSchedules(w) }} approved · {{ w.executions.length }} executions</p>
      <section class="panel" aria-labelledby="scope-heading"><h2 id="scope-heading">{{ w.scope.groupName }} · v{{ w.scope.version }}</h2>
        <dl class="facts"><dt>Method</dt><dd><code>{{ w.scope.method }}</code></dd><dt>Reporting currency</dt><dd><code>{{ w.scope.reportingCurrency }}</code></dd>
          <dt>Perimeter</dt><dd>{{ w.scope.status }} · group revision {{ w.scope.groupRevision }}</dd><dt>Approved components</dt><dd>{{ w.scope.approvedComponentCount }}</dd>
          <dt>Approved reviewed journals</dt><dd>{{ w.scope.approvedReviewedJournalCount }}</dd></dl>
        <p><small>This workflow calls the existing scope-checked commands. Approval remains maker/checker separated, source and method evidence are re-read server-side, and no action enables a professional or external release gate.</small></p></section>
      <section class="panel schedule-input" aria-labelledby="schedule-submit-heading"><h2 id="schedule-submit-heading">Submit source-bound schedule</h2>
        <p><small>Inputs are kept as a browser draft for this signed-in user. The server canonicalizes the JSON, validates the selected method and creates a submitted schedule only; it never accepts client-supplied approval state.</small></p>
        <label>Source manifest JSON * <textarea name="manifest" [(ngModel)]="manifest" (ngModelChange)="persist()" rows="10" maxlength="20000" required></textarea></label>
        <label>Method input and statement JSON * <textarea name="snapshot" [(ngModel)]="snapshot" (ngModelChange)="persist()" rows="14" maxlength="20000" required></textarea></label>
        <details><summary>Input guidance</summary><p><small>The source manifest must contain a <code>sources</code> array with each approved component's exact <code>componentId</code>, source <code>kind</code>, source <code>id</code> and package hash. Acquisition/NCI, ownership-change and asset-transfer methods also require approved reviewed-journal references. Foreign-currency and nested-group methods require their approved rate or source-run evidence. The input snapshot must contain the method-specific fields and a balanced, duplicate-free <code>statementLines</code> array.</small></p></details>
        @if (w.canPrepare) { <button matButton="filled" (click)="submit(w.scope.id)" [disabled]="cmd.busy() || cmd.uncertain()">{{ cmd.busy() ? 'Submitting…' : 'Submit schedule' }}</button> }
        @else { <p>An explicit preparer grant for this group is required to submit a schedule.</p> }</section>
      <section class="panel" aria-labelledby="schedule-list-heading"><h2 id="schedule-list-heading">Schedules</h2>
        <div class="table-scroll"><table aria-label="Advanced consolidation schedules"><thead><tr><th>Created</th><th>Status</th><th>Schedule</th><th><span class="sr-only">Action</span></th></tr></thead>
          <tbody>@for (s of w.schedules; track s.id) {
            <tr><td>{{ s.createdAt.slice(0, 16).replace('T', ' ') }} UTC<small><code>{{ s.createdByUserId }}</code></small></td><td><audit-status [value]="s.status" /></td><td><code>{{ s.id }}</code><small>input {{ s.inputDigest.slice(0, 12) }}…</small></td>
              <td>@if (s.status === 'SUBMITTED' && w.canReview) { <button matButton="filled" (click)="run('/api/ui/consolidation/schedules/' + s.id + '/approve', 'Schedule approved.')" [disabled]="cmd.busy() || cmd.uncertain()">Approve context</button> }
                @else if (s.status === 'SUBMITTED') { <small>Waiting for an independent reviewer</small> } @else { <small>Approved {{ s.approvedAt ? s.approvedAt.slice(0, 16).replace('T', ' ') + ' UTC' : '' }}</small> }</td></tr>
          } @empty { <tr><td colspan="4">No method schedule has been submitted for this scope.</td></tr> }</tbody></table></div></section>
      <section class="panel" aria-labelledby="execution-heading"><h2 id="execution-heading">Verified executions</h2>
        <p><small>Execution recalculates from the current approved schedule, components and group revision. Separate approval is required before the result is treated as ready.</small></p>
        @if (w.canPrepare && approvedSchedules(w) > 0) { <button matButton="filled" (click)="run('/api/ui/consolidation/advanced/' + w.scope.id + '/executions', 'Verified execution created.')" [disabled]="cmd.busy() || cmd.uncertain()">{{ cmd.busy() ? 'Running…' : 'Run verified execution' }}</button> }
        <div class="table-scroll"><table aria-label="Advanced consolidation executions"><thead><tr><th>Created</th><th>Status</th><th>Balances</th><th>Execution</th><th><span class="sr-only">Action</span></th></tr></thead>
          <tbody>@for (e of w.executions; track e.id) {
            <tr><td>{{ e.createdAt.slice(0, 16).replace('T', ' ') }} UTC<small><code>{{ e.createdByUserId }}</code></small></td><td><audit-status [value]="e.status" /></td>
              <td>Comparative {{ e.comparativeSignedTotal | money }}<br />Current {{ e.currentSignedTotal | money }}</td><td><code>{{ e.id }}</code><small>output {{ e.outputDigest.slice(0, 12) }}…</small></td>
              <td>@if (e.status === 'VERIFIED' && w.canReview) { <button matButton="filled" (click)="run('/api/ui/consolidation/executions/' + e.id + '/approve', 'Execution approved.')" [disabled]="cmd.busy() || cmd.uncertain()">Approve context</button> }
                @else if (e.status === 'VERIFIED') { <small>Waiting for an independent reviewer</small> } @else { <small>Approved {{ e.approvedAt ? e.approvedAt.slice(0, 16).replace('T', ' ') + ' UTC' : '' }}</small> }</td></tr>
          } @empty { <tr><td colspan="5">No verified execution exists for this scope.</td></tr> }</tbody></table></div></section>
    }
    @if (cmd.uncertain()) {
      <section class="panel recovery" aria-labelledby="outcome-heading">
        <h2 id="outcome-heading">Command outcome needs review</h2>
        <p role="alert">{{ cmd.message() }}</p>
        <p>Read the persisted schedules and executions before taking another action. AuditSphere will not resend this command automatically.</p>
        <button matButton="outlined" (click)="checkPersistedWorkspace()" [disabled]="checkingOutcome()">
          {{ checkingOutcome() ? 'Reading persisted state…' : 'Read persisted workspace' }}
        </button>
        @if (recoveryError()) { <p role="alert">{{ recoveryError() }}</p> }
        @if (checkedWorkspace(); as checked) {
          <p role="status" aria-live="polite">Persisted workspace re-read: {{ checked.schedules.length }} schedule{{ checked.schedules.length === 1 ? '' : 's' }} and {{ checked.executions.length }} execution{{ checked.executions.length === 1 ? '' : 's' }}.</p>
          <p>Review the refreshed records and their statuses above. This read did not repeat the command.</p>
          <button matButton="outlined" (click)="acknowledgeOutcome()">I reviewed this persisted state</button>
        }
      </section>
    }
    @if (recoveryAcknowledged()) { <p role="status">Persisted state reviewed. Choose the next action only after checking the current schedule and execution statuses.</p> }
    @if (!cmd.uncertain()) { <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" /> }
  `,
  styles: [`
    :host { display: block; max-width: 100%; min-width: 0; }
    .schedule-input label { display: block; max-width: 100%; }
    .schedule-input textarea { display: block; width: 100%; min-width: 0; resize: vertical; }
  `],
})
export class AdvancedConsolidation {
  private readonly api = inject(Api);
  private readonly drafts = inject(Drafts);
  readonly id = routeGuid();
  readonly ws = this.api.resource(() => (this.id() ? `/api/ui/consolidation/advanced/${this.id()}` : null), decodeAdvanced,
    'The requested consolidation scope is not available in the current firm and group grant.');
  readonly cmd = new CommandState(this.api);
  readonly checkedWorkspace = signal<ReturnType<typeof decodeAdvanced> | null>(null);
  readonly checkingOutcome = signal(false);
  readonly recoveryError = signal('');
  readonly recoveryAcknowledged = signal(false);
  readonly workspace = computed(() => this.checkedWorkspace() ?? (this.cmd.uncertain() ? null : this.ws.data()));
  manifest = '{}';
  snapshot = '{}';
  private draftFor = '';
  private recoveryScope = '';
  constructor() {
    effect(() => {
      const scopeId = this.id() ?? '';
      untracked(() => {
        if (scopeId === this.recoveryScope) return;
        this.recoveryScope = scopeId;
        this.checkedWorkspace.set(null);
        this.recoveryError.set('');
        this.recoveryAcknowledged.set(false);
        this.cmd.uncertain.set(false);
        this.cmd.failed.set(false);
        this.cmd.message.set('');
      });
    });
    effect(() => {
      const w = this.ws.data();
      untracked(() => {
        if (!w || this.draftFor === w.scope.id) return;
        this.draftFor = w.scope.id;
        const d = this.drafts.load(this.scopeKey(), (v) => (v && typeof v === 'object' && typeof (v as { manifest: unknown }).manifest === 'string' ? v as { manifest: string; snapshot: string } : null));
        this.manifest = d?.manifest ?? w.suggestedSourceManifestJson; this.snapshot = d?.snapshot ?? '{}';
      });
    });
  }
  private scopeKey(): string { return `advanced-schedule-${this.id()}`; }
  persist(): void { this.drafts.save(this.scopeKey(), { manifest: this.manifest, snapshot: this.snapshot }); }
  approvedSchedules(w: ReturnType<typeof decodeAdvanced>): number { return w.schedules.filter((s) => s.status === 'APPROVED').length; }
  async run(url: string, ok: string): Promise<void> {
    await this.executeCommand(url, {}, ok);
  }
  async submit(scopeId: string): Promise<void> {
    for (const [label, value] of [['Source manifest', this.manifest], ['Method input', this.snapshot]]) {
      try { JSON.parse(value); } catch { this.cmd.failed.set(true); this.cmd.message.set(`${label} must be valid JSON.`); return; }
    }
    await this.executeCommand(`/api/ui/consolidation/advanced/${scopeId}/schedules`,
      { sourceManifestJson: this.manifest, inputSnapshotJson: this.snapshot },
      'Schedule submitted for independent review.', () => this.drafts.clear(this.scopeKey()));
  }
  async checkPersistedWorkspace(): Promise<void> {
    const scopeId = this.id();
    if (!scopeId || !this.cmd.uncertain() || this.checkingOutcome()) return;
    this.checkingOutcome.set(true);
    this.recoveryError.set('');
    this.checkedWorkspace.set(null);
    try {
      const current = await this.api.get(`/api/ui/consolidation/advanced/${scopeId}`, decodeAdvanced);
      if (this.id() !== scopeId) return;
      this.checkedWorkspace.set(current);
    } catch (e) {
      if (this.id() === scopeId) this.recoveryError.set(e instanceof Error ? e.message : 'Persisted state could not be read. Retry the check.');
    } finally {
      this.checkingOutcome.set(false);
    }
  }
  acknowledgeOutcome(): void {
    if (!this.checkedWorkspace() || this.checkingOutcome() || !this.cmd.uncertain()) return;
    this.cmd.uncertain.set(false);
    this.cmd.failed.set(false);
    this.cmd.message.set('Persisted workspace re-read. Review its records before choosing the next action.');
    this.recoveryAcknowledged.set(true);
  }
  private async executeCommand(url: string, body: unknown, success: string, after?: (value: unknown) => void): Promise<void> {
    const scopeId = this.id();
    this.checkedWorkspace.set(null);
    this.recoveryError.set('');
    this.recoveryAcknowledged.set(false);
    try {
      await this.cmd.run(url, body, success, after);
    } finally {
      if (scopeId === this.id()) {
        if (!this.cmd.uncertain()) this.ws.reload();
      } else {
        this.cmd.uncertain.set(false);
        this.cmd.failed.set(false);
        this.cmd.message.set('');
      }
    }
  }
}
