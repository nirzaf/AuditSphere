import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { Api } from '../../core/api';
import { SessionService } from '../../core/session';
import { archiveIntentKey } from './archive-intent-recovery';
import {
  decodePopulation,
  decodeFinding,
  decodeReviewPoint,
  decodeRelease,
  decodeArchive,
  ArchiveRecord,
} from './records';

const id1 = '11111111-1111-4111-8111-111111111111';
const id2 = '22222222-2222-4222-8222-222222222222';
const id3 = '33333333-3333-4333-8333-333333333333';
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

  async function setup(review = false) {
    const data = signal<ReturnType<typeof decodeArchive> | null>(null);
    const resource = { data, loading: signal(false), error: signal(''), reload: vi.fn() };
    let decode!: typeof decodeArchive;
    const session = { current: signal({ userId: id1, firmId: id3, generation: '1', staff: true }), invalidation: signal(0), refresh: vi.fn() };
    const params = new BehaviorSubject(convertToParamMap({ id: id1 }));
    TestBed.configureTestingModule({ imports: [ArchiveRecord], providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
      { provide: ActivatedRoute, useValue: { paramMap: params } }, { provide: SessionService, useValue: session },
      { provide: Api, useValue: { resource: (_url: unknown, decoder: typeof decodeArchive) => { decode = decoder; return resource; } } }] });
    let fixture = TestBed.createComponent(ArchiveRecord);
    const raw = { id: id1, engagementId: id2, status: review ? 'MANIFEST_BUILT' : 'ISSUED', createdAt: '2026-11-01T10:00:00Z',
      profileId: 'ISQM1-PROFILE', profileVersion: 1, manifestVersion: review ? 1 : null, manifestId: review ? id2 : null,
      manifestStatus: review ? 'BUILT' : null, manifestDigest: review ? hash64 : null, completenessStatus: review ? 'COMPLETE' : null,
      completenessException: null, canExport: false, canReview: review, entries: [], totalEntryCount: 0, nextOrdinal: null,
      activeHoldCount: 0, observedProtection: null, desiredLabel: null, observedLabel: null, actionState: null, externalReference: null, secondarySharePointCopy: null };
    const read = async (changes: Record<string, unknown> = {}) => { Object.assign(raw, changes); data.set(decode(raw, 'archive')); await fixture.whenStable(); };
    await read();
    return { get component() { return fixture.componentInstance; }, get fixture() { return fixture; }, read, resource, session, params,
      http: TestBed.inject(HttpTestingController), recreate: async () => { fixture.destroy(); fixture = TestBed.createComponent(ArchiveRecord); await read(); } };
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
    await h.fixture.whenStable();
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
    await h.fixture.whenStable(); request.flush(receipt('build')); await pending;
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
    let decode!: (raw: unknown, path: string) => ReturnType<typeof decodeArchive>;
    const api = { resource: vi.fn((_url: unknown, decoder: typeof decode) => { decode = decoder; return resource; }) };
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
      externalReference: null, secondarySharePointCopy: null, canReview: false,
    }, 'archive'));
    await fixture.whenStable();

    const page = fixture.nativeElement as HTMLElement;
    const exportLink = page.querySelector<HTMLAnchorElement>(`a[href="/api/ui/records/archives/${id1}/export"]`);
    expect(exportLink !== null).toBe(linkVisible);
    expect(page.textContent).toContain('Manifest version 1');
    expect(page.textContent).toContain('Manifest hash');
    if (linkVisible) expect(exportLink?.textContent).toContain('Download read-only evidence export');
  });
});
