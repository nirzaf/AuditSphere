import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { SessionService } from '../../core/session';
import { ConsolidationComponentCorrection } from './component-correction';

const scopeId = '11111111-1111-4111-8111-111111111111';
const wrongScopeId = '22222222-2222-4222-8222-222222222222';
const groupId = '33333333-3333-4333-8333-333333333333';
const wrongGroupId = '44444444-4444-4444-8444-444444444444';
const componentId = '55555555-5555-4555-8555-555555555555';
const clientId = '66666666-6666-4666-8666-666666666666';
const actorId = '77777777-7777-4777-8777-777777777777';
const firmId = '88888888-8888-4888-8888-888888888888';
const scopeUrl = `/api/ui/consolidation/scopes/${scopeId}?selectedComponentId=${componentId}`;
const packageHash = 'a'.repeat(64);

function workspace(overrides: Record<string, unknown> = {}) {
  return {
    scope: {
      id: scopeId, groupId, groupName: 'Holding Company', groupCode: 'HOLDCO', version: 4,
      groupRevision: 9, periodId: '99999999-9999-4999-8999-999999999999', reportingCurrency: 'QAR',
      method: 'FULLY_OWNED_SAME_CURRENCY', status: 'APPROVED', openingBasis: 'APPROVED_PRIOR_SCOPE', isAdvanced: false,
    },
    canPrepare: false, canReview: false, currentUserId: actorId,
    components: [{
      id: componentId, clientId, clientLegalName: 'Operating Company', sourceType: 'INTERNAL_PACKAGE',
      packageId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', externalComponentPackId: null, packageHash,
      periodBasis: 'ANNUAL', taxonomyVersion: 'taxonomy-v3', mappingVersion: 'mapping-v7', currency: 'QAR',
      ownershipPercent: '100.000000', controlMethod: 'CONTROLLED', status: 'APPROVED',
      submittedByUserId: actorId, submittedAt: '2026-10-12T10:11:12Z',
      approvedByUserId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', approvedAt: '2026-10-13T11:12:13Z',
      canApprove: false, approvalBlocker: null,
    }],
    componentPaging: { totalCount: 1, page: 1, pageSize: 100 },
    resolvedComponent: null,
    readinessPaging: { totalCount: 0, page: 1, pageSize: 100 },
    eligiblePackages: { items: [], totalCount: 0, page: 1, pageSize: 25 },
    journals: { items: [], totalCount: 0, page: 1, pageSize: 25 },
    runs: { items: [], totalCount: 0, page: 1, pageSize: 25 },
    latestReport: { available: false, value: null, errorCode: 'GATE_BLOCKED', message: 'No approved run.' },
    componentReadiness: { available: true, value: [], errorCode: null, message: null },
    intercompanyExceptions: { available: false, value: null, errorCode: 'GATE_BLOCKED', message: 'No approved run.' },
    ...overrides,
  };
}

