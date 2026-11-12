import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { ArchiveRecord } from './records';
import { archiveIntentKey } from './archive-intent-recovery';

const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const hash = 'e'.repeat(64), created = '2026-10-02T18:00:00Z';
const entry = (ordinal: number) => ({ ordinal, entryKind: 'DOCUMENT', relativeName: `evidence/item-${ordinal}.pdf`, contentHash: hash, byteCount: ordinal });
const copy = (state: 'NOT_REQUESTED' | 'REQUESTED' | 'OBSERVED' | 'BLOCKED_EXTERNAL' = 'NOT_REQUESTED') => ({
  state, operationId: state === 'NOT_REQUESTED' ? null : id(80), targetRevision: state === 'NOT_REQUESTED' ? null : 2,
  manifestVersion: 1, manifestSha256: hash, latestFileReadBackAt: state === 'OBSERVED' ? created : null,
  permissionReadBackAt: state === 'OBSERVED' ? created : null,
  note: state === 'OBSERVED'
    ? 'Historical readback only; the AuditSphere release/archive store remains authoritative.'
    : 'No SharePoint secondary-copy request is recorded. The AuditSphere release/archive store remains authoritative.'
});
const archive = (n = 1, secondarySharePointCopy = copy(), fields: Record<string, unknown> = {}) => ({ id: id(n), engagementId: id(20), status: 'SEALED', createdAt: created,
  profileId: 'SYNTHETIC-ARCHIVE', profileVersion: 1, manifestVersion: 1, manifestId: id(40 + n), manifestStatus: 'BUILT', manifestDigest: hash, completenessStatus: 'COMPLETE', completenessException: null, canExport: false, canReview: false,
  entries: Array.from({ length: 100 }, (_, i) => entry(i + 1)), totalEntryCount: 205, nextOrdinal: 100,
  activeHoldCount: 1, observedProtection: null, desiredLabel: 'SYNTHETIC LABEL', observedLabel: null, actionState: 'REQUESTED', externalReference: 'SYNTHETIC REFERENCE', secondarySharePointCopy, ...fields });
const unbuiltArchive = (n = 1) => archive(n, copy(), { manifestVersion: null, manifestId: null, manifestStatus: null,
  manifestDigest: null, completenessStatus: null, completenessException: null, totalEntryCount: 0, entries: [], nextOrdinal: null });
const page2 = () => ({ ...archive(), entries: Array.from({ length: 100 }, (_, i) => entry(i + 101)), nextOrdinal: 200 });
const page3 = () => ({ ...archive(), entries: Array.from({ length: 5 }, (_, i) => entry(i + 201)), nextOrdinal: null });

