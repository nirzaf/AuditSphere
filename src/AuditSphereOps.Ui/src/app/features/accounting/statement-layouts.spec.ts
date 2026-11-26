import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialogModule } from '@angular/material/dialog';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { vi } from 'vitest';
import { SessionService } from '../../core/session';
import { TabDrafts } from '../../core/tab-drafts';
import { StatementLayoutSetup, decodeStatementLayoutCatalogue, decodeStatementLayoutDetail,
  statementLayoutIntent } from './statement-layouts';

const clientId = '11111111-1111-4111-8111-111111111111';
const actorId = '22222222-2222-4222-8222-222222222222';
const otherActor = '33333333-3333-4333-8333-333333333333';
const root = `/api/ui/accounting/clients/${clientId}/statement-layouts`;
const layoutHash = 'a'.repeat(64);
const validLine = { lineCode: 'ASSETS', label: 'Assets', kind: 'HEADING', section: 'FINANCIAL_POSITION', order: 1,
  displaySign: 'SIGNED', taxonomyNodeCode: null, childLineCodes: null, referencedLineCodes: null, operations: null,
  zeroDenominatorYieldsZero: false, denominatorLineCode: null, isSubtotal: false };
const catalogue = (items: unknown[] = [], canCreate = true, canPublish = true) => ({ clientId, page: 0, pageSize: 50,
  totalCount: items.length, hasMore: false, canCreate, canPublish, items });
const item = (status = 'VALIDATED', authorId = otherActor) => ({ layoutVersionId: '44444444-4444-4444-8444-444444444444',
  frameworkPolicy: 'IFRS', name: 'Primary layout', versionNumber: 1, status, layoutHash, lineCount: 1, authorId,
  publisherId: null, createdAt: '2026-11-26T00:00:00Z', publishedAt: null, canEdit: false, canPublish: true });
const detail = (status = 'VALIDATED', canEdit = false, canPublish = true) => ({ ...item(status), clientId,
  supersedesLayoutId: null, publishReason: null, canEdit, canPublish, lines: [validLine], errors: [] });
const resultReceipt = (body: Record<string, any>, action: 'SAVE' | 'PUBLISH' = 'SAVE') => ({
  id: '55555555-5555-4555-8555-555555555555', firmId: clientId, clientId, actorId, actorEpoch: '1',
  requestId: body['requestId'], action, requestHash: 'b'.repeat(64),
  layoutVersionId: body['layoutVersionId'] ?? '44444444-4444-4444-8444-444444444444',
  expectedLayoutHash: body['expectedLayoutHash'] ?? null, resultLayoutHash: 'c'.repeat(64), resultStatus: action === 'PUBLISH' ? 'PUBLISHED' : 'VALIDATED',
  resultVersionNumber: 1, inputJson: JSON.stringify(body), resultJson: JSON.stringify({ status: action }), createdAt: '2026-11-26T00:00:00Z'
});

