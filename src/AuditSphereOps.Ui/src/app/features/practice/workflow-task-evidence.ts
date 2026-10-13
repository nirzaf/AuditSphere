import { Component, effect, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { Api } from '../../core/api';
import { arr, bool, guid, instant, int, nullable, obj, oneOf, str } from '../../core/decode';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';

const kind = oneOf('CURRENT_MATERIALITY_APPROVED', 'PBC_ACCEPTED_OR_CLOSED', 'CURRENT_PROCEDURE_REVIEWED');
const taskEvidence = obj({ engagementId: guid, taskId: guid, taskRevision: int, canManage: bool,
  rules: arr(obj({ id: guid, kind, sourceId: nullable(guid), state: oneOf('REQUIRED', 'WITHDRAWN'),
    version: int, satisfied: bool, sourceState: str(40), message: str(1000), sourceRevision: nullable(int),
    inputGeneration: nullable(int), reason: str(1000), occurredAt: instant }), 8) });
export const decodeTaskEvidence = (value: unknown, path: string) => {
  const result = taskEvidence(value, path);
  const references = new Set<string>();
  if (result.taskRevision < 1) throw new Error('Invalid task revision.');
  for (const rule of result.rules) {
    const reference = `${rule.kind}:${rule.sourceId}`;
    if (references.has(reference) || rule.version < 1 || !rule.reason.trim() ||
      (rule.sourceRevision !== null && rule.sourceRevision < 1) || (rule.inputGeneration !== null && rule.inputGeneration < 1) ||
      ((rule.kind === 'CURRENT_MATERIALITY_APPROVED') !== (rule.sourceId === null))) throw new Error('Invalid prerequisite reference.');
    references.add(reference);
    if (rule.satisfied && !(rule.kind === 'CURRENT_MATERIALITY_APPROVED' ? rule.sourceState === 'APPROVED' :
      rule.kind === 'PBC_ACCEPTED_OR_CLOSED' ? ['ACCEPTED', 'CLOSED'].includes(rule.sourceState) : rule.sourceState === 'REVIEWED_CURRENT'))
      throw new Error('Evidence state does not support satisfaction.');
  }
  return result;
};
export const decodeEvidenceReceipt = obj({ engagementId: guid, taskId: guid, idempotencyKey: guid,
  ruleRevisionId: guid, taskRevision: int });
type Kind = ReturnType<typeof kind>;
type Change = { kind: Kind; sourceId: string | null; required: boolean; reason: string; expectedRevision: number; idempotencyKey: string };

@Component({
  selector: 'audit-workflow-task-evidence',
  imports: [FormsModule, MatButtonModule, RouterLink, ...SHARED],
  template: `
    <section aria-labelledby="evidence-heading" class="panel">
      <h2 id="evidence-heading">Evidence prerequisites</h2>
      <p>{{ title() }}. Sources are evaluated live. Completing this operational task never approves audit evidence or advances the engagement.</p>
      <button matButton (click)="workspace.reload()" [disabled]="busy()">Refresh prerequisites</button>
      <audit-state [loading]="workspace.loading()" [error]="workspace.error()" label="evidence prerequisites" />
      @if (workspace.data(); as data) {
        <ul>
          @for (rule of data.rules; track rule.id) {
            <li>
              <strong>{{ label(rule.kind) }}</strong> · {{ rule.state === 'WITHDRAWN' ? 'Withdrawn' : rule.satisfied ? 'Satisfied' : 'Pending' }}
              <p>{{ rule.message }}@if (rule.sourceRevision !== null) { · Result revision {{ rule.sourceRevision }} }@if (rule.inputGeneration !== null) { · Current generation {{ rule.inputGeneration }} }</p>
              @if (rule.sourceId) { <small>Source reference: {{ rule.sourceId }}</small> }
              <small>Change version {{ rule.version }} · {{ rule.reason }}</small>
              @if (data.canManage && !pending()) {
                <button matButton (click)="prepare(rule.kind, rule.sourceId, rule.state === 'WITHDRAWN')" [disabled]="busy()">
                  {{ rule.state === 'WITHDRAWN' ? 'Require again' : 'Review withdrawal' }}
                </button>
              }
            </li>
          } @empty { <li>No domain-evidence prerequisites are recorded for this task.</li> }
        </ul>
        <p class="links"><a matButton [routerLink]="['/app/engagements', engagementId(), 'audit-plan']">Audit planning</a>
          <a matButton [routerLink]="['/app/engagements', engagementId(), 'pbc']">Client requests</a>
          <a matButton [routerLink]="['/app/engagements', engagementId(), 'audit-fieldwork']">Fieldwork</a></p>
        @if (data.canManage && !pending()) {
          @if (review(); as change) {
            <section aria-label="Review prerequisite change">
              <h3>Review exact change</h3>
              <p>{{ change.required ? 'Require' : 'Withdraw' }} {{ label(change.kind) }}@if (change.sourceId) { · {{ change.sourceId }} }</p>
              <p>{{ change.reason }} · Task revision {{ change.expectedRevision }}</p>
              <label><input type="checkbox" [(ngModel)]="confirmed" /> I reviewed this exact prerequisite change.</label>
              <div class="links"><button matButton="filled" (click)="submit()" [disabled]="!confirmed || busy() || data.taskRevision !== change.expectedRevision">Save reviewed change</button>
                <button matButton (click)="review.set(null); confirmed = false" [disabled]="busy()">Back to editing</button></div>
            </section>
          } @else {
            <form (submit)="$event.preventDefault(); prepare(selectedKind, source.trim() || null, true)">
              <h3>Require authoritative evidence</h3>
              <label>Evidence source<select name="kind" [(ngModel)]="selectedKind" (ngModelChange)="source = ''">
                <option value="CURRENT_MATERIALITY_APPROVED">Current approved materiality</option>
                <option value="PBC_ACCEPTED_OR_CLOSED">Accepted or closed client request</option>
                <option value="CURRENT_PROCEDURE_REVIEWED">Current independently reviewed procedure</option>
              </select></label>
              @if (selectedKind !== 'CURRENT_MATERIALITY_APPROVED') {
                <label>Exact source reference<input name="source" [(ngModel)]="source" maxlength="36" required aria-describedby="source-help" /></label>
                <small id="source-help">Use the request or procedure identifier from this engagement's authorized workspace.</small>
              }
              <label>Reason for the change<textarea name="reason" [(ngModel)]="reason" maxlength="1000" required rows="3"></textarea></label>
              <button matButton="filled" type="submit" [disabled]="busy()">Review prerequisite</button>
            </form>
          }
        }
        @if (pending()) {
          <p role="status">A change request is awaiting a confirmed receipt. Do not submit another change.</p>
          <button matButton (click)="recover()" [disabled]="busy()">Read saved prerequisite change</button>
        }
      }
      <audit-command-message [message]="message()" [failed]="failed()" />
    </section>
  `,
  styles: `:host { display: block; } .panel { margin-block: 1rem; padding: 1rem; border: 1px solid var(--line); border-radius: .5rem; }
    form, label { display: grid; gap: .5rem; } form { max-width: 42rem; gap: 1rem; } input, select, textarea { min-width: 0; max-width: 100%; }
    li { padding-block: .75rem; overflow-wrap: anywhere; } small { display: block; } .links { display: flex; flex-wrap: wrap; gap: .5rem; }`,
})
export class WorkflowTaskEvidencePanel {
  readonly engagementId = input.required<string>();
  readonly taskId = input.required<string>();
  readonly title = input.required<string>();
  readonly changed = output<void>();
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  readonly workspace = this.api.resource(() => this.url(), (value, path) => {
    const data = decodeTaskEvidence(value, path);
    if (data.engagementId !== this.engagementId() || data.taskId !== this.taskId()) throw new Error('The task scope changed.');
    return data;
  }, 'Prerequisites are unavailable in your current task scope.');
  readonly review = signal<Change | null>(null);
  readonly pending = signal<string | null>(null);
  readonly busy = signal(false);
  readonly message = signal('');
  readonly failed = signal(false);
  selectedKind: Kind = 'CURRENT_MATERIALITY_APPROVED';
  source = '';
  reason = '';
  confirmed = false;

  constructor() {
    effect(() => {
      this.engagementId(); this.taskId(); this.session.invalidation(); this.session.current();
      this.review.set(null); this.message.set(''); this.failed.set(false);
      this.reason = ''; this.source = ''; this.confirmed = false;
      try { const saved = sessionStorage.getItem(this.storageKey()); this.pending.set(saved ? guid(saved, 'saved request key') : null); }
      catch { this.pending.set(null); }
    });
  }

  label(value: string): string {
    return ({ CURRENT_MATERIALITY_APPROVED: 'Current approved materiality', PBC_ACCEPTED_OR_CLOSED: 'Accepted or closed client request',
      CURRENT_PROCEDURE_REVIEWED: 'Current independently reviewed procedure' } as Record<string, string>)[value] ?? value;
  }

  prepare(kindValue: Kind, sourceId: string | null, required: boolean): void {
    const data = this.workspace.data();
    if (!data?.canManage || this.busy() || this.pending()) return;
    this.failed.set(false); this.message.set('');
    try {
      kind(kindValue, 'kind');
      sourceId = kindValue === 'CURRENT_MATERIALITY_APPROVED' ? null : guid(sourceId, 'source reference');
      const reason = this.reason.trim();
      if (!reason || reason.length > 1000) throw new Error('Enter a reason of 1 to 1,000 characters before reviewing the change.');
      this.review.set({ kind: kindValue, sourceId, required, reason, expectedRevision: data.taskRevision, idempotencyKey: crypto.randomUUID() });
      this.confirmed = false;
    } catch {
      this.failed.set(true); this.message.set('Enter a reason and a valid exact source reference before reviewing this change.');
    }
  }

  async submit(): Promise<void> {
    const change = this.review(); const data = this.workspace.data();
    if (!change || !data?.canManage || !this.confirmed || this.busy() || this.pending() || data.taskRevision !== change.expectedRevision) return;
    const key = this.storageKey(); const url = this.url(); const generation = this.session.invalidation();
    try { sessionStorage.setItem(key, change.idempotencyKey); }
    catch { this.failed.set(true); this.message.set('A recovery key could not be saved, so no change was submitted.'); return; }
    this.pending.set(change.idempotencyKey); this.busy.set(true);
    try {
      const result = await this.api.command<unknown>(url, change);
      if (generation !== this.session.invalidation() || url !== this.url()) return;
      if (result.ok) this.accept(result.value, change.idempotencyKey, key);
      else {
        this.failed.set(true); this.message.set(result.message);
        if (!result.unknown) { sessionStorage.removeItem(key); this.pending.set(null); this.review.set(null); this.workspace.reload(); }
      }
    } catch { this.failed.set(true); this.message.set('The change outcome is unconfirmed. Read its saved receipt before another action.'); }
    finally { this.busy.set(false); }
  }

  async recover(): Promise<void> {
    const key = this.pending(); if (!key || this.busy()) return;
    const url = this.url(); const storageKey = this.storageKey(); const generation = this.session.invalidation();
    this.busy.set(true);
    try {
      const value = await this.api.get(`${url}/receipts/${encodeURIComponent(key)}`, decodeEvidenceReceipt);
      if (generation === this.session.invalidation() && url === this.url()) this.accept(value, key, storageKey);
    } catch { this.failed.set(true); this.message.set('A saved receipt was not confirmed. Keep the recovery key and review the task; no change was resent.'); }
    finally { this.busy.set(false); }
  }

  private accept(value: unknown, key: string, storageKey: string): void {
    const receipt = decodeEvidenceReceipt(value, 'receipt');
    if (receipt.engagementId !== this.engagementId() || receipt.taskId !== this.taskId() || receipt.idempotencyKey !== key)
      throw new Error('Receipt did not match the reviewed change.');
    sessionStorage.removeItem(storageKey); this.pending.set(null); this.review.set(null);
    this.reason = ''; this.confirmed = false; this.failed.set(false); this.message.set('Prerequisite change confirmed.');
    this.workspace.reload(); this.changed.emit();
  }

  private url(): string { return `/api/ui/engagements/${this.engagementId()}/workflow/tasks/${this.taskId()}/evidence`; }
  private storageKey(): string { return `auditsphere.workflow-evidence:${this.session.current()?.firmId ?? 'none'}:${this.session.current()?.userId ?? 'none'}:${this.engagementId()}:${this.taskId()}`; }
}
