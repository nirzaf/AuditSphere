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
  });

  it('rejects numeric percentages instead of accepting a rounded browser number', () => {
    expect(() => decodeAnalytics({ ...payload, milestones: { ...payload.milestones, completionPercent: 75 } }, 'analytics')).toThrow();
  });

  it('rejects workload rows without bounded integer counts', () => {
    expect(() => decodeAnalytics({ ...payload, workload: [{ ...payload.workload[0], overdue: '1' }] }, 'analytics')).toThrow();
  });
});
