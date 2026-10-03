import { MatDialog } from '@angular/material/dialog';
import { of, Subject } from 'rxjs';
import { TabDrafts } from '../../core/tab-drafts';
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
        canPrepareBudget: false,
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
        canPrepareBudget: false,
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
        canPrepareBudget: false,
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
        canPrepareBudget: false,
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
    canPrepareBudget: true,
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
  it('does not substitute staffing authority for reviewed budget preparation', async () => {
    const component = open();
    component.data.set({ ...planning, canPrepareBudget: false });
    TestBed.tick();
    expect(component.budgetFields.currency().disabled()).toBe(true);
    expect(component.saveEditableBudget()).toBe(false);
    const before = component.budgetLines.length;
    component.addBudgetLine();
    expect(component.budgetLines.length).toBe(before);
    await component.saveBudget();
    TestBed.inject(HttpTestingController).expectNone((r) => r.method === 'POST');
  });
  it('fails closed when the API omits action-specific preparation authority', () => {
    const { canPrepareBudget: _, ...missing } = planning;
    expect(() => decodePlanning(missing)).toThrow();
  });
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
  it('keeps edits or saves only budget fields before leaving', async () => {
    const component = open();
    expect(await component.confirmNavigation()).toBe(true);
    component.currency = 'USD';
    const dialog = vi.spyOn(TestBed.inject(MatDialog), 'open');
    dialog.mockReturnValue({ afterClosed: () => of('keep') } as never);
    expect(await component.confirmNavigation()).toBe(false);
    expect(component.currency).toBe('USD');
    dialog.mockReturnValue({ afterClosed: () => of('save') } as never);
    expect(await component.confirmNavigation()).toBe(true);
    expect(component.planningDirty()).toBe(false);
    const stored = sessionStorage.getItem(sessionStorage.key(0)!);
    expect(stored).toContain('USD');
    expect(stored).not.toContain('reviewed');
    component.currency = 'QAR';
    component.restoreEditableBudget();
    expect(component.currency).toBe('USD');
    TestBed.inject(HttpTestingController).expectNone((r) => r.method !== 'GET');
  });
  it('never treats staffing or approval choices as saved budget drafts', async () => {
    const component = open();
    component.selectedLevel = 'AUDIT_MANAGER';
    const dialog = vi
      .spyOn(TestBed.inject(MatDialog), 'open')
      .mockReturnValue({ afterClosed: () => of('save') } as never);
    expect(await component.confirmNavigation()).toBe(false);
    expect(sessionStorage.length).toBe(0);
    expect(component.commandStatus()).toContain('cannot save staffing');
    dialog.mockReturnValue({ afterClosed: () => of('discard') } as never);
    expect(await component.confirmNavigation()).toBe(true);
    expectCleared(component);
  });
  it('fences dialog results by session and never leaves an unconfirmed write', async () => {
    const component = open();
    component.uncertain.set(true);
    const dialog = vi.spyOn(TestBed.inject(MatDialog), 'open');
    expect(await component.confirmNavigation()).toBe(false);
    expect(dialog).not.toHaveBeenCalled();
    component.uncertain.set(false);
    component.currency = 'USD';
    const close = new Subject<string>();
    dialog.mockReturnValue({ afterClosed: () => close } as never);
    const navigation = component.confirmNavigation();
    expect(await component.confirmNavigation()).toBe(false);
    TestBed.inject(SessionService).clear();
    close.next('save');
    expect(await navigation).toBe(true);
    expect(sessionStorage.length).toBe(0);
    TestBed.tick();
  });
  it('warns on tab departure for edits and unknown writes but does not trap sign-out', () => {
    const component = open();
    const clean = new Event('beforeunload', { cancelable: true });
    component.beforeUnload(clean as BeforeUnloadEvent);
    expect(clean.defaultPrevented).toBe(false);
    component.currency = 'USD';
    const edited = new Event('beforeunload', { cancelable: true });
    component.beforeUnload(edited as BeforeUnloadEvent);
    expect(edited.defaultPrevented).toBe(true);
    component.currency = 'QAR';
    component.uncertain.set(true);
    const unknown = new Event('beforeunload', { cancelable: true });
    component.beforeUnload(unknown as BeforeUnloadEvent);
    expect(unknown.defaultPrevented).toBe(true);
    TestBed.inject(SessionService).clear();
    const signedOut = new Event('beforeunload', { cancelable: true });
    component.beforeUnload(signedOut as BeforeUnloadEvent);
    expect(signedOut.defaultPrevented).toBe(false);
    TestBed.tick();
  });
  it('retains edits when bounded tab saving fails', async () => {
    const component = open();
    component.currency = 'USD';
    vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({
      afterClosed: () => of('save'),
    } as never);
    vi.spyOn(TestBed.inject(TabDrafts), 'save').mockReturnValue(false);
    expect(await component.confirmNavigation()).toBe(false);
    expect(component.currency).toBe('USD');
    expect(component.planningDirty()).toBe(true);
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
  it('associates staffing labels without including selectable option text', () => {
    const fixture = TestBed.createComponent(EngagementPlanning);
    fixture.componentRef.setInput('engagementId', id);
    fixture.detectChanges();
    TestBed.tick();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/ui/engagements/' + id + '/planning')
      .flush(planning);
    TestBed.tick();
    for (const [field, label] of [
      ['person', 'Person'],
      ['level', 'Level'],
    ]) {
      const select = fixture.nativeElement.querySelector(
        '#staffing-' + field + '-' + id,
      ) as HTMLSelectElement;
      expect(select).not.toBeNull();
      expect(select.labels?.[0]?.textContent?.trim()).toBe(label);
      expect(select.labels?.[0]?.querySelector('option')).toBeNull();
    }
  });
  it('refuses reused staffing assent immediately before effects render', () => {
    const component = open();
    component.selectedUser = id;
    component.reviewed = true;
    expect(component.reviewed).toBe(true);
    component.staffingModel.update((m) => ({ ...m, level: 'SENIOR_AUDITOR' }));
    expect(component.reviewed).toBe(false);
    component.assign();
    TestBed.inject(HttpTestingController).expectNone((r) => r.method === 'POST');
    TestBed.tick();
    expect(component.staffingModel().reviewed).toBe(false);
    component.reviewed = true;
    component.staffingModel.update((m) => ({
      ...m,
      userId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
    }));
    expect(component.reviewed).toBe(false);
    component.assign();
    TestBed.inject(HttpTestingController).expectNone((r) => r.method === 'POST');
    TestBed.tick();
    component.selectedLevel = 'UNSUPPORTED';
    component.reviewed = true;
    expect(component.staffingFields.level().invalid()).toBe(true);
    component.assign();
    TestBed.inject(HttpTestingController).expectNone((r) => r.method === 'POST');
    component.uncertain.set(true);
    expect(component.staffingFields.userId().disabled()).toBe(true);
    expect(component.staffingFields.reviewed().disabled()).toBe(true);
  });
  it('binds approval assent to the exact displayed draft and refuses another draft id', () => {
    const component = open();
    const draft = { id, version: '1', currency: 'QAR', canApprove: true, lines: [] };
    component.data.set({ ...planning, draft });
    component.budgetReviewed = true;
    expect(component.budgetReviewed).toBe(true);
    component.approveBudget('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb');
    TestBed.inject(HttpTestingController).expectNone((r) => r.method === 'POST');
    component.data.set({ ...planning, draft: { ...draft, version: '2' } });
    expect(component.budgetReviewed).toBe(false);
    component.approveBudget(id);
    TestBed.inject(HttpTestingController).expectNone((r) => r.method === 'POST');
    TestBed.tick();
    expect(component.approvalModel().reviewed).toBe(false);
  });
  it('binds preparation assent to the exact preview before render catches up', async () => {
    const component = open(),
      http = TestBed.inject(HttpTestingController);
    const preparing = component.saveBudget();
    const request = http.expectOne('/api/ui/engagements/' + id + '/budget-preparation/preview');
    const fields = request.request.body.fields;
    const preview = {
      engagementId: id,
      requestId: request.request.body.requestId,
      requestHash: 'a'.repeat(64),
      reviewBasis: 'b'.repeat(64),
      fields,
      lines: [
        { ...fields.lines[0], rateCardId: id, ratePerHour: '100.125', forecastCost: '801.000000' },
      ],
      forecastCost: '801.000000',
    };
    request.flush({ value: preview });
    await preparing;
    component.preparationReviewed = true;
    expect(component.preparationReviewed).toBe(true);
    component.budgetPreview.set({ ...preview, forecastCost: '802.000000' });
    expect(component.preparationReviewed).toBe(false);
    await component.confirmBudgetPreparation();
    http.expectNone('/api/ui/engagements/' + id + '/budget-preparation');
    TestBed.tick();
    expect(component.preparationModel().reviewed).toBe(false);
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
