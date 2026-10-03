import { describe, expect, it } from 'vitest';
import {
  decodeScopeWorkspace,
  decodeConsolidationJournal,
  decodeConsolidationComponent,
  decodeScopeSummary,
} from './scope-contracts';

const id1 = '11111111-1111-4111-8111-111111111111';
const id2 = '22222222-2222-4222-8222-222222222222';
const id3 = '33333333-3333-4333-8333-333333333333';
const hash64 = 'a'.repeat(64);

describe('Consolidation Scope Workspace Contracts', () => {
  it('decodes a complete scope workspace structure', () => {
    const raw = {
      scope: {
        id: id1,
        groupId: id2,
        groupName: 'Acme Holdings Group',
        groupCode: 'ACME-GRP',
        version: 1,
        groupRevision: 1,
        periodId: id3,
        reportingCurrency: 'QAR',
        method: 'PROPORTIONATE',
        status: 'Draft',
        openingBasis: 'OPENING_BALANCE',
        isAdvanced: false,
      },
      canPrepare: true,
      canReview: true,
      currentUserId: id1,
      components: [
        {
          id: id2,
          clientId: id3,
          clientLegalName: 'Acme Subsidiary QSTP-LLC',
          sourceType: 'Package',
          packageId: id1,
          externalComponentPackId: null,
          packageHash: hash64,
          periodBasis: '2026-01-01..2026-12-31',
          taxonomyVersion: 'IFRS-2026',
          mappingVersion: '1',
          currency: 'QAR',
          ownershipPercent: '100',
          controlMethod: 'CONTROLLED',
          status: 'SUBMITTED',
          submittedByUserId: id2,
          submittedAt: '2026-10-01T10:00:00Z',
          approvedByUserId: null,
          approvedAt: null,
        },
      ],
      eligiblePackages: [
        {
          packageId: id1,
          clientId: id3,
          clientLegalName: 'Acme Subsidiary QSTP-LLC',
          engagementId: id2,
          periodBasis: '2026-01-01..2026-12-31',
          taxonomyVersion: 'IFRS-2026',
          mappingVersion: '1',
          currency: 'QAR',
          calculationHash: hash64,
        },
      ],
      journals: [
        {
          id: id1,
          journalNumber: 'EJ-001',
          journalType: 'INTERCOMPANY_ELIMINATION',
          currency: 'QAR',
          totalDebits: '1500.00',
          totalCreditsAbs: '1500.00',
          evidenceReference: 'IC-RECON-01',
          status: 'Submitted',
          returnReason: null,
          createdByUserId: id2,
          createdAt: '2026-10-01T11:00:00Z',
          approvedByUserId: null,
          approvedAt: null,
          lines: [
            {
              id: id1,
              taxonomyCode: 'BS-100',
              debit: '1500.00',
              credit: '0.00',
              description: 'Eliminate IC AR',
              intercompanyMatchId: null,
            },
            {
              id: id2,
              taxonomyCode: 'BS-200',
              debit: '0.00',
              credit: '1500.00',
              description: 'Eliminate IC AP',
              intercompanyMatchId: null,
            },
          ],
        },
      ],
      runs: [
        {
          id: id1,
          engineVersion: '1.0',
          runHash: hash64,
          signedTotal: '0.00',
          status: 'VERIFIED',
          createdByUserId: id2,
          createdAt: '2026-10-01T12:00:00Z',
          approvedByUserId: null,
          approvedAt: null,
        },
      ],
      latestReport: {
        scopeVersionId: id1,
        runId: id1,
        state: 'CURRENT_APPROVED',
        approvedAt: '2026-10-01T12:30:00Z',
        lines: [
          {
            component: 'Sub 1',
            taxonomyCode: 'BS-100',
            componentAmount: '1500.00',
            alignmentAmount: '0.00',
            eliminationAmount: '-1500.00',
            consolidatedAmount: '0.00',
            currency: 'QAR',
          },
        ],
      },
      componentReadiness: [
        {
          clientId: id3,
          memberName: 'Acme Subsidiary QSTP-LLC',
          componentState: 'READY',
          componentId: id2,
          currency: 'QAR',
          periodBasis: '2026-01-01..2026-12-31',
          taxonomyVersion: 'IFRS-2026',
          mappingVersion: '1',
          currencyCompatible: true,
          mismatchReason: null,
        },
      ],
      intercompanyExceptions: [],
    };

    const decoded = decodeScopeWorkspace(raw, 'root');
    expect(decoded.scope.groupCode).toBe('ACME-GRP');
    expect(decoded.components.length).toBe(1);
    expect(decoded.components[0].clientLegalName).toBe('Acme Subsidiary QSTP-LLC');
    expect(decoded.eligiblePackages.length).toBe(1);
    expect(decoded.journals.length).toBe(1);
    expect(decoded.journals[0].lines.length).toBe(2);
    expect(decoded.journals[0].totalDebits).toBe('1500.00');
    expect(decoded.runs.length).toBe(1);
    expect(decoded.latestReport?.lines[0].consolidatedAmount).toBe('0.00');
  });

  it('rejects invalid or missing identifiers and malformed decoders', () => {
    expect(() =>
      decodeScopeSummary(
        {
          id: 'bad-uuid',
          groupId: id2,
          groupName: 'Group',
          groupCode: 'GRP',
          version: 1,
          groupRevision: 1,
          periodId: id3,
          reportingCurrency: 'QAR',
          method: 'METHOD',
          status: 'Draft',
          openingBasis: 'BASIS',
          isAdvanced: false,
        },
        'root',
      ),
    ).toThrow();

    expect(() =>
      decodeConsolidationJournal(
        {
          id: id1,
          journalNumber: 'EJ-001',
          journalType: 'IC',
          currency: 'QAR',
          totalDebits: 'not-a-decimal',
          totalCreditsAbs: '1500.00',
          evidenceReference: 'REF',
          status: 'Submitted',
          returnReason: null,
          createdByUserId: id2,
          createdAt: '2026-10-01T11:00:00Z',
          approvedByUserId: null,
          approvedAt: null,
          lines: [],
        },
        'root',
      ),
    ).toThrow();
  });
});
