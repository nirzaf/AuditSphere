import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { IntakeCurrencyReview, decodeCurrencyReview, reviewThreshold } from './currency-review';
import { SessionService } from '../../core/session';
const id = '11111111-1111-4111-8111-111111111111',
  other = '22222222-2222-4222-8222-222222222222';
const rate = {
  fromCurrency: 'USD',
  toCurrency: 'QAR',
  rateType: 'CLOSING',
  rateDate: '2026-12-31',
  rate: '3.700000',
  direction: 'DIRECT',
  source: 'Synthetic approved rates',
  setVersion: 1,
  rateSetId: id,
  observationId: id,
  setCode: 'SYN-FX',
  effectiveFrom: null,
  effectiveTo: null,
};
const line = {
  accountCode: '1000',
  accountName: 'Synthetic cash',
  sourceAmount: '100.123456',
  translatedAmount: '370.46',
  priorTranslatedAmount: null,
  variance: null,
  variancePercent: null,
  highlighted: false,
  highlightReason: null,
};
const response = {
  datasetId: id,
  clientId: id,
  engagementId: id,
  periodId: id,
  sourceCurrency: 'USD',
  presentationCurrency: 'QAR',
  periodEnd: '2026-12-31',
  currentRate: rate,
  priorDatasetId: null,
  priorPeriodEnd: null,
  priorRate: null,
  thresholdPercent: '10.123456',
  thresholdAmount: '0',
  method: 'UPLOAD_CLOSING_COMPARISON',
  rounding: 'Two decimal places; midpoint to even',
  revision: 'a'.repeat(64),
  lines: [line],
};
const url = `/api/ui/datasets/${id}/currency-review?presentation=QAR&percent=10.123456&amount=0`;
describe('Native intake currency review', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [IntakeCurrencyReview],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.inject(SessionService).current.set({
      userId: id,
      firmId: id,
      generation: '0',
      staff: true,
    });
  });
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
  });
  function open() {
    const f = TestBed.createComponent(IntakeCurrencyReview);
    f.componentRef.setInput('datasetId', id);
    f.componentRef.setInput('clientId', id);
    f.componentRef.setInput('engagementId', id);
    f.detectChanges();
    TestBed.tick();
    const c = f.componentInstance;
    c.model.set({ presentation: 'QAR', thresholdPercent: '10.123456', thresholdAmount: '0' });
    TestBed.tick();
    f.detectChanges();
    return { f, c, http: TestBed.inject(HttpTestingController) };
  }
  it('validates exact threshold text and refuses rounded, exponential, locale or negative input', () => {
    expect(reviewThreshold('10.123456')).toBe('10.123456');
    expect(reviewThreshold(' 0 ')).toBe('0');
    for (const v of ['1.1234567', '123456789012345', '1e3', '1,000', '-1'])
      expect(reviewThreshold(v)).toBeNull();
  });
  it('accepts explicit same-currency identity without invented market evidence and rejects malformed rate context', () => {
    const identity = {
      ...response,
      presentationCurrency: 'USD',
      currentRate: {
        ...rate,
        toCurrency: 'USD',
        rate: '1',
        rateType: 'IDENTITY',
        source: 'Same currency',
        setVersion: 0,
        rateSetId: null,
        observationId: null,
        setCode: null,
      },
    };
    expect(decodeCurrencyReview(identity).currentRate.rateType).toBe('IDENTITY');
    for (const currentRate of [
      { ...rate, rate: '0' },
      { ...rate, rate: '-1' },
      { ...rate, direction: 'INVERSE' },
      { ...rate, rateDate: '2025-12-31' },
      { ...rate, rateSetId: null },
      { ...rate, fromCurrency: 'EUR' },
    ])
      expect(() => decodeCurrencyReview({ ...response, currentRate })).toThrow();
    expect(() =>
      decodeCurrencyReview({
        ...identity,
        currentRate: { ...identity.currentRate, observationId: id },
      }),
    ).toThrow();
  });
  it('renders exact provenance, declared method and bounded pages without any approval control', async () => {
    const { f, c, http } = open();
    const pending = c.load();
    expect(c.busy()).toBe(true);
    http
      .expectOne(url)
      .flush({
        ...response,
        lines: Array.from({ length: 51 }, (_, n) => ({ ...line, accountCode: String(n) })),
      });
    await pending;
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('100.123456');
    expect(f.nativeElement.textContent).toContain('Synthetic approved rates');
    expect(f.nativeElement.textContent).toContain('Observation');
    expect(f.nativeElement.textContent).toContain('midpoint to even');
    expect(f.nativeElement.textContent).toContain('No authorized prior');
    expect(f.nativeElement.querySelectorAll('tbody tr').length).toBe(50);
    c.page.set(1);
    f.detectChanges();
    expect(f.nativeElement.querySelectorAll('tbody tr').length).toBe(1);
    expect(
      [...f.nativeElement.querySelectorAll('button')].some((b: unknown) =>
        /approve/i.test((b as HTMLElement).textContent ?? ''),
      ),
    ).toBe(false);
    f.destroy();
  });
  it('clears an old result before a failed refreshed calculation and keeps the blocker actionable', async () => {
    const { f, c, http } = open();
    let pending = c.load();
    http.expectOne(url).flush(response);
    await pending;
    pending = c.load();
    expect(c.review()).toBeNull();
    http
      .expectOne(url)
      .flush(
        {
          message:
            'No valid approved DIRECT closing rate; review direction and positive observation.',
        },
        { status: 400, statusText: 'Bad Request' },
      );
    await pending;
    f.detectChanges();
    expect(c.error()).toContain('DIRECT');
    expect(f.nativeElement.querySelector('table')).toBeNull();
    f.destroy();
  });
  it('does not replace current filters with a late response and marks displayed results stale after filter edits', async () => {
    const { f, c, http } = open();
    let pending = c.load();
    c.model.update((m) => ({ ...m, thresholdPercent: '20' }));
    http.expectOne(url).flush(response);
    await pending;
    expect(c.review()).toBeNull();
    pending = c.load();
    http.expectOne(url.replace('10.123456', '20')).flush({ ...response, thresholdPercent: '20' });
    await pending;
    c.model.update((m) => ({ ...m, thresholdAmount: '1' }));
    f.detectChanges();
    expect(c.stale()).toBe(true);
    expect(f.nativeElement.textContent).toContain('Request a fresh review');
    f.destroy();
  });
  it('withdraws protected state across dataset context changes and session revocation', async () => {
    const { f, c, http } = open();
    const pending = c.load();
    f.componentRef.setInput('datasetId', other);
    TestBed.tick();
    http.expectOne(url).flush(response);
    await pending;
    expect(c.review()).toBeNull();
    expect(c.model().presentation).toBe('');
    c.model.set({ presentation: 'QAR', thresholdPercent: '10.123456', thresholdAmount: '0' });
    const next = c.load();
    TestBed.inject(SessionService).clear();
    TestBed.tick();
    http.expectOne(url.replace(id, other)).flush({ ...response, datasetId: other });
    await next;
    expect(c.review()).toBeNull();
    expect(c.model().presentation).toBe('');
    expect(c.canRead()).toBe(false);
    f.destroy();
  });
  it('refuses a server response for another client or engagement even when the dataset ID matches', async () => {
    const { f, c, http } = open();
    const pending = c.load();
    http.expectOne(url).flush({ ...response, clientId: other });
    await pending;
    expect(c.review()).toBeNull();
    expect(c.error()).toContain('Unsupported review context');
    f.destroy();
  });
});
