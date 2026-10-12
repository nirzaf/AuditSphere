import { describe, expect, it } from 'vitest';
import { decodeActivity } from './work-queue';

const activity = {
  engagementId: 'b43c00a8-a7a1-4efb-8e3d-1fd88f7ee447',
  taskId: '3c2d3786-43bb-4353-9bf7-9710c54f5315',
  from: '2026-07-15',
  to: '2026-10-12',
  events: [{
    id: '86feee9b-4a84-4502-b85c-c38cac5fe0f5',
    kind: 'TASK_REASSIGNED',
    sourceLabel: 'Operational Task',
    actorDisplayName: 'Engagement manager',
    assigneeDisplayName: 'Audit staff',
    taskRevision: 4,
    occurredAt: '2026-10-12T06:30:00Z',
  }],
  hasMore: true,
  nextCursor: { occurredAt: '2026-10-12T06:30:00Z', activityId: '86feee9b-4a84-4502-b85c-c38cac5fe0f5' },
};

describe('workflow task activity contract', () => {
  it('decodes the source, actor, revision, timestamp and stable page cursor', () => {
    const decoded = decodeActivity(activity, 'activity');
    expect(decoded.events[0].sourceLabel).toBe('Operational Task');
    expect(decoded.events[0].actorDisplayName).toBe('Engagement manager');
    expect(decoded.events[0].assigneeDisplayName).toBe('Audit staff');
    expect(decoded.events[0].taskRevision).toBe(4);
    expect(decoded.nextCursor?.activityId).toBe(activity.events[0].id);
  });

  it('rejects events from an unimplemented source or malformed actor and timestamp', () => {
    expect(() => decodeActivity({ ...activity, events: [{ ...activity.events[0], sourceLabel: 'Risk Decision' }] }, 'activity')).toThrow();
    expect(() => decodeActivity({ ...activity, events: [{ ...activity.events[0], actorDisplayName: '' }] }, 'activity')).toThrow();
    expect(() => decodeActivity({ ...activity, events: [{ ...activity.events[0], occurredAt: 'yesterday' }] }, 'activity')).toThrow();
  });

  it('supports an empty bounded timeline without an invented cursor', () => {
    const decoded = decodeActivity({ ...activity, events: [], hasMore: false, nextCursor: null }, 'activity');
    expect(decoded.events).toEqual([]);
    expect(decoded.hasMore).toBe(false);
    expect(decoded.nextCursor).toBeNull();
  });
});
