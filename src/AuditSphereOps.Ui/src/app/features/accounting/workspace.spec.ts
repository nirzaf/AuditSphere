import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { SessionService } from '../../core/session';
import { AccountingWorkspace, decodeClients, decodeWorkspace, decodeSources, decodeDimensions } from './workspace';

const id = '11111111-1111-4111-8111-111111111111';
const guid = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const clientA = guid(1);
const clientB = guid(2);
const userId = guid(90);
const firmId = guid(91);

function period(periodId: string, code: string) {
  return { id: periodId, revision: '1', code, start: '2026-01-01', end: '2026-12-31', basis: 'STATUTORY', currency: 'QAR', status: 'OPEN' };
}

function clientWorkspace(clientId: string, name: string, profileId: string, periods: ReturnType<typeof period>[], hasMorePeriods = false, profileRevision = '1', currency = 'QAR') {
  return {
    clientId, name,
    profile: { id: profileId, revision: profileRevision, jurisdiction: 'QA', currency, fiscalMonth: 1, fiscalDay: 1,
      sourceSystem: 'SYNTHETIC', sourceIdentifier: 'TEST', sourceMode: 'EXTERNAL_SOURCE', status: 'ACTIVE' },
    periods, hasMorePeriods, books: [], hasMoreBooks: false, amendments: [], hasMoreAmendments: false,
    openingBridges: [], canReviewOpening: false,
  };
}

describe('Accounting contracts', () => {
  it('binds package selection to the exact reviewed period and currency', () => {
    const prior = { id, revision: '2', currency: 'QAR', code: '2026', start: '2026-01-01', end: '2026-12-31', basis: 'STATUTORY', status: 'CLOSED' };
    const response = { periodId: id, revision: '2', hasMore: false, items: [{ id, hash: 'f'.repeat(64), currency: 'QAR' }] };
    expect(decodeSources(response, prior).items[0].hash).toBe('f'.repeat(64));
    expect(() => decodeSources({ ...response, revision: '3' }, prior)).toThrow();
    expect(() => decodeSources({ ...response, items: [{ id, hash: 'f'.repeat(64), currency: 'USD' }] }, prior)).toThrow();
  });
  it('accepts bounded scoped clients and missing setup without fabricated defaults', () => {
    expect(decodeClients({ items: [{ id, name: 'Client', profileConfigured: false }], total: 1, page: 0, pageSize: 25 }).items.length).toBe(1);
    expect(decodeWorkspace({ clientId: id, name: 'Client', profile: null, periods: [], hasMorePeriods: false, books: [], hasMoreBooks: false, amendments: [], hasMoreAmendments: false, openingBridges: [], canReviewOpening: false }).profile).toBeNull();
    expect(decodeDimensions([])).toEqual([]);
  });
  it('decodes valid accounting dimensions', () => {
    const dims = decodeDimensions([{
      id,
      dimensionType: 'BRANCH',
      code: 'BR01',
      name: 'Main Branch',
      status: 'ACTIVE',
      revision: 1,
      createdAt: '2026-10-01T00:00:00Z'
    }]);
    expect(dims.length).toBe(1);
    expect(dims[0].dimensionType).toBe('BRANCH');
    expect(dims[0].code).toBe('BR01');
  });
  it('rejects invalid identity, page size and revisions encoded as numbers', () => {
    expect(() => decodeClients({ items: [], total: 0, page: 0, pageSize: 0 })).toThrow();
    expect(() => decodeClients({ items: [{ id: 'bad', name: 'Client', profileConfigured: true }], total: 1, page: 0, pageSize: 25 })).toThrow();
    expect(() => decodeWorkspace({ clientId: id, name: 'Client', profile: null, hasMorePeriods: false, books: [], hasMoreBooks: false, amendments: [], hasMoreAmendments: false, openingBridges: [], canReviewOpening: false,
      periods: [{ id, revision: 1, code: '2026', start: '2026-01-01', end: '2026-12-31', basis: 'IFRS', currency: 'QAR', status: 'DRAFT' }] })).toThrow();
    expect(() => decodeDimensions([{ id: 'bad', dimensionType: 'BRANCH', code: 'BR01', name: 'Main', status: 'ACTIVE', revision: 1, createdAt: '' }])).toThrow();
    expect(() => decodeDimensions([{ id, dimensionType: 'UNRECOGNIZED', code: 'BR01', name: 'Main', status: 'ACTIVE', revision: 1, createdAt: '2026-10-01T00:00:00Z' }])).toThrow();
    const boundedDimension = { id, dimensionType: 'BRANCH', code: 'BR01', name: 'Main', status: 'ACTIVE', revision: 1, createdAt: '2026-10-01T00:00:00Z' };
    expect(() => decodeDimensions(Array.from({ length: 1002 }, () => boundedDimension))).toThrow();
  });
});

