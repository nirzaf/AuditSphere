import { describe, expect, it } from 'vitest';
import {
  CANONICAL_STAGES,
  decodeEngagementLifecycleReport,
} from './engagement-lifecycle-contracts';

const id = '11111111-2222-3333-4444-555555555555';
const clientId = '66666666-7777-8888-9999-000000000000';

describe('Engagement lifecycle projection contracts (AS-COMP-02)', () => {
  it('defines the canonical eleven-state progression in sequential order', () => {
    expect(CANONICAL_STAGES).toHaveLength(11);
    expect(CANONICAL_STAGES.map((s) => s.index)).toEqual([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]);
    expect(CANONICAL_STAGES[0].key).toBe('LEAD_INGESTION');
    expect(CANONICAL_STAGES[4].key).toBe('PORTAL_ACTIVE_PLANNING');
    expect(CANONICAL_STAGES[9].key).toBe('COMPLIANCE_COUNTDOWN');
    expect(CANONICAL_STAGES[10].key).toBe('ARCHIVED_READ_ONLY');
  });

  it('decodes a valid lifecycle projection report truthfully', () => {
    const raw = {
      engagementId: id,
      clientId,
      clientName: 'Al-Rayyan Trading Co.',
      serviceRoute: 'Audit',
      summary: {
        canonicalStage: 'FIELDWORK_EXECUTION',
        stageLabel: 'Fieldwork Execution',
        stageIndex: 6,
        blockedReasons: [
          '2 substantive audit procedure(s) pending execution',
          'Open hold: Client pending PBC bank confirmation',
        ],
        responsibleRole: 'SeniorAuditor',
        deepLink: `/app/engagements/${id}/audit-fieldwork`,
        complianceCountdownDays: null,
        isArchived: false,
        isLegacyUnverified: false,
      },
      evaluatedAtUtc: '2026-10-06T12:00:00Z',
    };

    const decoded = decodeEngagementLifecycleReport(raw);
    expect(decoded.engagementId).toBe(id);
    expect(decoded.clientId).toBe(clientId);
    expect(decoded.clientName).toBe('Al-Rayyan Trading Co.');
    expect(decoded.summary.stageIndex).toBe(6);
    expect(decoded.summary.canonicalStage).toBe('FIELDWORK_EXECUTION');
    expect(decoded.summary.responsibleRole).toBe('SeniorAuditor');
    expect(decoded.summary.blockedReasons).toHaveLength(2);
    expect(decoded.summary.complianceCountdownDays).toBeNull();
    expect(decoded.summary.isArchived).toBe(false);
  });

  it('decodes compliance countdown state with remaining days', () => {
    const raw = {
      engagementId: id,
      clientId,
      clientName: 'Doha Construction LLC',
      serviceRoute: 'Audit',
      summary: {
        canonicalStage: 'COMPLIANCE_COUNTDOWN',
        stageLabel: 'Compliance Countdown',
        stageIndex: 10,
        blockedReasons: [],
        responsibleRole: 'EngagementPartner',
        deepLink: `/app/engagements/${id}/completion`,
        complianceCountdownDays: 45,
        isArchived: false,
        isLegacyUnverified: false,
      },
      evaluatedAtUtc: '2026-10-06T12:00:00Z',
    };

    const decoded = decodeEngagementLifecycleReport(raw);
    expect(decoded.summary.stageIndex).toBe(10);
    expect(decoded.summary.complianceCountdownDays).toBe(45);
    expect(decoded.summary.isArchived).toBe(false);
  });

  it('fails closed when stage index is out of bounds', () => {
    const rawInvalidStage = {
      engagementId: id,
      clientId,
      clientName: 'Invalid Stage Entity',
      serviceRoute: 'Audit',
      summary: {
        canonicalStage: 'UNKNOWN',
        stageLabel: 'Unknown',
        stageIndex: 12,
        blockedReasons: [],
        responsibleRole: 'None',
        deepLink: '/app',
        complianceCountdownDays: null,
        isArchived: false,
        isLegacyUnverified: false,
      },
      evaluatedAtUtc: '2026-10-06T12:00:00Z',
    };

    expect(() => decodeEngagementLifecycleReport(rawInvalidStage)).toThrow(
      'Invalid lifecycle stage index bounds',
    );
  });

  it('fails closed when identifiers or timestamps are invalid', () => {
    const malformed = {
      engagementId: 'invalid-guid',
      clientId,
      clientName: 'Entity',
      serviceRoute: 'Audit',
      summary: {
        canonicalStage: 'LEAD_INGESTION',
        stageLabel: 'Lead Ingestion',
        stageIndex: 1,
        blockedReasons: [],
        responsibleRole: 'CommercialManager',
        deepLink: '/app',
        complianceCountdownDays: null,
        isArchived: false,
        isLegacyUnverified: false,
      },
      evaluatedAtUtc: 'not-a-timestamp',
    };

    expect(() => decodeEngagementLifecycleReport(malformed)).toThrow();
  });
});
