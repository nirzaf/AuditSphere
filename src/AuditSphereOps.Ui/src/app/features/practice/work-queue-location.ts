import type { ParamMap } from '@angular/router';
import { guidPattern } from '../../core/contracts';
import { QUEUE_DISPLAYS, type QueueDisplay } from './work-queue-views';

export const QUEUE_VIEW_KEYS = ['MY_TASKS', 'TEAM', 'DUE_TODAY', 'UPCOMING', 'WAITING_FOR_CLIENT',
  'UNDER_REVIEW', 'CHANGES_REQUIRED', 'OVERDUE', 'ON_HOLD', 'COMPLETED', 'CANCELLED'] as const;
export type QueueView = typeof QUEUE_VIEW_KEYS[number];
export type QueueOwner = 'ALL' | 'ME' | 'UNASSIGNED';
export type WorkQueueLocation = { view: QueueView; owner: QueueOwner; display: QueueDisplay;
  clientFilterId: string | null; engagementFilterId: string | null };

const emptyGuid = '00000000-0000-0000-0000-000000000000';
function optionalGuid(values: string[]): string | null | undefined {
  if (values.length > 1) return undefined;
  if (!values.length) return null;
  const value = values[0];
  if (!guidPattern.test(value) || value.toLowerCase() === emptyGuid) return undefined;
  return value.toLowerCase();
}

/** The reminder engagementId/taskId pair is intentionally not a queue population filter. */
export function parseWorkQueueLocation(params: ParamMap): WorkQueueLocation | null {
  const views = params.getAll('queueView');
  const owners = params.getAll('queueAssignment');
  const displays = params.getAll('queueDisplay');
  const clients = optionalGuid(params.getAll('queueClientId'));
  const engagements = optionalGuid(params.getAll('queueEngagementId'));
  if (views.length > 1 || owners.length > 1 || displays.length > 1 || clients === undefined || engagements === undefined) return null;
  const view = views[0] ?? 'MY_TASKS';
  const owner = owners[0] ?? 'ALL';
  const display = displays[0] ?? 'LIST';
  if (!QUEUE_VIEW_KEYS.some(value => value === view) ||
      !['ALL', 'ME', 'UNASSIGNED'].includes(owner) ||
      !QUEUE_DISPLAYS.some(value => value.key === display) ||
      (view === 'MY_TASKS' && owner !== 'ALL')) return null;
  return { view: view as QueueView, owner: owner as QueueOwner, display: display as QueueDisplay,
    clientFilterId: clients, engagementFilterId: engagements };
}
