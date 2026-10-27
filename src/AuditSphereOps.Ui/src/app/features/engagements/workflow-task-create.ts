import { Component, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { guidPattern } from '../../core/contracts';
import { SessionService } from '../../core/session';

export interface WorkflowTaskPhaseChoice {
  readonly code: string;
  readonly label: string;
}

interface ManualTaskRequest {
  title: string;
  phase: string;
  ordinal: number;
  dueDate: string | null;
  plannedEffortMinutes: number | null;
  assigneeUserId: null;
  mappingVersionId: null;
  fsliCode: null;
  assignedRole: null;
  idempotencyKey: string;
  requiredTask?: boolean;
}

interface PendingManualTask {
  engagementId: string;
  request: ManualTaskRequest;
}

function isPendingManualTask(value: unknown, engagementId: string): value is PendingManualTask {
  if (!value || typeof value !== 'object') return false;
  const pending = value as Record<string, unknown>;
  const request = pending['request'];
  if (pending['engagementId'] !== engagementId || !request || typeof request !== 'object') return false;
  const body = request as Record<string, unknown>;
  return typeof body['title'] === 'string' && body['title'].length > 0 && body['title'].length <= 300 &&
    typeof body['phase'] === 'string' && Number.isInteger(body['ordinal']) &&
    (body['dueDate'] === null || typeof body['dueDate'] === 'string') &&
    (body['plannedEffortMinutes'] === null || Number.isInteger(body['plannedEffortMinutes'])) &&
    body['assigneeUserId'] === null && body['mappingVersionId'] === null && body['fsliCode'] === null &&
    body['assignedRole'] === null && typeof body['idempotencyKey'] === 'string' && guidPattern.test(body['idempotencyKey']) &&
    (body['requiredTask'] === undefined || typeof body['requiredTask'] === 'boolean');
}

/** Creates ordinary operational work with the server's MANUAL binding; it never advances a lifecycle gate. */
@Component({
  selector: 'audit-workflow-task-create',
  imports: [FormsModule, MatButtonModule],
  template: `
    <section class="manual-task" aria-labelledby="manual-task-heading">
      <h4 id="manual-task-heading">Add manual workflow task</h4>
      <p>Creates an operational task bound to this engagement and phase. Task completion does not approve audit work or advance the engagement lifecycle.</p>
      @if (corruptPending()) {
        <p role="alert">A saved task request could not be read safely. Do not create a duplicate; ask an administrator to inspect the engagement task list.</p>
      } @else if (pending(); as saved) {
        <div role="status" aria-live="polite">
          <p>The task creation outcome is unconfirmed. Retry sends the exact saved request with the same idempotency key.</p>
          <p><strong>{{ saved.request.title }}</strong> · {{ phaseLabel(saved.request.phase) }} · order {{ saved.request.ordinal }}</p>
          <code>{{ saved.request.idempotencyKey }}</code>
          <button matButton type="button" (click)="retry()" [disabled]="busy()">Retry exact request</button>
        </div>
      } @else {
        <form (ngSubmit)="submit()" #taskForm="ngForm">
          <label>Task title
            <input name="title" [(ngModel)]="title" required maxlength="300" autocomplete="off" />
          </label>
          <label>Workflow phase
            <select name="phase" [(ngModel)]="phase" required>
              @for (choice of phases(); track choice.code) { <option [value]="choice.code">{{ choice.label }}</option> }
            </select>
          </label>
          <label>Order in phase
            <input name="ordinal" type="number" min="1" max="100000" step="1" [(ngModel)]="ordinal" required />
          </label>
          <label>Due date (optional)
            <input name="dueDate" type="date" [(ngModel)]="dueDate" />
          </label>
          <label>Planned effort in minutes (optional)
            <input name="plannedEffortMinutes" type="number" min="1" max="1000000" step="1" [(ngModel)]="plannedEffortMinutes" />
          </label>
          <label class="check"><input name="requiredTask" type="checkbox" [(ngModel)]="requiredTask" />
            Required task (counts toward engagement progress)</label>
          <button matButton="filled" type="submit" [disabled]="busy() || !taskForm.valid || !phases().length">Create workflow task</button>
        </form>
      }
      @if (message()) { <p [attr.role]="failed() ? 'alert' : 'status'">{{ message() }}</p> }
    </section>
  `,
  styles: [`
    :host { display: block; margin-block: 1rem; }
    .manual-task { border: 1px solid var(--as-line, #d8e1eb); border-radius: .625rem; padding: 1rem; background: var(--as-surface-raised, #fff); }
    h4 { margin-block: 0 .5rem; }
    form { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 14rem), 1fr)); gap: .75rem; align-items: end; }
    label { display: grid; gap: .3rem; font-weight: 600; }
    label.check { display: flex; align-items: center; gap: .5rem; min-height: 2.5rem; }
    label.check input { min-height: 0; }
    input, select { min-height: 2.5rem; max-width: 100%; box-sizing: border-box; }
    button { justify-self: start; }
    code { display: block; overflow-wrap: anywhere; margin-block: .4rem; }
  `],
})
export class WorkflowTaskCreate {
  readonly engagementId = input.required<string>();
  readonly phases = input.required<readonly WorkflowTaskPhaseChoice[]>();
  readonly created = output<void>();
  readonly pending = signal<PendingManualTask | null>(null);
  readonly corruptPending = signal(false);
  readonly busy = signal(false);
  readonly failed = signal(false);
  readonly message = signal('');
  title = '';
  phase = '';
  ordinal: number | null = 1;
  dueDate = '';
  plannedEffortMinutes: number | null = null;
  requiredTask = true;

  private readonly api = inject(Api);
  private readonly session = inject(SessionService);

  constructor() {
    effect(() => {
      const choices = this.phases();
      if (!choices.some((choice) => choice.code === this.phase)) this.phase = choices[0]?.code ?? '';
    });
    effect(() => {
      const engagementId = this.engagementId().toLowerCase();
      this.session.invalidation();
      const identity = this.session.current();
      untracked(() => this.restorePending(engagementId, identity?.firmId, identity?.userId));
    });
  }

  phaseLabel(code: string): string {
    return this.phases().find((choice) => choice.code === code)?.label ?? code;
  }

  async submit(): Promise<void> {
    if (this.busy() || this.pending() || this.corruptPending()) return;
    const engagementId = this.engagementId().toLowerCase();
    const title = this.title.trim();
    const phase = this.phase || this.phases()[0]?.code || '';
    const dueDate = this.dueDate || null;
    const ordinal = Number(this.ordinal);
    const effortText = this.plannedEffortMinutes;
    const plannedEffortMinutes = effortText === null || effortText === undefined ? null : Number(effortText);
    if (!guidPattern.test(engagementId) || !title || title.length > 300 ||
        !this.phases().some((choice) => choice.code === phase) || !Number.isInteger(ordinal) || ordinal < 1 || ordinal > 100000 ||
        dueDate !== null && !/^\d{4}-\d{2}-\d{2}$/.test(dueDate) ||
        plannedEffortMinutes !== null && (!Number.isInteger(plannedEffortMinutes) || plannedEffortMinutes < 1 || plannedEffortMinutes > 1000000)) {
      this.failed.set(true);
      this.message.set('Enter a task title and valid server-listed phase, ordering number, and optional values.');
      return;
    }
    const request: ManualTaskRequest = {
      title, phase, ordinal, dueDate, plannedEffortMinutes, assigneeUserId: null,
      mappingVersionId: null, fsliCode: null, assignedRole: null, idempotencyKey: crypto.randomUUID(),
      requiredTask: this.requiredTask,
    };
    const saved = { engagementId, request };
    try { sessionStorage.setItem(this.storageKey(engagementId), JSON.stringify(saved)); }
    catch {
      this.failed.set(true);
      this.message.set('A private recovery record could not be saved, so the task was not submitted.');
      return;
    }
    this.pending.set(saved);
    await this.send(saved);
  }

  async retry(): Promise<void> {
    const saved = this.pending();
    if (!saved || this.busy() || this.corruptPending()) return;
    await this.send(saved);
  }

  private async send(saved: PendingManualTask): Promise<void> {
    const url = `/api/ui/engagements/${saved.engagementId}/workflow/tasks`;
    this.busy.set(true); this.failed.set(false); this.message.set('');
    try {
      const outcome = await this.api.command<unknown>(url, saved.request);
      if (outcome.ok) {
        const taskId = typeof outcome.value === 'string' ? outcome.value :
          outcome.value && typeof outcome.value === 'object' && 'id' in outcome.value ? String(outcome.value['id']) : '';
        if (!guidPattern.test(taskId)) {
          this.failed.set(true);
          this.message.set('The server response did not identify the created task. Keep the recovery request and retry it exactly.');
          return;
        }
        sessionStorage.removeItem(this.storageKey(saved.engagementId));
        this.pending.set(null); this.message.set(`Workflow task created (${taskId}).`);
        this.title = ''; this.dueDate = ''; this.plannedEffortMinutes = null; this.ordinal = 1; this.requiredTask = true;
        this.created.emit();
      } else if (outcome.unknown) {
        this.failed.set(true); this.message.set('The task outcome is unconfirmed. Use Retry exact request to replay the same payload safely.');
      } else {
        sessionStorage.removeItem(this.storageKey(saved.engagementId));
        this.pending.set(null); this.failed.set(true); this.message.set(outcome.message);
      }
    } catch {
      this.failed.set(true); this.message.set('The task outcome is unconfirmed. Keep the request and retry it exactly.');
    } finally { this.busy.set(false); }
  }

  private restorePending(engagementId: string, firmId?: string, userId?: string): void {
    this.pending.set(null); this.corruptPending.set(false);
    if (!firmId || !userId || !guidPattern.test(engagementId)) return;
    let raw: string | null;
    try { raw = sessionStorage.getItem(this.storageKey(engagementId)); }
    catch { this.corruptPending.set(true); return; }
    if (!raw) return;
    try {
      const parsed: unknown = JSON.parse(raw);
      if (!isPendingManualTask(parsed, engagementId)) throw new Error('Invalid pending task request');
      this.pending.set(parsed); this.message.set('');
    } catch { this.corruptPending.set(true); }
  }

  private storageKey(engagementId: string): string {
    const session = this.session.current();
    return `auditsphere.workflow-task-create:${session?.firmId ?? 'none'}:${session?.userId ?? 'none'}:${engagementId}`;
  }
}