describe('Archive detail and exact paging safety', () => {
  let fixture: ComponentFixture<ArchiveRecord>, component: ArchiveRecord, http: HttpTestingController, params: BehaviorSubject<ReturnType<typeof convertToParamMap>>, session: SessionService;
  const url = (n = 1) => `/api/ui/records/archives/${id(n)}`;
  const ownedRecoveryKeys = [[90, 1], [90, 2], [92, 1]].map(([actor, archive]) => archiveIntentKey({
    firmId: id(91), actorUserId: id(actor), engagementId: id(20), archiveId: id(archive),
  }));
  beforeEach(async () => {
    ownedRecoveryKeys.forEach(key => sessionStorage.removeItem(key));
    params = new BehaviorSubject(convertToParamMap({ id: id(1) }));
    TestBed.configureTestingModule({ imports: [ArchiveRecord], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([{ path: '**', component: ArchiveRecord }]), { provide: ActivatedRoute, useValue: { paramMap: params } }] });
    session = TestBed.inject(SessionService); session.current.set({ userId: id(90), firmId: id(91), generation: '1', staff: true });
    http = TestBed.inject(HttpTestingController); fixture = TestBed.createComponent(ArchiveRecord); component = fixture.componentInstance;
    TestBed.tick(); http.expectOne(url()).flush(archive()); await fixture.whenStable();
  });
  afterEach(() => { try { http.verify({ ignoreCancelled: true }); } finally {
    TestBed.resetTestingModule(); ownedRecoveryKeys.forEach(key => sessionStorage.removeItem(key));
  } });
  it('renders creation, exact rows, holds and requested-versus-observed protection honestly', () => {
    const host: HTMLElement = fixture.nativeElement;
    expect(host.textContent).toContain(created); expect(host.textContent).toContain('Records action requested; this is not evidence of Purview protection.');
    expect(host.textContent).toContain('SharePoint secondary access copy'); expect(host.textContent).toContain('NOT_REQUESTED');
    expect(host.textContent).toContain('release/archive store remains authoritative');
    expect(host.textContent).toContain('Unreleased local hold'); expect(host.textContent).toContain('Not observed');
    const cells = host.querySelector('tbody tr')!.querySelectorAll('td');
    expect(Array.from(cells, c => c.textContent?.trim())).toEqual(['1', 'DOCUMENT', 'evidence/item-1.pdf', hash, '1']);
  });
  it('keeps the current archive command busy when a same-scope refresh invalidates only its read', async () => {
    component.refresh(); TestBed.tick(); http.expectOne(url()).flush(unbuiltArchive()); await fixture.whenStable();

    const task = component.buildManifest(component.archive()!);
    const request = http.expectOne(url() + '/build');
    component.refresh(); TestBed.tick(); http.expectOne(url()).flush(unbuiltArchive()); await fixture.whenStable();
    const remainedBusyForSameOwner = component.actionBusy();
    const buildButton = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'))
      .find(button => button.textContent?.includes('Build archive manifest'));

    request.flush({}, { status: 503, statusText: 'Build outcome unavailable' }); await task;
    expect(remainedBusyForSameOwner).toBe(true);
    expect(buildButton?.disabled).toBe(true);
    expect(component.actionBusy()).toBe(false);
  });
  it('refreshes a successful build into the same-component exact review flow', async () => {
    component.refresh(); TestBed.tick(); http.expectOne(url()).flush(unbuiltArchive()); await fixture.whenStable();

    const buildTask = component.buildManifest(component.archive()!);
    const build = http.expectOne(url() + '/build');
    expect(component.actionBusy()).toBe(true);
    build.flush({ value: { archiveId: id(1), manifestId: id(41), manifestVersion: 1, status: 'BUILT',
      digest: hash, entryCount: 205, completenessStatus: 'COMPLETE' } });
    await buildTask;
    http.expectOne(url()).flush(archive(1, copy(), { status: 'MANIFEST_BUILT', manifestStatus: 'BUILT', canReview: true }));
    await fixture.whenStable();
    expect(component.actionBusy()).toBe(false);
    expect(component.archive()?.canReview).toBe(true);

    const reviewTask = component.reviewManifest(component.archive()!);
    const review = http.expectOne(url() + '/review');
    expect(review.request.body).toMatchObject({ manifestId: id(41), manifestVersion: 1, manifestDigest: hash });
    review.flush({ value: { archiveId: id(1), manifestId: id(41), manifestVersion: 1, status: 'REVIEWED',
      digest: hash, entryCount: 205, completenessStatus: 'COMPLETE' } });
    await reviewTask;
    http.expectOne(url()).flush(archive(1, copy(), { status: 'ASSEMBLY_REVIEWED', manifestStatus: 'REVIEWED', canReview: false }));
    await fixture.whenStable();
    expect(component.archive()?.status).toBe('ASSEMBLY_REVIEWED');
  });
  it('does not let a previous archive command clear the next archive command busy owner', async () => {
    component.refresh(); TestBed.tick(); http.expectOne(url()).flush(unbuiltArchive()); await fixture.whenStable();
    const oldTask = component.buildManifest(component.archive()!);
    const oldRequest = http.expectOne(url() + '/build');

    params.next(convertToParamMap({ id: id(2) })); TestBed.tick();
    http.expectOne(url(2)).flush(unbuiltArchive(2)); await fixture.whenStable();
    expect(component.actionBusy()).toBe(false);
    const currentTask = component.buildManifest(component.archive()!);
    const currentRequest = http.expectOne(url(2) + '/build');
    expect(component.actionBusy()).toBe(true);

    oldRequest.flush({ value: { archiveId: id(1), manifestId: id(41), manifestVersion: 1, status: 'BUILT',
      digest: hash, entryCount: 205, completenessStatus: 'COMPLETE' } });
    await oldTask;
    expect(component.actionBusy()).toBe(true);

    currentRequest.flush({ value: { archiveId: id(2), manifestId: id(42), manifestVersion: 1, status: 'BUILT',
      digest: hash, entryCount: 205, completenessStatus: 'COMPLETE' } });
    await currentTask;
    http.expectOne(url(2)).flush(archive(2, copy(), { status: 'MANIFEST_BUILT', manifestStatus: 'BUILT', canReview: true }));
    await fixture.whenStable();
    expect(component.actionBusy()).toBe(false);
    expect(component.archive()?.id).toBe(id(2));
    expect(component.archive()?.canReview).toBe(true);
  });
  it('reviews the exact displayed manifest and accepts only its matching receipt', async () => {
    component.refresh(); TestBed.tick();
    http.expectOne(url()).flush(archive(1, copy(), { status: 'MANIFEST_BUILT', canReview: true })); await fixture.whenStable();
    const reviewButton = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'))
      .find(button => button.textContent?.includes('Review exact manifest'));
    expect(reviewButton).toBeTruthy();
    const task = component.reviewManifest(component.archive()!);
    const request = http.expectOne(url() + '/review');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ manifestId: id(41), manifestVersion: 1, manifestDigest: hash,
      requestKey: expect.stringMatching(/^[0-9a-f-]{36}$/i) });
    request.flush({ value: { archiveId: id(1), manifestId: id(41), manifestVersion: 1, status: 'REVIEWED',
      digest: hash, entryCount: 205, completenessStatus: 'COMPLETE' } });
    await task;
    http.expectOne(url()).flush(archive(1, copy(), { status: 'ASSEMBLY_REVIEWED', manifestStatus: 'REVIEWED' }));
    await fixture.whenStable();
    expect(component.archive()?.status).toBe('ASSEMBLY_REVIEWED');
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Review exact manifest');
  });
  it('reuses the same review request identity after an unknown HTTP outcome', async () => {
    component.refresh(); TestBed.tick();
    http.expectOne(url()).flush(archive(1, copy(), { status: 'MANIFEST_BUILT', canReview: true })); await fixture.whenStable();
    const firstTask = component.reviewManifest(component.archive()!);
    const first = http.expectOne(url() + '/review');
    const firstBody = first.request.body, firstKey = firstBody.requestKey;
    first.flush({}, { status: 502, statusText: 'Gateway response lost' }); await firstTask;
    expect(component.pendingIntent()?.intent.requestKey).toBe(firstKey);
    await component.reviewManifest(component.archive()!);
    http.expectNone(url() + '/review');
    const secondTask = component.recoverManifest(component.archive()!);
    const second = http.expectOne(url() + '/review');
    expect(second.request.body.requestKey).toBe(firstKey);
    expect(second.request.body).toEqual(firstBody);
    second.flush({ value: { archiveId: id(1), manifestId: id(41), manifestVersion: 1, status: 'REVIEWED',
      digest: hash, entryCount: 205, completenessStatus: 'COMPLETE' } });
    await secondTask;
    http.expectOne(url()).flush(archive(1, copy(), { status: 'ASSEMBLY_REVIEWED', manifestStatus: 'REVIEWED' }));
    await fixture.whenStable();
    expect(component.archive()?.status).toBe('ASSEMBLY_REVIEWED');
  });
  it('does not expose or reuse another actor pending review after session replacement', async () => {
    component.refresh(); TestBed.tick();
    http.expectOne(url()).flush(archive(1, copy(), { status: 'MANIFEST_BUILT', canReview: true })); await fixture.whenStable();
    const firstTask = component.reviewManifest(component.archive()!);
    const first = http.expectOne(url() + '/review');
    const firstKey = first.request.body.requestKey;

    session.clear();
    session.current.set({ userId: id(92), firmId: id(91), generation: '2', staff: true });
    TestBed.tick();
    first.flush({ value: { archiveId: id(1), manifestId: id(41), manifestVersion: 1, status: 'REVIEWED',
      digest: hash, entryCount: 205, completenessStatus: 'COMPLETE' } });
    await firstTask;
    expect(component.archive()).toBeNull();

    http.expectOne(url()).flush(archive(1, copy(), { status: 'MANIFEST_BUILT', canReview: true })); await fixture.whenStable();
    const secondTask = component.reviewManifest(component.archive()!);
    const second = http.expectOne(url() + '/review');
    expect(second.request.body.requestKey).not.toBe(firstKey);
    second.flush({ value: { archiveId: id(1), manifestId: id(41), manifestVersion: 1, status: 'REVIEWED',
      digest: hash, entryCount: 205, completenessStatus: 'COMPLETE' } });
    await secondTask;
    http.expectOne(url()).flush(archive(1, copy(), { status: 'ASSEMBLY_REVIEWED', manifestStatus: 'REVIEWED' }));
    await fixture.whenStable();
    expect(component.archive()?.status).toBe('ASSEMBLY_REVIEWED');
  });
  it('requires a fresh read and click after a first-send refusal but retains the same refusal on recovery', async () => {
    component.refresh();
    TestBed.tick();
    http.expectOne(url()).flush(archive(1, copy(), { status: 'MANIFEST_BUILT', canReview: true }));
    await fixture.whenStable();
    const original = component.archive()!;
    const reviewUrl = url() + '/review';
    const invalidRequest = { code: 'records-manifest.request.invalid', message: 'The review request identity is invalid.' };
    const badRequest = { status: 400, statusText: 'Bad Request' };

    const first = component.reviewManifest(original);
    const firstRequest = http.expectOne(reviewUrl);
    const firstKey = firstRequest.request.body.requestKey;
    const storageKey = ownedRecoveryKeys[0]!;
    expect(component.pendingIntent()?.intent.requestKey).toBe(firstKey);
    firstRequest.flush(invalidRequest, badRequest);
    await first;
    expect(sessionStorage.getItem(storageKey)).toBeNull();
    expect(component.pendingIntent()).toBeNull();
    expect(component.archive()).toBeNull();
    await component.reviewManifest(original);
    http.expectNone(reviewUrl);

    TestBed.tick();
    const newDigest = 'f'.repeat(64);
    http.expectOne(url()).flush(archive(1, copy(), {
      status: 'MANIFEST_BUILT', canReview: true, manifestId: id(60), manifestVersion: 2, manifestDigest: newDigest,
    }));
    await fixture.whenStable();
    http.expectNone(reviewUrl);
    const reviewButton = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'))
      .find(button => button.textContent?.trim() === 'Review exact manifest');
    expect(reviewButton?.disabled).toBe(false);
    reviewButton!.click();
    const confirmed = http.expectOne(reviewUrl);
    const confirmedBody = confirmed.request.body;
    expect(confirmedBody).toMatchObject({ manifestId: id(60), manifestVersion: 2, manifestDigest: newDigest });
    expect(confirmedBody.requestKey).not.toBe(firstKey);
    confirmed.flush({}, { status: 502, statusText: 'Outcome unknown' });
    await fixture.whenStable();
    const pending = sessionStorage.getItem(storageKey);
    expect(pending).not.toBeNull();

    const recovery = component.recoverManifest(component.archive()!);
    const replay = http.expectOne(reviewUrl);
    expect(replay.request.body).toEqual(confirmedBody);
    replay.flush(invalidRequest, badRequest);
    await recovery;
    expect(sessionStorage.getItem(storageKey)).toBe(pending);
    expect(component.pendingIntent()?.intent.requestKey).toBe(confirmedBody.requestKey);
    expect(component.recoveryBlocked()).toBe(true);
    http.expectNone(reviewUrl);
  });
  it('refreshes instead of accepting a receipt for another manifest identity', async () => {
    component.refresh(); TestBed.tick();
    http.expectOne(url()).flush(archive(1, copy(), { status: 'MANIFEST_BUILT', canReview: true })); await fixture.whenStable();
    const task = component.reviewManifest(component.archive()!);
    const request = http.expectOne(url() + '/review');
    request.flush({ value: { archiveId: id(1), manifestId: id(42), manifestVersion: 1, status: 'REVIEWED',
      digest: hash, entryCount: 205, completenessStatus: 'COMPLETE' } });
    await task;
    http.expectOne(url()).flush(archive(1, copy(), { status: 'MANIFEST_BUILT', canReview: true }));
    await fixture.whenStable();
    expect(component.archive()?.status).toBe('MANIFEST_BUILT');
    expect(component.actionError()).toContain('did not confirm');
  });
  it('renders a separate historical SharePoint observation without implying current protection', async () => {
    component.refresh(); TestBed.tick(); http.expectOne(url()).flush(archive(1, copy('OBSERVED'))); await fixture.whenStable();
    const host: HTMLElement = fixture.nativeElement;
    expect(host.textContent).toContain('OBSERVED'); expect(host.textContent).toContain('Permission readback');
    expect(host.textContent).toContain('Historical readback only');
    expect(host.textContent).toContain('Not observed');
  });
  it('preserves loaded entries/cursor after transient failure and retries the same exact GET', async () => {
    const base = component.archive()!; const failed = component.loadMore(base);
    http.expectOne(url() + '?afterOrdinal=100').flush({}, { status: 503, statusText: 'Unavailable' }); await failed;
    expect(component.visibleEntries(base)).toHaveLength(100); expect(component.nextOrdinal()).toBe(100); expect(component.loadingEntries()).toBe(false); expect(component.entriesError()).toContain('Retry');
    const retry = component.loadMore(base); http.expectOne(url() + '?afterOrdinal=100').flush(page2()); await retry;
    expect(component.visibleEntries(base).map(e => e.ordinal)).toEqual(Array.from({ length: 200 }, (_, i) => i + 1)); expect(component.entriesError()).toBe('');
    const last = component.loadMore(base); http.expectOne(url() + '?afterOrdinal=200').flush(page3()); await last;
    expect(component.visibleEntries(base)).toHaveLength(205); expect(component.nextOrdinal()).toBeNull(); http.expectNone(r => r.method === 'POST');
  });
  it('blocks duplicate programmatic paging and rejects a stale base object', async () => {
    const base = component.archive()!, pending = component.loadMore(base); const request = http.expectOne(url() + '?afterOrdinal=100');
    await component.loadMore(base); await component.loadMore({ ...base }); http.expectNone(r => r.urlWithParams.includes('afterOrdinal'));
    request.flush(page2()); await pending; expect(component.visibleEntries(base)).toHaveLength(200);
  });
  for (const status of [401, 403, 404]) {
    it(`clears all protected paging content after ${status} and restores only after authorized refresh`, async () => {
      const base = component.archive()!, pending = component.loadMore(base);
      http.expectOne(url() + '?afterOrdinal=100').flush({}, { status, statusText: 'Denied' }); await pending;
      if (status === 401) { http.expectOne('/api/ui/session').flush({}, { status: 401, statusText: 'Ended' }); await Promise.resolve(); TestBed.tick(); }
      await fixture.whenStable(); expect(component.archive()).toBeNull(); expect(component.additionalEntries()).toEqual([]); expect(component.nextOrdinal()).toBeNull();
      expect(fixture.nativeElement.textContent).not.toContain('SYNTHETIC-ARCHIVE'); expect(fixture.nativeElement.textContent).not.toContain('evidence/item-1.pdf');
      await component.loadMore(base); http.expectNone(r => r.urlWithParams.includes('afterOrdinal'));
      if (status === 401) { session.current.set({ userId: id(90), firmId: id(91), generation: '2', staff: true }); TestBed.tick(); }
      else component.refresh();
      http.expectOne(url()).flush(archive()); await fixture.whenStable(); expect(component.archive()?.id).toBe(id(1));
    });
  }
  for (const fail of [false, true]) {
    it(`discards A-B-A late ${fail ? 'error' : 'success'} and does not clear the latest busy state in finally`, async () => {
      const base = component.archive()!, oldTask = component.loadMore(base), oldRequest = http.expectOne(url() + '?afterOrdinal=100');
      params.next(convertToParamMap({ id: id(2) })); TestBed.tick(); http.expectOne(url(2)).flush(archive(2)); TestBed.tick();
      params.next(convertToParamMap({ id: id(1) })); TestBed.tick(); http.expectOne(url()).flush(archive()); await fixture.whenStable();
      const current = component.archive()!, newTask = component.loadMore(current), newRequest = http.expectOne(url() + '?afterOrdinal=100');
      if (fail) oldRequest.flush({}, { status: 403, statusText: 'Old denied' }); else oldRequest.flush(page2()); await oldTask;
      expect(component.loadingEntries()).toBe(true); expect(component.entriesError()).toBe(''); expect(component.archive()).toBe(current); expect(component.additionalEntries()).toEqual([]);
      newRequest.flush(page2()); await newTask; expect(component.loadingEntries()).toBe(false); expect(component.visibleEntries(current)).toHaveLength(200);
    });
    it(`discards same-ID/same-manifest refresh late ${fail ? 'error' : 'success'}`, async () => {
      const task = component.loadMore(component.archive()!), request = http.expectOne(url() + '?afterOrdinal=100');
      component.refresh(); TestBed.tick(); http.expectOne(url()).flush(archive()); await fixture.whenStable();
      if (fail) request.flush({}, { status: 500, statusText: 'Old error' }); else request.flush(page2()); await task;
      expect(component.additionalEntries()).toEqual([]); expect(component.entriesError()).toBe(''); expect(component.nextOrdinal()).toBe(100);
    });
    it(`discards destroyed lifetime late ${fail ? 'error' : 'success'}`, async () => {
      const task = component.loadMore(component.archive()!), request = http.expectOne(url() + '?afterOrdinal=100'); fixture.destroy();
      if (fail) request.flush({}, { status: 403, statusText: 'Old denied' }); else request.flush(page2()); await task;
      expect(component.additionalEntries()).toEqual([]); expect(component.entriesError()).toBe(''); expect(component.loadingEntries()).toBe(false); http.expectNone(url());
    });
  }
  it('fences session invalidation while paging is pending', async () => {
    const task = component.loadMore(component.archive()!), request = http.expectOne(url() + '?afterOrdinal=100');
    session.invalidation.update(n => n + 1); TestBed.tick(); http.expectOne(url()).flush(archive()); await fixture.whenStable();
    request.flush(page2()); await task; expect(component.additionalEntries()).toEqual([]); expect(component.entriesError()).toBe('');
  });
  it('fences navigation away and back to the same archive', async () => {
    const router = TestBed.inject(Router);
    const task = component.loadMore(component.archive()!), request = http.expectOne(url() + '?afterOrdinal=100');
    await router.navigateByUrl('/app'); TestBed.tick(); expect(component.archive()).toBeNull();
    await router.navigateByUrl('/app/records/archives/' + id(1)); TestBed.tick(); http.expectOne(url()).flush(archive()); await fixture.whenStable();
    request.flush(page2()); await task; expect(component.additionalEntries()).toEqual([]);
  });
  it('rejects wrong returned root ID and supports explicit retry', async () => {
    component.refresh(); TestBed.tick(); http.expectOne(url()).flush(archive(2)); await fixture.whenStable(); expect(component.archive()).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('SYNTHETIC-ARCHIVE');
    component.refresh(); http.expectOne(url()).flush(archive()); await fixture.whenStable(); expect(component.archive()?.id).toBe(id(1));
  });
  const invalidPages = [
    ['returned ID', () => ({ ...page2(), id: id(2) })], ['engagement', () => ({ ...page2(), engagementId: id(22) })],
    ['manifest digest', () => ({ ...page2(), manifestDigest: 'f'.repeat(64) })], ['manifest version', () => ({ ...page2(), manifestVersion: 2 })],
    ['manifest status', () => ({ ...page2(), manifestStatus: 'REVIEWED' })], ['total', () => ({ ...page2(), totalEntryCount: 206 })],
    ['duplicate ordinal', () => ({ ...page2(), entries: [entry(101), entry(101), ...page2().entries.slice(2)] })],
    ['old ordinal', () => ({ ...page2(), entries: [entry(100), ...page2().entries.slice(1)] })],
    ['out of order', () => ({ ...page2(), entries: [entry(102), entry(101), ...page2().entries.slice(2)] })],
    ['non-progressing cursor', () => ({ ...page2(), nextOrdinal: 100 })], ['cursor not last row', () => ({ ...page2(), nextOrdinal: 199 })],
    ['premature terminal cursor', () => ({ ...page2(), nextOrdinal: null })], ['empty progressing page', () => ({ ...page2(), entries: [] })],
    ['short page', () => ({ ...page2(), entries: page2().entries.slice(0, 99) })], ['negative bytes', () => ({ ...page2(), entries: [{ ...entry(101), byteCount: -1 }, ...page2().entries.slice(1)] })],
  ] as const;
  for (const [label, payload] of invalidPages) it(`rejects ${label} without appending rows or advancing the cursor`, async () => {
    const base = component.archive()!, task = component.loadMore(base); http.expectOne(url() + '?afterOrdinal=100').flush(payload()); await task;
    expect(component.visibleEntries(base)).toHaveLength(100); expect(component.nextOrdinal()).toBe(100); expect(component.entriesError()).toContain('Refresh'); expect(component.loadingEntries()).toBe(false);
  });
});
