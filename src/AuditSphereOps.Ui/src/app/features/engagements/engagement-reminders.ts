import { Component, DestroyRef, inject, input } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { SessionService } from '../../core/session';
import { arr, bool, date, guid, nullable, obj, str } from '../../core/decode';
import { SHARED } from '../../core/ui';

const reminderKinds = new Set([
  'TASK_DUE', 'CLIENT_WAIT', 'FIELDWORK_START', 'DRAFT_REPORT', 'FINAL_REPORT', 'STATUTORY_CUTOFF',
]);
const reminderTones = new Set(['OVERDUE', 'DUE_TODAY', 'UPCOMING', 'PENDING']);

const reminderRow = obj({
  sourceId: guid,
  kind: str(40),
  title: str(300),
  dueDate: nullable(date),
  tone: str(40),
  message: str(500),
  sourceHref: str(300),
});

const reminderResponse = obj({
  engagementId: guid,
  asOfDateUtc: date,
  items: arr(reminderRow, 50),
  truncated: bool,
});

export interface EngagementDeadlineReminderRow {
  sourceId: string;
  kind: string;
  title: string;
  dueDate: string | null;
  tone: string;
  message: string;
  sourceHref: string;
}

export interface EngagementDeadlineReminderData {
  engagementId: string;
  asOfDateUtc: string;
  items: EngagementDeadlineReminderRow[];
  truncated: boolean;
}

export function decodeEngagementDeadlineReminders(
  value: unknown, expectedEngagementId: string, path = '',
): EngagementDeadlineReminderData {
  const decoded = reminderResponse(value, path);
  const engagementId = guid(expectedEngagementId, 'engagementId').toLowerCase();
  if (decoded.engagementId.toLowerCase() !== engagementId)
    throw new Error('Deadline reminder engagement context mismatch.');
  assertDateOnly(decoded.asOfDateUtc, 'asOfDateUtc');

  const raw = value as Record<string, unknown>;
  if (!Array.isArray(raw['items'])) throw new Error('Deadline reminders must be a list.');
  for (let index = 0; index < decoded.items.length; index++) {
    const reminder = decoded.items[index];
    const rawReminder = raw['items'][index];
    if (!rawReminder || typeof rawReminder !== 'object' || Array.isArray(rawReminder) ||
        !Object.prototype.hasOwnProperty.call(rawReminder, 'dueDate'))
      throw new Error('Deadline reminder dueDate must be explicit.');
    if (!reminderKinds.has(reminder.kind) || !reminderTones.has(reminder.tone) ||
        !reminder.title.trim() || !reminder.message.trim())
      throw new Error('Unsupported deadline reminder values.');
    if (reminder.dueDate !== null) assertDateOnly(reminder.dueDate, `items[${index}].dueDate`);

    const isClientWait = reminder.kind === 'CLIENT_WAIT';
    if (isClientWait !== (reminder.dueDate === null) || isClientWait !== (reminder.tone === 'PENDING'))
      throw new Error('Deadline reminder date and tone do not match its kind.');
    const expectedHref = reminder.kind === 'TASK_DUE'
      ? `/app/work?engagementId=${engagementId}&taskId=${reminder.sourceId.toLowerCase()}`
      : isClientWait
        ? `/app/engagements/${engagementId}/pbc`
        : `/app/engagements/${engagementId}/audit-plan`;
    if (reminder.sourceHref !== expectedHref)
      throw new Error('Deadline reminder link is outside its allowed workspace.');
  }
  return { ...decoded, engagementId };
}

function assertDateOnly(value: string, field: string): void {
  const parsed = new Date(`${value}T00:00:00.000Z`);
  if (value.startsWith('0000') || Number.isNaN(parsed.valueOf()) || parsed.toISOString().slice(0, 10) !== value)
    throw new Error(`${field} is outside the supported date range.`);
}

@Component({
  selector: 'audit-engagement-reminders',
  imports: [RouterLink, MatButtonModule, ...SHARED],
  template: `
    <section class="panel" aria-labelledby="deadline-reminders-heading">
      <div class="workflow-header">
        <h3 id="deadline-reminders-heading">Deadline reminders</h3>
        <button matButton (click)="refresh()" [disabled]="reminders.loading()">Refresh reminders</button>
      </div>
      @if (reminders.loading()) {
        <p>Loading reminders...</p>
      }
      @if (reminders.error()) {
        <p>Reminders unavailable. Existing workflow gates are unchanged.</p>
      }
      @if (reminders.data(); as data) {
        <p>As of {{ data.asOfDateUtc }} (UTC). Reminders do not clear workflow gates.</p>
        @if (data.items.length === 0) {
          <p>No reminders in the current view.</p>
        } @else {
          <ul aria-label="Deadline reminders">
            @for (reminder of data.items; track reminder.kind + ':' + reminder.sourceId) {
              <li>
                <strong>{{ reminder.title }}</strong>
                <span> · {{ reminder.tone }}</span>
                <p>{{ reminder.dueDate ?? 'No date' }}</p>
                <p>{{ reminder.message }}</p>
                <a [routerLink]="router.parseUrl(reminder.sourceHref)">
                  {{ reminder.kind === 'TASK_DUE' ? 'Open task' : 'Open reminder source' }}
                </a>
              </li>
            }
          </ul>
        }
        @if (data.truncated) {
          <p>More reminders exist. Open My work or the planning workspace.</p>
        }
      }
    </section>
  `,
})
export class EngagementReminders {
  readonly engagementId = input.required<string>();

  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  readonly router = inject(Router);
  readonly reminders = this.api.resource(
    () => {
      const id = this.engagementId()?.toLowerCase();
      return id && this.session.current()?.staff
        ? `/api/ui/engagements/${id}/deadline-reminders`
        : null;
    },
    (value, path) => decodeEngagementDeadlineReminders(value, this.engagementId(), path),
    'Reminders unavailable. Existing workflow gates are unchanged.',
  );

  private readonly visibilityListener = () => this.refreshWhenEligible();

  constructor() {
    document.addEventListener('visibilitychange', this.visibilityListener);
    const interval = setInterval(() => this.refreshWhenEligible(), 60_000);
    inject(DestroyRef).onDestroy(() => {
      clearInterval(interval);
      document.removeEventListener('visibilitychange', this.visibilityListener);
    });
  }

  refresh(): void {
    this.refreshWhenEligible();
  }

  private refreshWhenEligible(): void {
    if (document.visibilityState !== 'visible' || !this.session.current()?.staff || this.reminders.loading()) return;
    this.reminders.reload();
  }
}