describe('consolidation component correction handoff', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      imports: [ConsolidationComponentCorrection],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    TestBed.inject(SessionService).current.set({ userId: actorId, firmId, generation: '1', staff: true });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    try { http.verify({ ignoreCancelled: true }); }
    finally { TestBed.resetTestingModule(); sessionStorage.clear(); }
  });

  function createComponent(ids = { scopeId, groupId, componentId }) {
    const fixture = TestBed.createComponent(ConsolidationComponentCorrection);
    fixture.componentRef.setInput('scopeId', ids.scopeId);
    fixture.componentRef.setInput('groupId', ids.groupId);
    fixture.componentRef.setInput('componentId', ids.componentId);
    fixture.detectChanges();
    return fixture;
  }

  it('re-reads and shows exact pinned scope, component and package evidence with a new-scope link', async () => {
    const fixture = createComponent();
    http.expectOne(scopeUrl).flush(workspace());
    await fixture.whenStable();
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    expect(page.textContent).toContain(scopeId);
    expect(page.textContent).toContain(groupId);
    expect(page.textContent).toContain(componentId);
    expect(page.textContent).toContain(clientId);
    expect(page.textContent).toContain('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa');
    expect(page.textContent).toContain(packageHash);
    expect(page.textContent).toContain('taxonomy-v3');
    expect(page.textContent).toContain('mapping-v7');
    const link = page.querySelector<HTMLAnchorElement>('a[href*="/app/consolidation"]');
    expect(link).not.toBeNull();
    const target = new URL(link!.href);
    expect(target.searchParams.get('correctionScopeId')).toBe(scopeId);
    expect(target.searchParams.get('correctionGroupId')).toBe(groupId);
    http.expectNone(request => request.method !== 'GET');
  });

  it('resolves a component beyond the first page and refuses mismatched resolved identity', async () => {
    const fixture = createComponent();
    const original = workspace().components[0];
    http.expectOne(scopeUrl).flush(workspace({ components: [{ ...original, id: wrongScopeId }], componentPaging: { totalCount: 2, page: 1, pageSize: 1 }, resolvedComponent: original }));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.componentInstance.source().state).toBe('ready');
    fixture.componentRef.setInput('componentId', clientId);
    fixture.detectChanges();
    http.expectOne(`/api/ui/consolidation/scopes/${scopeId}?selectedComponentId=${clientId}`).flush(workspace({ resolvedComponent: original }));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.componentInstance.source().state).toBe('unknown-component');
    expect(fixture.nativeElement.querySelector('a[href*="/app/consolidation"]')).toBeNull();
  });

  it('withholds evidence and the correction link when the fetched scope belongs to another group', async () => {
    const fixture = createComponent();
    http.expectOne(scopeUrl).flush(workspace({ scope: { ...workspace().scope, groupId: wrongGroupId } }));
    await fixture.whenStable();
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    expect(page.textContent).toContain('different group than this handoff');
    expect(page.textContent).not.toContain(packageHash);
    expect(page.querySelector('a[href*="/app/consolidation"]')).toBeNull();
    http.expectNone(request => request.method !== 'GET');
  });

  it('rejects stale scope and unknown component identities', async () => {
    const staleFixture = createComponent();
    http.expectOne(scopeUrl).flush(workspace({ scope: { ...workspace().scope, id: wrongScopeId } }));
    await staleFixture.whenStable();
    staleFixture.detectChanges();
    expect((staleFixture.nativeElement as HTMLElement).textContent).toContain('scope identity no longer matches');
    expect((staleFixture.nativeElement as HTMLElement).querySelector('a[href*="/app/consolidation"]')).toBeNull();
    staleFixture.destroy();

    const unknownFixture = createComponent({ scopeId, groupId, componentId: wrongScopeId });
    http.expectOne(`/api/ui/consolidation/scopes/${scopeId}?selectedComponentId=${wrongScopeId}`).flush(workspace());
    await unknownFixture.whenStable();
    unknownFixture.detectChanges();
    expect((unknownFixture.nativeElement as HTMLElement).textContent).toContain('component identity is not present');
    expect((unknownFixture.nativeElement as HTMLElement).querySelector('a[href*="/app/consolidation"]')).toBeNull();
    http.expectNone(request => request.method !== 'GET');
  });

  it('does not fetch when a source identity is malformed', async () => {
    const fixture = createComponent({ scopeId: 'invalid', groupId, componentId });
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('missing or invalid');
    http.expectNone(() => true);
    fixture.destroy();
    const failed = createComponent();
    http.expectOne(scopeUrl).flush({}, { status: 503, statusText: 'Unavailable' });
    await failed.whenStable();
    failed.detectChanges();
    const page = failed.nativeElement as HTMLElement;
    expect(page.querySelectorAll('[role="alert"]')).toHaveLength(1);
    expect(page.textContent).not.toContain('Reopen this handoff from an authorized scope.');
  });
});
