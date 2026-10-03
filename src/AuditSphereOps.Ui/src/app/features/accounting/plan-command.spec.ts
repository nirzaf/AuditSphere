import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { vi } from 'vitest';
import { SessionService } from '../../core/session';
import { TabDrafts } from '../../core/tab-drafts';
import { PlanCommand } from './plan-command';
import { decodePlanCommandPreview, decodePlanCommandReceipt, decodePlanCommandLookup, planFields } from './plan-command-contracts';

const id = '11111111-1111-4111-8111-111111111111', actor = '22222222-2222-4222-8222-222222222222',
  root = `/api/ui/datasets/${id}/adjustment-plans`;
const journal = { journalId: id, journalNumber: 'AJ-SYN', revision: 1, purpose: 'REPORTING_ADJUSTMENT',
  layer: 'REPORTING', reflectionState: 'NOT_REFLECTED', canInclude: true, blocker: null };
const context = { source: { datasetId: id, clientId: id, engagementId: id, datasetRevision: 1,
  datasetDigest: 'd'.repeat(64), periodId: id, periodCode: 'FY26', periodStart: '2026-01-01',
  periodEnd: '2026-12-31', bookId: id, bookCode: 'STAT', currency: 'QAR', basis: 'IFRS', entity: 'Synthetic',
  reviewBasis: 'a'.repeat(64), canCreate: true, blocker: null }, reviewBasis: 'b'.repeat(64),
  journals: [journal], page: 0, hasMore: false };
const fields = { reason: 'Exact synthetic rationale', evidenceReference: 'Synthetic evidence', journals: [{ journalId: id, revision: 1 }] };
const preview = { action: 'CREATE', reviewBasis: context.reviewBasis, requestHash: 'c'.repeat(64), canProceed: true,
  blocker: null, journals: [journal], debits: null, credits: null, appliedCount: null, resultHash: null };
const receipt = (requestId: string) => ({ id, requestId, requestHash: preview.requestHash, planId: id, datasetId: id,
  action: 'CREATE', actorId: actor, reason: fields.reason, evidenceReference: fields.evidenceReference,
  status: 'Draft', resultHash: null, debits: null, credits: null, appliedCount: null, createdAt: '2026-10-03T00:00:00Z' });

