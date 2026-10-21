import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SessionService } from '../../core/session';
import { PortalFinanceReceipts, portalFinanceReceiptPage } from './finance-receipts';

const id = (n: number) => `40000000-0000-4000-8000-${n.toString().padStart(12, '0')}`;
const agreementId = id(1);
const guidWithLetters = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const receipt = (n: number, kind: 'ADVANCE' | 'BALANCE' = 'ADVANCE') => ({
  receiptId: id(n), documentId: null, kind, amount: '100.00', appliedAmount: '100.00', currency: 'QAR',
  reference: `RECEIPT-${n}`, receivedAt: '2026-10-18T08:00:00Z', downloadUrl: null,
});
const receipts = (start: number) => Array.from({ length: 50 }, (_, index) => receipt(start + index));
const receiptPage = (rows: ReturnType<typeof receipt>[], hasMore = false, nextCursor: string | null = null, owner = agreementId) => ({
  agreementId: owner, receipts: rows, pageSize: 50, hasMore, nextCursor,
});

describe('Portal finance receipt history', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PortalFinanceReceipts],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.inject(SessionService).current.set({ userId: id(2), firmId: id(3), generation: '1', staff: false });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    try { http.verify({ ignoreCancelled: true }); } finally { TestBed.resetTestingModule(); }
  });

  it('accepts one receipt at both milestone kinds and rejects duplicate kind plus case-insensitive receipt ID', () => {
    const shared = (kind: 'ADVANCE' | 'BALANCE', receiptId = guidWithLetters) => ({
      ...receipt(10, kind), receiptId,
    });
    expect(() => portalFinanceReceiptPage({ ...receiptPage([
      shared('ADVANCE'), shared('BALANCE'),
    ]) }, '')).not.toThrow();
    expect(() => portalFinanceReceiptPage({ ...receiptPage([
      shared('ADVANCE'), shared('ADVANCE', guidWithLetters.toUpperCase()),
    ]) }, '')).toThrow();
    expect(() => portalFinanceReceiptPage({ ...receiptPage([]), receipts: [
      { ...shared('ADVANCE'), kind: 'UNKNOWN' },
    ] }, '')).toThrow();
  });

  it('loads older pages on demand, returns to inline rows, and refreshes the first page explicitly', async () => {
    const fixture = TestBed.createComponent(PortalFinanceReceipts);
    fixture.componentRef.setInput('agreementId', agreementId);
    fixture.componentRef.setInput('initialReceipts', receipts(100));
    fixture.componentRef.setInput('initialHasMore', true);
    fixture.componentRef.setInput('initialNextCursor', 'Y3Vyc29yLTE');
    fixture.detectChanges(); TestBed.tick(); fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const endpoint = `/api/ui/portal/finance/agreements/${agreementId}/receipts`;
    http.expectNone(`${endpoint}?pageSize=50&cursor=Y3Vyc29yLTE`);
    expect(root.querySelectorAll('table[aria-label="Payment receipts"] tbody tr')).toHaveLength(50);

    const button = (label: string) => Array.from(root.querySelectorAll('button')).find((item) => item.textContent?.trim() === label)!;
    const older = button('Older receipts');
    older.focus(); older.click(); fixture.detectChanges(); TestBed.tick();
    http.expectOne(`${endpoint}?pageSize=50&cursor=Y3Vyc29yLTE`).flush(receiptPage([receipt(900)]));
    await fixture.whenStable(); fixture.detectChanges();
    expect(root.textContent).toContain('RECEIPT-900');
    expect(root.textContent).toContain('100.00 QAR');
    expect(document.activeElement).toBe(root.querySelector('h4#portal-finance-receipts-40000000-0000-4000-8000-000000000001'));

    const newer = button('Newer receipts');
    newer.focus(); newer.click(); fixture.detectChanges(); TestBed.tick(); fixture.detectChanges();
    http.expectNone(`${endpoint}?pageSize=50`);
    expect(root.querySelectorAll('table[aria-label="Payment receipts"] tbody tr')).toHaveLength(50);
    expect(root.textContent).toContain('RECEIPT-100');
    expect(root.textContent).not.toContain('RECEIPT-900');
    expect(document.activeElement).toBe(root.querySelector('h4#portal-finance-receipts-40000000-0000-4000-8000-000000000001'));

    const refresh = button('Refresh receipts from start');
    refresh.focus(); refresh.click(); fixture.detectChanges(); TestBed.tick();
    http.expectOne(`${endpoint}?pageSize=50`).flush(receiptPage(receipts(1000)));
    await fixture.whenStable(); fixture.detectChanges();
    expect(root.textContent).toContain('RECEIPT-1000');
    const currentReferences = Array.from(root.querySelectorAll<HTMLElement>(
      'table[aria-label="Payment receipts"] tbody tr td:nth-child(2) code',
    )).map((cell) => cell.textContent?.trim() ?? '');
    expect(currentReferences).not.toContain('RECEIPT-100');
    expect(document.activeElement).toBe(refresh);
    fixture.destroy();
  });

  it('clears cached rows after a forbidden continuation and requires a successful server refresh to restore them', async () => {
    const fixture = TestBed.createComponent(PortalFinanceReceipts);
    fixture.componentRef.setInput('agreementId', agreementId);
    fixture.componentRef.setInput('initialReceipts', receipts(200));
    fixture.componentRef.setInput('initialHasMore', true);
    fixture.componentRef.setInput('initialNextCursor', 'Y3Vyc29yLTI');
    fixture.detectChanges(); TestBed.tick(); fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const endpoint = `/api/ui/portal/finance/agreements/${agreementId}/receipts`;
    const button = (label: string) => Array.from(root.querySelectorAll('button')).find((item) => item.textContent?.trim() === label)!;

    button('Older receipts').click(); fixture.detectChanges(); TestBed.tick();
    http.expectOne(`${endpoint}?pageSize=50&cursor=Y3Vyc29yLTI`).flush({}, { status: 403, statusText: 'Forbidden' });
    await fixture.whenStable(); TestBed.tick(); fixture.detectChanges();
    expect(root.textContent).not.toContain('RECEIPT-200');
    expect(root.querySelector('table[aria-label="Payment receipts"]')).toBeNull();
    expect(root.querySelector('nav[aria-label="Payment receipt pages"]')?.textContent).toContain('Refresh receipts from start');

    button('Newer receipts').click(); fixture.detectChanges(); TestBed.tick(); fixture.detectChanges();
    expect(root.textContent).not.toContain('RECEIPT-200');
    expect(root.querySelector('table[aria-label="Payment receipts"]')).toBeNull();
    http.expectNone(`${endpoint}?pageSize=50`);

    button('Refresh receipts from start').click(); fixture.detectChanges(); TestBed.tick();
    http.expectOne(`${endpoint}?pageSize=50`).flush(receiptPage(receipts(1300), false, null, id(999)));
    await fixture.whenStable(); fixture.detectChanges();
    expect(root.textContent).not.toContain('RECEIPT-1300');
    expect(root.querySelector('table[aria-label="Payment receipts"]')).toBeNull();

    button('Refresh receipts from start').click(); fixture.detectChanges(); TestBed.tick();
    http.expectOne(`${endpoint}?pageSize=50`).flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable(); fixture.detectChanges();
    expect(root.querySelector('table[aria-label="Payment receipts"]')).toBeNull();
    expect(button('Refresh receipts from start').disabled).toBe(false);

    button('Refresh receipts from start').click(); fixture.detectChanges(); TestBed.tick();
    http.expectOne(`${endpoint}?pageSize=50`).flush(receiptPage(receipts(1200)));
    await fixture.whenStable(); fixture.detectChanges();
    expect(root.textContent).toContain('RECEIPT-1200');
    expect(root.textContent).not.toContain('RECEIPT-200');
    fixture.destroy();
  });

  it('keeps continuation stacks and loaded rows isolated by agreement', async () => {
    const secondAgreementId = id(4);
    const first = TestBed.createComponent(PortalFinanceReceipts);
    first.componentRef.setInput('agreementId', agreementId);
    first.componentRef.setInput('initialReceipts', receipts(1600));
    first.componentRef.setInput('initialHasMore', true);
    first.componentRef.setInput('initialNextCursor', 'Y3Vyc29yLTM');
    const second = TestBed.createComponent(PortalFinanceReceipts);
    second.componentRef.setInput('agreementId', secondAgreementId);
    second.componentRef.setInput('initialReceipts', receipts(1700));
    second.componentRef.setInput('initialHasMore', true);
    second.componentRef.setInput('initialNextCursor', 'Y3Vyc29yLTM');
    first.detectChanges(); second.detectChanges();
    TestBed.tick(); first.detectChanges(); second.detectChanges();
    const rootOne = first.nativeElement as HTMLElement;
    const rootTwo = second.nativeElement as HTMLElement;
    const button = (root: HTMLElement, label: string) =>
      Array.from(root.querySelectorAll('button')).find((item) => item.textContent?.trim() === label)!;
    const endpointOne = `/api/ui/portal/finance/agreements/${agreementId}/receipts`;
    const endpointTwo = `/api/ui/portal/finance/agreements/${secondAgreementId}/receipts`;
    http.expectNone((request) => request.url.includes('/receipts'));

    button(rootOne, 'Older receipts').click(); first.detectChanges(); TestBed.tick();
    http.expectOne(`${endpointOne}?pageSize=50&cursor=Y3Vyc29yLTM`).flush(receiptPage([receipt(2100)], false, null, agreementId));
    await first.whenStable(); first.detectChanges();
    expect(rootOne.textContent).toContain('RECEIPT-2100');
    expect(rootTwo.textContent).toContain('RECEIPT-1700');
    expect(rootTwo.textContent).not.toContain('RECEIPT-2100');

    button(rootTwo, 'Older receipts').click(); second.detectChanges(); TestBed.tick();
    http.expectOne(`${endpointTwo}?pageSize=50&cursor=Y3Vyc29yLTM`).flush(receiptPage([receipt(2200)], false, null, secondAgreementId));
    await second.whenStable(); second.detectChanges();
    expect(rootOne.textContent).toContain('RECEIPT-2100');
    expect(rootTwo.textContent).toContain('RECEIPT-2200');
    expect(rootTwo.textContent).not.toContain('RECEIPT-1600');
    first.destroy(); second.destroy();
  });

  it('clears retained rows when the authenticated client session is invalidated', () => {
    const fixture = TestBed.createComponent(PortalFinanceReceipts);
    fixture.componentRef.setInput('agreementId', agreementId);
    fixture.componentRef.setInput('initialReceipts', receipts(1400));
    fixture.componentRef.setInput('initialHasMore', false);
    fixture.componentRef.setInput('initialNextCursor', null);
    fixture.detectChanges(); TestBed.tick(); fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('RECEIPT-1400');
    TestBed.inject(SessionService).clear();
    TestBed.tick(); fixture.detectChanges();
    expect(root.textContent).not.toContain('RECEIPT-1400');
    expect(root.querySelector('table[aria-label="Payment receipts"]')).toBeNull();
    fixture.destroy();
  });
});
