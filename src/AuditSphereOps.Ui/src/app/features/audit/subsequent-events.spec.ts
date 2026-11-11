import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SessionService } from '../../core/session';
import { SubsequentEvents } from './subsequent-events';
import { subsequentEventRecord, subsequentEventState, subsequentEventPreview } from './subsequent-events-contracts';

type RecordView = ReturnType<typeof subsequentEventRecord>;
type State = ReturnType<typeof subsequentEventState>;
type Preview = ReturnType<typeof subsequentEventPreview>;
const id = (n: number) => `${n.toString().repeat(8)}-${n.toString().repeat(4)}-4${n.toString().repeat(3)}-8${n.toString().repeat(3)}-${n.toString().repeat(12)}`;
const engagement = id(1), client = id(2), event = id(3), actor = id(4), firm = id(5), preparer = id(6);
const base = `/api/ui/engagements/${engagement}/completion/subsequent-events`, stateUrl = `${base}/state`, selectedUrl = `${stateUrl}?selectedRecordId=${event}`;
const storageKey = `auditsphere-subsequent-events-pending-v1:${firm}:${actor}:${engagement}`;
const sessionValue = { userId: actor, firmId: firm, generation: '7', staff: true };
const fields = { procedureId: null, periodEndDate: '2026-12-31', eventDate: '2027-01-20', description: 'Customer administration',
  classification: 'NON_ADJUSTING' as const, adjustmentRequired: false, disclosureRequired: true, disclosureReference: 'Note 27',
  currency: 'QAR', rationale: 'Conditions arose after period end', financialEffect: '250000.123456', evidenceReferences: ['news-article'] };
const record: RecordView = { ...fields, id: event, clientId: client, engagementId: engagement,
  revision: '9007199254740993', recordedByUserId: preparer, reviewedByUserId: null, recordedAt: '2027-01-21T00:00:00Z', reviewedAt: null, reviewedRevision: null };
const blank: State = { engagementId: engagement, clientId: client, selectedRecordId: null, expectedRevision: '0', reviewBasis: 'a'.repeat(64),
  record: null, canPrepare: true, canReview: false, blockers: [] };
const reviewState: State = { ...blank, selectedRecordId: event, record, expectedRevision: record.revision, canPrepare: false, canReview: true };

