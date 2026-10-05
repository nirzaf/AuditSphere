import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { PackageReviews } from './reviews';
import { SessionService } from '../../core/session';
import { TabDrafts } from '../../core/tab-drafts';

const userId = '11111111-1111-4111-8111-111111111111';
const packageId = '22222222-2222-4222-8222-222222222222';
const removedPackageId = '33333333-3333-4333-8333-333333333333';
const queue = [{ packageId, framework: 'IFRS', periodStart: '2026-01-01', periodEnd: '2026-12-31', currency: 'QAR', packageHash: 'a'.repeat(64),
  managementDecision: 'APPROVED', accountingDecision: 'PENDING', partnerDecision: 'PENDING', nextAction: 'ACCOUNTING_REVIEW_REQUIRED' }];

describe('package review selection draft', () => {
  let http: HttpTestingController;
  let session: SessionService;
  let drafts: TabDrafts;
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ imports: [PackageReviews], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    session = TestBed.inject(SessionService);
    session.current.set({ userId, firmId: userId, generation: '4', staff: true });
    drafts = TestBed.inject(TabDrafts);
    TestBed.tick();
  });
  afterEach(() => {
    http.verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
    sessionStorage.clear();
  });
  function open() {
    const fixture = TestBed.createComponent(PackageReviews);
    fixture.detectChanges();
    TestBed.tick();
    http.expectOne('/api/ui/accounting/reviews').flush(queue);
    fixture.detectChanges();
    return fixture;
  }
  it('saves only selected package IDs and restores them after the fresh queue loads', () => {
    const first = open();
    first.componentInstance.toggle(packageId, { target: { checked: true } } as unknown as Event);
    first.componentInstance.saveSelection(queue);
    expect(first.componentInstance.selectionDraftMessage()).toContain('saved in this tab');
    const raw = sessionStorage.getItem(sessionStorage.key(0)!);
    expect(raw ?? '').toContain(packageId);
    expect(raw ?? '').not.toContain('packageHash');
    expect(raw ?? '').not.toContain('accountingDecision');
    first.destroy();

    const second = open();
    second.componentInstance.restoreSelection(queue);
    expect(second.componentInstance.selected()).toEqual([packageId]);
    expect(second.componentInstance.selectionDraftMessage()).toContain('Restored 1 package');
    second.componentInstance.preview(queue.map((x) => ({ packageId: x.packageId, nextAction: x.nextAction })));
    expect(second.componentInstance.previewText()).toContain('No decision was recorded');
    http.expectNone((request) => request.method !== 'GET');
  });
  it('discards saved IDs that are no longer in the authorized queue', () => {
    expect(drafts.save({ entity: 'accounting/package-review-selection', baseRevision: '22bf59ad8353c8c0a5729323c669339cb5a1a0adf8b7968e2a62f9cbb63db46f' },
      [packageId, removedPackageId], (value) => Array.isArray(value) && value.every((x) => typeof x === 'string') ? value as string[] : null)).toBe(true);
    const fixture = open();
    fixture.componentInstance.restoreSelection(queue);
    expect(fixture.componentInstance.selected()).toEqual([packageId]);
    expect(fixture.componentInstance.selectionDraftMessage()).toContain('discarded 1 no longer available');
  });
  it('clears selection and its session draft when the authenticated session is invalidated', () => {
    const fixture = open();
    fixture.componentInstance.toggle(packageId, { target: { checked: true } } as unknown as Event);
    fixture.componentInstance.saveSelection(queue);
    expect(sessionStorage.length).toBe(1);
    session.clear();
    TestBed.tick();
    fixture.detectChanges();
    expect(fixture.componentInstance.selected()).toEqual([]);
    expect(sessionStorage.length).toBe(0);
  });
});
