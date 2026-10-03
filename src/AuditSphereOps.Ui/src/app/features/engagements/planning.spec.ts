import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SessionService } from '../../core/session';
import { EngagementPlanning } from './planning';
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

describe('Planning protected editor clearing', () => {
  const id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
  const planning = {
    team: [],
    latestBudgetVersion: '0',
    draft: null,
    canManageStaffing: true,
    candidates: [],
    budget: null,
    budgetState: 'UNAVAILABLE',
  };
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [EngagementPlanning],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.inject(SessionService).current.set({
      userId: id,
      firmId: id,
      generation: '1',
      staff: true,
    });
  });
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
  });
  function open() {
    const fixture = TestBed.createComponent(EngagementPlanning);
    fixture.componentRef.setInput('engagementId', id);
    fixture.detectChanges();
    TestBed.tick();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/ui/engagements/' + id + '/planning')
      .flush(planning);
    return fixture.componentInstance;
  }
  function expectCleared(component: EngagementPlanning) {
    expect(component.data()).toBeNull();
    expect(component.budgetLines).toEqual([]);
    expect(component.currency).toBe('');
    expect(component.selectedUser).toBe('');
    expect(component.reviewed).toBe(false);
    expect(component.budgetReviewed).toBe(false);
    expect(component.revokeTarget()).toBeNull();
  }
  it('clears protected fields immediately on scope refusal and revalidates access', () => {
    const component = open();
    component.selectedUser = id;
    component.reviewed = true;
    component.budgetReviewed = true;
    component.assign();
    const http = TestBed.inject(HttpTestingController);
    http
      .expectOne('/api/ui/engagements/' + id + '/staffing')
      .flush({}, { status: 403, statusText: 'Forbidden' });
    expectCleared(component);
    http
      .expectOne('/api/ui/engagements/' + id + '/planning')
      .flush({}, { status: 403, statusText: 'Forbidden' });
    expectCleared(component);
  });
  it('clears editable protected content when a refreshed projection is malformed', () => {
    const component = open();
    component.selectedUser = id;
    component.reviewed = true;
    component.load();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/ui/engagements/' + id + '/planning')
      .flush({ ...planning, latestBudgetVersion: 1 });
    expectCleared(component);
    expect(component.error()).toContain('unsupported');
  });
});
