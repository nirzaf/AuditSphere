import { PendingTasks, signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { Api } from '../../core/api';
import { SessionService } from '../../core/session';
import { archiveIntentKey } from './archive-intent-recovery';
import { decode as decodeValue } from '../../core/decode';
import {
  decodePopulation,
  decodeFinding,
  decodeReviewPoint,
  decodeRelease,
  decodeArchive,
  decodeArchiveCommercialBundlePage,
  decodeArchiveCopyEvidence,
  decodeArchiveCopyVerificationPlan,
  ArchiveRecord,
  ReleaseCandidate,
} from './records';

const id1 = '11111111-1111-4111-8111-111111111111';
const id2 = '22222222-2222-4222-8222-222222222222';
const id3 = '33333333-3333-4333-8333-333333333333';
const id4 = '44444444-4444-4444-8444-444444444444';
const hash64 = 'e'.repeat(64);

describe('Audit Records Contracts', () => {
  afterEach(() => TestBed.resetTestingModule());
  it('decodes a valid population payload', () => {
    const raw = {
      id: id1,
      engagementId: id2,
      purpose: 'Accounts Payable Testing',
      assertion: 'Completeness',
      sourceReceiptReference: 'AP-LIST-2026',
      extractionParameters: 'Balances > 10,000 QAR',
      rowCount: 320,
      monetaryControlTotal: '4500000.00',
      currency: 'QAR',
      exclusions: 'Intercompany vendors',
      status: 'EXTRACTED',
      createdAt: '2026-10-01T10:00:00Z',
      selections: [
        {
          method: 'MUS',
          status: 'COMPLETED',
          rationale: 'High coverage testing',
          itemCount: 25,
          testCount: 25,
          reviewedTestCount: 25,
        },
      ],
      evidence: [
        {
          purpose: 'Vendor confirmation',
          assertion: 'Existence',
          receiptToken: 'RCP-001',
        },
      ],
    };

    const decoded = decodePopulation(raw, 'population');
    expect(decoded.id).toBe(id1);
    expect(decoded.rowCount).toBe(320);
    expect(decoded.selections[0].method).toBe('MUS');
  });

  it('decodes a valid finding payload', () => {
    const raw = {
      id: id1,
      engagementId: id2,
      findingType: 'CONTROL_DEFICIENCY',
      impactDescription: 'Lack of segregation of duties in purchase authorization.',
      monetaryAmount: null,
      corrected: false,
      managementResponse: 'Management is hiring an additional finance officer.',
      revision: '1', canRecordResponse: true, canDesignate: false,
      status: 'REPORTED',
      createdAt: '2026-10-01T11:00:00Z',
      professionalWorkBlocked: false,
    };

    const decoded = decodeFinding(raw, 'finding');
    expect(decoded.id).toBe(id1);
    expect(decoded.findingType).toBe('CONTROL_DEFICIENCY');
    expect(decoded.managementResponse).toContain('hiring an additional');
  });

  it('decodes a valid review point payload', () => {
    const raw = {
      id: id1,
      engagementId: id2,
      targetKind: 'WORKPAPER',
      targetId: id3,
      targetRevision: 2,
      raisedByUserId: id1,
      raisedAt: '2026-10-01T14:00:00Z',
      significant: true,
      cleared: false,
      comment: 'Please expand on cut-off procedure sample sizing.',
      dispositionRevision: '1', canChangeDisposition: true,
      status: 'OPEN',
    };

    const decoded = decodeReviewPoint(raw, 'reviewPoint');
    expect(decoded.id).toBe(id1);
    expect(decoded.significant).toBe(true);
    expect(decoded.cleared).toBe(false);
  });

  it('decodes a valid release candidate payload', () => {
    const raw = {
      id: id1,
      status: 'PENDING_PREFLIGHT',
      targetKind: 'FINANCIAL_PACKAGE',
      targetRevision: 1,
      revision: 1,
      manifestDigest: hash64,
      createdAt: '2026-10-02T16:00:00Z',
      checkpoint: { state: 'VERIFIED', detail: 'Independent partner review verified.' },
      attestation: { state: 'VERIFIED', detail: 'Signed engagement representations in place.' },
      lineage: { state: 'VERIFIED', detail: 'Lineage traced to sealed sources.' },
      preflightReady: true,
    };

    const decoded = decodeRelease(raw, 'release');
    expect(decoded.id).toBe(id1);
    expect(decoded.preflightReady).toBe(true);
    expect(decoded.checkpoint.state).toBe('VERIFIED');
  });

  it('decodes a valid archive payload', () => {
    const raw = {
      id: id1,
      engagementId: id2,
      status: 'SEALED',
      createdAt: '2026-10-02T18:00:00Z',
      profileId: 'ISQM1-PROFILE',
      profileVersion: 1,
      manifestVersion: 1,
      manifestId: id2,
      manifestStatus: 'REVIEWED',
      manifestDigest: hash64,
      completenessStatus: 'COMPLETE',
      canExport: true,
      canBuild: false,
      completenessException: null,
      entries: [
        {
          ordinal: 1,
          entryKind: 'SIGNED_REPORT',
          relativeName: 'AuditReport2026.pdf',
          contentHash: hash64,
          byteCount: 1048576,
        },
      ],
      totalEntryCount: 1,
      nextOrdinal: null,
      activeHoldCount: 0,
      observedProtection: 'PROTECTED',
      desiredLabel: 'CONFIDENTIAL',
      observedLabel: 'CONFIDENTIAL',
      actionState: 'COMPLETED',
      externalReference: 'PURVIEW-REC-99',
      canReview: false,
    };

    const decoded = decodeArchive(raw, 'archive');
    expect(decoded.id).toBe(id1);
    expect(decoded.status).toBe('SEALED');
    expect(decoded.entries.length).toBe(1);
    expect(decoded.entries[0].byteCount).toBe(1048576);
  });


});

describe('Archive intent recovery', () => {
  const scope = { firmId: id3, actorUserId: id1, engagementId: id2, archiveId: id1 };
  const key = archiveIntentKey(scope);
  afterEach(() => { vi.restoreAllMocks(); sessionStorage.removeItem(key); TestBed.resetTestingModule(); });

  async function setup(review = false, reviewNullBundle = true) {
    const data = signal<ReturnType<typeof decodeArchive> | null>(null);
    const resource = { data, loading: signal(false), error: signal(''), reload: vi.fn() };
    const bundleData = signal<ReturnType<typeof decodeArchiveCommercialBundlePage> | null>(null);
    const bundleResource = { data: bundleData, loading: signal(false), error: signal(''), reload: vi.fn() };
    let decode!: typeof decodeArchive;
    let nextResourceIsBundle = false;
    const session = { current: signal({ userId: id1, firmId: id3, generation: '1', staff: true }), invalidation: signal(0), refresh: vi.fn() };
    const params = new BehaviorSubject(convertToParamMap({ id: id1 }));
    let copyReadPayload: unknown = null;
    const api = { get: vi.fn(async (_url: string, decoder: typeof decodeArchiveCopyEvidence | typeof decodeArchiveCopyVerificationPlan) =>
      decoder === decodeArchiveCopyEvidence ? decodeValue(decodeArchiveCopyEvidence, copyReadPayload) : decodeValue(decodeArchiveCopyVerificationPlan, copyReadPayload)),
      resource: (_url: unknown, decoder: typeof decodeArchive) => {
        if (nextResourceIsBundle) { nextResourceIsBundle = false; return bundleResource; }
        nextResourceIsBundle = true; decode = decoder; return resource;
      } };
    TestBed.configureTestingModule({ imports: [ArchiveRecord], providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
      { provide: ActivatedRoute, useValue: { paramMap: params } }, { provide: SessionService, useValue: session },
      { provide: Api, useValue: api }] });
    let fixture = TestBed.createComponent(ArchiveRecord);
    const raw = { id: id1, engagementId: id2, status: review ? 'MANIFEST_BUILT' : 'ISSUED', createdAt: '2026-11-01T10:00:00Z',
      profileId: 'ISQM1-PROFILE', profileVersion: 1, manifestVersion: review ? 1 : null, manifestId: review ? id2 : null,
      manifestStatus: review ? 'BUILT' : null, manifestDigest: review ? hash64 : null, completenessStatus: review ? 'COMPLETE' : null,
      completenessException: null, canExport: false, canReview: review, canBuild: !review, entries: [], totalEntryCount: 0, nextOrdinal: null,
      activeHoldCount: 0, observedProtection: null, desiredLabel: null, observedLabel: null, actionState: null, externalReference: null, secondarySharePointCopy: null };
    const read = async (changes: Record<string, unknown> = {}) => {
      Object.assign(raw, changes);
      data.set(decode(raw, 'archive'));
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    };
    await read();
    bundleData.set({ archiveId: id1, engagementId: id2, page: 0, hasMore: false, bundles: [
      { id: id3, contentSha256: hash64, manifestSha256: 'a'.repeat(64), assembledAt: '2026-11-26T10:00:00Z' },
      { id: id4, contentSha256: 'b'.repeat(64), manifestSha256: 'c'.repeat(64), assembledAt: '2026-11-25T10:00:00Z' },
    ] });
    if (reviewNullBundle) {
      fixture.componentInstance.selectBundle('');
      fixture.componentInstance.reviewBundleSelection();
    }
    return { get component() { return fixture.componentInstance; }, get fixture() { return fixture; }, read, resource, bundleResource, session, params,
      http: TestBed.inject(HttpTestingController), api, setCopyReadPayload: (value: unknown) => { copyReadPayload = value; },
      recreate: async () => { fixture.destroy(); fixture = TestBed.createComponent(ArchiveRecord); await read(); } };
  }
  const receipt = (action: string, completenessStatus = 'COMPLETE') => ({ value: {
    archiveId: id1, manifestId: id2, manifestVersion: 1, digest: hash64, entryCount: 0,
    status: action === 'build' ? 'BUILT' : 'REVIEWED', completenessStatus } });

  it.each(['build', 'review'] as const)('retains original %s across response loss, recreation and session replacement', async action => {
    const h = await setup(action === 'review');
    const pending = action === 'build' ? h.component.buildManifest(h.component.archive()!) : h.component.reviewManifest(h.component.archive()!);
    const first = h.http.expectOne(`/api/ui/records/archives/${id1}/${action}`), body = first.request.body;
    const stored = sessionStorage.getItem(key);
    expect(stored).toContain(body.requestKey);
    first.error(new ProgressEvent('error')); await pending;
    await h.recreate();
    h.session.current.set({ ...h.session.current(), generation: '2' }); h.session.invalidation.set(1);
    h.fixture.detectChanges();
    expect(h.component.archive()).toBeNull(); expect(h.component.pendingIntent()).toBeNull();
    await h.read(action === 'review' ? { status: 'ASSEMBLY_REVIEWED', manifestStatus: 'REVIEWED', canReview: false } : {});
    expect(h.component.pendingIntent()?.raw).toBe(stored);
    await h.component.buildManifest(h.component.archive()!); h.http.expectNone(`/api/ui/records/archives/${id1}/build`);
    const retry = h.component.recoverManifest(h.component.archive()!);
    const request = h.http.expectOne(`/api/ui/records/archives/${id1}/${action}`);
    expect(request.request.body).toEqual(body);
    request.flush(receipt(action, action === 'build' ? 'INCOMPLETE' : 'COMPLETE')); await retry;
    expect(sessionStorage.getItem(key)).toBeNull(); expect(h.resource.reload).toHaveBeenCalled();
    expect(h.component.archive()).toBeNull(); h.http.verify();
  });

  it('requires a deliberate bundle decision and clears review when the selection changes', async () => {
    const h = await setup(false, false);
    h.component.reviewBundleSelection();
    expect(h.component.bundleSelectionReviewed()).toBe(false);
    expect(h.component.bundleSelectionError()).toContain('Select a bundle');
    h.component.selectBundle(id3);
    h.component.reviewBundleSelection();
    expect(h.component.bundleSelectionReviewed()).toBe(true);
    h.component.selectBundle(id4);
    expect(h.component.bundleSelectionReviewed()).toBe(false);
    h.component.reviewBundleSelection();
    expect(h.component.bundleSelectionReviewed()).toBe(true);
    h.component.selectBundle('');
    expect(h.component.selectedBundleId()).toBeNull();
    expect(h.component.bundleSelectionReviewed()).toBe(false);
  });

  it('retains the selected bundle ID and request key across response loss and sends the original selection on recovery', async () => {
    const h = await setup();
    h.component.selectBundle(id3);
    h.component.reviewBundleSelection();
    const firstAttempt = h.component.buildManifest(h.component.archive()!);
    const first = h.http.expectOne(`/api/ui/records/archives/${id1}/build`), body = first.request.body;
    expect(body).toEqual({ commercialBundleId: id3, requestKey: expect.any(String) });
    expect(JSON.parse(sessionStorage.getItem(key)!).commercialBundleId).toBe(id3);
    first.error(new ProgressEvent('error')); await firstAttempt;
    await h.recreate();
    expect(h.component.pendingIntent()?.intent.commercialBundleId).toBe(id3);
    const retry = h.component.recoverManifest(h.component.archive()!);
    const request = h.http.expectOne(`/api/ui/records/archives/${id1}/build`);
    expect(request.request.body).toEqual(body);
    request.flush(receipt('build', 'INCOMPLETE')); await retry;
    expect(sessionStorage.getItem(key)).toBeNull();
    h.http.verify();
  });

  it('reads only the exact secondary-copy operation evidence and verification plan', async () => {
    const h = await setup();
    const copy = { state: 'REQUESTED', operationId: id4, targetRevision: 2, manifestVersion: 1,
      manifestSha256: hash64, latestFileReadBackAt: null, permissionReadBackAt: null, note: 'Pending exact readback.' };
    await h.read({ secondarySharePointCopy: copy });
    h.setCopyReadPayload({ operationId: id4, firmId: id3, clientId: id4, engagementId: id2, operationStatus: 'PENDING',
      operationResultDigest: null, result: null, fileReceipts: [], permissionObservations: [] });
    await h.component.readCopyEvidence(h.component.archive()!);
    expect(h.api.get).toHaveBeenLastCalledWith(`/api/ui/records/archive-copies/${id4}/evidence`, decodeArchiveCopyEvidence);
    expect(h.component.copyEvidence()?.operationId).toBe(id4);

    h.setCopyReadPayload({ schemaVersion: 1, operationId: id4, firmId: id3, tenantId: id3, clientId: id4, engagementId: id2,
      archiveId: id1, manifestId: id2, manifestVersion: 1, manifestSha256: hash64, recordsProfileId: id3,
      recordsProfileVersion: 1, releaseId: id4, releasedArtifactId: id1, checkpointId: id2, clientSharePointSiteId: null,
      targetRevisionId: id3, targetRevision: 2, targetConfigurationSha256: hash64, authorizedStaffSha256: hash64,
      destinationTenantId: id3, destinationSiteId: 'site-id', destinationDriveId: 'drive-id',
      destinationLibraryRootItemId: 'root-id', staffGroupId: 'group-id', expectedPermissionsSha256: hash64, files: [] });
    await h.component.readCopyVerificationPlan(h.component.archive()!);
    expect(h.api.get).toHaveBeenLastCalledWith(`/api/ui/records/archive-copies/${id4}/verification-plan`, decodeArchiveCopyVerificationPlan);
    expect(h.component.copyPlan()?.archiveId).toBe(id1);
    h.http.verify();
  });

  it('rejects secondary-copy readback for a different operation', async () => {
    const h = await setup();
    await h.read({ secondarySharePointCopy: { state: 'REQUESTED', operationId: id4, targetRevision: 2, manifestVersion: 1,
      manifestSha256: hash64, latestFileReadBackAt: null, permissionReadBackAt: null, note: 'Pending exact readback.' } });
    h.setCopyReadPayload({ operationId: id3, firmId: id3, clientId: id4, engagementId: id2,
      operationStatus: 'PENDING', operationResultDigest: null, result: null, fileReceipts: [], permissionObservations: [] });
    await h.component.readCopyEvidence(h.component.archive()!);
    expect(h.component.copyEvidence()).toBeNull();
    expect(h.component.copyReadError()).toContain('does not match');
    h.http.verify();
  });

  it('preserves a newer raw reference when an older valid response arrives', async () => {
    const h = await setup(); const pending = h.component.buildManifest(h.component.archive()!);
    const request = h.http.expectOne(`/api/ui/records/archives/${id1}/build`);
    const replacement = JSON.stringify({ ...JSON.parse(sessionStorage.getItem(key)!), requestKey: id3 });
    sessionStorage.setItem(key, replacement); request.flush(receipt('build')); await pending;
    expect(sessionStorage.getItem(key)).toBe(replacement); expect(h.resource.reload).not.toHaveBeenCalled();
    expect(h.component.actionError()).toContain('changed');
  });

  it.each(['actor', 'scope', 'session'] as const)('fences a late response after %s changes', async change => {
    const h = await setup(); const pending = h.component.buildManifest(h.component.archive()!);
    const request = h.http.expectOne(`/api/ui/records/archives/${id1}/build`), stored = sessionStorage.getItem(key);
    if (change === 'actor') h.session.current.set({ ...h.session.current(), userId: id2 });
    else if (change === 'scope') h.params.next(convertToParamMap({ id: id3 }));
    else h.session.invalidation.set(1);
    // Scope invalidation must reach the view while unrelated work is still pending.
    const completeOtherWork = TestBed.inject(PendingTasks).add();
    try {
      h.fixture.detectChanges();
      expect(h.component.pendingIntent()).toBeNull();
      request.flush(receipt('build'));
      await pending;
    } finally {
      completeOtherWork();
    }
    expect(sessionStorage.getItem(key)).toBe(stored); expect(h.resource.reload).not.toHaveBeenCalled();
    expect(h.component.pendingIntent()).toBeNull(); expect(h.component.actionError()).toBe('');
  });

  it('retries the original review basis after a newer manifest and rejects a newer receipt', async () => {
    const h = await setup(true); const pending = h.component.reviewManifest(h.component.archive()!);
    const first = h.http.expectOne(`/api/ui/records/archives/${id1}/review`), body = first.request.body;
    first.error(new ProgressEvent('error')); await pending;
    await h.read({ manifestId: id3, manifestVersion: 2 });
    const retry = h.component.recoverManifest(h.component.archive()!);
    const request = h.http.expectOne(`/api/ui/records/archives/${id1}/review`); expect(request.request.body).toEqual(body);
    request.flush({ value: { ...receipt('review').value, manifestId: id3, manifestVersion: 2 } }); await retry;
    expect(sessionStorage.getItem(key)).not.toBeNull(); expect(h.component.actionError()).toContain('did not confirm');
  });

  it.each(['getItem', 'setItem'] as const)('sends nothing when storage %s fails', async method => {
    const h = await setup(); vi.spyOn(Storage.prototype, method).mockImplementation(() => { throw new Error('Storage unavailable'); });
    await h.component.buildManifest(h.component.archive()!);
    h.http.expectNone(`/api/ui/records/archives/${id1}/build`); expect(h.component.recoveryUnavailable()).toBe(true);
  });

  it('fails closed on an invalid retained reference', async () => {
    sessionStorage.setItem(key, '{invalid'); const h = await setup();
    await h.component.buildManifest(h.component.archive()!); h.http.expectNone(`/api/ui/records/archives/${id1}/build`);
    expect(sessionStorage.getItem(key)).toBe('{invalid'); expect(h.component.recoveryUnavailable()).toBe(true);
  });

  it.each([403, 409])('preserves uncertainty for HTTP %s', async status => {
    const h = await setup(); const pending = h.component.buildManifest(h.component.archive()!);
    h.http.expectOne(`/api/ui/records/archives/${id1}/build`).flush({ code: 'request.failed' }, { status, statusText: 'Refused' }); await pending;
    expect(sessionStorage.getItem(key)).not.toBeNull(); expect(h.component.recoveryBlocked()).toBe(true);
  });

  it('resolves only a first-send explicit validation refusal and requires a fresh read', async () => {
    const h = await setup(); const pending = h.component.buildManifest(h.component.archive()!);
    h.http.expectOne(`/api/ui/records/archives/${id1}/build`).flush({ code: 'records-manifest.request.invalid' }, { status: 400, statusText: 'Invalid' }); await pending;
    expect(sessionStorage.getItem(key)).toBeNull(); expect(h.component.archive()).toBeNull();
    await h.read(); const next = h.component.buildManifest(h.component.archive()!);
    h.http.expectOne(`/api/ui/records/archives/${id1}/build`).error(new ProgressEvent('error')); await next;
    const stored = sessionStorage.getItem(key), retry = h.component.recoverManifest(h.component.archive()!);
    h.http.expectOne(`/api/ui/records/archives/${id1}/build`).flush({ code: 'records-manifest.request.invalid' }, { status: 400, statusText: 'Invalid' }); await retry;
    expect(sessionStorage.getItem(key)).toBe(stored);
  });
});

