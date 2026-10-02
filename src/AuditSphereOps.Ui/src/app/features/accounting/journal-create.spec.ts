import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { JournalCreate } from './journal-create';
import { decodeJournalCreation, journalCreationFields } from './journal-creation-contracts';
const id = '11111111-1111-4111-8111-111111111111',
  actor = '22222222-2222-4222-8222-222222222222',
  root = '/api/ui/datasets/' + id + '/journal-drafts';
const context = {
  datasetId: id,
  clientId: id,
  engagementId: id,
  datasetRevision: 1,
  datasetDigest: 'd'.repeat(64),
  periodId: id,
  periodCode: 'FY26',
  periodStart: '2026-01-01',
  periodEnd: '2026-12-31',
  bookId: id,
  bookCode: 'AUDIT',
  currency: 'QAR',
  basis: 'IFRS',
  entity: 'Synthetic',
  reviewBasis: 'a'.repeat(64),
  canCreate: true,
  blocker: null,
};
const fields = {
  journalNumber: 'AJ-NEW',
  purpose: 'REPORTING_ADJUSTMENT' as const,
  origin: 'AUDIT_PROPOSED' as const,
  reason: 'Synthetic reason',
  evidenceReference: 'Synthetic evidence',
  supersedesId: '',
  supersedesRevision: '',
  lines: [
    { accountCode: '1000', debit: '200.123456', credit: '0' },
    { accountCode: '3000', debit: '0', credit: '200.123456' },
  ],
};
const preview = {
  action: 'CREATE',
  reviewBasis: context.reviewBasis,
  requestHash: 'c'.repeat(64),
  canProceed: true,
  blocker: null,
  errors: [],
  totalDebit: '200.123456',
  totalCredit: '200.123456',
  lines: fields.lines,
};
function receipt(requestId: string) {
  return {
    id,
    requestId,
    requestHash: preview.requestHash,
    journalId: id,
    resultJournalId: id,
    action: 'CREATE',
    oldRevision: 0,
    newRevision: 1,
    oldStatus: 'NOT_CREATED',
    newStatus: 'Draft',
    actorId: actor,
    reason: fields.reason,
    evidenceReference: fields.evidenceReference,
    createdAt: '2026-10-02T23:00:00Z',
  };
}
describe('Reviewed native journal creation', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      imports: [JournalCreate],
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
  async function open() {
    const f = TestBed.createComponent(JournalCreate);
    f.detectChanges();
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(root).flush(context);
    await f.whenStable();
    TestBed.tick();
    const c = f.componentInstance;
    c.model.set(structuredClone(fields));
    TestBed.tick();
    return { f, c, http };
  }
  async function prepare(c: JournalCreate, http: HttpTestingController) {
    const task = c.prepare();
    http.expectOne(root + '/preview').flush({ value: preview });
    await task;
    TestBed.tick();
  }
  it('requires fresh complete preview and assent and retains exact decimal strings', async () => {
    const { c, http } = await open();
    await c.execute();
    http.expectNone(root);
    await prepare(c, http);
    await c.execute();
    http.expectNone(root);
    c.reviewed.set(true);
    const task = c.execute();
    await c.execute();
    const r = http.expectOne(root);
    expect(r.request.body.lines[0].debit).toBe('200.123456');
    expect(r.request.body.reviewed).toBe(true);
    r.flush({ value: receipt(r.request.body.requestId) });
    await task;
    expect(c.receipt()?.action).toBe('CREATE');
    expect(c.uncertain()).toBe(false);
    expect(c.dirty()).toBe(false);
    await c.prepare();
    http.expectNone(root + '/preview');
  });
  it('blocks an immediate field edit even before clearing effects run', async () => {
    const { c, http } = await open();
    await prepare(c, http);
    c.reviewed.set(true);
    c.model.update((m) => ({ ...m, journalNumber: 'UNREVIEWED' }));
    await c.execute();
    http.expectNone(root);
    TestBed.tick();
    expect(c.preview()).toBeNull();
    expect(c.reviewed()).toBe(false);
  });
  it('requires explicit acknowledgment of a recovered creation receipt', async () => {
    const { c, http } = await open();
    await prepare(c, http);
    c.reviewed.set(true);
    const task = c.execute();
    const r = http.expectOne(root);
    const requestId = r.request.body.requestId;
    r.flush({}, { status: 503, statusText: 'Unavailable' });
    await task;
    expect(c.uncertain()).toBe(true);
    expect(await c.confirmNavigation()).toBe(false);
    const check = c.reconcile();
    http
      .expectOne(root + '/receipts/' + requestId + '?requestHash=' + preview.requestHash)
      .flush({ found: true, receipt: receipt(requestId) });
    await check;
    expect(c.uncertain()).toBe(true);
    c.acknowledge();
    expect(c.uncertain()).toBe(false);
    expect(c.dirty()).toBe(false);
    expect(Object.values(sessionStorage).join('')).not.toContain('reviewed');
  });
  it('absence never permits a different intent to reuse an unconfirmed request', async () => {
    const { c, http } = await open();
    await prepare(c, http);
    c.reviewed.set(true);
    const task = c.execute();
    http.expectOne(root).flush({}, { status: 503, statusText: 'Unavailable' });
    await task;
    const p = c.pending()!;
    const check = c.reconcile();
    http
      .expectOne(root + '/receipts/' + p.requestId + '?requestHash=' + p.requestHash)
      .flush({ found: false, receipt: null });
    await check;
    c.model.update((m) => ({ ...m, journalNumber: 'Another number' }));
    TestBed.tick();
    const retry = c.prepare();
    const r = http.expectOne(root + '/preview');
    expect(r.request.body.requestId).toBe(p.requestId);
    r.flush({ value: { ...preview, requestHash: 'e'.repeat(64) } });
    await retry;
    expect(c.preview()).toBeNull();
    expect(c.uncertain()).toBe(true);
    await c.execute();
    http.expectNone(root);
  });
  it('restores same-basis editable fields without assent or retained balances', async () => {
    const { c } = await open();
    expect(c.saveDraft()).toBe(true);
    c.model.update((m) => ({ ...m, journalNumber: 'Other local number' }));
    c.restoreDraft();
    TestBed.tick();
    expect(c.model().journalNumber).toBe(fields.journalNumber);
    expect(c.reviewed()).toBe(false);
    expect(journalCreationFields({ ...fields, reviewed: true })).toBeNull();
    expect(Object.values(sessionStorage).join('')).not.toContain(context.datasetDigest);
  });
  it('requires prior identity and positive revision together', async () => {
    const { c, http } = await open();
    c.model.update((m) => ({ ...m, supersedesId: id, supersedesRevision: '' }));
    TestBed.tick();
    await c.prepare();
    http.expectNone(root + '/preview');
    expect(c.message()).toContain('revision together');
  });
  it('clears protected fields and late previews after session invalidation', async () => {
    const { c, http } = await open();
    const task = c.prepare();
    const r = http.expectOne(root + '/preview');
    TestBed.inject(SessionService).clear();
    TestBed.tick();
    r.flush({ value: preview });
    await task;
    expect(c.context.data()).toBeNull();
    expect(c.preview()).toBeNull();
    expect(c.model().journalNumber).toBe('');
    expect(c.receipt()).toBeNull();
  });
  it('rejects wrong source and unsafe configured contexts', async () => {
    expect(() =>
      decodeJournalCreation({ ...context, canCreate: true, blocker: 'Closed' }),
    ).toThrow();
    const { c, http } = await open();
    c.context.reload();
    http.expectOne(root).flush({ ...context, datasetId: actor });
    TestBed.tick();
    expect(c.context.data()).toBeNull();
  });
});
