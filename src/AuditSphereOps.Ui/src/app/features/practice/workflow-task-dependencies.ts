import { Component, DestroyRef, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { arr, bool, guid, int, obj, str } from '../../core/decode';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';

const dependencyState = obj({ engagementId: guid, taskId: guid, taskRevision: int, canManage: bool,
  tasks: arr(obj({ taskId: guid, bindingId: guid, title: str(300), status: str(40), revision: int }), 500),
  references: arr(obj({ predecessorTaskId: guid, required: bool, version: int, reason: str(1000) }), 500) });
export const decodeTaskDependencies = (value: unknown, path: string) => {
  const data = dependencyState(value, path); const ids = new Set(data.tasks.map(t => t.taskId));
  if (ids.size !== data.tasks.length || new Set(data.tasks.map(t => t.bindingId)).size !== data.tasks.length ||
    data.taskRevision < 1 || data.tasks.some(t => t.revision < 1 || !t.title.trim()) ||
    data.tasks.find(t => t.taskId === data.taskId)?.revision !== data.taskRevision ||
    new Set(data.references.map(r => r.predecessorTaskId)).size !== data.references.length ||
    data.references.some(r => r.predecessorTaskId === data.taskId || !ids.has(r.predecessorTaskId) || r.version < 0 || !r.reason.trim()))
    throw new Error('Invalid dependency graph reference.');
  return data;
};
const receiptDecoder = obj({ engagementId: guid, taskId: guid, idempotencyKey: guid, changeId: guid, taskRevision: int });
type Change = { predecessorTaskId: string; required: boolean; expectedRevision: number; expectedPredecessorRevision: number;
  reason: string; idempotencyKey: string; predecessorTitle: string };

@Component({
  selector: 'audit-workflow-task-dependencies',
  imports: [FormsModule, MatButtonModule, ...SHARED],
  template: `
    <section aria-labelledby="dependency-heading" class="panel">
      <h2 id="dependency-heading">Task ordering</h2>
      <p>{{ title() }}. Dependencies control operational completion only. Professional approvals and the pinned template remain unchanged.</p>
      <button matButton (click)="workspace.reload()" [disabled]="busy()">Refresh task ordering</button>
      <audit-state [loading]="workspace.loading()" [error]="workspace.error()" label="task ordering" />
      @if (workspace.data(); as data) {
        <ul>@for (reference of data.references; track reference.predecessorTaskId) {
          <li><strong>{{ predecessorTitle(reference.predecessorTaskId) }}</strong> · {{ reference.required ? 'Required first' : 'Withdrawn' }}
            <p>{{ reference.version === 0 ? 'Pinned template edge' : 'Amendment version ' + reference.version }} · {{ reference.reason }}</p>
            @if (data.canManage && !pending()) {
              <button matButton (click)="prepare(reference.predecessorTaskId, !reference.required)" [disabled]="busy()">
                {{ reference.required ? 'Review dependency withdrawal' : 'Review requirement' }}
              </button>
            }
          </li>
        } @empty { <li>No task prerequisites are recorded.</li> }</ul>
        @if (data.canManage && !pending()) {
          @if (review(); as change) {
            <section aria-label="Review task dependency change">
              <h3>Review exact ordering change</h3>
              <p>{{ change.required ? 'Require first' : 'Withdraw' }} {{ change.predecessorTitle }} · {{ change.predecessorTaskId }} · revision {{ change.expectedPredecessorRevision }}</p>
              <p>Successor: {{ title() }} · revision {{ change.expectedRevision }}</p><p>{{ change.reason }}</p>
              <label><input type="checkbox" [(ngModel)]="confirmed" /> I reviewed this exact task ordering change.</label>
              <div class="actions"><button matButton="filled" (click)="submit()" [disabled]="!confirmed || busy() || data.taskRevision !== change.expectedRevision || predecessorRevision(change.predecessorTaskId) !== change.expectedPredecessorRevision">Save reviewed ordering</button>
                <button matButton (click)="review.set(null); confirmed = false" [disabled]="busy()">Back to ordering</button></div>
            </section>
          } @else {
            <form (submit)="$event.preventDefault(); prepare(selected, required)">
              <label for="ordering-predecessor">Predecessor task</label><select id="ordering-predecessor" name="predecessor" [(ngModel)]="selected" required>
                <option value="">Choose an exact task</option>
                @for (task of data.tasks; track task.taskId) { @if (task.taskId !== taskId()) {
                  <option [value]="task.taskId">{{ task.title }} · {{ task.status }} · {{ task.taskId }}</option>
                } }
              </select>
              <label for="ordering-action">Ordering action</label><select id="ordering-action" name="required" [(ngModel)]="required"><option [ngValue]="true">Require predecessor completion</option><option [ngValue]="false">Withdraw dependency</option></select>
              <label>Reason for ordering change<textarea name="reason" [(ngModel)]="reason" maxlength="1000" required rows="3"></textarea></label>
              <button matButton="filled" type="submit" [disabled]="busy()">Review task ordering</button>
            </form>
          }
        }
        @if (pending()) {
          <p role="status">The ordering outcome is unconfirmed. Read its saved receipt before another change.</p>
          <button matButton (click)="recover()" [disabled]="busy()">Read saved ordering change</button>
        }
      }
      <audit-command-message [message]="message()" [failed]="failed()" />
    </section>
  `,
  styles: `:host { display: block; } .panel { margin-block: 1rem; padding: 1rem; border: 1px solid var(--line); border-radius: .5rem; }
    form, label { display: grid; gap: .5rem; } form { max-width: 42rem; gap: 1rem; } select, textarea { min-width: 0; max-width: 100%; }
    li, p { overflow-wrap: anywhere; } .actions { display: flex; flex-wrap: wrap; gap: .5rem; }`,
})
export class WorkflowTaskDependenciesPanel {
  readonly engagementId = input.required<string>(); readonly taskId = input.required<string>(); readonly title = input.required<string>();
  readonly accessCurrent = input<() => boolean>(() => true);
  readonly accessRevision = input<() => number>(() => 0);
  readonly changed = output<void>(); private readonly api = inject(Api); private readonly session = inject(SessionService);
  private destroyed = false;
  private workspaceRevision = 0;
  private verifiedRevision = -1;
  private readonly workspaceResource = this.api.resource(() => {
    this.workspaceRevision = this.accessRevision()();
    return this.accessCurrent()() ? this.url() : null;
  }, (value, path) => {
    if (!this.accessCurrent()() || this.workspaceRevision !== this.accessRevision()()) throw new Error('Task access changed.');
    const data = decodeTaskDependencies(value, path);
    if (data.engagementId !== this.engagementId() || data.taskId !== this.taskId()) throw new Error('Task scope changed.');
    this.verifiedRevision = this.accessRevision()();
    return data;
  }, 'Task ordering is unavailable in your current scope.');
  readonly workspace = { ...this.workspaceResource, reload: () => {
    if (this.accessCurrent()() && this.workspaceRevision === this.accessRevision()()) this.workspaceResource.reload();
  } };
  authorizedWorkspace(): boolean {
    const data = this.workspace.data();
    return this.accessCurrent()() && this.verifiedRevision === this.accessRevision()() && !!data &&
      data.engagementId === this.engagementId() && data.taskId === this.taskId();
  }
  readonly review = signal<Change | null>(null); readonly pending = signal<string | null>(null);
  readonly busy = signal(false); readonly message = signal(''); readonly failed = signal(false);
  selected = ''; required = true; reason = ''; confirmed = false;

  constructor() {
    effect(() => {
      this.engagementId(); this.taskId(); this.session.invalidation(); this.session.current();
      this.review.set(null); this.message.set(''); this.failed.set(false); this.reason = ''; this.selected = ''; this.confirmed = false;
      try { const saved = sessionStorage.getItem(this.storageKey()); this.pending.set(saved ? guid(saved, 'saved ordering key') : null); }
      catch { this.pending.set(null); }
    });
    effect(() => {
      this.accessRevision()(); this.accessCurrent()();
      untracked(() => { this.review.set(null); this.confirmed = false; });
    });
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; });
  }
  predecessorTitle(id: string): string { return this.workspace.data()?.tasks.find(t => t.taskId === id)?.title ?? 'Unavailable task'; }
  predecessorRevision(id: string): number | undefined { return this.workspace.data()?.tasks.find(t => t.taskId === id)?.revision; }
  prepare(predecessorTaskId: string, required: boolean): void {
    const data = this.workspace.data(); const predecessor = data?.tasks.find(t => t.taskId === predecessorTaskId);
    if (!this.accessCurrent()() || this.workspaceRevision !== this.accessRevision()() || !data?.canManage || this.pending() || this.busy()) return;
    this.failed.set(false); this.message.set('');
    if (!predecessor || predecessorTaskId === this.taskId() || !this.reason.trim() || this.reason.trim().length > 1000 ||
      (required && predecessor.status === 'CANCELLED')) {
      this.failed.set(true); this.message.set('Choose an eligible different task and enter a reason before reviewing.'); return;
    }
    this.review.set({ predecessorTaskId, required, expectedRevision: data.taskRevision, expectedPredecessorRevision: predecessor.revision,
      predecessorTitle: predecessor.title, reason: this.reason.trim(), idempotencyKey: crypto.randomUUID() }); this.confirmed = false;
  }
  async submit(): Promise<void> {
    const change = this.review(); const data = this.workspace.data();
    if (!this.accessCurrent()() || this.workspaceRevision !== this.accessRevision()() || !change || !data?.canManage || !this.confirmed || this.pending() || this.busy() || data.taskRevision !== change.expectedRevision ||
      this.predecessorRevision(change.predecessorTaskId) !== change.expectedPredecessorRevision) return;
    const url = this.url(); const key = this.storageKey(); const generation = this.session.invalidation();
    const accessRevision = this.accessRevision()();
    try { sessionStorage.setItem(key, change.idempotencyKey); }
    catch { this.failed.set(true); this.message.set('No recovery key could be saved, so no change was submitted.'); return; }
    this.pending.set(change.idempotencyKey); this.busy.set(true);
    try {
      const { predecessorTitle: _title, ...body } = change;
      const result = await this.api.command<unknown>(url, body);
      if (!this.active(key, generation, url)) return;
      if (result.ok && (!this.accessCurrent()() || accessRevision !== this.accessRevision()())) {
        this.message.set('Access changed. Read the saved receipt after access returns.');
      } else if (result.ok) this.accept(result.value, change.idempotencyKey, key);
      else {
        this.failed.set(true); this.message.set(result.message);
        if (!result.unknown) { sessionStorage.removeItem(key); this.pending.set(null); this.review.set(null); this.workspace.reload(); }
      }
    } catch { if (this.active(key, generation, url)) { this.failed.set(true); this.message.set('The ordering outcome is unconfirmed. Read its receipt; no change was resent.'); } }
    finally { this.busy.set(false); }
  }
  async recover(): Promise<void> {
    const pending = this.pending(); if (!this.accessCurrent()() || this.workspaceRevision !== this.accessRevision()() || !this.workspace.data() || !pending || this.busy()) return;
    const url = this.url(); const key = this.storageKey(); const generation = this.session.invalidation(); this.busy.set(true);
    const accessRevision = this.accessRevision()();
    try {
      const receipt = await this.api.get(`${url}/receipts/${encodeURIComponent(pending)}`, receiptDecoder);
      if (this.active(key, generation, url) && this.accessCurrent()() && accessRevision === this.accessRevision()()) this.accept(receipt, pending, key);
    } catch { if (this.active(key, generation, url) && this.accessCurrent()() && accessRevision === this.accessRevision()()) { this.failed.set(true); this.message.set('No receipt confirmed. Keep its recovery key; no change was resent.'); } }
    finally { this.busy.set(false); }
  }
  private accept(value: unknown, id: string, key: string): void {
    const receipt = receiptDecoder(value, 'ordering receipt');
    if (receipt.taskRevision < 2 || receipt.engagementId !== this.engagementId() || receipt.taskId !== this.taskId() || receipt.idempotencyKey !== id)
      throw new Error('Ordering receipt does not match the reviewed change.');
    sessionStorage.removeItem(key); this.pending.set(null); this.review.set(null); this.reason = ''; this.confirmed = false;
    this.failed.set(false); this.message.set('Task ordering change confirmed.'); this.workspace.reload(); this.changed.emit();
  }
  private url(): string { return `/api/ui/engagements/${this.engagementId()}/workflow/tasks/${this.taskId()}/dependencies`; }
  private active(key: string, generation: number, url: string): boolean {
    return !this.destroyed && generation === this.session.invalidation() && key === this.storageKey() && url === this.url();
  }
  private storageKey(): string { return `auditsphere.workflow-ordering:${this.session.current()?.firmId ?? 'none'}:${this.session.current()?.userId ?? 'none'}:${this.engagementId()}:${this.taskId()}`; }
}