describe('Archive export visibility', () => {
  afterEach(() => TestBed.resetTestingModule());

  it.each([
    { canExport: false, label: 'metadata-only reader', linkVisible: false },
    { canExport: true, label: 'authorized custodian', linkVisible: true },
  ])('shows export only for the server-authorized $label', async ({ canExport, linkVisible }) => {
    const archiveData = signal<ReturnType<typeof decodeArchive> | null>(null);
    const resource = { data: archiveData, loading: signal(false), error: signal(''), reload: vi.fn() };
    const bundleResource = { data: signal<ReturnType<typeof decodeArchiveCommercialBundlePage> | null>(null), loading: signal(false), error: signal(''), reload: vi.fn() };
    let decode!: (raw: unknown, path: string) => ReturnType<typeof decodeArchive>;
    let nextResourceIsBundle = false;
    const api = { resource: vi.fn((_url: unknown, decoder: typeof decode) => {
      if (nextResourceIsBundle) { nextResourceIsBundle = false; return bundleResource; }
      nextResourceIsBundle = true; decode = decoder; return resource;
    }) };
    const session = {
      current: signal({ userId: id1, firmId: id3, generation: '1', staff: true }),
      invalidation: signal(0), refresh: vi.fn(),
    };
    const params = new BehaviorSubject(convertToParamMap({ id: id1 }));
    TestBed.configureTestingModule({
      imports: [ArchiveRecord],
      providers: [provideRouter([]), provideHttpClient(),
        { provide: ActivatedRoute, useValue: { paramMap: params } },
        { provide: Api, useValue: api },
        { provide: SessionService, useValue: session }],
    });

    const fixture = TestBed.createComponent(ArchiveRecord);
    archiveData.set(decode({
      id: id1, engagementId: id2, status: 'SEALED', createdAt: '2026-11-01T10:00:00Z',
      profileId: 'ISQM1-PROFILE', profileVersion: 1, manifestVersion: 1, manifestId: id2, manifestStatus: 'REVIEWED',
      manifestDigest: hash64, completenessStatus: 'COMPLETE', completenessException: null, canExport,
      entries: [{ ordinal: 1, entryKind: 'SIGNED_REPORT', relativeName: 'Report.pdf', contentHash: hash64, byteCount: 42 }],
      totalEntryCount: 1, nextOrdinal: null, activeHoldCount: 0, observedProtection: 'PROTECTED',
      desiredLabel: 'CONFIDENTIAL', observedLabel: 'CONFIDENTIAL', actionState: 'OBSERVED',
      externalReference: null, secondarySharePointCopy: null, canReview: false, canBuild: false,
    }, 'archive'));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    const exportLink = page.querySelector<HTMLAnchorElement>(`a[href="/api/ui/records/archives/${id1}/export"]`);
    expect(exportLink !== null).toBe(linkVisible);
    expect(page.textContent).toContain('Manifest version 1');
    expect(page.textContent).toContain('Manifest hash');
    if (linkVisible) expect(exportLink?.textContent).toContain('Download read-only evidence export');
  });
});

