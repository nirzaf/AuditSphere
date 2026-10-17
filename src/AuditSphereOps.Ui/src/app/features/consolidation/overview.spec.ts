import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SessionService } from '../../core/session';
import { ConsolidationOverview, decodeConsolidation, displayUtcInstant, resolveCorrectionContext } from './overview';

const groupId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const actorId = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const firmId = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';
const groupsUrl = '/api/ui/consolidation';
const optionsUrl = `${groupsUrl}/groups/${groupId}/scope-options`;
const scopeId = 'dddddddd-dddd-4ddd-8ddd-dddddddddddd';
const otherGroupId = 'ffffffff-ffff-4fff-8fff-ffffffffffff';
const createdGroup = { id: groupId, code: 'HOLDCO-01', name: 'Holding Company Group', scopes: [] };
const correctionScope = {
  id: scopeId, groupName: createdGroup.name, version: 3, method: 'FULLY_OWNED_SAME_CURRENCY', currency: 'QAR',
  ownershipEdges: 1, groupedMatches: 0, outsideReviews: 0, openMatchCount: 0, componentCount: 1,
  approvedComponentCount: 1, externalPackCount: 0, approvedExternalPackCount: 0, advancedScheduleCount: 0,
  approvedAdvancedScheduleCount: 0, advancedExecutionCount: 0, approvedAdvancedExecutionCount: 0,
  advancedScheduleStatus: 'NONE', advancedScheduleGuidance: '', rateSetStatus: 'NOT_REQUIRED',
  translationPolicyStatus: 'NOT_REQUIRED', eliminationJournalCount: 0, approvedEliminationJournalCount: 0,
  status: 'APPROVED', runStatus: 'APPROVED', isAdvanced: false, report: null,
};
const scopeOptions = {
  groupId, groupCode: 'HOLDCO-01', groupName: 'Holding Company Group', groupRevision: 7, canCreateScope: true,
  periods: [{ id: 'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee', clientName: 'Operating Company', code: 'FY2026',
    startDate: '2026-01-01', endDate: '2026-12-31', basis: 'ANNUAL', currency: 'QAR', status: 'CLOSED' }],
  currencies: ['QAR'],
  methods: [
    { code: 'FULLY_OWNED_SAME_CURRENCY', label: 'Fully owned · same currency', enabled: true, guidance: 'Same currency.' },
    { code: 'CONTROLLED_FOREIGN_OPERATION_TRANSLATION_V1', label: 'Foreign operation', enabled: false, guidance: 'Approved FX policy required.' },
  ],
  approvedRateSets: [], approvedTranslationPolicies: [], approvedPriorScopes: [], existingScopes: [],
  guidance: 'Options are limited to the authorized group.'
};

function overview(groups: object[] = [], canCreateGroup = true) { return { canCreateGroup, groups }; }
function membership() {
  return { groupId, groupCode: 'HOLDCO-01', groupName: 'Holding Company Group', groupRevision: 1, page: 0, pageSize: 25,
    total: 0, members: [], eligibleClients: [], canAddMember: true };
}

describe('consolidation report timestamps', () => {
  it('shows the approval instant in UTC with an explicit timezone label', () => {
    expect(displayUtcInstant('2026-10-07T10:00:00Z')).toBe('2026-10-07 10:00 UTC');
    expect(displayUtcInstant('2026-10-07T13:00:00+03:00')).toBe('2026-10-07 10:00 UTC');
    expect(displayUtcInstant(null)).toBe('');
  });
});

