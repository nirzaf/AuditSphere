import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { CurrencyConfiguration } from './currency-configuration';
import { SessionService } from '../../core/session';
import {
  currencyDraft,
  decodeSet,
  emptyCurrencyIntent,
  exactRate,
} from './currency-configuration-contracts';
const id = '11111111-1111-4111-8111-111111111111',
  other = '22222222-2222-4222-8222-222222222222';
const root = '/api/ui/accounting/currency-configuration';
const set = {
  id,
  firmId: id,
  code: 'SYN-SET',
  version: 1,
  source: 'Synthetic source',
  effectiveFrom: '2026-01-01',
  effectiveTo: '2026-12-31',
  status: 'DRAFT',
  createdByUserId: id,
  createdAt: '2026-01-01T00:00:00Z',
  approvedByUserId: null,
  approvedAt: null,
};
const catalogue = { revision: 'a'.repeat(64), rateSets: [set], policies: [], canWrite: true };
const review = { revision: 'b'.repeat(64), set, rates: [], canWrite: true, canApprove: false };
describe('Native firm currency configuration', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      imports: [CurrencyConfiguration],
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
    sessionStorage.clear();
  });
  function open() {
    const f = TestBed.createComponent(CurrencyConfiguration);
    f.detectChanges();
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(root).flush(catalogue);
    TestBed.tick();
    f.detectChanges();
    return { f, c: f.componentInstance, http };
  }
  async function newSet(c: CurrencyConfiguration) {
    await c.choose('set');
    c.model.set({
      ...emptyCurrencyIntent(),
      reviewed: false,
      code: 'SYN-NEW',
      source: 'Reviewed synthetic source',
    });
    TestBed.tick();
    c.confirmInputs();
  }
  it('bounds exact positive rates and never restores assent or extra sensitive fields', () => {
    expect(exactRate('3.700001')).toBe(true);
    for (const x of ['0', '-1', '3.7000001', '1e3', '3,70', '1234567890123'])
      expect(exactRate(x)).toBe(false);
    expect(currencyDraft(emptyCurrencyIntent())).toEqual(emptyCurrencyIntent());
    expect(currencyDraft({ ...emptyCurrencyIntent(), reviewed: true })).toBeNull();
    expect(currencyDraft({ ...emptyCurrencyIntent(), password: 'unused' })).toBeNull();
    expect(() =>
      decodeSet({
        ...review,
        rates: [
          {
            id,
            firmId: other,
            rateSetVersionId: id,
            fromCurrency: 'USD',
            toCurrency: 'QAR',
            rateDate: '2026-12-31',
            rateType: 'CLOSING',
            rate: '3.700001',
            direction: 'DIRECT',
            createdAt: '2026-01-01T00:00:00Z',
          },
        ],
      }),
    ).toThrow();
  });
  it('requires review and sends a single exact rate with the current set revision', async () => {
    const { c, http } = open();
    const selection = c.choose('rate', id);
    await Promise.resolve();
    http.expectOne(root + '/sets/' + id).flush(review);
    await selection;
    c.model.set({
      ...emptyCurrencyIntent(),
      reviewed: false,
      fromCurrency: 'USD',
      toCurrency: 'QAR',
      date: '2026-12-31',
      rate: '3.700001',
    });
    TestBed.tick();
    await c.submit();
    http.expectNone(root + `/sets/${id}/rates`);
    c.confirmInputs();
    const submit = c.submit();
    const req = http.expectOne(root + `/sets/${id}/rates`);
    expect(req.request.body.rate).toBe('3.700001');
    expect(req.request.body.revision).toBe(review.revision);
    expect(req.request.body.direction).toBe('DIRECT');
    const duplicate = c.submit();
    http.expectNone(root + `/sets/${id}/rates`);
    req.flush({ value: true });
    await submit;
    await duplicate;
    http.expectOne(root).flush(catalogue);
    http.expectOne(root + '/sets/' + id).flush({ ...review, revision: 'c'.repeat(64) });
    await Promise.resolve();
  });
  it('retains saved drafts across context changes and recovery requires new assent', async () => {
    const { c, http } = open();
    await newSet(c);
    expect(c.saveDraft()).toBe(true);
    const select = c.choose('rate', id);
    await Promise.resolve();
    http.expectOne(root + '/sets/' + id).flush(review);
    await select;
    await c.choose('set');
    c.recoverDraft();
    TestBed.tick();
    expect(c.model().code).toBe('SYN-NEW');
    expect(c.reviewed()).toBe(false);
  });
  it('refuses stale bases after refresh until explicit current-revision review', async () => {
    const { c, http } = open();
    await newSet(c);
    c.refresh();
    http.expectOne(root).flush({ ...catalogue, revision: 'c'.repeat(64) });
    TestBed.tick();
    expect(c.stale()).toBe(true);
    expect(c.reviewed()).toBe(false);
    c.confirmInputs();
    await c.submit();
    http.expectNone(root + '/sets');
    c.rebase();
    expect(c.stale()).toBe(false);
    expect(c.reviewed()).toBe(false);
  });
  it('unknown writes never retry and require a successful refreshed persisted-state review', async () => {
    const { c, http } = open();
    await newSet(c);
    const send = c.submit();
    http.expectOne(root + '/sets').flush({}, { status: 503, statusText: 'Unavailable' });
    await send;
    expect(c.uncertain()).toBe(true);
    c.rebase();
    expect(c.uncertain()).toBe(true);
    c.confirmInputs();
    await c.submit();
    http.expectNone(root + '/sets');
    c.refresh();
    http.expectOne(root).flush(catalogue);
    TestBed.tick();
    c.rebase();
    expect(c.uncertain()).toBe(false);
    expect(c.reviewed()).toBe(false);
  });
  it('withdraws visible review assent on a native rate input event', async () => {
    const { c, f, http } = open();
    const select = c.choose('rate', id);
    await Promise.resolve();
    http.expectOne(root + '/sets/' + id).flush(review);
    await select;
    c.model.set({
      ...emptyCurrencyIntent(),
      reviewed: false,
      fromCurrency: 'USD',
      toCurrency: 'QAR',
      date: '2026-12-31',
      rate: '3.7000001',
    });
    TestBed.tick();
    c.confirmInputs();
    f.detectChanges();
    const input = f.nativeElement.querySelector('input[inputmode="decimal"]') as HTMLInputElement;
    const checkbox = f.nativeElement.querySelector('input[type="checkbox"]') as HTMLInputElement;
    expect(checkbox.checked).toBe(true);
    input.value = '3.700001';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await f.whenStable();
    f.detectChanges();
    expect(c.model().rate).toBe('3.700001');
    expect(c.reviewed()).toBe(false);
    expect(checkbox.checked).toBe(false);
  });
  it('resets the native checkbox when edits arrive before its checked state has rendered', async () => {
    const { c, f } = open(); await newSet(c); c.invalidateReview(); f.detectChanges();
    const checkbox = f.nativeElement.querySelector('input[type="checkbox"]') as HTMLInputElement;
    const code = Array.from(f.nativeElement.querySelectorAll('label')).find((x: unknown) => (x as HTMLLabelElement).textContent?.trim() === 'Code') as HTMLLabelElement;
    checkbox.checked = true; checkbox.dispatchEvent(new Event('input', { bubbles: true })); checkbox.dispatchEvent(new Event('change', { bubbles: true }));
    const input = code.querySelector('input')!; input.value = 'SYN-CHANGED'; input.dispatchEvent(new Event('input', { bubbles: true }));
    expect(checkbox.checked).toBe(false); await f.whenStable(); expect(c.reviewed()).toBe(false);
  });
  it('clears protected details, intent and tab storage when the session changes', async () => {
    const { c } = open();
    await newSet(c);
    c.saveDraft();
    TestBed.inject(SessionService).clear();
    TestBed.tick();
    expect(c.model().code).toBe('');
    expect(c.setDetail()).toBeNull();
    expect(c.reviewed()).toBe(false);
    expect(sessionStorage.length).toBe(0);
  });
});