describe('Accounting workspace mode and period navigation', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AccountingWorkspace],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).current.set({ userId, firmId, generation: '1', staff: true });
  });
  afterEach(() => {
    http.verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
  });

  async function settle(fixture: ComponentFixture<AccountingWorkspace>) {
    TestBed.tick();
    await fixture.whenStable();
    TestBed.tick();
  }

  async function open() {
    const fixture = TestBed.createComponent(AccountingWorkspace);
    await settle(fixture);
    http.expectOne('/api/ui/accounting/task-owners').flush({ hasAssignedEngagements: false, hasMoreOwners: false, owners: [] });
    http.expectOne(request => request.url === '/api/ui/accounting/clients').flush({
      items: [
        { id: clientA, name: 'Client A', profileConfigured: true },
        { id: clientB, name: 'Client B', profileConfigured: true },
      ], total: 2, page: 0, pageSize: 25,
    });
    await settle(fixture);
    return fixture;
  }

  async function select(fixture: ComponentFixture<AccountingWorkspace>, clientId: string, response: object) {
    fixture.componentInstance.select(clientId);
    await settle(fixture);
    http.expectOne('/api/ui/accounting/clients/' + clientId).flush(response);
    await settle(fixture);
    http.expectOne('/api/ui/accounting/clients/' + clientId + '/dimensions').flush([]);
    await settle(fixture);
  }

  it('locks unsupported mode changes, links exact periods, and distinguishes rejection from unknown outcomes by client', async () => {
    const fixture = await open();
    const workspace = fixture.componentInstance;
    const periodA = guid(11);
    await select(fixture, clientA, clientWorkspace(clientA, 'Client A', guid(31), [period(periodA, 'FY-A')]));

    const modeSelect = fixture.nativeElement.querySelector('select[name="sourceMode"]') as HTMLSelectElement;
    expect(modeSelect.disabled).toBe(true);
    expect((fixture.nativeElement.querySelector('input[name="currency"]') as HTMLInputElement).disabled).toBe(false);
    const periodLink = fixture.nativeElement.querySelector(`a[href="/app/accounting/periods/${periodA}"]`) as HTMLAnchorElement | null;
    expect(periodLink?.textContent?.trim()).toBe('FY-A');
    expect(fixture.nativeElement.textContent).toContain('a reason does not enable that cutover');

    workspace.form.sourceMode = 'NATIVE_BOOKKEEPING';
    workspace.reviewed = true;
    workspace.saveProfile();
    http.expectNone(request => request.method === 'POST' && request.url.endsWith('/profile'));

    await select(fixture, clientB, clientWorkspace(clientB, 'Client B', guid(32), []));
    expect(workspace.workspace()?.clientId).toBe(clientB);
    expect(fixture.nativeElement.querySelector(`a[href="/app/accounting/periods/${periodA}"]`)).toBeNull();
    expect((fixture.nativeElement.querySelector('select[name="sourceMode"]') as HTMLSelectElement).disabled).toBe(false);
    expect((fixture.nativeElement.querySelector('input[name="currency"]') as HTMLInputElement).disabled).toBe(false);

    workspace.form.currency = 'USD';
    workspace.form.reason = 'Synthetic blocked currency revision';
    workspace.reviewed = true;
    workspace.saveProfile();
    const rejected = http.expectOne(request => request.method === 'POST' && request.url.endsWith('/profile'));
    rejected.flush({ code: 'accounting.gate-blocked' }, { status: 400, statusText: 'Bad Request' });
    await settle(fixture);
    expect(workspace.uncertain()).toBe(false);
    expect(workspace.profileOutcomeUnknown()).toBe(false);
    expect(workspace.error()).toContain('rejected');

    workspace.form.currency = 'EUR';
    workspace.form.reason = 'Synthetic functional currency revision';
    workspace.reviewed = true;
    workspace.saveProfile();
    const acknowledged = http.expectOne(request => request.method === 'POST' && request.url === `/api/ui/accounting/clients/${clientB}/profile`);
    expect(acknowledged.request.body).toMatchObject({ profileId: guid(32), revision: '1', currency: 'EUR', reason: 'Synthetic functional currency revision' });
    acknowledged.flush({ revision: '2' });
    await settle(fixture);
    const readback = http.expectOne(`/api/ui/accounting/clients/${clientB}`);
    readback.flush(clientWorkspace(clientB, 'Client B', guid(32), [], false, '2', 'EUR'));
    await settle(fixture);
    http.expectOne(`/api/ui/accounting/clients/${clientB}/dimensions`).flush([]);
    await settle(fixture);
    expect(workspace.workspace()?.clientId).toBe(clientB);
    expect(workspace.workspace()?.profile?.revision).toBe('2');
    expect(workspace.workspace()?.profile?.currency).toBe('EUR');
    expect(workspace.form.currency).toBe('EUR');
    expect((fixture.nativeElement.querySelector('input[name="currency"]') as HTMLInputElement).value).toBe('EUR');

    workspace.form.currency = 'USD';
    workspace.form.reason = 'Synthetic currency reversion';
    workspace.reviewed = true;
    workspace.saveProfile();
    const unknown = http.expectOne(request => request.method === 'POST' && request.url.endsWith('/profile'));
    expect(unknown.request.body).toMatchObject({ profileId: guid(32), revision: '2', currency: 'USD' });
    unknown.flush({ code: 'server.unavailable' }, { status: 503, statusText: 'Unavailable' });
    await settle(fixture);
    expect(workspace.profileOutcomeUnknown()).toBe(true);
    expect(workspace.writeBlocked()).toBe(true);
    workspace.saveProfile();
    http.expectNone(request => request.method === 'POST' && request.url.endsWith('/profile'));

    await select(fixture, clientA, clientWorkspace(clientA, 'Client A', guid(31), [period(periodA, 'FY-A')]));
    expect(workspace.workspace()?.clientId).toBe(clientA);
    expect(workspace.profileOutcomeUnknown()).toBe(false);
    await select(fixture, clientB, clientWorkspace(clientB, 'Client B', guid(32), [], false, '2', 'EUR'));
    expect(workspace.workspace()?.clientId).toBe(clientB);
    expect(workspace.profileOutcomeUnknown()).toBe(true);
    expect(fixture.nativeElement.querySelector(`a[href="/app/accounting/periods/${periodA}"]`)).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Client B');
    workspace.saveProfile();
    http.expectNone(request => request.method === 'POST' && request.url.endsWith('/profile'));
  });

  it('locks the mode when older periods fall beyond the returned page', async () => {
    const fixture = await open();
    const workspace = fixture.componentInstance;
    await select(fixture, clientA, clientWorkspace(clientA, 'Client A', guid(31), [], true));
    expect(fixture.nativeElement.querySelector('select[name="sourceMode"]')?.disabled).toBe(true);
    workspace.form.sourceMode = 'NATIVE_BOOKKEEPING';
    workspace.reviewed = true;
    workspace.saveProfile();
    http.expectNone(request => request.method === 'POST' && request.url.endsWith('/profile'));
  });

  it('requires and sends a meaningful bounded reason for accounting basis changes', async () => {
    const fixture = await open();
    const workspace = fixture.componentInstance;
    await select(fixture, clientB, clientWorkspace(clientB, 'Client B', guid(32), []));
    const reasonInput = fixture.nativeElement.querySelector('textarea[name="reason"]') as HTMLTextAreaElement;
    expect(reasonInput.maxLength).toBe(2000);

    const currencyInput = fixture.nativeElement.querySelector('input[name="currency"]') as HTMLInputElement;
    currencyInput.value = 'USD';
    currencyInput.dispatchEvent(new Event('input'));
    workspace.reviewed = true;
    await settle(fixture);
    expect(workspace.profileReasonRequired(workspace.workspace()!)).toBe(true);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('A meaningful reason is required when changing jurisdiction, functional currency or fiscal calendar.');
    const renderedReasonInput = fixture.nativeElement.querySelector('textarea[name="reason"]') as HTMLTextAreaElement;
    expect(renderedReasonInput.getAttribute('required')).not.toBeNull();
    expect(renderedReasonInput.required).toBe(true);
    workspace.saveProfile();
    http.expectNone(request => request.method === 'POST' && request.url.endsWith('/profile'));

    workspace.form.reason = '   ';
    workspace.saveProfile();
    http.expectNone(request => request.method === 'POST' && request.url.endsWith('/profile'));

    workspace.form.reason = 'x'.repeat(2001);
    workspace.saveProfile();
    http.expectNone(request => request.method === 'POST' && request.url.endsWith('/profile'));
    expect(workspace.form.reason).toHaveLength(2001);

    workspace.form.reason = 'Synthetic functional currency change';
    workspace.saveProfile();
    const request = http.expectOne(request => request.method === 'POST' && request.url.endsWith('/profile'));
    expect(request.request.body.reason).toBe('Synthetic functional currency change');
    request.flush({ code: 'accounting.gate-blocked' }, { status: 400, statusText: 'Bad Request' });
    await settle(fixture);
    expect(workspace.error()).toContain('rejected');
  });

  it('keeps malformed successful profile responses unresolved after a scoped readback', async () => {
    const fixture = await open();
    const workspace = fixture.componentInstance;
    await select(fixture, clientB, clientWorkspace(clientB, 'Client B', guid(32), []));
    workspace.form.currency = 'USD';
    workspace.form.reason = 'Synthetic malformed-response currency revision';
    workspace.reviewed = true;
    workspace.saveProfile();
    http.expectOne(request => request.method === 'POST' && request.url.endsWith('/profile'))
      .flush({ revision: 2 });
    await settle(fixture);
    expect(workspace.profileOutcomeUnknown()).toBe(true);

    await select(fixture, clientB, clientWorkspace(clientB, 'Client B', guid(32), []));
    expect(workspace.workspace()?.clientId).toBe(clientB);
    expect(workspace.profileOutcomeUnknown()).toBe(true);
    workspace.saveProfile();
    http.expectNone(request => request.method === 'POST' && request.url.endsWith('/profile'));
  });
});