describe('Client statement layout producer', () => {
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  beforeEach(() => {
    sessionStorage.clear(); params = new BehaviorSubject(convertToParamMap({ clientId }));
    TestBed.configureTestingModule({ imports: [StatementLayoutSetup, MatDialogModule], providers: [provideHttpClient(),
      provideHttpClientTesting(), provideRouter([]), { provide: ActivatedRoute, useValue: { paramMap: params } }] });
    TestBed.inject(SessionService).current.set({ userId: actorId, firmId: clientId, generation: '1', staff: true });
  });
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true }); vi.restoreAllMocks();
    TestBed.resetTestingModule(); sessionStorage.clear();
  });
  async function open(items: unknown[] = [], canCreate = true, canPublish = true) {
    const fixture = TestBed.createComponent(StatementLayoutSetup); fixture.detectChanges(); TestBed.tick();
    const http = TestBed.inject(HttpTestingController); http.expectOne(root).flush(catalogue(items, canCreate, canPublish));
    await fixture.whenStable(); TestBed.tick(); return { c: fixture.componentInstance, fixture, http };
  }
  function fillNew(c: StatementLayoutSetup) {
    c.setModel({ frameworkPolicy: 'IFRS 2026', name: 'Primary layout', lines: [{ ...c.model().lines[0], lineCode: 'ASSETS', label: 'Assets' }] });
  }

  it('decodes bounded producer context and refuses malformed retained intents', () => {
    expect(decodeStatementLayoutCatalogue(catalogue(), '').items).toEqual([]);
    expect(() => decodeStatementLayoutCatalogue({ ...catalogue(), items: Array(52).fill(item()) }, '')).toThrow();
    expect(decodeStatementLayoutDetail(detail(), '').layoutHash).toBe(layoutHash);
    expect(statementLayoutIntent({ requestId: clientId, action: 'SAVE', layoutVersionId: null, expectedLayoutHash: null,
      draft: { clientId, frameworkPolicy: 'IFRS', name: 'Primary', supersedesLayoutId: null, lines: [validLine] }, reason: null, reviewed: false })).not.toBeNull();
    expect(statementLayoutIntent({ requestId: clientId, action: 'SAVE', layoutVersionId: null, expectedLayoutHash: null,
      draft: { clientId, frameworkPolicy: 'IFRS', name: 'Primary', supersedesLayoutId: null, lines: [validLine] }, reason: null, reviewed: true })).toBeNull();
  });

  it('creates from an empty catalogue through the reviewed draft route and requires receipt acknowledgement', async () => {
    const { c, http, fixture } = await open(); c.newDraft(); fillNew(c); c.reviewed.set(true);
    const task = c.save(), request = http.expectOne(root + '/drafts');
    expect(request.request.body.draft.clientId).toBe(clientId);
    expect(request.request.body.draft.supersedesLayoutId).toBeNull();
    expect(request.request.body.reviewed).toBe(true);
    const saved = resultReceipt(request.request.body); request.flush({ value: saved }); await task;
    expect(c.receipt()?.requestId).toBe(saved.requestId); expect(c.uncertain()).toBe(true);
    expect(await c.confirmNavigation()).toBe(false);
    c.acknowledge(); expect(c.pending()).toBeNull(); expect(c.uncertain()).toBe(false);
    http.expectOne(root).flush(catalogue([item('VALIDATED', actorId)]));
    TestBed.tick();
    http.expectOne(`${root}/${saved.layoutVersionId}`).flush({ ...detail('VALIDATED', true, false),
      layoutVersionId: saved.layoutVersionId, authorId: actorId, layoutHash: saved.resultLayoutHash });
    await fixture.whenStable(); TestBed.tick();
    expect(c.layoutVersionId()).toBe(saved.layoutVersionId);
    expect(c.dirty()).toBe(false);
    expect(await c.confirmNavigation()).toBe(true);
    expect(TestBed.inject(TabDrafts).readPendingSubmission({ entity: `statement-layout/${clientId}/command`, baseRevision: '0'.repeat(64) }, statementLayoutIntent).state).toBe('absent');
  });

  it('recovers a lost response by receipt and retries only the fixed original intent after explicit assent', async () => {
    const { c, http } = await open(); c.newDraft(); fillNew(c); c.reviewed.set(true);
    const first = c.save(), body = http.expectOne(root + '/drafts'); const original = structuredClone(body.request.body);
    body.flush({}, { status: 503, statusText: 'Unavailable' }); await first;
    expect(c.uncertain()).toBe(true); expect(c.pending()?.requestId).toBe(original.requestId);
    const lookup = c.reconcile(); http.expectOne(`${root}/receipts/${original.requestId}`).flush({ found: false, receipt: null }); await lookup;
    expect(c.absent()).toBe(true);
    c.model.set({ ...c.model(), name: 'Changed selection' }); c.reviewed.set(true);
    const retry = c.retryOriginal(), retried = http.expectOne(root + '/drafts');
    expect(retried.request.body).toEqual(original); retried.flush({ value: resultReceipt(original) }); await retry;
    expect(c.receipt()?.requestId).toBe(original.requestId); c.acknowledge();
    http.expectOne(root).flush(catalogue([item('VALIDATED', actorId)]));
  });

  it('lets an accounting reviewer read a validated layout and publish only as a different author', async () => {
    const { c, http, fixture } = await open([item()], false, true); c.open(item().layoutVersionId); TestBed.tick();
    http.expectOne(`${root}/${item().layoutVersionId}`).flush(detail()); await fixture.whenStable(); TestBed.tick();
    expect(c.selected()?.canPublish).toBe(true); expect(c.editing()).toBe(false);
    c.publishReason.set('Independent layout review'); c.reviewed.set(true);
    const task = c.publish(), request = http.expectOne(`${root}/${item().layoutVersionId}/publish`);
    expect(request.request.body.expectedLayoutHash).toBe(layoutHash); expect(request.request.body.reviewed).toBe(true);
    const saved = resultReceipt(request.request.body, 'PUBLISH'); request.flush({ value: saved }); await task;
    expect(c.receipt()?.action).toBe('PUBLISH');
  });
});
