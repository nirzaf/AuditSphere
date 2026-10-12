import { describe, expect, it } from 'vitest';
import { decodeAnalytics } from './analytics';

const payload = {
  from: '2026-10-01',
  to: '2026-10-31',
  engagements: [],
  departments: [],
  milestones: {
    due: 4,
    completed: 3,
    completedOnTime: 2,
    completedLate: 1,
    overdue: 1,
    completionPercent: '75.0',
    onTimePercent: '50.0',
  },
  workload: [
    { assigneeKey: 'staff-1', assigneeName: 'Audit staff', due: 2, completed: 1, open: 1, overdue: 1 },
    { assigneeKey: 'unassigned', assigneeName: 'Unassigned', due: 1, completed: 0, open: 1, overdue: 0 },
  ],
  phaseWorkload: [
    { phase: 'AUDIT_FIELDWORK', label: 'Audit Fieldwork', due: 1, completed: 1, open: 0, overdue: 0, completionPercent: '100.0' },
    { phase: 'UNASSIGNED_PHASE', label: 'Unassigned phase', due: 2, completed: 0, open: 2, overdue: 1, completionPercent: '0.0' },
  ],
  reviewTurnaround: { decisions: 4, reviewed: 3, changesRequired: 1, measured: 2, excluded: 2, averageHours: '7.5' },
  clientWait: { intervals: 3, completed: 1, open: 1, excluded: 1, averageCompletedHours: '4.0', averageOpenHours: '12.5', available: true, unavailableReason: null },
  definitions: [],
};

describe('Practice analytics task completion contract', () => {
  it('keeps the applicable-task counts and exact decimal percentages', () => {
    const decoded = decodeAnalytics(payload, 'analytics');
    expect(decoded.milestones.due).toBe(4);
    expect(decoded.milestones.completed).toBe(3);
    expect(decoded.milestones.completionPercent).toBe('75.0');
    expect(decoded.workload[0]).toEqual(payload.workload[0]);
    expect(decoded.workload[1].assigneeKey).toBe('unassigned');
    expect(decoded.reviewTurnaround.averageHours).toBe('7.5');
    expect(decoded.reviewTurnaround.excluded).toBe(2);
    expect(decoded.clientWait).toEqual(payload.clientWait);
    expect(decoded.phaseWorkload).toHaveLength(2);
    expect(decoded.phaseWorkload[0].completionPercent).toBe('100.0');
    expect(decoded.phaseWorkload[1].phase).toBe('UNASSIGNED_PHASE');
  });

  it('represents an empty review sample without inventing a zero-hour duration', () => {
    const decoded = decodeAnalytics({ ...payload, reviewTurnaround: { decisions: 0, reviewed: 0, changesRequired: 0, measured: 0, excluded: 0, averageHours: null } }, 'analytics');
    expect(decoded.reviewTurnaround.averageHours).toBeNull();
  });

  it('represents empty or over-limit client-wait evidence without inventing a duration', () => {
    const empty = decodeAnalytics({ ...payload, clientWait: { intervals: 0, completed: 0, open: 0, excluded: 0,
      averageCompletedHours: null, averageOpenHours: null, available: true, unavailableReason: null } }, 'analytics');
    expect(empty.clientWait.averageCompletedHours).toBeNull();
    const capped = decodeAnalytics({ ...payload, clientWait: { intervals: 0, completed: 0, open: 0, excluded: 0,
      averageCompletedHours: null, averageOpenHours: null, available: false, unavailableReason: 'Narrow the period.' } }, 'analytics');
    expect(capped.clientWait.available).toBe(false);
    expect(capped.clientWait.unavailableReason).toBe('Narrow the period.');
  });

  it('rejects numeric percentages instead of accepting a rounded browser number', () => {
    expect(() => decodeAnalytics({ ...payload, milestones: { ...payload.milestones, completionPercent: 75 } }, 'analytics')).toThrow();
  });

  it('rejects workload rows without bounded integer counts', () => {
    expect(() => decodeAnalytics({ ...payload, workload: [{ ...payload.workload[0], overdue: '1' }] }, 'analytics')).toThrow();
  });
});