describe('native exact subsequent-event commands', () => {
  function setup() {
    TestBed.configureTestingModule({ imports: [SubsequentEvents], providers: [provideHttpClient(), provideHttpClientTesting()] });
    const session = TestBed.inject(SessionService); session.current.set(sessionValue);
    const fixture = TestBed.createComponent(SubsequentEvents); fixture.componentRef.setInput('engagementId', engagement); fixture.detectChanges();
    return { fixture, page: fixture.componentInstance, http: TestBed.inject(HttpTestingController), session };
  }
  type Setup = ReturnType<typeof setup>;
  async function load(s: Setup, state = blank) {
    s.page.select(state.selectedRecordId); const task = s.page.load();
    s.http.expectOne(state.selectedRecordId ? selectedUrl : stateUrl).flush(state); await task; s.fixture.detectChanges();
  }
  function fill(s: Setup) {
    Object.assign(s.page.form, { ...fields, procedureId: '', currency: '\u0085qar\u0085', description: '\u0085Customer administration\u0085',
      adjustmentRequired: 'false', disclosureRequired: 'true', evidenceReferences: 'news-article' });
    s.page.inputsChanged(); s.fixture.detectChanges();
  }
  async function preview(s: Setup, state = blank, action: 'PREPARE' | 'REVIEW' = 'PREPARE') {
    const task = s.page.preparePreview(action), request = s.http.expectOne(`${base}/${action === 'PREPARE' ? 'prepare' : `${event}/review`}/preview`);
    expect(request.request.body.expectedRevision).toBe(state.expectedRevision); expect(request.request.body.selectedRecordId).toBe(state.selectedRecordId);
    if (action === 'PREPARE') expect(request.request.body.fields).toEqual(fields);
    const response: Preview = { requestId: request.request.body.requestId, action, requestHash: 'b'.repeat(64), reviewBasis: state.reviewBasis,
      expectedRevision: state.expectedRevision, currentRecord: state.record, canProceed: true, blockers: [], summary: 'Exact intent' };
    request.flush({ value: response }); await task; s.page.assent = true; return response;
  }
  function receipt(p: Preview, result: RecordView) {
    return { id: id(7), engagementId: engagement, subsequentEventReviewId: event, actorId: actor, actorEpoch: '7',
      requestId: p.requestId, requestHash: p.requestHash, action: p.action, createdAt: '2027-01-21T00:01:00Z', preview: p, result };
  }
  async function nextRequest(s: Setup, url: string) {
    let req: ReturnType<HttpTestingController['match']>[number] | undefined;
    await vi.waitFor(() => { req = s.http.match(url)[0] ?? req; expect(req).toBeDefined(); }); return req!;
  }
  afterEach(() => { TestBed.inject(HttpTestingController).verify(); TestBed.resetTestingModule(); vi.restoreAllMocks(); sessionStorage.clear(); });

  it('requires explicit unanswered choices, allows nullable effect/free evidence, and does not infer professional classification', async () => {
    const s = setup(); await load(s); expect(s.page.form.classification).toBe(''); expect(s.page.form.adjustmentRequired).toBe(''); expect(s.page.form.disclosureRequired).toBe('');
    fill(s); s.page.form.classification = ''; expect(s.page.canPreview('PREPARE')).toBe(false);
    s.page.form.classification = 'PENDING_ASSESSMENT'; s.page.form.financialEffect = ''; s.page.form.evidenceReferences = ''; s.page.form.rationale = '';
    expect(s.page.canPreview('PREPARE')).toBe(true);
    const task = s.page.preparePreview('PREPARE'), request = s.http.expectOne(`${base}/prepare/preview`);
    expect(request.request.body.fields).toMatchObject({ financialEffect: null, evidenceReferences: [], procedureId: null, classification: 'PENDING_ASSESSMENT' });
    request.flush({ error: { code: 'mapping.invalid', message: 'Explicit server refusal' } }, { status: 400, statusText: 'Bad Request' }); await task;
    s.fixture.detectChanges(); expect((s.fixture.nativeElement as HTMLElement).querySelectorAll('select option[value=""]').length).toBe(3);
  });
  it('lists multiple same-date events without choosing one, loads only an explicit ID and prevents identity edits from becoming creates', async () => {
    const s = setup(), task = s.page.loadList(); s.http.expectOne(stateUrl).flush(blank);
    (await nextRequest(s, `${base}?skip=0`)).flush({ records: [record, { ...record, id: id(8), description: 'Another event' }], hasMore: false }); await task;
    expect(s.page.records()).toHaveLength(2); expect(s.page.selectedId()).toBeNull(); expect(s.page.state()).toBeNull();
    await load(s, { ...reviewState, canPrepare: true, canReview: false, record: { ...record, recordedByUserId: actor } });
    expect(s.page.form.description).toBe(fields.description); s.page.form.description = 'Replacement'; s.page.inputsChanged();
    expect(s.page.canPreview('PREPARE')).toBe(false); await s.page.preparePreview('PREPARE'); s.http.expectNone(`${base}/prepare/preview`);
    s.page.select(null); expect(s.page.form.description).toBe(''); expect(s.page.state()).toBeNull();
  });
  it('stores only exact recovery identities before mutation and confirms full preparation then refreshes the returned new ID', async () => {
    const s = setup(); await load(s); fill(s); const p = await preview(s), emitted = vi.spyOn(s.page.refreshed, 'emit'), task = s.page.submit();
    const command = s.http.expectOne(`${base}/prepare`), raw = sessionStorage.getItem(storageKey)!;
    expect(raw.length).toBeLessThan(4096); expect(JSON.parse(raw).eventId).toBeNull();
    for (const data of [fields.description, fields.rationale, fields.financialEffect, fields.evidenceReferences[0], fields.eventDate]) expect(raw).not.toContain(data);
    expect(command.request.body).toMatchObject({ fields, selectedRecordId: null, expectedRequestHash: p.requestHash, reviewed: true });
    const result = { ...record, revision: '1', recordedByUserId: actor }; command.flush({ value: receipt(p, result) });
    (await nextRequest(s, selectedUrl)).flush({ ...blank, selectedRecordId: event, expectedRevision: '1', record: result }); await task;
    expect(sessionStorage.getItem(storageKey)).toBeNull(); expect(s.page.selectedId()).toBe(event); expect(s.page.state()?.record?.reviewedByUserId).toBeNull();
    expect(s.page.history()).toContain('Exact event state refreshed'); expect(emitted).toHaveBeenCalledOnce();
  });
  it.each(['pending', 'self', 'reviewed', 'denied'])('cannot review ineligible event even with conflicting flags: %s', async kind => {
    const s = setup(), r = { ...record };
    if (kind === 'pending') r.classification = 'PENDING_ASSESSMENT';
    if (kind === 'self') r.recordedByUserId = actor;
    if (kind === 'reviewed') { r.reviewedByUserId = actor; r.reviewedAt = '2027-01-21T00:01:00Z'; r.reviewedRevision = '9007199254740992'; }
    await load(s, { ...reviewState, record: r, canReview: kind !== 'denied' }); expect(s.page.canPreview('REVIEW')).toBe(false);
  });
  it('reviews the exact independent Int64 revision and retains unrelated gates', async () => {
    const s = setup(); await load(s, reviewState); const p = await preview(s, reviewState, 'REVIEW'), task = s.page.submit();
    const request = s.http.expectOne(`${base}/${event}/review`); expect(request.request.body.expectedRevision).toBe('9007199254740993'); expect(request.request.body.fields).toBeNull();
    const result = { ...record, revision: '9007199254740994', reviewedRevision: record.revision, reviewedByUserId: actor, reviewedAt: '2027-01-21T00:01:00Z' };
    request.flush({ value: receipt(p, result) }); (await nextRequest(s, selectedUrl)).flush({ ...reviewState, record: result, expectedRevision: result.revision, canReview: false, blockers: ['unrelated:gate'] }); await task;
    expect(s.page.state()?.record?.reviewedRevision).toBe(record.revision); expect(s.page.state()?.blockers).toEqual(['unrelated:gate']);
  });
  it('retains unknown outcome over remount and missing lookup without resend, then reconciles only the exact receipt', async () => {
    let s = setup(); await load(s); fill(s); const p = await preview(s), task = s.page.submit();
    s.http.expectOne(`${base}/prepare`).flush({}, { status: 503, statusText: 'Unavailable' }); await task;
    const raw = sessionStorage.getItem(storageKey); s.fixture.destroy(); TestBed.resetTestingModule(); s = setup();
    expect(s.page.pending()?.requestId).toBe(p.requestId); expect(s.page.form.description).toBe(''); await load(s);
    await s.page.submit(); s.http.expectNone(`${base}/prepare`);
    let recovery = s.page.recover(); s.http.expectOne(`${base}/receipts/${p.requestId}?requestHash=${p.requestHash}`).flush({ found: false, receipt: null }); await recovery;
    expect(sessionStorage.getItem(storageKey)).toBe(raw);
    const result = { ...record, revision: '1', recordedByUserId: actor }; recovery = s.page.recover();
    s.http.expectOne(`${base}/receipts/${p.requestId}?requestHash=${p.requestHash}`).flush({ found: true, receipt: receipt(p, result) });
    (await nextRequest(s, selectedUrl)).flush({ ...blank, selectedRecordId: event, expectedRevision: '1', record: result }); await recovery;
    expect(sessionStorage.getItem(storageKey)).toBeNull();
  });
  it.each(['actor', 'epoch', 'effect', 'null-effect', 'basis', 'nested-request', 'reviewed', 'omitted-nullable'])('retains uncertainty for mismatched response: %s', async change => {
    const s = setup(); await load(s); fill(s); const p = await preview(s), task = s.page.submit(), r = receipt(p, { ...record, revision: '1', recordedByUserId: actor });
    if (change === 'actor') r.actorId = preparer;
    if (change === 'epoch') r.actorEpoch = '8';
    if (change === 'effect') r.result.financialEffect = '250000.123455';
    if (change === 'null-effect') r.result.financialEffect = null;
    if (change === 'basis') r.preview = { ...p, currentRecord: record };
    if (change === 'nested-request') r.preview = { ...p, requestId: id(8) };
    if (change === 'reviewed') r.result.reviewedByUserId = preparer;
    const response = structuredClone(r) as unknown as { result: Record<string, unknown> }; if (change === 'omitted-nullable') delete response.result['reviewedAt'];
    s.http.expectOne(`${base}/prepare`).flush({ value: response }); await task;
    expect(s.page.pending()).not.toBeNull(); expect(sessionStorage.getItem(storageKey)).not.toBeNull(); expect(s.page.history()).toBe(''); s.http.expectNone(selectedUrl);
  });
  it.each(['scope', 'session', 'inputs', 'storage'])('rechecks context after asynchronous receipt digest: %s', async change => {
    const s = setup(); await load(s); fill(s); const p = await preview(s); let finish!: (v: ArrayBuffer) => void;
    const original = crypto.subtle.digest.bind(crypto.subtle), spy = vi.spyOn(crypto.subtle, 'digest').mockReturnValueOnce(new Promise<ArrayBuffer>(r => { finish = r; }));
    const task = s.page.submit(); s.http.expectOne(`${base}/prepare`).flush({ value: receipt(p, { ...record, revision: '1', recordedByUserId: actor }) });
    await vi.waitFor(() => expect(spy).toHaveBeenCalledOnce()); let raw = sessionStorage.getItem(storageKey);
    if (change === 'scope') { s.fixture.componentRef.setInput('engagementId', id(8)); s.fixture.detectChanges(); }
    if (change === 'session') { s.session.current.set({ ...sessionValue, generation: '8' }); s.fixture.detectChanges(); }
    if (change === 'inputs') { s.page.form.description = 'Changed'; s.page.inputsChanged(); }
    if (change === 'storage') { raw = JSON.stringify({ ...s.page.pending(), requestId: id(8) }); sessionStorage.setItem(storageKey, raw); }
    finish(await original('SHA-256', new TextEncoder().encode(JSON.stringify([null, fields.periodEndDate, fields.eventDate, fields.description,
      fields.classification, false, true, fields.disclosureReference, fields.rationale, fields.currency, '250000123456', fields.evidenceReferences]))));
    await task; expect(sessionStorage.getItem(storageKey)).toBe(raw); expect(s.page.history()).toBe(''); s.http.expectNone(selectedUrl);
  });
  it.each([['idempotency.conflict', false], ['unknown', false], ['generation.stale', true]] as const)('only clears matching first-send proven refusal: %s', async (code, clear) => {
    const s = setup(); await load(s); fill(s); await preview(s); const task = s.page.submit();
    s.http.expectOne(`${base}/prepare`).flush({ code, message: 'Refused' }, { status: 409, statusText: 'Conflict' }); await task;
    expect(sessionStorage.getItem(storageKey) === null).toBe(clear); expect(s.page.state()).toBeNull();
  });
  it('distinguishes confirmed receipt history from failed current-state refresh', async () => {
    const s = setup(); await load(s); fill(s); const p = await preview(s), task = s.page.submit();
    s.http.expectOne(`${base}/prepare`).flush({ value: receipt(p, { ...record, revision: '1', recordedByUserId: actor }) });
    (await nextRequest(s, selectedUrl)).flush({}, { status: 403, statusText: 'Forbidden' }); await task;
    expect(s.page.pending()).toBeNull(); expect(s.page.state()).toBeNull(); expect(s.page.history()).toContain('Current state could not be refreshed');
  });
  it('does not send when recovery storage fails', async () => {
    const s = setup(); await load(s); fill(s); await preview(s); vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('Unavailable'); });
    await s.page.submit(); s.http.expectNone(`${base}/prepare`); expect(s.page.error()).toContain('Unavailable');
  });
});