describe('accounting group creation', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      imports: [ConsolidationOverview],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    TestBed.inject(SessionService).current.set({ userId: actorId, firmId, generation: '1', staff: true });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    try { http.verify({ ignoreCancelled: true }); }
    finally { TestBed.resetTestingModule(); sessionStorage.clear(); vi.restoreAllMocks(); }
  });

  async function open(initial = overview()) {
    const fixture = TestBed.createComponent(ConsolidationOverview);
    fixture.detectChanges();
    http.expectOne(groupsUrl).flush(initial);
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, component: fixture.componentInstance };
  }

  it('posts the exact group intent, refreshes permitted groups and navigates to its perimeter', async () => {
    const { fixture, component } = await open();
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    component.groupCode = ' HOLDCO-01 ';
    component.groupName = ' Holding Company Group ';

    const creating = component.createGroup();
    const post = http.expectOne(groupsUrl + '/groups');
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ code: 'HOLDCO-01', name: 'Holding Company Group' });
    post.flush({ value: groupId });
    await creating;
    expect(navigate).toHaveBeenCalledWith([], { fragment: `group-${groupId}` });
    http.expectOne(groupsUrl).flush(overview([createdGroup]));
    await fixture.whenStable();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('HOLDCO-01');
    const members = http.expectOne(request => request.urlWithParams === `/api/ui/consolidation/groups/${groupId}/members?page=0&pageSize=25`);
    members.flush(membership());
    await fixture.whenStable();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Holding Company Group');
  });

  it('reconciles an unknown create only from an exact permitted group and never resends', async () => {
    const { fixture, component } = await open();
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    component.groupCode = 'HOLDCO-01';
    component.groupName = 'Holding Company Group';

    const creating = component.createGroup();
    http.expectOne(request => request.method === 'POST' && request.url === groupsUrl + '/groups')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    await creating;
    expect(component.createCommand.uncertain()).toBe(true);

    const recovering = component.recoverGroup();
    http.expectOne(request => request.method === 'GET' && request.url === groupsUrl).flush(overview([createdGroup]));
    await recovering;
    http.expectOne(groupsUrl).flush(overview([createdGroup]));
    await fixture.whenStable();
    http.expectNone(request => request.method === 'POST' && request.url === groupsUrl + '/groups');
    expect(component.createCommand.uncertain()).toBe(false);
    expect(navigate).toHaveBeenCalledWith([], { fragment: `group-${groupId}` });
    fixture.detectChanges();
    http.expectOne(request => request.urlWithParams === `/api/ui/consolidation/groups/${groupId}/members?page=0&pageSize=25`)
      .flush(membership());
  });

  it('rejects an overview that omits explicit creation authority or the permitted group list', () => {
    expect(() => decodeConsolidation({ groups: [] }, 'overview')).toThrow();
    expect(() => decodeConsolidation({ canCreateGroup: 'true', groups: [] }, 'overview')).toThrow();
    expect(() => decodeConsolidation(overview([{ ...createdGroup, id: 'invalid' }]), 'overview')).toThrow();
  });

  it('loads authorized options and posts explicit nullable pins before navigating to the new scope workspace', async () => {
    const { fixture, component } = await open(overview([createdGroup]));
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    component.selectScopeGroup(groupId);
    fixture.detectChanges();
    http.expectOne(optionsUrl).flush(scopeOptions);
    await fixture.whenStable();
    fixture.detectChanges();
    const periodId = scopeOptions.periods[0].id;
    component.scopePeriodId = periodId;
    component.scopeMethod = 'FULLY_OWNED_SAME_CURRENCY';
    component.scopeCurrency = 'QAR';

    const creating = component.createScope();
    const post = http.expectOne(groupsUrl + '/scopes');
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({
      groupId, periodId, reportingCurrency: 'QAR', method: 'FULLY_OWNED_SAME_CURRENCY',
      openingBasis: 'INITIAL_CONSOLIDATION', exchangeRateSetVersionId: null,
      translationPolicyVersionId: null, translationRateDate: null, translationRateType: null,
      priorScopeVersionId: null, expectedGroupRevision: 7
    });
    post.flush({ value: scopeId });
    await creating;
    expect(navigate).toHaveBeenCalledWith(['/app/consolidation/scopes', scopeId]);
  });

  it('reconciles an uncertain save from the exact scoped version and does not resend', async () => {
    const { fixture, component } = await open(overview([createdGroup]));
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    component.selectScopeGroup(groupId);
    fixture.detectChanges();
    http.expectOne(optionsUrl).flush(scopeOptions);
    await fixture.whenStable();
    component.scopePeriodId = scopeOptions.periods[0].id;
    component.scopeMethod = 'FULLY_OWNED_SAME_CURRENCY';
    component.scopeCurrency = 'QAR';

    const creating = component.createScope();
    http.expectOne(groupsUrl + '/scopes').flush({}, { status: 503, statusText: 'Unavailable' });
    await creating;
    expect(component.scopeCommand.uncertain()).toBe(true);
    const recovering = component.recoverScope();
    http.expectOne(optionsUrl).flush({ ...scopeOptions, existingScopes: [{
      id: scopeId, periodId: scopeOptions.periods[0].id, reportingCurrency: 'QAR',
      method: 'FULLY_OWNED_SAME_CURRENCY', openingBasis: 'INITIAL_CONSOLIDATION',
      exchangeRateSetVersionId: null, translationPolicyVersionId: null, translationRateDate: null,
      translationRateType: '', priorScopeVersionId: null, groupRevision: 7, status: 'DRAFT'
    }] });
    await recovering;
    expect(component.scopeCommand.uncertain()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/app/consolidation/scopes', scopeId]);
    http.expectNone(request => request.method === 'POST' && request.url === groupsUrl + '/scopes');
  });

  it('refreshes pinned group basis after a stale refusal without retrying creation', async () => {
    const { fixture, component } = await open(overview([createdGroup]));
    component.selectScopeGroup(groupId);
    fixture.detectChanges();
    http.expectOne(optionsUrl).flush(scopeOptions);
    await fixture.whenStable();
    component.scopePeriodId = scopeOptions.periods[0].id;
    component.scopeMethod = 'FULLY_OWNED_SAME_CURRENCY';
    component.scopeCurrency = 'QAR';
    const creating = component.createScope();
    http.expectOne(groupsUrl + '/scopes').flush({ code: 'generation.stale', message: 'The group changed.' },
      { status: 409, statusText: 'Conflict' });
    await creating;
    expect(component.scopeCommand.uncertain()).toBe(false);
    expect(component.scopeValidation()).toContain('group basis changed');
    http.expectOne(optionsUrl).flush({ ...scopeOptions, groupRevision: 8 });
    await fixture.whenStable();
    http.expectNone(request => request.method === 'POST' && request.url === groupsUrl + '/scopes');
  });
});

