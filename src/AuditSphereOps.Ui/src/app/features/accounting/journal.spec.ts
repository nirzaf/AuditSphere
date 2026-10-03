import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { AdjustmentJournal } from './journal';
import { decodeJournalReview, decodeJournalPreview, journalFields } from './journal-contracts';

const id = '11111111-1111-4111-8111-111111111111',
  actor = '22222222-2222-4222-8222-222222222222',
  root = '/api/ui/accounting/journals/' + id;
const lines = [
  { accountCode: '1000', debit: '100.123456', credit: '0' },
  { accountCode: '3000', debit: '0', credit: '100.123456' },
];
const v = {
  journalId: id,
  clientId: id,
  engagementId: id,
  journalNumber: 'AJ-SYN',
  status: 'Draft',
  revision: 1,
  reviewBasis: 'a'.repeat(64),
  datasetId: id,
  datasetRevision: 1,
  datasetDigest: 'b'.repeat(64),
  periodId: id,
  periodStart: '2026-01-01',
  periodEnd: '2026-12-31',
  bookId: id,
  currency: 'QAR',
  purpose: 'REPORTING_ADJUSTMENT',
  origin: 'AUDIT_PROPOSED',
  reason: 'Synthetic reason',
  evidenceReference: 'Synthetic evidence',
  returnReason: null,
  preparerId: actor,
  supersedesId: null,
  reversalOfId: null,
  lines,
  totalDebit: '100.123456',
  totalCredit: '100.123456',
  blocker: null,
  canEdit: true,
  canSubmit: true,
  canReturn: false,
  canPost: false,
  canReverse: false,
  historyCount: 0,
  historyPage: 1,
  history: [],
  managementDecision: null,
  sourceReflection: null,
  canReconcileReflection: false,
};
const preview = {
  action: 'UPDATE',
  reviewBasis: v.reviewBasis,
  requestHash: 'c'.repeat(64),
  canProceed: true,
  blocker: null,
  errors: [],
  totalDebit: '100.123456',
  totalCredit: '100.123456',
  lines,
};
describe('Exact native journal editor and receipt recovery', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      imports: [AdjustmentJournal],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { paramMap: new BehaviorSubject(convertToParamMap({ id })) },
        },
      ],
    });
    TestBed.inject(SessionService).current.set({
      userId: actor,
      firmId: id,
      generation: '1',
      staff: true,
    });
  });
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
    sessionStorage.clear();
  });
  async function open(view: object = v) {
    const f = TestBed.createComponent(AdjustmentJournal);
    f.detectChanges();
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(root + '/workspace?historyPage=1').flush(view);
    await f.whenStable();
    TestBed.tick();
    return { f, c: f.componentInstance, http };
  }
  async function prepare(c: AdjustmentJournal, http: HttpTestingController) {
    const task = c.prepare();
    http.expectOne(root + '/preview').flush({ value: preview });
    await task;
    TestBed.tick();
  }
  function receipt(requestId: string) {
    return {
      id,
      requestId,
      requestHash: preview.requestHash,
      journalId: id,
      resultJournalId: id,
      action: 'UPDATE',
      oldRevision: 1,
      newRevision: 2,
      oldStatus: 'Draft',
      newStatus: 'Draft',
      actorId: actor,
      reason: 'Synthetic reason',
      evidenceReference: 'Synthetic evidence',
      createdAt: '2026-10-02T22:00:00Z',
    };
  }
  it('requires fresh preview and assent and dispatches exactly once', async () => {
    const { c, http } = await open();
    await c.execute();
    http.expectNone(root + '/actions');
    await prepare(c, http);
    await c.execute();
    http.expectNone(root + '/actions');
    c.reviewed.set(true);
    const first = c.execute();
    await c.execute();
    const request = http.expectOne(root + '/actions');
    expect(request.request.body.lines[0].debit).toBe('100.123456');
    expect(request.request.body.reviewed).toBe(true);
    request.flush({ value: receipt(request.request.body.requestId) });
    await first;
    TestBed.tick();
    http
      .expectOne(root + '/workspace?historyPage=1')
      .flush({ ...v, revision: 2, reviewBasis: 'd'.repeat(64) });
    TestBed.tick();
    expect(c.uncertain()).toBe(false);
    expect(c.preview()).toBeNull();
  });
  it('resets preview and assent when a field changes and undo never reverses retained state', async () => {
    const { c, http } = await open();
    await prepare(c, http);
    c.reviewed.set(true);
    c.model.update((m) => ({ ...m, reason: 'Changed rationale' }));
    TestBed.tick();
    expect(c.preview()).toBeNull();
    expect(c.reviewed()).toBe(false);
    expect(c.dirty()).toBe(true);
    expect(c.discardDraft()).toBe(true);
    TestBed.tick();
    expect(c.model().reason).toBe(v.reason);
    await c.execute();
    http.expectNone(root + '/actions');
  });
  it('refuses a field change even before the clearing effect has run', async () => {
    const { c, http } = await open();
    await prepare(c, http);
    c.reviewed.set(true);
    c.model.update((m) => ({ ...m, reason: 'Unreviewed immediate edit' }));
    await c.execute();
    http.expectNone(root + '/actions');
  });
  it('keeps unknown requests fenced until a matching receipt is explicitly acknowledged', async () => {
    const { c, http } = await open();
    await prepare(c, http);
    c.reviewed.set(true);
    const task = c.execute();
    const request = http.expectOne(root + '/actions');
    const requestId = request.request.body.requestId;
    request.flush({}, { status: 503, statusText: 'Unavailable' });
    await task;
    expect(c.uncertain()).toBe(true);
    expect(await c.confirmNavigation()).toBe(false);
    http.expectNone(root + '/actions');
    const stored = Object.keys(sessionStorage)
      .map((k) => sessionStorage.getItem(k))
      .join('');
    expect(stored).not.toContain('"reviewed":true');
    expect(stored).not.toContain(v.datasetDigest);
    const checking = c.reconcile();
    http
      .expectOne(root + '/receipts/' + requestId + '?requestHash=' + preview.requestHash)
      .flush({ found: true, receipt: receipt(requestId) });
    await checking;
    expect(c.uncertain()).toBe(true);
    c.acknowledge();
    TestBed.tick();
    http
      .expectOne(root + '/workspace?historyPage=1')
      .flush({ ...v, revision: 2, reviewBasis: 'd'.repeat(64) });
    TestBed.tick();
    expect(c.uncertain()).toBe(false);
  });
  it('refuses changed pending intent instead of treating absence as authority to create another request', async () => {
    const { c, http } = await open();
    await prepare(c, http);
    c.reviewed.set(true);
    const task = c.execute();
    const request = http.expectOne(root + '/actions');
    request.flush({}, { status: 503, statusText: 'Unavailable' });
    await task;
    const pending = c.pending()!;
    const checking = c.reconcile();
    http
      .expectOne(root + '/receipts/' + pending.requestId + '?requestHash=' + pending.requestHash)
      .flush({ found: false, receipt: null });
    await checking;
    const retry = c.prepare();
    http.expectOne(root + '/preview').flush({ value: { ...preview, requestHash: 'e'.repeat(64) } });
    await retry;
    expect(c.uncertain()).toBe(true);
    expect(c.preview()).toBeNull();
    expect(c.message()).toContain('intent');
    await c.execute();
    http.expectNone(root + '/actions');
  });
  it('explicit tab recovery restores fields without review and rejects extra draft authority', async () => {
    const { c, http } = await open();
    c.model.update((m) => ({ ...m, reason: 'Recovered fields' }));
    TestBed.tick();
    expect(c.saveDraft()).toBe(true);
    c.model.update((m) => ({ ...m, reason: 'Other local fields' }));
    c.restoreDraft();
    TestBed.tick();
    expect(c.model().reason).toBe('Recovered fields');
    expect(c.reviewed()).toBe(false);
    expect(c.preview()).toBeNull();
    expect(journalFields({ ...c.model(), reviewed: true })).toBeNull();
    http.expectNone(root + '/actions');
  });
  it('clears the editor, evidence, drafts and late preview after epoch loss', async () => {
    const { c, http } = await open();
    const task = c.prepare();
    const request = http.expectOne(root + '/preview');
    TestBed.inject(SessionService).clear();
    TestBed.tick();
    request.flush({ value: preview });
    await task;
    expect(c.journal.data()).toBeNull();
    expect(c.preview()).toBeNull();
    expect(c.model().lines).toEqual([]);
    expect(c.receipt()).toBeNull();
  });
  it('rejects mixed totals, unsupported financial precision and posted edit flags', () => {
    expect(decodeJournalReview(v).lines[0].debit).toBe('100.123456');
    expect(() => decodeJournalReview({ ...v, totalDebit: '100.123455' })).toThrow();
    expect(() => decodeJournalReview({ ...v, status: 'Posted', canEdit: true })).toThrow();
    expect(() => decodeJournalPreview({ ...preview, totalDebit: '1e2' })).toThrow();
    expect(() =>
      decodeJournalPreview({ ...preview, canProceed: true, errors: ['Unbalanced'] }),
    ).toThrow();
  });
  it('never projects a wrong journal or retained snapshot into this route', async () => {
    const { c } = await open({ ...v, journalId: actor });
    expect(c.journal.data()).toBeNull();
    expect(c.journal.error()).toContain('Unsupported response');
  });
  it('reconciles source reflection on posted journals and validates line bridge evidence', async () => {
    const postedView = {
      ...v,
      status: 'Posted',
      canEdit: false,
      canSubmit: false,
      canPost: false,
      canReturn: false,
      canReverse: true,
      canReconcileReflection: true,
    };
    const { c, http } = await open(postedView);
    expect(c.reflectionState()).toBe('NOT_REFLECTED');

    c.reflectionState.set('REFLECTED');
    c.reflectionEvidence.set('   ');
    await c.reconcileReflection();
    expect(c.message()).toBe('Line-level bridge evidence is required for reflected treatments.');
    expect(c.failed()).toBe(true);
    http.expectNone(root + '/source-reflection');

    c.reflectionEvidence.set('Bridged to 1000 in December close');
    const task = c.reconcileReflection();
    const req = http.expectOne(root + '/source-reflection');
    expect(req.request.body.state).toBe('REFLECTED');
    expect(req.request.body.evidence).toBe('Bridged to 1000 in December close');
    expect(req.request.body.reviewBasis).toBe(v.reviewBasis);
    req.flush({ ok: true });
    await task;
    TestBed.tick();

    expect(c.message()).toBe('Source reflection reconciliation recorded.');
    http.expectOne(root + '/workspace?historyPage=1').flush({
      ...postedView,
      sourceReflection: {
        state: 'REFLECTED',
        evidence: 'Bridged to 1000 in December close',
        reviewedByUserId: actor,
        reviewedAt: '2026-03-01T12:00:00Z',
        isExactRevision: true,
      },
    });
    TestBed.tick();
    expect(c.journal.data()?.sourceReflection?.state).toBe('REFLECTED');
  });
  it('decodes management disposition and source reflection in review contracts', () => {
    const decoded = decodeJournalReview({
      ...v,
      status: 'Posted',
      canEdit: false,
      canSubmit: false,
      canPost: false,
      canReturn: false,
      canReverse: true,
      canReconcileReflection: true,
      managementDecision: {
        id,
        journalRevision: 1,
        decision: 'ACCEPTED',
        evidenceMode: 'SIGNED_IN',
        evidenceReference: 'Portal accepted',
        decidedByUserId: actor,
        decidedAt: '2026-02-01T10:00:00Z',
      },
      sourceReflection: {
        state: 'REFLECTED',
        evidence: 'Bridged 1000',
        reviewedByUserId: actor,
        reviewedAt: '2026-02-02T11:00:00Z',
        isExactRevision: true,
      },
    });
    expect(decoded.managementDecision?.decision).toBe('ACCEPTED');
    expect(decoded.sourceReflection?.state).toBe('REFLECTED');
    expect(() =>
      decodeJournalReview({ ...v, status: 'Draft', canReconcileReflection: true }),
    ).toThrow();
  });
});
