import { Component, inject } from '@angular/core';
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
          <button matButton="filled" (click)="run('/api/ui/operations/lift-quarantine', {}, 'Recovery quarantine was lifted; operating mode is now LOCAL_ONLY.')" [disabled]="cmd.busy()">Lift recovery quarantine</button></section>
      }
      <p role="status">{{ count(o, queued) }} queued · {{ count(o, processing) }} processing · {{ count(o, attention) }} need attention · {{ count(o, ['COMPLETED']) }} completed · {{ count(o, ['CANCELLED_WITH_DISPOSITION']) }} cancelled</p>
      <p><small>Counts summarize only the latest {{ o.operations.length }} firm operations shown below; they are not a backlog total or worker-health verdict.</small></p>
      <section class="panel" aria-labelledby="operations-heading"><h2 id="operations-heading">Latest 50 durable operations</h2>
        <label>Cancellation disposition <input name="disposition" [(ngModel)]="disposition" maxlength="2000" placeholder="Why is the queued operation being cancelled?" /></label>
        <div class="table-scroll"><table><thead><tr><th>Kind</th><th>Status</th><th>Attempts</th><th>Next attempt</th><th>Code</th><th>Disposition</th><th>Action</th></tr></thead>
          <tbody>@for (x of o.operations; track x.id) {
            <tr><td>{{ x.kind }}</td><td><audit-status [value]="x.status" /></td><td>{{ x.attemptCount }}</td><td>{{ x.nextAttemptAt ? x.nextAttemptAt.slice(0, 16).replace('T', ' ') : '—' }}</td>
              <td>{{ x.errorCode ?? '—' }}</td><td>{{ x.cancellationDisposition ?? '—' }}</td>
              <td class="actions">
                @if (queued.includes(x.status)) { <button matButton="outlined" (click)="cancel(x.id)" [disabled]="cmd.busy() || !disposition.trim()">Cancel queued work</button> }
                @if (o.retryableStates.includes(x.status)) { <button matButton="filled" (click)="run('/api/ui/operations/' + x.id + '/retry', {}, 'The operation was re-armed for retry; the worker picks it up on its next attempt window.')" [disabled]="cmd.busy()">Re-arm for retry</button> }
              </td></tr>
          } @empty { <tr><td colspan="7">No durable operations are recorded for this firm.</td></tr> }</tbody></table></div></section>
    }
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
})
export class Operations {
  private readonly api = inject(Api);
  readonly ops = this.api.resource(() => '/api/ui/operations', decodeOperations, 'Sign in with an authorized administrator identity to view operations.');
  readonly cmd = new CommandState(this.api);
  readonly queued = QUEUED; readonly processing = PROCESSING; readonly attention = ATTENTION;
  disposition = '';
  count(o: ReturnType<typeof decodeOperations>, states: string[]): number { return o.operations.filter((x) => states.includes(x.status)).length; }
  run(url: string, body: unknown, ok: string): void { void this.cmd.run(url, body, ok).finally(() => this.ops.reload()); }
  cancel(id: string): void { this.run(`/api/ui/operations/${id}/cancel`, { disposition: this.disposition }, 'The queued operation was cancelled and cannot be claimed by a worker.'); }
}