describe('consolidation correction handoff context', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      imports: [ConsolidationOverview],
      providers: [provideHttpClient(), provideHttpClientTesting(),
        provideRouter([{ path: 'app/consolidation', component: ConsolidationOverview }])],
    });
    TestBed.inject(SessionService).current.set({ userId: actorId, firmId, generation: '1', staff: true });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    try { http.verify({ ignoreCancelled: true }); }
    finally { TestBed.resetTestingModule(); sessionStorage.clear(); vi.restoreAllMocks(); }
  });

  it('binds both correction query identities to the authorized fetched group and scope', async () => {
    const harness = await RouterTestingHarness.create();
    const navigation = harness.navigateByUrl(
      `/app/consolidation?correctionScopeId=${scopeId}&correctionGroupId=${groupId}`, ConsolidationOverview);
    const component = await navigation;
    http.expectOne(groupsUrl).flush(overview([{ ...createdGroup, scopes: [correctionScope] }]));
    expect(component.correctionContext()).toEqual({
      state: 'valid', group: { ...createdGroup, scopes: [correctionScope] }, scope: correctionScope,
    });
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
    http.expectOne(optionsUrl).flush(scopeOptions);
    await harness.fixture.whenStable();
    expect(component.scopeGroupId()).toBe(groupId);
    expect(harness.routeNativeElement?.textContent).toContain('Correct a component in a new scope version');
    expect(harness.routeNativeElement?.textContent).toContain('newly reviewed component package');
    expect(harness.routeNativeElement?.textContent).toContain('original scope, component, package and prior run remain');
    expect(harness.routeNativeElement?.querySelector<HTMLSelectElement>('select[name="scopeGroup"]')?.disabled).toBe(true);
    http.expectNone(request => request.method === 'POST');
    component.scopePeriodId = scopeOptions.periods[0].id;
    await component.createScope();
    expect(component.scopeValidation()).toContain('retain the original reporting period');
    http.expectNone(request => request.method === 'POST');

    component.scopeOptions.reload();
    http.expectOne(optionsUrl).flush({ ...scopeOptions,
      periods: [...scopeOptions.periods, { ...scopeOptions.periods[0], id: otherGroupId, code: 'FY2027' }],
      existingScopes: [{ id: scopeId, periodId: scopeOptions.periods[0].id, reportingCurrency: 'QAR',
        method: correctionScope.method, openingBasis: 'INITIAL_CONSOLIDATION', exchangeRateSetVersionId: null,
        translationPolicyVersionId: null, translationRateDate: null, translationRateType: '',
        priorScopeVersionId: null, groupRevision: 7, status: 'APPROVED' }] });
    await harness.fixture.whenStable();
    component.scopePeriodId = otherGroupId;
    await component.createScope();
    expect(component.scopeValidation()).toContain('retain the original reporting period');
    http.expectNone(request => request.method === 'POST');

    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    component.scopePeriodId = scopeOptions.periods[0].id;
    component.scopeMethod = correctionScope.method;
    component.scopeCurrency = 'QAR';
    const creating = component.createScope();
    const create = http.expectOne({ method: 'POST', url: groupsUrl + '/scopes' });
    expect(create.request.body.periodId).toBe(scopeOptions.periods[0].id);
    expect(create.request.body.expectedGroupRevision).toBe(7);
    create.flush({ value: '12121212-1212-4212-8212-121212121212' });
    await creating;
  });

  it('rejects a stale, unknown, malformed or wrong-group correction source', () => {
    const authorized = [{ ...createdGroup, scopes: [correctionScope] },
      { id: otherGroupId, code: 'OTHER', name: 'Other Group', scopes: [] }];
    expect(resolveCorrectionContext(authorized, scopeId, otherGroupId).state).toBe('wrong-group');
    expect(resolveCorrectionContext(authorized, '11111111-1111-4111-8111-111111111111', groupId).state).toBe('unavailable');
    expect(resolveCorrectionContext(authorized, 'unknown', groupId).state).toBe('invalid');
    expect(resolveCorrectionContext(null, scopeId, groupId).state).toBe('pending');
    expect(resolveCorrectionContext(authorized, null, null).state).toBe('none');
  });
});
