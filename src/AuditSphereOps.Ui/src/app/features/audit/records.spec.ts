import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { Api } from '../../core/api';
import { SessionService } from '../../core/session';
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
    };

    const decoded = decodeArchive(raw, 'archive');
    expect(decoded.id).toBe(id1);
    expect(decoded.status).toBe('SEALED');
    expect(decoded.entries.length).toBe(1);
    expect(decoded.entries[0].byteCount).toBe(1048576);
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
      profileId: 'ISQM1-PROFILE', profileVersion: 1, manifestVersion: 1, manifestStatus: 'REVIEWED',
      manifestDigest: hash64, completenessStatus: 'COMPLETE', completenessException: null, canExport,
      entries: [{ ordinal: 1, entryKind: 'SIGNED_REPORT', relativeName: 'Report.pdf', contentHash: hash64, byteCount: 42 }],
      totalEntryCount: 1, nextOrdinal: null, activeHoldCount: 0, observedProtection: 'PROTECTED',
      desiredLabel: 'CONFIDENTIAL', observedLabel: 'CONFIDENTIAL', actionState: 'OBSERVED',
      externalReference: null, secondarySharePointCopy: null,
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
