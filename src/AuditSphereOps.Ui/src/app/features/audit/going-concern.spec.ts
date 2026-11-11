import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SessionService } from '../../core/session';
import { GoingConcern } from './going-concern';
import { goingConcernRecord, goingConcernState, goingConcernPreview } from './going-concern-contracts';

type RecordView = ReturnType<typeof goingConcernRecord>;
type State = ReturnType<typeof goingConcernState>;
type Preview = ReturnType<typeof goingConcernPreview>;
const id = (n: number) => `${n.toString().repeat(8)}-${n.toString().repeat(4)}-4${n.toString().repeat(3)}-8${n.toString().repeat(3)}-${n.toString().repeat(12)}`;
const engagement = id(1), client = id(2), assessment = id(3), actor = id(4), firm = id(5), preparer = id(6);
const date = '2026-12-31', base = `/api/ui/engagements/${engagement}/completion/going-concern-assessments`;
const stateUrl = `${base}/state?assessmentDate=${date}`;
const storageKey = `auditsphere-going-concern-pending-v1:${firm}:${actor}:${engagement}`;
const sessionValue = { userId: actor, firmId: firm, generation: '7', staff: true };
const fields = { procedureId: null, assessmentDate: date, periodCoveredTo: '2027-06-30', forecastReviewOutcome: 'Forecast scrutinised',
  materialUncertaintyIdentified: true, disclosureAdequate: true, currency: 'USD', rationale: 'Disclosed uncertainty', evidenceReferences: ['procedure-result:exact'] };
const record: RecordView = { ...fields, id: assessment, clientId: client, engagementId: engagement, conclusion: 'MATERIAL_UNCERTAINTY_DISCLOSED',
  revision: '9007199254740993', recordedByUserId: preparer, reviewedByUserId: null, recordedAt: '2026-12-31T00:00:00Z', reviewedAt: null, reviewedRevision: null };
const blank: State = { engagementId: engagement, clientId: client, assessmentDate: date, expectedRevision: '0', reviewBasis: 'a'.repeat(64),
  record: null, canPrepare: true, canReview: false, blockers: [] };
const reviewState: State = { ...blank, record, expectedRevision: record.revision, canPrepare: false, canReview: true };

