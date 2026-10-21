import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { decode } from '../../core/decode';
import { SessionService } from '../../core/session';
import { FirmReceivablesAging } from './receivables-aging';
import { decodeFirmUnbilledTime, FirmUnbilledTime } from './unbilled-time';

const id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const invoiceId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
const notice = 'Current approved-time and billing-link observations for the selected work dates. Values are standard charge-out value, not recognized WIP, revenue, collectible debt or billing authorization. Fixed-fee time and corrections require their own finance review.';

function payload(page = 0, hasMore = false, value: string | null = '3.333333') {
  return {
    from: '2026-04-15', to: '2026-04-15', observedAtUtc: '2026-04-15T12:00:00Z',
    totalEntries: '1', page, pageSize: 50, hasMore,
    rows: [{
      timeEntryId: id, revision: 1, clientId: id, clientName: 'Synthetic client',
      engagementId: id, engagementLabel: 'Synthetic engagement', workDate: '2026-04-15', durationMinutes: 1,
      currency: 'QAR', standardValue: value, bucket: value === null ? 'REVIEW_REQUIRED' : 'RESERVED', feeBasis: 'FIXED_FEE_REFERENCE',
      invoiceId, invoiceNumber: 'SYN-WIP-001', reason: null,
    }],
    subtotals: [{ currency: 'QAR', bucket: value === null ? 'REVIEW_REQUIRED' : 'RESERVED', feeBasis: 'FIXED_FEE_REFERENCE',
      entryCount: '1', minutes: '1', standardValue: value, unavailableValueCount: value === null ? '1' : '0' }],
    basisNotice: notice,
  };
}

const agingReport = {
  asOfDate: '2026-04-15', dateBasis: 'UTC', bucketPolicy: 'Synthetic report policy.', rows: [],
  clientCurrencySubtotals: [], engagementCurrencySubtotals: [], currencySubtotals: [],
};
const unbilledTimePath = '/api/ui/finance/unbilled-time';
const query = (url: string) => new URL(url, 'http://localhost').searchParams;

describe('Firm unbilled time contract', () => {
  it('keeps exact amounts, nulls, string counters and allowlisted classifications', () => {
    const report = decode(decodeFirmUnbilledTime, payload());
    expect([report.rows[0].standardValue, report.subtotals[0].entryCount, report.totalEntries])
      .toEqual(['3.333333', '1', '1']);
    expect(decode(decodeFirmUnbilledTime, payload(0, false, null)).rows[0].standardValue).toBeNull();
    const invalidCurrency = decode(decodeFirmUnbilledTime, {
      ...payload(0, false, null),
      rows: [{ ...payload(0, false, null).rows[0], currency: 'qar' }],
      subtotals: [{ ...payload(0, false, null).subtotals[0], currency: 'qar' }],
    });
    expect([invalidCurrency.rows[0].currency, invalidCurrency.rows[0].standardValue]).toEqual(['qar', null]);
    const mixed = decode(decodeFirmUnbilledTime, {
      ...payload(), subtotals: [...payload().subtotals, { currency: 'USD', bucket: 'RESERVED',
        feeBasis: 'TIME_SOURCE_REFERENCE', entryCount: '1', minutes: '60', standardValue: '200', unavailableValueCount: '0' }],
    });
    expect(mixed.subtotals.map(item => `${item.currency}:${item.feeBasis}`))
      .toEqual(['QAR:FIXED_FEE_REFERENCE', 'USD:TIME_SOURCE_REFERENCE']);
    expect(() => decode(decodeFirmUnbilledTime, {
      ...payload(), rows: [{ ...payload().rows[0], standardValue: 3.333333 }],
    })).toThrow();
    expect(() => decode(decodeFirmUnbilledTime, {
      ...payload(), rows: [{ ...payload().rows[0], bucket: 'READY_TO_BILL' }],
    })).toThrow();
    expect(() => decode(decodeFirmUnbilledTime, {
      ...payload(), subtotals: [{ ...payload().subtotals[0], entryCount: 1 }],
    })).toThrow();
    expect(() => decode(decodeFirmUnbilledTime, {
      ...payload(), rows: [{ ...payload().rows[0], invoiceId: 'javascript:alert(1)' }],
    })).toThrow();
  });
});

