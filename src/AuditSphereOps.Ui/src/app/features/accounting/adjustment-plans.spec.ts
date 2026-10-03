import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { AdjustmentPlanReview, AdjustmentPlanQueue, decodePlanReview } from './adjustment-plans';

const id = '11111111-1111-4111-8111-111111111111',
  other = '22222222-2222-4222-8222-222222222222';
const row = {
  journalNumber: 'AJ-SYN',
  journalRevision: 1,
  layer: 'REPORTING',
  technicalStatus: 'Posted',
  reflectionState: 'NOT_REFLECTED',
  classification: 'ELIGIBLE',
  reasonCode: '',
  plannedReflectionState: 'NOT_REFLECTED',
  journalId: id,
  evidence: 'Synthetic exact source bridge',
  reviewedByUserId: other,
  reviewedAt: '2026-10-03T00:00:00Z',
};
const view = {
  id,
  clientId: id,
  engagementId: id,
  datasetId: id,
  sourceRevision: 1,
  sourceDigest: 'd'.repeat(64),
  periodId: id,
  periodCode: 'FY26',
  periodStart: '2026-01-01',
  periodEnd: '2026-12-31',
  bookId: id,
  bookCode: 'STAT',
  basis: 'IFRS',
  currency: 'QAR',
  status: 'Draft',
  createdByUserId: other,
  createdAt: '2026-10-03T00:00:00Z',
  resultHash: null,
  retainedDebits: '0',
  retainedCredits: '0',
  retainedAppliedCount: 0,
  membershipDigest: 'a'.repeat(64),
  reviewBasis: 'b'.repeat(64),
  eligibleCount: 1,
  excludedCount: 0,
  blockedCount: 0,
  blockers: [],
  journals: [row],
  page: 0,
  hasMore: false,
};

describe('native adjustment plan review', () => {
  let http: HttpTestingController;
  function setup() {
    const params = new BehaviorSubject(convertToParamMap({ id }));
    TestBed.configureTestingModule({
      imports: [AdjustmentPlanReview, AdjustmentPlanQueue],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap: params, data: new BehaviorSubject({}) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).current.set({
      userId: other,
      firmId: id,
      generation: '1',
      staff: true,
    });
    return params;
  }
  afterEach(() => {
    http.verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
  });
  it('refuses truncated membership, numeric amounts and impossible eligible reflection', () => {
    setup();
    expect(decodePlanReview(view).eligibleCount).toBe(1);
    expect(() => decodePlanReview({ ...view, journals: [] })).toThrow();
    expect(() => decodePlanReview({ ...view, retainedDebits: 100.123456 })).toThrow();
    expect(() =>
      decodePlanReview({ ...view, journals: [{ ...row, reflectionState: 'UNKNOWN' }] }),
    ).toThrow();
    expect(() => decodePlanReview({ ...view, status: 'Finalized' })).toThrow();
  });
  it('shows retained exact amounts separately from blocked current applicability', () => {
    setup();
    const f = TestBed.createComponent(AdjustmentPlanReview);
    f.detectChanges();
    http
      .expectOne(`/api/ui/accounting/adjustment-plans/${id}?page=0`)
      .flush({
        ...view,
        status: 'Finalized',
        resultHash: 'c'.repeat(64),
        retainedDebits: '200.246912',
        retainedCredits: '200.246912',
        retainedAppliedCount: 1,
        eligibleCount: 0,
        blockedCount: 1,
        blockers: ['Resolve the changed source bridge.'],
        journals: [
          {
            ...row,
            reflectionState: 'REFLECTED',
            classification: 'BLOCKED',
            reasonCode: 'reflection.changed-since-plan',
          },
        ],
      });
    f.detectChanges();
    http.expectOne(`/api/ui/accounting/adjustment-plans/${id}/history?page=0`).flush({ items: [], page: 0, hasMore: false });
    f.detectChanges();
    const text = f.nativeElement.textContent as string;
    expect(text).toContain('Retained plan calculation');
    expect(text).toContain('200.246912');
    expect(text).toContain('0 eligible');
    expect(text).toContain('Source reflection changed after this plan was created');
    expect(f.nativeElement.querySelector('form')).toBeNull();
  });
  it('clears old membership during same-component route changes', () => {
    const params = setup();
    const f = TestBed.createComponent(AdjustmentPlanReview);
    f.detectChanges();
    http.expectOne(`/api/ui/accounting/adjustment-plans/${id}?page=0`).flush(view);
    f.detectChanges();
    http.expectOne(`/api/ui/accounting/adjustment-plans/${id}/history?page=0`).flush({ items: [], page: 0, hasMore: false });
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('AJ-SYN');
    params.next(convertToParamMap({ id: other }));
    f.detectChanges();
    expect(f.nativeElement.textContent).not.toContain('AJ-SYN');
    http
      .expectOne(`/api/ui/accounting/adjustment-plans/${other}?page=0`)
      .flush({ code: 'scope.denied' }, { status: 403, statusText: 'Forbidden' });
    f.detectChanges();
    expect(f.nativeElement.textContent).not.toContain('AJ-SYN');
    expect(f.nativeElement.textContent).toContain('current scope');
  });
  it('cancels a pending protected read when the session ends', () => {
    setup();
    const f = TestBed.createComponent(AdjustmentPlanReview);
    f.detectChanges();
    const request = http.expectOne(`/api/ui/accounting/adjustment-plans/${id}?page=0`);
    TestBed.inject(SessionService).clear();
    f.detectChanges();
    expect(request.cancelled).toBe(true);
    expect(f.nativeElement.textContent).not.toContain('AJ-SYN');
  });
  it('server-pages the authorized queue without displaying a tenant-wide total', () => {
    setup();
    const f = TestBed.createComponent(AdjustmentPlanQueue);
    f.detectChanges();
    http
      .expectOne('/api/ui/accounting/adjustment-plans?page=0')
      .flush({
        items: [
          {
            id,
            datasetId: id,
            clientName: 'Scoped client',
            engagementName: 'Audit',
            status: 'Draft',
            createdAt: '2026-10-03T00:00:00Z',
          },
        ],
        page: 0,
        hasMore: true,
      });
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Scoped client');
    f.componentInstance.page.set(1);
    f.detectChanges();
    expect(f.nativeElement.textContent).not.toContain('Scoped client');
    http
      .expectOne('/api/ui/accounting/adjustment-plans?page=1')
      .flush({ items: [], page: 1, hasMore: false });
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('No adjustment plans');
  });
});