describe('Reviewed native plan commands', () => {
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  beforeEach(() => {
    sessionStorage.clear(); params = new BehaviorSubject(convertToParamMap({ id }));
    TestBed.configureTestingModule({ imports: [PlanCommand], providers: [provideHttpClient(), provideHttpClientTesting(),
      provideRouter([]), { provide: ActivatedRoute, useValue: { paramMap: params, data: new BehaviorSubject({ action: 'CREATE' }) } }] });
    TestBed.inject(SessionService).current.set({ userId: actor, firmId: id, generation: '1', staff: true });
  });
  afterEach(() => { TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    vi.restoreAllMocks(); TestBed.resetTestingModule(); sessionStorage.clear(); });
  async function open() {
    const f = TestBed.createComponent(PlanCommand); f.detectChanges(); TestBed.tick();
    const http = TestBed.inject(HttpTestingController); http.expectOne(root + '?page=0').flush(context);
    await f.whenStable(); TestBed.tick(); const c = f.componentInstance; c.model.set(structuredClone(fields)); TestBed.tick();
    return { c, f, http };
  }
  async function prepare(c: PlanCommand, http: HttpTestingController) {
    const task = c.prepare(); http.expectOne(root + '/preview').flush({ value: preview }); await task; TestBed.tick();
  }
  it('rejects numeric amounts, partial eligible reflection, inconsistent lookup and draft assent', () => {
    expect(decodePlanCommandPreview(preview).canProceed).toBe(true);
    expect(() => decodePlanCommandPreview({ ...preview, journals: [{ ...journal, reflectionState: 'PARTIALLY_REFLECTED' }] })).toThrow();
    expect(() => decodePlanCommandPreview({ ...preview, action: 'FINALIZE', journals: [], debits: 200.246912, credits: '200.246912', appliedCount: 1, resultHash: 'e'.repeat(64) })).toThrow();
    expect(() => decodePlanCommandReceipt({ ...receipt(id), status: 'Finalized' })).toThrow();
    expect(() => decodePlanCommandLookup({ found: true, receipt: null })).toThrow();
    expect(planFields({ ...fields, reviewed: true })).toBeNull();
    expect(planFields({ ...fields, journals: [fields.journals[0], fields.journals[0]] })).toBeNull();
  });
  it('requires preview and fresh assent, suppresses double submit and retains exact immutable IDs', async () => {
    const { c, http } = await open(); await c.execute(); http.expectNone(root);
    await prepare(c, http); await c.execute(); http.expectNone(root);
    c.reviewed.set(true); const task = c.execute(); await c.execute(); const request = http.expectOne(root);
    expect(request.request.body.journals).toEqual(fields.journals); expect(request.request.body.reviewed).toBe(true);
    request.flush({ value: receipt(request.request.body.requestId) }); await task;
    expect(c.receipt()?.status).toBe('Draft'); expect(c.uncertain()).toBe(false); expect(c.dirty()).toBe(false);
    await c.prepare(); http.expectNone(root + '/preview');
  });
  it('refuses an immediate unreviewed change and unavailable recovery storage', async () => {
    const { c, http } = await open(); await prepare(c, http); c.reviewed.set(true);
    c.model.update(m => ({ ...m, journals: [] })); await c.execute(); http.expectNone(root); TestBed.tick();
    expect(c.preview()).toBeNull(); expect(c.reviewed()).toBe(false);
    c.model.set(structuredClone(fields)); TestBed.tick(); await prepare(c, http); c.reviewed.set(true);
    vi.spyOn(TestBed.inject(TabDrafts), 'save').mockReturnValue(false); await c.execute(); http.expectNone(root);
    expect(c.message()).toContain('No command was sent');
  });
  it('keeps unknown commands fenced until an actor-owned persisted receipt is acknowledged', async () => {
    const { c, http } = await open(); await prepare(c, http); c.reviewed.set(true);
    const task = c.execute(), request = http.expectOne(root), requestId = request.request.body.requestId;
    request.flush({}, { status: 503, statusText: 'Unavailable' }); await task;
    expect(c.uncertain()).toBe(true); expect(await c.confirmNavigation()).toBe(false);
    const wrong = c.reconcile(); http.expectOne(`${root}/receipts/${requestId}?requestHash=${preview.requestHash}`)
      .flush({ found: true, receipt: { ...receipt(requestId), actorId: id } }); await wrong;
    expect(c.receipt()).toBeNull(); expect(c.uncertain()).toBe(true);
    const check = c.reconcile(); http.expectOne(`${root}/receipts/${requestId}?requestHash=${preview.requestHash}`)
      .flush({ found: true, receipt: receipt(requestId) }); await check;
    expect(c.uncertain()).toBe(true); c.acknowledge(); expect(c.uncertain()).toBe(false); expect(c.dirty()).toBe(false);
  });
  it('receipt absence permits only the identical request after a new preview', async () => {
    const { c, http } = await open(); await prepare(c, http); c.reviewed.set(true);
    const task = c.execute(); http.expectOne(root).flush({}, { status: 503, statusText: 'Unavailable' }); await task;
    const pending = c.pending()!, check = c.reconcile();
    http.expectOne(`${root}/receipts/${pending.requestId}?requestHash=${pending.requestHash}`).flush({ found: false, receipt: null }); await check;
    c.model.update(m => ({ ...m, reason: 'Different intent' })); TestBed.tick(); const retry = c.prepare();
    const request = http.expectOne(root + '/preview'); expect(request.request.body.requestId).toBe(pending.requestId);
    request.flush({ value: { ...preview, requestHash: 'f'.repeat(64) } }); await retry;
    expect(c.preview()).toBeNull(); expect(c.uncertain()).toBe(true); await c.execute(); http.expectNone(root);
  });
  it('restores fields explicitly with no review assent and clears protected context on route or epoch loss', async () => {
    const { c, f, http } = await open(); expect(c.saveDraft()).toBe(true);
    c.model.update(m => ({ ...m, reason: 'Local unsaved change' })); c.restoreDraft(); TestBed.tick();
    expect(JSON.parse(JSON.stringify(c.model()))).toEqual(fields); expect(c.reviewed()).toBe(false); expect(c.preview()).toBeNull();
    expect(Object.values(sessionStorage).join('')).not.toContain('reviewed');
    params.next(convertToParamMap({ id: actor })); TestBed.tick();
    expect(c.model().reason).toBe(''); expect(c.context()).toBeNull();
    const r = http.expectOne(`/api/ui/datasets/${actor}/adjustment-plans?page=0`);
    TestBed.inject(SessionService).clear(); TestBed.tick(); expect(r.cancelled).toBe(true);
    expect(c.context()).toBeNull(); expect(c.pending()).toBeNull(); expect(f.nativeElement.textContent).not.toContain('Synthetic evidence');
  });
  it('ignores a late preview response from a replaced route', async () => {
    const { c, http } = await open(); const task = c.prepare(); const late = http.expectOne(root + '/preview');
    params.next(convertToParamMap({ id: actor })); TestBed.tick();
    http.expectOne(`/api/ui/datasets/${actor}/adjustment-plans?page=0`).flush({ code: 'scope.denied' }, { status: 403, statusText: 'Forbidden' });
    late.flush({ value: preview }); await task; TestBed.tick(); expect(c.preview()).toBeNull(); expect(c.model().reason).toBe('');
  });
});
