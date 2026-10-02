import { describe, expect, it } from 'vitest';
import { decodePlanning, exactDecimal } from './planning';
describe('Exact planning contracts', () => {
  it('never accepts floating point money', () => {
    expect(exactDecimal('9007199254740993.01')).toBe(true);
    expect(exactDecimal(9007199254740993.01)).toBe(false);
    expect(exactDecimal('1e3')).toBe(false);
    expect(exactDecimal('NaN')).toBe(false);
  });
  it('distinguishes missing budget from approved zero', () => {
    expect(
      decodePlanning({
        team: [],
        latestBudgetVersion: '0',
        draft: null,
        canManageStaffing: false,
        candidates: [],
        budget: null,
        budgetState: 'UNAVAILABLE',
      }).budget,
    ).toBeNull();
    expect(() =>
      decodePlanning({
        team: [],
        latestBudgetVersion: '0',
        draft: null,
        canManageStaffing: false,
        candidates: [],
        budget: null,
        budgetState: 'APPROVED',
      }),
    ).toThrow();
    const budget = {
      id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
      version: '1',
      currency: 'QAR',
      rows: [],
      forecastMinutes: 0,
      actualMinutes: 0,
      forecastCost: '0.00',
      actualCost: '0.00',
    };
    expect(
      decodePlanning({
        team: [],
        latestBudgetVersion: '0',
        draft: null,
        canManageStaffing: false,
        candidates: [],
        budget,
        budgetState: 'APPROVED',
      }).budget?.forecastCost,
    ).toBe('0.00');
    expect(() =>
      decodePlanning({
        team: [],
        latestBudgetVersion: '0',
        draft: null,
        canManageStaffing: false,
        candidates: [],
        budget: { ...budget, forecastCost: 0 },
        budgetState: 'APPROVED',
      }),
    ).toThrow();
  });
});
