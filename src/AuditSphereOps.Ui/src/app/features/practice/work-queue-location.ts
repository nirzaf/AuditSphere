import type { ParamMap } from '@angular/router';
import { QUEUE_DISPLAYS, type QueueDisplay } from './work-queue-views';

export const QUEUE_VIEW_KEYS = ['MY_TASKS', 'TEAM', 'DUE_TODAY', 'UPCOMING', 'WAITING_FOR_CLIENT',
  'UNDER_REVIEW', 'CHANGES_REQUIRED', 'OVERDUE', 'ON_HOLD', 'COMPLETED', 'CANCELLED'] as const;
export type QueueView = typeof QUEUE_VIEW_KEYS[number];
export type QueueOwner = 'ALL' | 'ME' | 'UNASSIGNED';
export type WorkQueueLocation = { view: QueueView; owner: QueueOwner; display: QueueDisplay };

/** Reserved context restriction keys are not supported by this partial route contract. */
export function hasUnsupportedQueueContext(params: ParamMap): boolean {
  return params.has('queueClientId') || params.has('queueEngagementId');
}

/** The reminder engagementId/taskId pair is intentionally not a queue population filter. */
export function parseWorkQueueLocation(params: ParamMap): WorkQueueLocation | null {
  if (hasUnsupportedQueueContext(params)) return null;
  const views = params.getAll('queueView');
  const owners = params.getAll('queueAssignment');
  const displays = params.getAll('queueDisplay');
  if (views.length > 1 || owners.length > 1 || displays.length > 1) return null;
  const view = views[0] ?? 'MY_TASKS';
  const owner = owners[0] ?? 'ALL';
  const display = displays[0] ?? 'LIST';
  if (!QUEUE_VIEW_KEYS.some(value => value === view) ||
      !['ALL', 'ME', 'UNASSIGNED'].includes(owner) ||
      !QUEUE_DISPLAYS.some(value => value.key === display) ||
      (view === 'MY_TASKS' && owner !== 'ALL')) return null;
  return { view: view as QueueView, owner: owner as QueueOwner, display: display as QueueDisplay };
}