describe('native exact going-concern commands', () => {
  function setup() {
    TestBed.configureTestingModule({ imports: [GoingConcern], providers: [provideHttpClient(), provideHttpClientTesting()] });
    const session = TestBed.inject(SessionService); session.current.set(sessionValue);
    const fixture = TestBed.createComponent(GoingConcern); fixture.componentRef.setInput('engagementId', engagement); fixture.detectChanges();
    return { fixture, page: fixture.componentInstance, http: TestBed.inject(HttpTestingController), session };
  }
  type Setup = ReturnType<typeof setup>;
  async function load(s: Setup, state = blank) {
    s.page.form.assessmentDate = date;
    const pending = s.page.load(); s.http.expectOne(stateUrl).flush(state); await pending; s.fixture.detectChanges();
  }
  function fill(s: Setup) {
    Object.assign(s.page.form, { ...fields, procedureId: '', currency: '\u0085usd\u0085', forecastReviewOutcome: '\u0085Forecast scrutinised\u0085',
      rationale: '\u0085Disclosed uncertainty\u0085', materialUncertaintyIdentified: 'true', disclosureAdequate: 'true', evidenceReferences: 'procedure-result:exact' });
    s.page.inputsChanged(); s.fixture.detectChanges();
  }
  async function preview(s: Setup, state = blank, action: 'PREPARE' | 'REVIEW' = 'PREPARE') {
    const task = s.page.preparePreview(action);
    const request = s.http.expectOne(`${base}/${action === 'PREPARE' ? 'prepare' : `${assessment}/review`}/preview`);
    expect(request.request.body.expectedRevision).toBe(state.expectedRevision);
    if (action === 'PREPARE') expect(request.request.body.fields).toEqual(fields);
    const response: Preview = { requestId: request.request.body.requestId, action, requestHash: 'b'.repeat(64), reviewBasis: state.reviewBasis,
      expectedRevision: state.expectedRevision, currentRecord: state.record, canProceed: true, blockers: [], summary: 'Exact intent' };
    request.flush({ value: response }); await task; s.page.assent = true;
    return response;
  }
  function receipt(p: Preview, result: RecordView) {
    return { id: id(7), engagementId: engagement, goingConcernAssessmentId: assessment, actorId: actor, actorEpoch: '7',
      requestId: p.requestId, requestHash: p.requestHash, action: p.action, createdAt: '2026-12-31T00:01:00Z', preview: p, result };
  }
  async function nextRequest(s: Setup, url: string) {
    let req: ReturnType<HttpTestingController['match']>[number] | undefined;
    await vi.waitFor(() => { req = s.http.match(url)[0] ?? req; expect(req).toBeDefined(); });
    return req!;
  }
  afterEach(() => { TestBed.inject(HttpTestingController).verify(); TestBed.resetTestingModule(); vi.restoreAllMocks(); sessionStorage.clear(); });

  it('leaves both professional choices unanswered and mounts a native exact-date form', async () => {
    const s = setup(); await load(s); fill(s);
    s.page.form.materialUncertaintyIdentified = ''; s.page.form.disclosureAdequate = ''; s.fixture.detectChanges();
    expect(s.page.canPreview('PREPARE')).toBe(false);
    await s.page.preparePreview('PREPARE'); s.http.expectNone(`${base}/prepare/preview`);
    const host = s.fixture.nativeElement as HTMLElement;
    expect(host.querySelectorAll('select option[value=""]').length).toBe(2);
    expect(host.textContent).toContain('No assessment exists at this exact date');
    s.page.form.materialUncertaintyIdentified = 'false'; expect(s.page.canPreview('PREPARE')).toBe(false);
    s.page.form.disclosureAdequate = 'false'; expect(s.page.canPreview('PREPARE')).toBe(true);
  });

  it('persists bounded recovery before sending exact normalized preparation, confirms unreviewed receipt and refreshes', async () => {
    const s = setup(); await load(s); fill(s); const p = await preview(s);
    const emitted = vi.spyOn(s.page.refreshed, 'emit'); const task = s.page.submit();
    const command = s.http.expectOne(`${base}/prepare`);
    const raw = sessionStorage.getItem(storageKey)!;
    expect(raw.length).toBeLessThan(4096); expect(JSON.parse(raw).requestId).toBe(p.requestId);
    for (const secret of [fields.rationale, fields.forecastReviewOutcome, fields.evidenceReferences[0]]) expect(raw).not.toContain(secret);
    expect(command.request.body).toMatchObject({ fields, expectedRequestHash: p.requestHash, reviewed: true });
    expect(s.page.state()).toBeNull();
    const result = { ...record, revision: '1', recordedByUserId: actor };
    command.flush({ value: receipt(p, result) });
    const refresh = await nextRequest(s, stateUrl); refresh.flush({ ...blank, expectedRevision: '1', record: result }); await task;
    expect(sessionStorage.getItem(storageKey)).toBeNull(); expect(s.page.pending()).toBeNull();
    expect(s.page.state()?.record?.reviewedByUserId).toBeNull(); expect(s.page.history()).toContain('Exact assessment state refreshed'); expect(emitted).toHaveBeenCalledOnce();
  });

  it.each(['NOT_ASSESSED', 'INADEQUATE_DISCLOSURE', 'self', 'ambiguous', 'denied'])('refuses ineligible review even if presentation flags conflict: %s', async kind => {
    const s = setup(); const r = { ...record };
    if (kind === 'self') r.recordedByUserId = actor;
    else if (kind !== 'ambiguous' && kind !== 'denied') r.conclusion = kind;
    await load(s, { ...reviewState, record: r, canReview: kind !== 'denied', blockers: kind === 'ambiguous' ? ['going-concern:ambiguous-assessment'] : [] });
    expect(s.page.canPreview('REVIEW')).toBe(false); await s.page.preparePreview('REVIEW'); s.http.expectNone(`${base}/${assessment}/review/preview`);
  });

  it('reviews independent exact revision without numeric rounding and retains insufficient coverage and unrelated blockers', async () => {
    const s = setup(); await load(s, reviewState); const p = await preview(s, reviewState, 'REVIEW');
    const task = s.page.submit(); const request = s.http.expectOne(`${base}/${assessment}/review`);
    expect(request.request.body.expectedRevision).toBe('9007199254740993'); expect(request.request.body.fields).toBeNull();
    const result = { ...record, revision: '9007199254740994', reviewedRevision: record.revision, reviewedByUserId: actor, reviewedAt: '2026-12-31T00:01:00Z' };
    request.flush({ value: receipt(p, result) });
    const refresh = await nextRequest(s, stateUrl);
    refresh.flush({ ...reviewState, record: result, expectedRevision: result.revision, canReview: false,
      blockers: ['going-concern:horizon-under-12-months', 'unrelated:gate'] }); await task; s.fixture.detectChanges();
    expect(s.page.state()?.record?.periodCoveredTo).toBe('2027-06-30');
    expect(s.fixture.nativeElement.textContent).toContain('going-concern:horizon-under-12-months');
    expect(s.fixture.nativeElement.textContent).toContain('unrelated:gate');
  });

  it('retains unknown execution across remount, never resends and only clears after exact receipt lookup', async () => {
    let s = setup(); await load(s); fill(s); const p = await preview(s);
    const task = s.page.submit(); s.http.expectOne(`${base}/prepare`).flush({}, { status: 503, statusText: 'Unavailable' }); await task;
    const raw = sessionStorage.getItem(storageKey)!; expect(s.page.pending()?.requestId).toBe(p.requestId);
    s.fixture.destroy(); TestBed.resetTestingModule(); s = setup();
    expect(s.page.pending()?.requestId).toBe(p.requestId); expect(s.page.form.rationale).toBe('');
    await load(s); fill(s); expect(s.page.canPreview('PREPARE')).toBe(false); await s.page.submit(); s.http.expectNone(`${base}/prepare`);
    let recovery = s.page.recover(); s.http.expectOne(`${base}/receipts/${p.requestId}?requestHash=${p.requestHash}`).flush({ found: false, receipt: null }); await recovery;
    expect(sessionStorage.getItem(storageKey)).toBe(raw); expect(s.page.error()).toContain('remains unresolved');
    const result = { ...record, revision: '1', recordedByUserId: actor };
    recovery = s.page.recover(); s.http.expectOne(`${base}/receipts/${p.requestId}?requestHash=${p.requestHash}`).flush({ found: true, receipt: receipt(p, result) });
    (await nextRequest(s, stateUrl)).flush({ ...blank, expectedRevision: '1', record: result }); await recovery;
    expect(sessionStorage.getItem(storageKey)).toBeNull(); expect(s.page.history()).toContain('Confirmed prepare receipt');
  });

  it.each(['actor', 'epoch', 'content', 'basis', 'reviewed'])('rejects a mismatched receipt and retains recovery: %s', async change => {
    const s = setup(); await load(s); fill(s); const p = await preview(s), task = s.page.submit();
    const r = receipt(p, { ...record, revision: '1', recordedByUserId: actor });
    if (change === 'actor') r.actorId = preparer;
    if (change === 'epoch') r.actorEpoch = '8';
    if (change === 'content') r.result.rationale = 'Different professional intent';
    if (change === 'basis') r.preview = { ...p, currentRecord: record };
    if (change === 'reviewed') r.result.reviewedByUserId = preparer;
    s.http.expectOne(`${base}/prepare`).flush({ value: r }); await task;
    expect(sessionStorage.getItem(storageKey)).not.toBeNull(); expect(s.page.pending()).not.toBeNull(); expect(s.page.state()).toBeNull();
    expect(s.page.history()).toBe(''); s.http.expectNone(stateUrl);
  });

  it('invalidates changed inputs and changed session during asynchronous preview hashing', async () => {
    const s = setup(); await load(s); fill(s);
    const original = crypto.subtle.digest.bind(crypto.subtle);
    let finish!: (value: ArrayBuffer) => void;
    const paused = new Promise<ArrayBuffer>(resolve => { finish = resolve; });
    const hashing = vi.spyOn(crypto.subtle, 'digest').mockReturnValueOnce(paused);
    const task = s.page.preparePreview('PREPARE'), request = s.http.expectOne(`${base}/prepare/preview`);
    request.flush({ value: { requestId: request.request.body.requestId, action: 'PREPARE', requestHash: 'b'.repeat(64), reviewBasis: blank.reviewBasis,
      expectedRevision: '0', currentRecord: null, canProceed: true, blockers: [], summary: 'Exact intent' } });
    await vi.waitFor(() => expect(hashing).toHaveBeenCalledOnce());
    s.page.form.rationale = 'Changed'; s.page.inputsChanged(); s.session.current.set({ ...sessionValue, generation: '8' }); s.fixture.detectChanges();
    finish(await original('SHA-256', new TextEncoder().encode('old'))); await task;
    expect(s.page.preview()).toBeNull(); expect(s.page.state()).toBeNull(); expect(s.page.busy()).toBe(false);
  });

  it('cannot clear a changed reference or emit stale approval while receipt hashing is pending', async () => {
    const s = setup(); await load(s); fill(s); const p = await preview(s);
    const original = crypto.subtle.digest.bind(crypto.subtle); let finish!: (value: ArrayBuffer) => void;
    const paused = new Promise<ArrayBuffer>(resolve => { finish = resolve; });
    const hashing = vi.spyOn(crypto.subtle, 'digest').mockReturnValueOnce(paused);
    const task = s.page.submit(), r = receipt(p, { ...record, revision: '1', recordedByUserId: actor });
    s.http.expectOne(`${base}/prepare`).flush({ value: r });
    await vi.waitFor(() => expect(hashing).toHaveBeenCalledOnce());
    const replacement = JSON.stringify({ ...s.page.pending(), requestId: id(8) }); sessionStorage.setItem(storageKey, replacement);
    finish(await original('SHA-256', new TextEncoder().encode(JSON.stringify([null, date, fields.periodCoveredTo, fields.forecastReviewOutcome, true, true, 'USD', fields.rationale, fields.evidenceReferences]))));
    await task;
    expect(sessionStorage.getItem(storageKey)).toBe(replacement); expect(s.page.history()).toBe(''); expect(s.page.error()).toContain('reference changed'); s.http.expectNone(stateUrl);
  });

  it('separates a confirmed receipt from a failed current refresh', async () => {
    const s = setup(); await load(s); fill(s); const p = await preview(s), task = s.page.submit();
    s.http.expectOne(`${base}/prepare`).flush({ value: receipt(p, { ...record, revision: '1', recordedByUserId: actor }) });
    (await nextRequest(s, stateUrl)).flush({}, { status: 403, statusText: 'Forbidden' }); await task;
    expect(s.page.pending()).toBeNull(); expect(s.page.state()).toBeNull(); expect(s.page.history()).toContain('Current state could not be refreshed');
  });

  it('refuses a command when persistent recovery storage is unavailable', async () => {
    const s = setup(); await load(s); fill(s); await preview(s);
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('Storage unavailable'); });
    await s.page.submit(); s.http.expectNone(`${base}/prepare`); expect(s.page.error()).toContain('Storage unavailable');
  });
  it.each(['missing-time', 'missing-basis', 'wrong-revision', 'self'])('refuses inconsistent review identity in authoritative read: %s', async kind => {
    const s = setup();
    const r = { ...record, revision: '9007199254740994', reviewedByUserId: actor, reviewedAt: '2026-12-31T00:01:00Z' as string | null,
      reviewedRevision: record.revision as string | null };
    if (kind === 'missing-time') r.reviewedAt = null;
    if (kind === 'missing-basis') r.reviewedRevision = null;
    if (kind === 'wrong-revision') r.reviewedRevision = '1';
    if (kind === 'self') r.reviewedByUserId = preparer;
    await load(s, { ...reviewState, record: r, expectedRevision: r.revision });
    expect(s.page.state()).toBeNull(); expect(s.page.error()).toContain('could not be verified');
    expect(s.fixture.nativeElement.querySelector('[data-gc-record]')).toBeNull();
  });

  it.each(['scope', 'session', 'inputs'])('invalidates a receipt while its content hash is awaiting: %s', async change => {
    const s = setup(); await load(s); fill(s); const p = await preview(s);
    const original = crypto.subtle.digest.bind(crypto.subtle); let finish!: (value: ArrayBuffer) => void;
    const hashing = vi.spyOn(crypto.subtle, 'digest').mockReturnValueOnce(new Promise<ArrayBuffer>(resolve => { finish = resolve; }));
    const task = s.page.submit(); s.http.expectOne(`${base}/prepare`).flush({ value: receipt(p, { ...record, revision: '1', recordedByUserId: actor }) });
    await vi.waitFor(() => expect(hashing).toHaveBeenCalledOnce());
    const raw = sessionStorage.getItem(storageKey);
    if (change === 'scope') { s.fixture.componentRef.setInput('engagementId', id(8)); s.fixture.detectChanges(); }
    if (change === 'session') { s.session.current.set({ ...sessionValue, generation: '8' }); s.fixture.detectChanges(); }
    if (change === 'inputs') { s.page.form.rationale = 'Changed while awaiting'; s.page.inputsChanged(); }
    finish(await original('SHA-256', new TextEncoder().encode(JSON.stringify([null, date, fields.periodCoveredTo, fields.forecastReviewOutcome, true, true, 'USD', fields.rationale, fields.evidenceReferences]))));
    await task;
    expect(sessionStorage.getItem(storageKey)).toBe(raw); expect(s.page.history()).toBe(''); s.http.expectNone(stateUrl);
  });

});
