import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { arr, guid, instant, int, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';
import { OperationsCommandState, operationStateUrl } from './operations-command-state';
import { OperationsCommandReview } from './operations-command-review';
import { decodeOperationState } from './operations-command-contracts';

export const decodeOperations = obj({ operatingMode: text, retryableStates: arr(text, 50),
  operations: arr(obj({ id: guid, kind: text, status: text, attemptCount: int, nextAttemptAt: nullable(instant), errorCode: nullable(text), cancellationDisposition: nullable(text) }), 500) });
const QUEUED = ['PENDING', 'RETRY_WAIT'], PROCESSING = ['CLAIMED', 'REMOTE_STARTED', 'VERIFYING', 'CANCEL_REQUESTED'],
  ATTENTION = ['AUTHORIZATION_BLOCKED', 'PROVIDER_BLOCKED', 'RESULT_UNCERTAIN', 'DEAD_LETTER'];
const ADMIN_ACCESS_UNAVAILABLE = 'Operations is limited to firm-wide AuditSphere Administrators. Your current account does not have that access; switch to an authorized administrator account or ask a firm administrator to review your access.';

@Component({
  selector: 'audit-operations',
  imports: [DatePipe, FormsModule, RouterLink, MatButtonModule, OperationsCommandReview, ...SHARED],
  template: `
    <a routerLink="/app">← Back to portfolio</a>
    <audit-page-header title="Operations" eyebrow="Firm recovery" description="Review the current firm's redacted durable work and take guarded recovery actions." />
    @if (!accessDenied()) {
      <button matButton="outlined" (click)="refresh()" [disabled]="cmd.busy() || ops.loading()">Refresh operations</button>
    }
    <audit-state [loading]="ops.loading()" [error]="ops.error()" label="the durable operation ledger" />
    @if (ops.data(); as o) {
      <section class="panel" aria-labelledby="operating-mode-heading"><h2 id="operating-mode-heading">Firm operating mode</h2>
        <p><strong>{{ o.operatingMode }}</strong></p>
        <p><small>Only classification codes and durable state are shown here; payloads, request bytes and lease owner identities stay hidden. Re-arming repeats guarded validation. Cancellation is limited to queued work without a worker lease.</small></p></section>
      @if (o.operatingMode === 'RECOVERY_QUARANTINE') {
        <section class="panel" role="alert"><h2>Recovery quarantine active</h2><p>Firm operating mode is quarantined. Side effects and operations are fenced until authorized recovery is lifted.</p>
          <button matButton="filled" (click)="liftQuarantine()" [disabled]="!cmd.canPrepare('LIFT_QUARANTINE') || !recovery.data()?.canLiftQuarantine">Lift recovery quarantine</button></section>
      }
      <audit-state [loading]="recovery.loading()" [error]="recovery.error()" label="current recovery eligibility" />
      @if (recovery.data(); as state) {
        @if (o.operatingMode === 'RECOVERY_QUARANTINE') {
          @for (blocker of state.liftQuarantineBlockers; track blocker) { <p>Lift quarantine: {{ blocker }}</p> }
          @for (blocker of state.approveRecoveryRestartBlockers; track blocker) { <p>Restart approval: {{ blocker }}</p> }
        }
        @if (state.canApproveRecoveryRestart) { @if (state.before.recoverySession; as session) {
          <section class="panel" aria-label="Current recovery restart"><h2>Review recovery restart</h2>
            <p>Session {{ session.id }} · external epoch {{ session.externalEpoch }}</p>
            <p>Restore point {{ session.restorePoint }} · reconciliation scope {{ session.reconciliationScope }}</p>
            <label for="operation-findings">Reconciliation findings</label><textarea id="operation-findings" [ngModel]="findings" (ngModelChange)="setFindings($event)" maxlength="10000"></textarea>
            <button matButton="outlined" (click)="approveRecoveryRestart(session.id)" [disabled]="!cmd.canPrepare('APPROVE_RECOVERY_RESTART', session.id) || !findings.trim()">Preview recovery restart approval</button>
          </section>
        } }
      }
      <p role="status">{{ count(o, queued) }} queued · {{ count(o, processing) }} processing · {{ count(o, attention) }} need attention · {{ count(o, ['COMPLETED']) }} completed · {{ count(o, ['CANCELLED_WITH_DISPOSITION']) }} cancelled</p>
      <p><small>Counts summarize only the latest {{ o.operations.length }} firm operations shown below; they are not a backlog total or worker-health verdict.</small></p>
      <section class="panel" aria-labelledby="operations-heading"><h2 id="operations-heading">Latest 50 durable operations</h2>
        <label for="operation-disposition">Cancellation disposition <input id="operation-disposition" name="disposition" [ngModel]="disposition" (ngModelChange)="setDisposition($event)" maxlength="2000" placeholder="Why is the queued operation being cancelled?" /></label>
        <div class="table-scroll"><table><caption>Latest 50 durable operations</caption><thead><tr><th scope="col">Operation</th><th scope="col">Status</th><th scope="col">Attempts</th><th scope="col">Next attempt (UTC)</th><th scope="col">Code</th><th scope="col">Disposition</th><th scope="col">Action</th></tr></thead>
          <tbody>@for (x of pageRows(o.operations); track x.id) {
            <tr><td>{{ x.kind }}<small>ID {{ x.id }}</small></td><td><audit-status [value]="x.status" /></td><td>{{ x.attemptCount }}</td><td>@if (x.nextAttemptAt) { <time [attr.datetime]="x.nextAttemptAt">{{ x.nextAttemptAt | date:'medium':'UTC' }} UTC</time> } @else { — }</td>
              <td>{{ x.errorCode ?? '—' }}</td><td>{{ x.cancellationDisposition ?? '—' }}</td>
              <td class="actions">
                @if (queued.includes(x.status)) { <button matButton="outlined" (click)="cancel(x.id)" [attr.aria-label]="'Cancel queued work for ' + x.kind + ', operation ' + x.id" [disabled]="!cmd.canPrepare('CANCEL', x.id) || !disposition.trim()">Cancel queued work</button> }
                @if (o.retryableStates.includes(x.status)) { <button matButton="filled" (click)="retry(x.id)" [attr.aria-label]="'Re-arm for retry for ' + x.kind + ', operation ' + x.id" [disabled]="!cmd.canPrepare('RETRY', x.id)">Re-arm for retry</button> }
              </td></tr>
          } @empty { <tr><td colspan="7">No durable operations are recorded for this firm.</td></tr> }</tbody></table></div>
        <nav aria-label="Durable operation pages" class="actions">
          <button type="button" matButton="outlined" (click)="previousPage()" [disabled]="pageIndex === 0">Previous</button>
          <span aria-live="polite">Page {{ pageIndex + 1 }} of {{ pageCount(o.operations.length) }} · {{ rangeStart(o.operations.length) }}–{{ rangeEnd(o.operations.length) }} of {{ o.operations.length }}</span>
          <button type="button" matButton="outlined" (click)="nextPage(o.operations.length)" [disabled]="pageIndex + 1 >= pageCount(o.operations.length)">Next</button>
          <label for="operation-page-size">Rows per page
            <select id="operation-page-size" aria-label="Rows per page" [value]="pageSize" (change)="setPageSize($event)">
              @for (size of pageSizes; track size) { <option [value]="size">{{ size }}</option> }
            </select>
          </label>
        </nav>
      </section>
    }
    <audit-operations-command-review [flow]="cmd" />
  `,
})
export class Operations {
  private readonly api = inject(Api);
  readonly ops = this.api.resource(() => '/api/ui/operations', decodeOperations,
    ADMIN_ACCESS_UNAVAILABLE);
  readonly accessDenied = computed(() => this.ops.error() === ADMIN_ACCESS_UNAVAILABLE);
  readonly recovery = this.api.resource(() => this.ops.data() ? operationStateUrl() : null, decodeOperationState);
  private readonly evidence = computed(() => {
    const operations = this.ops.data(), recovery = this.recovery.data();
    return operations && recovery ? { operations, recovery } : null;
  });
  readonly cmd = new OperationsCommandState(() => this.evidence(), () => this.ops.reload());
  readonly queued = QUEUED; readonly processing = PROCESSING; readonly attention = ATTENTION;
  private dispositionValue = ''; private findingsValue = '';
  get disposition(): string { return this.dispositionValue; }
  set disposition(value: string) { if (value !== this.dispositionValue) this.cmd.inputChanged(); this.dispositionValue = value; }
  get findings(): string { return this.findingsValue; }
  set findings(value: string) { if (value !== this.findingsValue) this.cmd.inputChanged(); this.findingsValue = value; }
  readonly pageSizes = [10, 25, 50] as const;
  pageSize = 10; pageIndex = 0;
  constructor() {
    effect(() => {
      const data = this.ops.data();
      untracked(() => { this.pageIndex = 0; if (!data) { this.disposition = ''; this.findings = ''; } });
    });
  }
  refresh(): void { this.cmd.invalidate(true); this.ops.reload(); }
  setDisposition(value: string): void { this.disposition = value; }
  setFindings(value: string): void { this.findings = value; }
  count(o: ReturnType<typeof decodeOperations>, states: string[]): number { return o.operations.filter((x) => states.includes(x.status)).length; }
  pageRows<T>(rows: T[]): T[] { const start = this.pageIndex * this.pageSize; return rows.slice(start, start + this.pageSize); }
  pageCount(count: number): number { return Math.max(1, Math.ceil(count / this.pageSize)); }
  rangeStart(count: number): number { return count === 0 ? 0 : this.pageIndex * this.pageSize + 1; }
  rangeEnd(count: number): number { return Math.min(count, (this.pageIndex + 1) * this.pageSize); }
  previousPage(): void { this.pageIndex = Math.max(0, this.pageIndex - 1); }
  nextPage(count: number): void { this.pageIndex = Math.min(this.pageCount(count) - 1, this.pageIndex + 1); }
  setPageSize(event: Event): void {
    const size = Number((event.target as HTMLSelectElement).value);
    if ((this.pageSizes as readonly number[]).includes(size)) { this.pageSize = size; this.pageIndex = 0; }
  }
  async retry(id: string): Promise<void> {
    const data = this.ops.data(), row = data?.operations.find(x => x.id === id);
    if (!row || !data?.retryableStates.includes(row.status)) return;
    await this.cmd.prepare(() => ({ action: 'RETRY', operationId: id }));
  }
  async cancel(id: string): Promise<void> {
    if (!this.ops.data()?.operations.some(x => x.id === id && QUEUED.includes(x.status))) return;
    await this.cmd.prepare(() => ({ action: 'CANCEL', operationId: id, disposition: this.disposition }));
  }
  async liftQuarantine(): Promise<void> {
    if (!this.recovery.data()?.canLiftQuarantine) return;
    await this.cmd.prepare(() => ({ action: 'LIFT_QUARANTINE', recoverySessionId: this.recovery.data()?.before.recoverySession?.id ?? null }));
  }
  async approveRecoveryRestart(id: string): Promise<void> {
    const state = this.recovery.data();
    if (!state?.canApproveRecoveryRestart || state.before.recoverySession?.id !== id) return;
    await this.cmd.prepare(() => ({ action: 'APPROVE_RECOVERY_RESTART', recoverySessionId: id, findings: this.findings }));
  }
}
