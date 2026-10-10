import { TestBed } from '@angular/core/testing';
import { APP_BASE_HREF } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';
import { BehaviorSubject } from 'rxjs';
import { Confirmations } from './confirmations';
import { SessionService } from '../../core/session';
import { decode } from '../../core/decode';
import {
  confirmationPage,
  confirmationDetail,
  substantiveResponse,
  confirmationAmount,
} from './confirmation-contracts';
const id = '11111111-1111-4111-8111-111111111111',
  path = '/api/ui/engagements/' + id + '/confirmations';
const row = {
  id,
  areaCode: 'CASH_BANK',
  sourceRecordId: 'source',
  respondent: 'Synthetic bank',
  bookedAmount: '123.12345678',
  currency: 'QAR',
  confirmationDate: '2026-10-02',
  status: 'APPROVED',
  dispatchedAt: null,
  monitoring: 'NOT_DISPATCHED',
  critical: true,
  criticalityRationale: 'Evidence',
  ownerId: id,
};
const page = {
  engagementId: id,
  page: 0,
  hasMore: false,
  outstandingCritical: 1,
  createReviewToken: 'a'.repeat(64),
  canPrepare: true,
  canReview: true,
  canSetCriticality: true,
  areaCodes: ['CASH_BANK', 'LEGAL'],
  items: [row],
};
const detail = {
  case: row,
  reviewToken: 'b'.repeat(64),
  contactValidationSource: 'Approved contact register',
  dispatchReference: null,
  preparedByMe: false,
  responses: [],
  alternatives: [],
  closure: null,
};
describe('Confirmation evidence controls', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      imports: [Confirmations],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: APP_BASE_HREF, useValue: '/ui/' },
        {
          provide: ActivatedRoute,
          useValue: { paramMap: new BehaviorSubject(convertToParamMap({ id })) },
        },
      ],
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
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
    sessionStorage.clear();
  });
  function open() {
    const f = TestBed.createComponent(Confirmations),
      http = TestBed.inject(HttpTestingController);
    f.detectChanges();
    http.expectOne(path + '?page=0&filter=ALL').flush(page);
    TestBed.tick();
    return { f, c: f.componentInstance, http };
  }
  function select(c: Confirmations, http: HttpTestingController) {
    c.select(id);
    TestBed.tick();
    http.expectOne(path + '/' + id).flush(detail);
    TestBed.tick();
  }
  it('binds native Signal Forms, preserves exact input and requires explicit review', async () => {
    const { f, c, http } = open();
    c.createModel.set({
      areaCode: 'CASH_BANK',
      sourceRecordId: 'bank-1',
      bookedAmount: '987654321.123456',
      currency: 'QAR',
      confirmationDate: '2026-10-02',
      respondent: 'Synthetic bank',
      contactValidationSource: 'Verified source',
      reviewed: false,
    });
    TestBed.tick();
    expect(c.canCreate()).toBe(false);
    c.createModel.update((m) => ({ ...m, reviewed: true }));
    TestBed.tick();
    expect(c.canCreate()).toBe(true);
    const save = c.create();
    const r = http.expectOne(path);
    expect(r.request.body.bookedAmount).toBe('987654321.123456');
    expect(r.request.body.reviewToken).toBe(page.createReviewToken);
    r.flush({ value: { confirmationCaseId: id, status: 'DRAFT' } });
    await save;
    http.expectOne(path + '?page=0&filter=ALL').flush(page);
    TestBed.tick();
    expect(c.createModel().reviewed).toBe(false);
    f.destroy();
  });
  it('never treats prepared state as dispatch and sends uncertain actions once', async () => {
    const { f, c, http } = open();
    select(c, http);
    expect(f.nativeElement.textContent).toContain('Not dispatched');
    expect(f.nativeElement.textContent).toContain('Exact booked amount 123.12345678');
    c.chooseAction('DISPATCH');
    c.actionModel.update((m) => ({ ...m, reference: 'Observed dispatch', reviewed: false }));
    TestBed.tick();
    c.actionModel.update((m) => ({ ...m, reviewed: true }));
    expect(c.canAct()).toBe(true);
    const save = c.act();
    http
      .expectOne(path + '/' + id + '/actions')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    await save;
    expect(c.uncertain()).toBe(true);
    await c.act();
    http.expectNone(path + '/' + id + '/actions');
    c.refresh();
    TestBed.tick();
    http.expectOne(path + '?page=0&filter=ALL').flush(page);
    TestBed.tick();
    expect(c.uncertain()).toBe(false);
    http.expectOne(path + '/' + id).flush(detail);
    TestBed.tick();
    expect(c.actionModel().reviewed).toBe(false);
    f.destroy();
  });
  it('clears reviewed action on new evidence and invalidates protected content on session change', () => {
    const { f, c, http } = open();
    select(c, http);
    c.chooseAction('CRITICALITY');
    c.actionModel.update((m) => ({ ...m, rationale: 'Material', reviewed: false }));
    TestBed.tick();
    c.actionModel.update((m) => ({ ...m, reviewed: true }));
    expect(c.canAct()).toBe(true);
    c.detail.reload();
    http.expectOne(path + '/' + id).flush({ ...detail, reviewToken: 'c'.repeat(64) });
    TestBed.tick();
    expect(c.actionModel().reviewed).toBe(false);
    TestBed.inject(SessionService).current.set(null);
    TestBed.inject(SessionService).invalidation.update((n) => n + 1);
    TestBed.tick();
    expect(c.ws.data()).toBeNull();
    expect(c.detail.data()).toBeNull();
    expect(f.nativeElement.textContent).not.toContain('Synthetic bank');
    f.destroy();
  });
  it('reviews every batch case, refuses duplicate sources and resets review after edits', async () => {
    const { f, c, http } = open();
    const cases = [
      {
        sourceRecordId: 'bank-1',
        bookedAmount: '987654321.123456',
        respondent: 'Bank 1',
        contactValidationSource: 'Verified source',
      },
      {
        sourceRecordId: 'bank-2',
        bookedAmount: '-1.000001',
        respondent: 'Bank 2',
        contactValidationSource: 'Verified source',
      },
    ];
    c.batchModel.set({
      areaCode: 'CASH_BANK',
      currency: 'QAR',
      confirmationDate: '2026-10-02',
      procedureId: '',
      cases,
      reviewed: false,
    });
    TestBed.tick();
    expect(c.canCreateBatch()).toBe(false);
    c.batchModel.update((m) => ({ ...m, reviewed: true }));
    TestBed.tick();
    expect(c.canCreateBatch()).toBe(true);
    c.batchModel.update((m) => ({
      ...m,
      cases: [cases[0], { ...cases[1], sourceRecordId: 'bank-1 ' }],
    }));
    TestBed.tick();
    expect(c.batchModel().reviewed).toBe(false);
    c.batchModel.update((m) => ({ ...m, reviewed: true }));
    TestBed.tick();
    expect(c.canCreateBatch()).toBe(false);
    c.batchModel.update((m) => ({ ...m, cases }));
    TestBed.tick();
    c.batchModel.update((m) => ({ ...m, reviewed: true }));
    TestBed.tick();
    const save = c.createBatch();
    const r = http.expectOne(path + '/batch');
    expect(r.request.body.cases[0].bookedAmount).toBe('987654321.123456');
    expect(r.request.body.cases[1].bookedAmount).toBe('-1.000001');
    expect(r.request.body.procedureId).toBeNull();
    expect(r.request.body.reviewToken).toBe(page.createReviewToken);
    r.flush({ value: { createdCount: 2 } });
    await save;
    http.expectOne(path + '?page=0&filter=ALL').flush(page);
    TestBed.tick();
    expect(c.batchModel().cases.length).toBe(1);
    expect(c.batchModel().reviewed).toBe(false);
    f.destroy();
  });
  it('bounds batch selection and fences lost batch outcomes without a retry', async () => {
    const { f, c, http } = open();
    c.addBatchCase();
    expect(c.batchModel().cases.length).toBe(2);
    c.removeBatchCase(0);
    expect(c.batchModel().cases.length).toBe(1);
    const row = {
      sourceRecordId: 'bank-1',
      bookedAmount: '1.000001',
      respondent: 'Bank 1',
      contactValidationSource: 'Verified source',
    };
    c.batchModel.set({
      areaCode: 'CASH_BANK',
      currency: 'QAR',
      confirmationDate: '2026-10-02',
      procedureId: '',
      cases: [row],
      reviewed: false,
    });
    TestBed.tick();
    c.batchModel.update((m) => ({ ...m, reviewed: true }));
    TestBed.tick();
    const save = c.createBatch();
    http.expectOne(path + '/batch').flush({}, { status: 503, statusText: 'Unavailable' });
    await save;
    expect(c.uncertain()).toBe(true);
    await c.createBatch();
    http.expectNone(path + '/batch');
    TestBed.inject(SessionService).current.set(null);
    TestBed.inject(SessionService).invalidation.update((n) => n + 1);
    TestBed.tick();
    expect(c.batchModel().cases[0].respondent).toBe('');
    f.destroy();
  });
  it('shows retained closure decisions and labels historical missing evidence honestly', () => {
    const { f, c, http } = open();
    c.select(id);
    TestBed.tick();
    http.expectOne(path + '/' + id).flush({
      ...detail,
      case: { ...row, status: 'CLOSED' },
      closure: {
        id,
        conclusion: 'Current independent evidence supports closure',
        evidenceSha256: 'd'.repeat(64),
        closedByUserId: id,
        closedAt: '2026-10-02T12:00:00Z',
      },
    });
    TestBed.tick();
    expect(f.nativeElement.textContent).toContain('Current independent evidence supports closure');
    expect(f.nativeElement.textContent).toContain('Reviewed evidence SHA-256');
    c.detail.reload();
    http.expectOne(path + '/' + id).flush({ ...detail, case: { ...row, status: 'CLOSED' } });
    TestBed.tick();
    expect(f.nativeElement.textContent).toContain(
      'Historical closure has no retained reviewer conclusion',
    );
    expect(f.nativeElement.textContent).not.toContain(
      'Current independent evidence supports closure',
    );
    f.destroy();
  });
  it('recovers only explicitly and requires fresh review after edits', () => {
    const { f, c, http } = open();
    c.createModel.update((m) => ({
      ...m,
      respondent: 'Recoverable bank',
      bookedAmount: '1.000001',
    }));
    TestBed.tick();
    expect(c.saveDraft('create')).toBe(true);
    f.destroy();
    const reopened = open();
    expect(reopened.c.createModel().respondent).toBe('');
    expect(reopened.c.candidates().create).toBe('ready');
    reopened.c.recoverDraft('create');
    TestBed.tick();
    expect(reopened.c.createModel().respondent).toBe('Recoverable bank');
    expect(reopened.c.createModel().reviewed).toBe(false);
    http.expectNone(path);
    reopened.f.destroy();
  });
  it('preserves intent but blocks submission against changed evidence until explicit rebase', () => {
    const { f, c, http } = open();
    select(c, http);
    c.chooseAction('CRITICALITY');
    c.actionModel.update((m) => ({ ...m, rationale: 'Retain me' }));
    TestBed.tick();
    c.actionModel.update((m) => ({ ...m, reviewed: true }));
    expect(c.canAct()).toBe(true);
    c.detail.reload();
    http.expectOne(path + '/' + id).flush({ ...detail, reviewToken: 'c'.repeat(64) });
    TestBed.tick();
    expect(c.actionModel().rationale).toBe('Retain me');
    expect(c.stale('action')).toBe(true);
    c.actionModel.update((m) => ({ ...m, reviewed: true }));
    expect(c.canAct()).toBe(false);
    c.rebase('action');
    expect(c.actionModel().reviewed).toBe(false);
    c.actionModel.update((m) => ({ ...m, reviewed: true }));
    expect(c.canAct()).toBe(true);
    f.destroy();
  });
  it('guards navigation and refresh, retains edits on keep, and warns honestly when storage fails', async () => {
    const { f, c, http } = open();
    c.createModel.update((m) => ({ ...m, respondent: 'Unsaved bank' }));
    TestBed.tick();
    const dialog = vi
      .spyOn(TestBed.inject(MatDialog), 'open')
      .mockReturnValue({ afterClosed: () => of('keep') } as never);
    expect(await c.confirmNavigation()).toBe(false);
    c.refresh();
    await Promise.resolve();
    http.expectNone(path + '?page=0&filter=ALL');
    expect(c.createModel().respondent).toBe('Unsaved bank');
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('Quota exceeded', 'QuotaExceededError');
    });
    dialog.mockReturnValue({ afterClosed: () => of('save') } as never);
    expect(await c.confirmNavigation()).toBe(false);
    expect(c.draftMessage()).toContain('memory only');
    const event = new Event('beforeunload', { cancelable: true });
    c.beforeUnload(event as BeforeUnloadEvent);
    expect(event.defaultPrevented).toBe(true);
    vi.restoreAllMocks();
    f.destroy();
  });
  it('restores native selector state when a dirty context change is declined', async () => {
    const { f, c } = open();
    c.createModel.update((m) => ({ ...m, respondent: 'Retain context' }));
    TestBed.tick();
    vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({
      afterClosed: () => of('keep'),
    } as never);
    const target = f.nativeElement.querySelector('select') as HTMLSelectElement;
    target.value = 'CRITICAL';
    target.dispatchEvent(new Event('change'));
    await Promise.resolve();
    TestBed.tick();
    expect(target.value).toBe('ALL');
    expect(c.filter()).toBe('ALL');
    f.destroy();
  });
  it('clears confirmation drafts and open dialogs when the session is revoked', async () => {
    const { f, c } = open();
    c.batchModel.update((m) => ({ ...m, areaCode: 'CASH_BANK' }));
    TestBed.tick();
    c.saveDraft('batch');
    expect(sessionStorage.length).toBe(1);
    TestBed.inject(SessionService).clear();
    TestBed.tick();
    expect(sessionStorage.length).toBe(0);
    expect(c.batchModel().areaCode).toBe('');
    expect(await c.confirmNavigation()).toBe(true);
    f.destroy();
  });
  it('recovers a lost submission as uncertain and never replays a command automatically', async () => {
    const { f, c, http } = open();
    c.createModel.set({
      areaCode: 'CASH_BANK',
      sourceRecordId: 'bank-unknown',
      bookedAmount: '1.000001',
      currency: 'QAR',
      confirmationDate: '2026-10-02',
      respondent: 'Uncertain bank',
      contactValidationSource: 'Verified source',
      reviewed: false,
    });
    TestBed.tick();
    c.createModel.update((m) => ({ ...m, reviewed: true }));
    const pending = c.create();
    http.expectOne(path).flush({}, { status: 503, statusText: 'Unavailable' });
    await pending;
    f.destroy();
    const reopened = open();
    reopened.c.recoverDraft('create');
    TestBed.tick();
    expect(reopened.c.uncertain()).toBe(true);
    expect(reopened.c.createModel().reviewed).toBe(false);
    await reopened.c.create();
    http.expectNone(path);
    reopened.f.destroy();
  });
  it('bounds response windows and does not interpret reviewed nonresponse as substantive evidence', () => {
    expect(() => decode(confirmationPage, { ...page, items: Array(26).fill(row) })).toThrow();
    const d = decode(confirmationDetail, {
      ...detail,
      responses: [
        {
          id,
          revision: '1',
          origin: 'FOLLOW_UP',
          channel: 'DIRECT',
          reference: 'observed',
          confirmedAmount: null,
          differenceAmount: null,
          authenticityAssessment: 'No response',
          decision: 'NO_RESPONSE',
          receivedAt: '2026-10-02T12:00:00Z',
          preparedByMe: false,
          reviewerId: id,
          reviewedAt: '2026-10-02T12:01:00Z',
        },
      ],
    });
    expect(substantiveResponse(d)).toBe(false);
    expect(confirmationAmount('123456789.12345678')).toBeNull();
    expect(confirmationAmount('123456789.123456')).toBe('123456789.123456');
  });
});