describe('Release checkpoint UI handoff', () => {
  afterEach(() => { TestBed.resetTestingModule(); vi.restoreAllMocks(); });

  function openRelease() {
    const data = signal<ReturnType<typeof decodeRelease> | null>(null);
    const resource = { data, loading: signal(false), error: signal(''), reload: vi.fn() };
    const api = { resource: vi.fn(() => resource), command: vi.fn().mockResolvedValue({ ok: true, value: id2 }) };
    const session = { current: signal({ userId: id3, firmId: id4, generation: '1', staff: true }), invalidation: signal(0), refresh: vi.fn() };
    const params = new BehaviorSubject(convertToParamMap({ id: id1 }));
    TestBed.configureTestingModule({
      imports: [ReleaseCandidate],
      providers: [provideRouter([]), provideHttpClient(),
        { provide: ActivatedRoute, useValue: { paramMap: params } },
        { provide: Api, useValue: api }, { provide: SessionService, useValue: session }],
    });
    const fixture = TestBed.createComponent(ReleaseCandidate);
    data.set(decodeRelease({ id: id1, status: 'READY', targetKind: 'FINANCIAL_PACKAGE', targetRevision: 1, revision: 4,
      manifestDigest: hash64, createdAt: '2026-11-01T10:00:00Z', checkpoint: { state: 'ABSENT', detail: 'Not recorded' },
      attestation: { state: 'ABSENT', detail: 'Not required' }, lineage: { state: 'ABSENT', detail: 'Optional' }, preflightReady: false }, 'release'));
    fixture.detectChanges();
    return { fixture, data, api, params, session, component: fixture.componentInstance };
  }

  async function manifestFile() {
    const bytes = new TextEncoder().encode('manifest');
    const digest = Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', bytes)), x => x.toString(16).padStart(2, '0')).join('');
    return { file: new File([bytes], 'release-manifest.json', { type: 'application/json' }), digest };
  }

  it('sends the exact reviewed manifest and one-time key only to the existing checkpoint endpoint', async () => {
    const { fixture, component, api, data } = openRelease();
    const { file, digest } = await manifestFile();
    data.set({ ...component.rec.data()!, manifestDigest: digest });
    await component.selectCheckpointManifest({ target: { files: { item: () => file } } } as unknown as Event);
    component.checkpointKey = 'synthetic-authorized-key';
    component.checkpointReviewed.set(true);
    const candidate = component.rec.data()!;
    expect(component.canRequestCheckpoint(candidate)).toBe(true);

    await component.requestCheckpoint(candidate);

    expect(api.command).toHaveBeenCalledTimes(1);
    expect(api.command).toHaveBeenCalledWith('/api/ui/releases/' + id1 + '/checkpoint', {
      expectedCandidateRevision: 4, authorizedReleaseKey: 'synthetic-authorized-key', manifestDigest: candidate.manifestDigest,
      manifestBytesBase64: 'bWFuaWZlc3Q=',
    }, 'POST');
    expect(component.checkpointKey).toBe('');
    expect(component.checkpointBytes()).toBeNull();
    expect(component.cmd.message()).toContain('queued');
    expect(component.cmd.message()).not.toContain('verified checkpoint');
    fixture.destroy();
  });

  it('clears manifest, key and assent after route or session identity changes', async () => {
    const { fixture, component, params, session, data } = openRelease();
    const { file, digest } = await manifestFile();
    data.set({ ...component.rec.data()!, manifestDigest: digest });
    await component.selectCheckpointManifest({ target: { files: { item: () => file } } } as unknown as Event);
    component.checkpointKey = 'synthetic-authorized-key';
    component.checkpointReviewed.set(true);
    expect(component.checkpointBytes()).not.toBeNull();

    params.next(convertToParamMap({ id: id2 }));
    TestBed.tick(); fixture.detectChanges();
    expect(component.checkpointBytes()).toBeNull();
    expect(component.checkpointKey).toBe('');
    expect(component.checkpointReviewed()).toBe(false);

    const { file: nextFile } = await manifestFile();
    params.next(convertToParamMap({ id: id1 }));
    TestBed.tick(); fixture.detectChanges();
    data.set({ ...component.rec.data()!, manifestDigest: digest });
    await component.selectCheckpointManifest({ target: { files: { item: () => nextFile } } } as unknown as Event);
    component.checkpointKey = 'second-synthetic-key';
    component.checkpointReviewed.set(true);
    session.invalidation.set(1); fixture.detectChanges();
    expect(component.checkpointBytes()).toBeNull();
    expect(component.checkpointKey).toBe('');
    expect(component.checkpointReviewed()).toBe(false);
    fixture.destroy();
  });

  it('refuses a manifest that does not match the current candidate digest', async () => {
    const { fixture, component, api } = openRelease();
    const file = new File(['different manifest'], 'release-manifest.json', { type: 'application/json' });
    await component.selectCheckpointManifest({ target: { files: { item: () => file } } } as unknown as Event);
    component.checkpointKey = 'synthetic-authorized-key'; component.checkpointReviewed.set(true);
    expect(component.checkpointManifestReady()).toBe(false);
    expect(component.canRequestCheckpoint(component.rec.data()!)).toBe(false);
    expect(api.command).not.toHaveBeenCalled();
    fixture.destroy();
  });
});