describe('Firm unbilled time workbench', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [FirmUnbilledTime, FirmReceivablesAging],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.inject(SessionService).current.set({ userId: id, firmId: id, generation: '0', staff: true });
  });

  afterEach(() => {
    try { TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true }); }
    finally { TestBed.resetTestingModule(); }
  });

  it('mounts below receivables only when opened and waits for Apply before requesting WIP', () => {
    const fixture = TestBed.createComponent(FirmReceivablesAging);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges(); TestBed.tick();
    http.expectOne(request => request.url.startsWith('/api/ui/finance/receivables-aging')).flush(agingReport);
    fixture.detectChanges();
    const details = fixture.nativeElement.querySelector('details.unbilled-time') as HTMLDetailsElement;
    expect(details.querySelector('audit-firm-unbilled-time')).toBeNull();
    details.open = true;
    details.dispatchEvent(new Event('toggle'));
    fixture.detectChanges();
    expect(details.querySelector('audit-firm-unbilled-time')).not.toBeNull();
    http.expectNone(request => request.url.startsWith(unbilledTimePath));
    fixture.destroy();
  });

  it('applies one bounded GET, preserves six-decimal values, and never turns null into zero', async () => {
    const fixture = TestBed.createComponent(FirmUnbilledTime);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges(); TestBed.tick();
    http.expectNone(request => request.url.startsWith(unbilledTimePath));
    const component = fixture.componentInstance;
    component.changeFrom('2026-04-15');
    component.changeTo('2026-04-15');
    const pending = component.apply();
    const request = http.expectOne(candidate => candidate.url.startsWith(unbilledTimePath));
    expect(request.request.method).toBe('GET');
    const requestQuery = query(request.request.urlWithParams);
    expect(Array.from(requestQuery.keys())).toEqual(['from', 'to', 'page', 'pageSize']);
    expect([requestQuery.get('from'), requestQuery.get('to'),
      requestQuery.get('page'), requestQuery.get('pageSize')])
      .toEqual(['2026-04-15', '2026-04-15', '0', '50']);
    request.flush(payload());
    await pending;
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('3.333333 QAR');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain(notice);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Current observations, not a historical accounting WIP balance.');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Fixed-fee reference — not an additional fee to bill');
    expect((fixture.nativeElement as HTMLElement).querySelector(`a[href="/app/practice/invoices/${invoiceId}"]`)).not.toBeNull();
    http.expectNone(candidate => candidate.method !== 'GET');

    component.changeTo('2026-04-16');
    const unknownPending = component.apply();
    const unknown = http.expectOne(candidate => candidate.url.startsWith(unbilledTimePath));
    unknown.flush({ ...payload(0, false, null), to: '2026-04-16' });
    await unknownPending;
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Unavailable — review captured rate/currency');
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('0.000000 QAR');
    fixture.destroy();
  });

  it('retains filters for paging, resets page on edits, and clears stale data on a scope refusal', async () => {
    const fixture = TestBed.createComponent(FirmUnbilledTime);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges(); TestBed.tick();
    const component = fixture.componentInstance;
    component.changeFrom('2026-04-15');
    component.changeTo('2026-04-15');
    component.changeClientId(id);
    component.changeEngagementId(id);
    const first = component.apply();
    const pageOne = http.expectOne(candidate => candidate.url.startsWith(unbilledTimePath));
    const pageOneQuery = query(pageOne.request.urlWithParams);
    expect([pageOneQuery.get('clientId'), pageOneQuery.get('engagementId')]).toEqual([id, id]);
    pageOne.flush(payload(0, true));
    await first;

    const next = component.nextPage();
    const pageTwo = http.expectOne(candidate => candidate.url.startsWith(unbilledTimePath));
    const pageTwoQuery = query(pageTwo.request.urlWithParams);
    expect([pageTwoQuery.get('page'), pageTwoQuery.get('clientId'), pageTwoQuery.get('engagementId')])
      .toEqual(['1', id, id]);
    pageTwo.flush(payload(1, true));
    await next;
    expect(component.page()).toBe(1);

    component.changeFrom('2026-04-14');
    expect(component.page()).toBe(0);
    expect(component.report()).toBeNull();
    const reloaded = component.apply();
    const refused = http.expectOne(candidate => candidate.url.startsWith(unbilledTimePath));
    refused.flush({ code: 'scope.denied', message: 'Access denied.' }, { status: 403, statusText: 'Forbidden' });
    await reloaded;
    expect(component.report()).toBeNull();
    expect(component.error()).toBe('Access denied.');
    fixture.destroy();
  });

  it('clears retained finance amounts when the session is invalidated and ignores pending completions after destruction', async () => {
    const fixture = TestBed.createComponent(FirmUnbilledTime);
    const http = TestBed.inject(HttpTestingController);
    const session = TestBed.inject(SessionService);
    await fixture.whenStable();
    const component = fixture.componentInstance;
    component.changeFrom('2026-04-15'); component.changeTo('2026-04-15');
    const first = component.apply();
    http.expectOne(candidate => candidate.url.startsWith(unbilledTimePath)).flush(payload());
    await first; await fixture.whenStable();
    expect(component.report()?.rows[0].standardValue).toBe('3.333333');
    session.current.set({ userId: invoiceId, firmId: id, generation: '1', staff: true });
    await fixture.whenStable();
    expect(component.report()).toBeNull();
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Synthetic client');
    expect(component.loading()).toBe(false);
    http.expectNone(candidate => candidate.url.startsWith(unbilledTimePath));

    session.current.set({ userId: id, firmId: id, generation: '1', staff: true });
    await fixture.whenStable();
    const oldSession = component.apply();
    const oldRequest = http.expectOne(candidate => candidate.url.startsWith(unbilledTimePath));
    session.clear(); await fixture.whenStable();
    oldRequest.flush(payload()); await oldSession;
    expect(component.report()).toBeNull();
    expect(component.error()).toBe('');
    session.current.set({ userId: invoiceId, firmId: id, generation: '2', staff: true });
    await fixture.whenStable();
    const pending = component.apply();
    const request = http.expectOne(candidate => candidate.url.startsWith(unbilledTimePath));
    fixture.destroy();
    request.flush(payload()); await pending;
    expect(component.report()).toBeNull();
    expect(component.loading()).toBe(false);
    expect(component.error()).toBe('');
  });

  it('rejects invalid filters and mismatched echoed queries without retaining amounts or accepting late responses', async () => {
    const fixture = TestBed.createComponent(FirmUnbilledTime);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges(); TestBed.tick();
    const component = fixture.componentInstance;
    component.changeClientId('not-a-guid');
    await component.apply();
    expect(component.error()).toBe('Choose a valid work-date range of at most 366 days and a supported page.');
    http.expectNone(request => request.url.startsWith(unbilledTimePath));

    component.changeClientId('');
    component.changeFrom('2026-04-15');
    component.changeTo('2026-04-15');
    const mismatched = component.apply();
    const request = http.expectOne(candidate => candidate.url.startsWith(unbilledTimePath));
    request.flush({ ...payload(), to: '2026-04-16' });
    await mismatched;
    expect(component.report()).toBeNull();
    expect(component.error()).toBe('Unsupported response.');

    const late = component.apply();
    const oldRequest = http.expectOne(candidate => candidate.url.startsWith(unbilledTimePath));
    component.changeTo('2026-04-16');
    oldRequest.flush(payload());
    await late;
    expect(component.report()).toBeNull();
    fixture.destroy();
  });
});
