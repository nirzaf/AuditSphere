import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SessionService } from '../../core/session';
import { EngagementPlanning } from './planning';
import { describe, expect, it } from 'vitest';
import { decodePlanning, exactDecimal, editablePlanningBudget } from './planning';
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
    sessionStorage.clear();
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
    sessionStorage.clear();
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
  it('validates budget fields and locks them while reviewing or recovering', () => {
    const component = open();
    component.currency = 'QQQQ';
    expect(component.budgetFields.currency().invalid()).toBe(true);
    component.currency = 'QAR';
    expect(component.budgetFields.currency().invalid()).toBe(false);
    component.budgetLines = [
      { role: '', activity: 'AUDIT', phase: 'PLANNING', riskArea: '', forecastMinutes: 0 },
    ];
    expect(component.budgetFields.lines[0].role().invalid()).toBe(true);
    expect(component.budgetFields.lines[0].forecastMinutes().invalid()).toBe(true);
    component.budgetLines = [
      { role: 'Senior', activity: 'AUDIT', phase: 'PLANNING', riskArea: '', forecastMinutes: 1.5 },
    ];
    expect(component.budgetFields.lines[0].forecastMinutes().invalid()).toBe(true);
    const previous = component.budgetLines;
    component.addBudgetLine();
    expect(previous).toHaveLength(1);
    expect(component.budgetLines).toHaveLength(2);
    component.removeBudgetLine(0);
    expect(component.budgetLines).toHaveLength(1);
    component.uncertain.set(true);
    expect(component.budgetFields.currency().disabled()).toBe(true);
    component.addBudgetLine();
    component.removeBudgetLine(0);
    expect(component.budgetLines).toHaveLength(1);
  });
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
  it('restores only editable fields against the same current budget revision without assent', () => {
    const component = open();
    component.currency = 'USD';
    component.budgetReviewed = true;
    component.saveEditableBudget();
    component.currency = 'QAR';
    component.restoreEditableBudget();
    expect(component.currency).toBe('USD');
    expect(component.budgetReviewed).toBe(false);
    component.data.set({ ...component.data()!, latestBudgetVersion: '1' });
    component.currency = 'QAR';
    component.restoreEditableBudget();
    expect(component.currency).toBe('QAR');
    expect(component.draftStatus()).toContain('stale');
  });
  it('refuses recovered fields from a pending submission and cannot restore during an unknown outcome', () => {
    const component = open();
    component.saveEditableBudget();
    const key = sessionStorage.key(0)!;
    const saved = JSON.parse(sessionStorage.getItem(key)!);
    saved.submissionPending = true;
    sessionStorage.setItem(key, JSON.stringify(saved));
    component.currency = 'USD';
    component.restoreEditableBudget();
    expect(component.currency).toBe('USD');
    saved.submissionPending = false;
    sessionStorage.setItem(key, JSON.stringify(saved));
    component.uncertain.set(true);
    component.restoreEditableBudget();
    expect(component.currency).toBe('USD');
  });
  it('allowlists bounded budget fields and rejects approval, money and invalid minute values', () => {
    const fields = {
      currency: 'QAR',
      lines: [
        {
          role: 'Senior',
          activity: 'AUDIT',
          phase: 'PLANNING',
          riskArea: '',
          forecastMinutes: 480,
        },
      ],
    };
    expect(editablePlanningBudget(fields)).toEqual(fields);
    expect(editablePlanningBudget({ ...fields, reviewed: true })).toBeNull();
    expect(
      editablePlanningBudget({ ...fields, lines: [{ ...fields.lines[0], cost: '10' }] }),
    ).toBeNull();
    expect(
      editablePlanningBudget({ ...fields, lines: [{ ...fields.lines[0], forecastMinutes: 0 }] }),
    ).toBeNull();
    expect(
      editablePlanningBudget({ ...fields, lines: [{ ...fields.lines[0], role: 'x'.repeat(51) }] }),
    ).toBeNull();
  });
  it('reviews exact rates, sends once and reconciles a lost response without resending', async () => {
    const component = open(),
      http = TestBed.inject(HttpTestingController);
    const prepare = component.saveBudget();
    const requested = http.expectOne('/api/ui/engagements/' + id + '/budget-preparation/preview');
    const fields = requested.request.body.fields;
    const preview = {
      engagementId: id,
      requestId: requested.request.body.requestId,
      requestHash: 'a'.repeat(64),
      reviewBasis: 'b'.repeat(64),
      fields,
      lines: [
        { ...fields.lines[0], rateCardId: id, ratePerHour: '100.125', forecastCost: '801.000000' },
      ],
      forecastCost: '801.000000',
    };
    requested.flush({ value: preview });
    await prepare;
    expect(component.budgetPreview()).toEqual(preview);
    await component.confirmBudgetPreparation();
    http.expectNone('/api/ui/engagements/' + id + '/budget-preparation');
    component.preparationReviewed = true;
    const execute = component.confirmBudgetPreparation();
    http
      .expectOne('/api/ui/engagements/' + id + '/budget-preparation')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    await execute;
    expect(component.uncertain()).toBe(true);
    expect(component.preparationReviewed).toBe(false);
    const reconcile = component.reconcileBudgetPreparation();
    const receipt = {
      id,
      budgetId: id,
      engagementId: id,
      actorId: id,
      requestId: preview.requestId,
      requestHash: preview.requestHash,
      reviewBasis: preview.reviewBasis,
      preview,
      createdAt: '2026-10-03T07:00:00Z',
    };
    http
      .expectOne(
        '/api/ui/engagements/' +
          id +
          '/budget-preparation/receipts/' +
          preview.requestId +
          '?requestHash=' +
          preview.requestHash,
      )
      .flush({ found: true, receipt });
    await reconcile;
    expect(component.budgetReceipt()).toEqual(receipt);
    http.expectNone('/api/ui/engagements/' + id + '/budget-preparation');
    component.acknowledgeBudgetPreparation();
    http
      .expectOne('/api/ui/engagements/' + id + '/planning')
      .flush({ ...planning, latestBudgetVersion: '1' });
    expect(component.uncertain()).toBe(false);
    expect(component.budgetPending()).toBeNull();
  });
  it('rejects a preview belonging to another request without dispatching preparation', async () => {
    const component = open(),
      http = TestBed.inject(HttpTestingController);
    const prepare = component.saveBudget();
    http
      .expectOne('/api/ui/engagements/' + id + '/budget-preparation/preview')
      .flush({ value: { engagementId: id, requestId: id } });
    await prepare;
    expect(component.budgetPreview()).toBeNull();
    expect(component.commandStatus()).toContain('unsupported');
    http.expectNone('/api/ui/engagements/' + id + '/budget-preparation');
  });
  it('drops budget approval assent whenever the authorized projection is refreshed', () => {
    const component = open();
    component.budgetReviewed = true;
    component.preparationReviewed = true;
    component.load();
    expect(component.budgetReviewed).toBe(false);
    expect(component.preparationReviewed).toBe(false);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/ui/engagements/' + id + '/planning')
      .flush(planning);
  });
});
