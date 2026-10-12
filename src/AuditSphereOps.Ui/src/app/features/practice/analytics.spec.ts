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
  definitions: [],
};

describe('Practice analytics task completion contract', () => {
  it('keeps the applicable-task counts and exact decimal percentages', () => {
    const decoded = decodeAnalytics(payload, 'analytics');
    expect(decoded.milestones.due).toBe(4);
    expect(decoded.milestones.completed).toBe(3);
    expect(decoded.milestones.completionPercent).toBe('75.0');
  });

  it('rejects numeric percentages instead of accepting a rounded browser number', () => {
    expect(() => decodeAnalytics({ ...payload, milestones: { ...payload.milestones, completionPercent: 75 } }, 'analytics')).toThrow();
  });
});
