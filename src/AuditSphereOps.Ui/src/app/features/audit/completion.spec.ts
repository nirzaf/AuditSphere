import { describe, expect, it } from 'vitest';
import { decodeCompletion } from './completion';

const id1 = '11111111-1111-4111-8111-111111111111';
const id2 = '22222222-2222-4222-8222-222222222222';
const id3 = '33333333-3333-4333-8333-333333333333';
const hash64 = 'f'.repeat(64);

describe('Audit Completion Contracts', () => {
  it('decodes a valid completion checklist payload', () => {
    const raw = {
      engagementId: id1,
      gates: [
        {
          name: 'Partner Review',
          status: 'CLEARED',
          tone: 'POSITIVE',
          authority: 'Lead Engagement Partner',
          date: '2026-10-02T10:00:00Z',
        },
      ],
      representations: [
        {
          code: 'REP-01',
          title: 'Management Responsibilities',
          narrative: 'Management acknowledges its responsibility for the financial statements.',
          obtained: true,
        },
      ],
      packageId: id2,
      packageStatus: 'VALIDATED',
      partnerApproved: true,
      eqrStatus: 'CONCURRED',
      releaseCandidateId: id3,
      canPrepareRelease: true,
      confirmations: [
        {
          caseId: id1,
          type: 'BANK',
          respondent: 'Qatar National Bank',
          bookedAmount: '5000000.00',
          currency: 'QAR',
          status: 'CONFIRMED',
          monitoring: 'ON_TRACK',
          daysSinceDispatch: 5,
          critical: true,
          criticalityRationale: 'Primary operational bank account',
        },
      ],
      deliverables: [
        {
          id: id2,
          kind: 'INDEPENDENT_AUDITORS_REPORT',
          title: "Independent Auditor's Report",
          version: 1,
          signed: true,
          current: true,
          contentSha256: hash64,
          createdAt: '2026-10-02T11:00:00Z',
        },
      ],
      clearance: {
        clearedAt: '2026-10-02T12:00:00Z',
      },
      opinion: {
        opinionType: 'UNMODIFIED',
        label: 'Clean (unmodified)',
        focusArea: null,
      },
      opinionAreas: [
        {
          id: id3,
          code: 'GEN',
          name: 'General Audit',
        },
      ],
      shared: [
        {
          title: 'Closing Meeting Memo',
          openComments: [],
        },
      ],
      signedLetters: [
        {
          id: id1,
          deliverableId: id2,
          version: 1,
          managementSignatory: 'Chief Financial Officer',
          contentSha256: hash64,
          uploadedAt: '2026-10-02T11:30:00Z',
          verified: true,
          current: true,
        },
      ],
      bundles: {
        blockers: [],
        bundles: [
          {
            id: id3,
            sha256: hash64,
            assembledAt: '2026-10-02T12:30:00Z',
          },
        ],
      },
      opinionTypes: ['UNMODIFIED', 'QUALIFIED', 'ADVERSE', 'DISCLAIMER'],
      freeze: {
        state: 'FROZEN',
        reportSignedAt: '2026-10-02T12:00:00Z',
        dueAt: '2026-12-01T12:00:00Z',
        externalReadOnly: 'LOCKED',
        daysRemaining: 60,
        amendments: [],
      },
      freezeDays: 60,
      trail: [
        {
          at: '2026-10-02T12:00:00Z',
          kind: 'OPINION_SIGNED',
          actor: 'Lead Partner',
          description: 'Signed audit opinion',
          entityId: id1,
        },
      ],
      trailCoverageNote: 'Complete audit event trail',
      locks: [
        {
          id: id2,
          documentKey: 'AUDIT_PACKAGE_FINAL',
          lockedBy: 'Lead Partner',
          lockedAt: '2026-10-02T12:05:00Z',
        },
      ],
    };

    const decoded = decodeCompletion(raw, 'completion');
    expect(decoded.engagementId).toBe(id1);
    expect(decoded.gates.length).toBe(1);
    expect(decoded.gates[0].name).toBe('Partner Review');
    expect(decoded.partnerApproved).toBe(true);
    expect(decoded.opinion?.opinionType).toBe('UNMODIFIED');
    expect(decoded.freeze?.state).toBe('FROZEN');
    expect(decoded.locks.length).toBe(1);
  });

  it('rejects invalid completion payloads', () => {
    expect(() => decodeCompletion(null, 'completion')).toThrow();
    expect(() => decodeCompletion({}, 'completion')).toThrow();
  });
});
