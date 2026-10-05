import { Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, CommandState } from '../../core/api';
import { arr, guid, instant, int, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

export const decodeOperations = obj({ operatingMode: text, retryableStates: arr(text, 50),
  operations: arr(obj({ id: guid, kind: text, status: text, attemptCount: int, nextAttemptAt: nullable(instant), errorCode: nullable(text), cancellationDisposition: nullable(text) }), 500) });
const QUEUED = ['PENDING', 'RETRY_WAIT'], PROCESSING = ['CLAIMED', 'REMOTE_STARTED', 'VERIFYING', 'CANCEL_REQUESTED'],
  ATTENTION = ['AUTHORIZATION_BLOCKED', 'PROVIDER_BLOCKED', 'RESULT_UNCERTAIN', 'DEAD_LETTER'];

@Component({
  selector: 'audit-operations',
  imports: [FormsModule, RouterLink, MatButtonModule, ...SHARED],
  template: `
    <a routerLink="/app">← Back to portfolio</a>
    <audit-page-header title="Operations" eyebrow="Firm recovery" description="Review the current firm's redacted durable work and take guarded recovery actions." />
    <button matButton="outlined" (click)="ops.reload()" [disabled]="cmd.busy()">Refresh operations</button>
    <audit-state [loading]="ops.loading()" [error]="ops.error()" label="the durable operation ledger" />
    @if (ops.data(); as o) {
      <section class="panel" aria-labelledby="operating-mode-heading"><h2 id="operating-mode-heading">Firm operating mode</h2>
        <p><strong>{{ o.operatingMode }}</strong></p>
        <p><small>Only classification codes and durable state are shown here; payloads, request bytes and lease owner identities stay hidden. Re-arming repeats guarded validation. Cancellation is limited to queued work without a worker lease.</small></p></section>
      @if (o.operatingMode === 'RECOVERY_QUARANTINE') {
        <section class="panel" role="alert"><h2>Recovery quarantine active</h2><p>Firm operating mode is quarantined. Side effects and operations are fenced until authorized recovery is lifted.</p>
          <button matButton="filled" (click)="run('/api/ui/operations/lift-quarantine', {}, 'Recovery quarantine was lifted; operating mode is now LOCAL_ONLY.')" [disabled]="cmd.busy() || cmd.uncertain()">Lift recovery quarantine</button></section>
      }
      <p role="status">{{ count(o, queued) }} queued · {{ count(o, processing) }} processing · {{ count(o, attention) }} need attention · {{ count(o, ['COMPLETED']) }} completed · {{ count(o, ['CANCELLED_WITH_DISPOSITION']) }} cancelled</p>
      <p><small>Counts summarize only the latest {{ o.operations.length }} firm operations shown below; they are not a backlog total or worker-health verdict.</small></p>
      <section class="panel" aria-labelledby="operations-heading"><h2 id="operations-heading">Latest 50 durable operations</h2>
        <label for="operation-disposition">Cancellation disposition <input id="operation-disposition" name="disposition" [(ngModel)]="disposition" maxlength="2000" placeholder="Why is the queued operation being cancelled?" /></label>
        <div class="table-scroll"><table><caption>Latest 50 durable operations</caption><thead><tr><th scope="col">Kind</th><th scope="col">Status</th><th scope="col">Attempts</th><th scope="col">Next attempt</th><th scope="col">Code</th><th scope="col">Disposition</th><th scope="col">Action</th></tr></thead>
          <tbody>@for (x of pageRows(o.operations); track x.id) {
            <tr><td>{{ x.kind }}</td><td><audit-status [value]="x.status" /></td><td>{{ x.attemptCount }}</td><td>{{ x.nextAttemptAt ? x.nextAttemptAt.slice(0, 16).replace('T', ' ') : '—' }}</td>
              <td>{{ x.errorCode ?? '—' }}</td><td>{{ x.cancellationDisposition ?? '—' }}</td>
              <td class="actions">
                @if (queued.includes(x.status)) { <button matButton="outlined" (click)="cancel(x.id)" [disabled]="cmd.busy() || cmd.uncertain() || !disposition.trim()">Cancel queued work</button> }
                @if (o.retryableStates.includes(x.status)) { <button matButton="filled" (click)="retry(x.id)" [disabled]="cmd.busy() || cmd.uncertain()">Re-arm for retry</button> }
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
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
})
export class Operations {
  private readonly api = inject(Api);
  readonly ops = this.api.resource(() => '/api/ui/operations', decodeOperations,
    'Operations is limited to firm-wide AuditSphere Administrators. Your current account does not have that access; switch to an authorized administrator account or ask a firm administrator to review your access.');
  readonly cmd = new CommandState(this.api);
  readonly retryReconciliationId = signal<string | null>(null);
  readonly queued = QUEUED; readonly processing = PROCESSING; readonly attention = ATTENTION;
  disposition = '';
  readonly pageSizes = [10, 25, 50] as const;
  pageSize = 10;
  pageIndex = 0;
  constructor() {
    effect(() => {
      const operationId = this.retryReconciliationId();
      const operations = this.ops.data()?.operations;
      if (!operationId || this.ops.loading() || !operations) return;
      const current = operations.find((operation) => operation.id === operationId);
      if (current?.status !== 'RETRY_WAIT') return;

      this.retryReconciliationId.set(null);
      this.cmd.uncertain.set(false);
      this.cmd.failed.set(false);
      this.cmd.message.set('Re-arm confirmed from the persisted operation state.');
    });
  }
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
  run(url: string, body: unknown, ok: string): void { void this.cmd.run(url, body, ok).finally(() => this.ops.reload()); }
  async retry(id: string): Promise<void> {
    const accepted = await this.cmd.run(`/api/ui/operations/${id}/retry`, {},
      'The operation was re-armed for retry; the worker picks it up on its next attempt window.');
    if (!accepted && this.cmd.uncertain()) this.retryReconciliationId.set(id);
    this.ops.reload();
  }
  cancel(id: string): void { this.run(`/api/ui/operations/${id}/cancel`, { disposition: this.disposition }, 'The queued operation was cancelled and cannot be claimed by a worker.'); }
}
