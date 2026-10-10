import { afterEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';
import { signal } from '@angular/core';
import { Api } from '../../core/api';
import {
  ArchiveRecord,
  decodePopulation,
  decodeFinding,
  decodeReviewPoint,
  decodeRelease,
  decodeArchive,
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

  it('preserves loaded archive entries and retries a failed next-page read', async () => {
    const archive = decodeArchive({
      id: id1, engagementId: id2, status: 'SEALED', createdAt: '2026-10-02T18:00:00Z',
      profileId: 'ISQM1-PROFILE', profileVersion: 1, manifestVersion: 1, manifestStatus: 'REVIEWED', manifestDigest: hash64,
      completenessStatus: 'COMPLETE', completenessException: null,
      entries: [{ ordinal: 1, entryKind: 'DOCUMENT', relativeName: 'first.pdf', contentHash: hash64, byteCount: 1 }],
      totalEntryCount: 2, nextOrdinal: 2, activeHoldCount: 0, observedProtection: null,
      desiredLabel: null, observedLabel: null, actionState: null, externalReference: null,
    }, 'archive');
    const secondPage = { ...archive, entries: [{ ordinal: 2, entryKind: 'DOCUMENT', relativeName: 'second.pdf', contentHash: hash64, byteCount: 2 }], totalEntryCount: 2, nextOrdinal: null };
    const api = {
      resource: () => ({ data: () => archive, error: () => '', loading: () => false, reload: () => undefined }),
      get: vi.fn().mockRejectedValueOnce(new Error('Temporarily unavailable. Retry shortly.')).mockResolvedValueOnce(secondPage),
    };
    TestBed.configureTestingModule({ providers: [
      { provide: Api, useValue: api },
      { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ id: id1 })) } },
    ] });

    const component = TestBed.runInInjectionContext(() => new ArchiveRecord());
    expect(component.id()).toBe(id1);
    component.nextOrdinal.set(2);
    await component.loadMore(archive);
    expect(component.visibleEntries(archive).map(entry => entry.ordinal)).toEqual([1]);
    expect(component.entriesError()).toBe('Temporarily unavailable. Retry shortly.');
    expect(component.nextOrdinal()).toBe(2);

    await component.loadMore(archive);
    expect(component.visibleEntries(archive).map(entry => entry.ordinal)).toEqual([1, 2]);
    expect(component.entriesError()).toBe('');
    expect(component.nextOrdinal()).toBeNull();
    expect(api.get).toHaveBeenCalledTimes(2);
  });
});
