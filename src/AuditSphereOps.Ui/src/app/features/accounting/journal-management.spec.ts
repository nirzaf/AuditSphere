import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { JournalManagement } from './journal-management';
import { decodeJournalManagement, managementFields } from './journal-management-contracts';
const id = '11111111-1111-4111-8111-111111111111',
  actor = '22222222-2222-4222-8222-222222222222';
const fields = {
  decision: 'PARTIAL' as const,
  reason: 'Synthetic partial rationale',
  evidenceReference: 'Synthetic accepted and rejected line bridge',
};
const view = {
  journalId: id,
  clientId: id,
  engagementId: id,
  journalNumber: 'AJ-SYN',
  revision: 1,
  status: 'Draft',
  purpose: 'REPORTING_ADJUSTMENT',
  datasetId: id,
  datasetRevision: 1,
  datasetDigest: 'd'.repeat(64),
  periodStart: '2026-01-01',
  periodEnd: '2026-12-31',
  currency: 'QAR',
  reason: 'Synthetic treatment',
  evidenceReference: 'Synthetic source',
  lines: [
    { accountCode: '1000', debit: '100.123456', credit: '0' },
    { accountCode: '3000', debit: '0', credit: '100.123456' },
  ],
  totalDebit: '100.123456',
  totalCredit: '100.123456',
  reviewBasis: 'a'.repeat(64),
  evidenceMode: 'OFFLINE',
  canDecide: true,
  blocker: null,
  management: null,
};
function receipt(requestId: string, client = false) {
  return {
    action: {
      id,
      requestId,
      requestHash: 'c'.repeat(64),
      journalId: id,
      resultJournalId: id,
      action: 'MANAGEMENT',
      oldRevision: 1,
      newRevision: 1,
      oldStatus: 'Draft',
      newStatus: 'Draft',
      actorId: actor,
      reason: fields.reason,
      evidenceReference: fields.evidenceReference,
      createdAt: '2026-10-02T23:00:00Z',
    },
    management: {
      id,
      journalRevision: 1,
      decision: 'PARTIAL',
      evidenceMode: client ? 'SIGNED_IN' : 'OFFLINE',
      evidenceReference: fields.evidenceReference,
      decidedByUserId: client ? actor : null,
      decidedAt: '2026-10-02T23:00:00Z',
    },
  };
}
describe('Separate native management response', () => {
  afterEach(() => {
    try {
      TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    } finally {
      TestBed.resetTestingModule();
      sessionStorage.clear();
    }
  });
  async function open(client = false) {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      imports: [JournalManagement],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            paramMap: new BehaviorSubject(convertToParamMap({ id })),
            data: new BehaviorSubject({ client }),
          },
        },
      ],
    });
    TestBed.inject(SessionService).current.set({
      userId: actor,
      firmId: id,
      generation: '1',
      staff: !client,
    });
    const f = TestBed.createComponent(JournalManagement);
    f.detectChanges();
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    const root = client
      ? '/api/ui/portal/accounting/journals/' + id
      : '/api/ui/accounting/journals/' + id + '/management';
    http.expectOne(root).flush({ ...view, evidenceMode: client ? 'SIGNED_IN' : 'OFFLINE' });
    await f.whenStable();
    TestBed.tick();
    const c = f.componentInstance;
    c.model.set({ ...fields });
    TestBed.tick();
    return { f, c, http, root };
  }
  async function prepare(
    c: JournalManagement,
    http: HttpTestingController,
    root: string,
    client = false,
  ) {
    const t = c.prepare();
    http.expectOne(root + '/preview').flush({
      value: {
        reviewBasis: view.reviewBasis,
        requestHash: 'c'.repeat(64),
        decision: fields.decision,
        evidenceMode: client ? 'SIGNED_IN' : 'OFFLINE',
        reason: fields.reason,
        evidenceReference: fields.evidenceReference,
        canProceed: true,
        blocker: null,
      },
    });
    await t;
    TestBed.tick();
  }
  it('requires preview and fresh assent and sends exactly once without selecting an evidence mode', async () => {
    const { c, http, root } = await open();
    await c.execute();
    http.expectNone(root);
    await prepare(c, http, root);
    await c.execute();
    http.expectNone(root);
    c.reviewed.set(true);
    const t = c.execute();
    await c.execute();
    const r = http.expectOne(root);
    expect(r.request.body.evidenceMode).toBeUndefined();
    expect(r.request.body.reviewed).toBe(true);
    r.flush({ value: receipt(r.request.body.requestId) });
    await t;
    TestBed.tick();
    http.expectOne(root).flush({
      ...view,
      canDecide: false,
      blocker: 'Retained',
      management: receipt('x').management,
    });
    expect(c.receipt()?.management.decision).toBe('PARTIAL');
    expect(c.uncertain()).toBe(false);
  });
  it('late preview cannot restore review after its fields change', async () => {
    const { c, http, root } = await open();
    const t = c.prepare(),
      r = http.expectOne(root + '/preview');
    c.model.update((v) => ({ ...v, reason: 'New unreviewed fields' }));
    r.flush({
      value: {
        reviewBasis: view.reviewBasis,
        requestHash: 'c'.repeat(64),
        decision: fields.decision,
        evidenceMode: 'OFFLINE',
        reason: fields.reason,
        evidenceReference: fields.evidenceReference,
        canProceed: true,
        blocker: null,
      },
    });
    await t;
    expect(c.preview()).toBeNull();
    expect(c.reviewed()).toBe(false);
  });
  it('a receipt for another journal revision remains unconfirmed', async () => {
    const { c, http, root } = await open();
    await prepare(c, http, root);
    c.reviewed.set(true);
    const task = c.execute(),
      request = http.expectOne(root);
    const wrong = receipt(request.request.body.requestId);
    wrong.action.oldRevision = 2;
    wrong.action.newRevision = 2;
    wrong.management.journalRevision = 2;
    request.flush({ value: wrong });
    await task;
    expect(c.receipt()).toBeNull();
    expect(c.uncertain()).toBe(true);
    expect(c.pending()).not.toBeNull();
    expect(await c.confirmNavigation()).toBe(false);
    http.expectNone(root);
  });
  it('immediate field change after assent blocks stale dispatch', async () => {
    const { c, http, root } = await open();
    await prepare(c, http, root);
    c.reviewed.set(true);
    c.model.update((v) => ({ ...v, reason: 'Changed' }));
    await c.execute();
    http.expectNone(root);
  });
  it('unknown response fences navigation until an exact retained receipt is acknowledged', async () => {
    const { c, http, root } = await open();
    await prepare(c, http, root);
    c.reviewed.set(true);
    const t = c.execute(),
      request = http.expectOne(root);
    const requestId = request.request.body.requestId;
    request.flush({}, { status: 503, statusText: 'Lost response' });
    await t;
    expect(c.uncertain()).toBe(true);
    expect(await c.confirmNavigation()).toBe(false);
    const check = c.reconcile();
    http
      .expectOne(root + '/receipts/' + requestId + '?requestHash=' + 'c'.repeat(64))
      .flush({ found: true, receipt: receipt(requestId) });
    await check;
    expect(c.uncertain()).toBe(true);
    c.acknowledge();
    TestBed.tick();
    http.expectOne(root).flush({
      ...view,
      canDecide: false,
      blocker: 'Retained',
      management: receipt(requestId).management,
    });
    expect(c.pending()).toBeNull();
    expect(c.uncertain()).toBe(false);
  });
  it('client responses are signed in and their fields are not stored as staff drafts', async () => {
    const { c, http, root } = await open(true);
    expect(c.saveDraft()).toBe(false);
    expect(sessionStorage.length).toBe(0);
    await prepare(c, http, root, true);
    c.reviewed.set(true);
    const t = c.execute(),
      r = http.expectOne(root);
    r.flush({ value: receipt(r.request.body.requestId, true) });
    await t;
    TestBed.tick();
    http.expectOne(root).flush({
      ...view,
      evidenceMode: 'SIGNED_IN',
      canDecide: false,
      blocker: 'Retained',
      management: receipt('x', true).management,
    });
    expect(c.receipt()?.management.decidedByUserId).toBe(actor);
    expect(sessionStorage.length).toBe(0);
  });
  it('staff draft recovery uses bounded fields without retaining assent', async () => {
    const { c, http, root } = await open();
    expect(c.saveDraft()).toBe(true);
    c.model.set({ decision: 'ACCEPTED', reason: '', evidenceReference: '' });
    c.reviewed.set(true);
    c.restoreDraft();
    expect(c.model()).toEqual(fields);
    expect(c.reviewed()).toBe(false);
    await c.execute();
    http.expectNone(root);
    expect(managementFields({ ...fields, reviewed: true })).toBeNull();
  });
  it('late preview after session loss cannot restore protected review', async () => {
    const { c, http, root } = await open();
    const t = c.prepare(),
      r = http.expectOne(root + '/preview');
    TestBed.inject(SessionService).clear();
    TestBed.tick();
    r.flush({
      value: {
        reviewBasis: view.reviewBasis,
        requestHash: 'c'.repeat(64),
        decision: 'PARTIAL',
        evidenceMode: 'OFFLINE',
        reason: fields.reason,
        evidenceReference: fields.evidenceReference,
        canProceed: true,
        blocker: null,
      },
    });
    await t;
    expect(c.preview()).toBeNull();
    expect(c.reviewed()).toBe(false);
    expect(c.model().reason).toBe('');
  });
  it('rejects mixed precision and an impossible signed-in identity claim', async () => {
    await open();
    expect(() => decodeJournalManagement({ ...view, totalDebit: '100.123455' })).toThrow();
    expect(() =>
      decodeJournalManagement({
        ...view,
        canDecide: false,
        management: {
          ...receipt('x').management,
          evidenceMode: 'SIGNED_IN',
          decidedByUserId: null,
        },
      }),
    ).toThrow();
    expect(managementFields({ ...fields, reason: 'a'.repeat(4001) })).toBeNull();
  });
});
