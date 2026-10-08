import { describe, expect, it } from 'vitest';
import { decodeAssessment } from './checklist';
const clientId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const decisionId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
const checklist = {
  clientId,
  generation: '2',
  path: 'NEW_CLIENT',
  currentDecision: null,
  priorDecision: null,
  ready: false,
  canEdit: false,
  canReview: false,
  canDecide: false,
  canStartContinuance: false,
  questions: [],
  clearances: [],
  blockers: [],
};
const decision = {
  id: decisionId,
  generation: '1',
  decision: 'Declined',
  serviceRoute: 'AccountingOnly',
  rationale: 'Exact historical record',
  conditions: null,
  path: 'NEW_CLIENT',
  decidedBy: 'Partner',
  decidedAt: '2026-10-03T10:00:00Z',
};
const context = {
  checklist,
  client: {
    id: clientId,
    legalName: 'Synthetic client',
    registrationNumber: null,
    status: 'PROSPECT',
  },
  selectedDecision: decision,
  historical: true,
  repository: null,
  answered: 0,
  total: 0,
  clearedReviews: 0,
  totalReviews: 0,
  sections: [],
  specialistTimeline: [],
};
describe('Assessment read projection', () => {
  it('retains the exact historical professional record', () => {
    const result = decodeAssessment(context);
    expect(result.selectedDecision?.id).toBe(decisionId);
    expect(result.selectedDecision?.rationale).toBe('Exact historical record');
    expect(result.checklist.generation).toBe('2');
    expect(result.historical).toBe(true);
  });
  it('rejects a different client identity', () => {
    expect(() =>
      decodeAssessment({ ...context, client: { ...context.client, id: decisionId } }),
    ).toThrow();
  });
  it('rejects fabricated completion', () => {
    expect(() => decodeAssessment({ ...context, answered: 1 })).toThrow();
    expect(() => decodeAssessment({ ...context, totalReviews: 1 })).toThrow();
  });
  it('requires read-only historical state', () => {
    expect(() =>
      decodeAssessment({ ...context, checklist: { ...checklist, canEdit: true } }),
    ).toThrow();
  });
  it('refuses misleading historical flags and unsafe recorded generations', () => {
    expect(() => decodeAssessment({ ...context, historical: false })).toThrow();
    expect(() =>
      decodeAssessment({
        ...context,
        selectedDecision: { ...decision, generation: '9223372036854775808' },
      }),
    ).toThrow();
  });
  it('requires an engagement-specific selection to remain read-only even at the current generation', () => {
    const selectedDecision = { ...decision, engagementId: decisionId, generation: '2' };
    expect(
      decodeAssessment({ ...context, selectedDecision, historical: false }).selectedDecision
        ?.engagementId,
    ).toBe(decisionId);
    expect(() =>
      decodeAssessment({
        ...context,
        selectedDecision,
        historical: false,
        checklist: { ...checklist, canEdit: true },
      }),
    ).toThrow();
  });
  it('does not expose provider locations from an unsupported reply', () => {
    const result = decodeAssessment({
      ...context,
      repository: {
        logicalKey: 'client-workspace',
        state: 'WAITING_FOR_INTEGRATION',
        lastVerifiedAt: null,
        downloadUrl: 'unsupported',
      },
    });
    expect(result.repository).not.toHaveProperty('downloadUrl');
  });
  it('accepts an ordered specialist event history and rejects an unsupported timeline action', () => {
    const history = [
      {
        id: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
        reviewId: 'dddddddd-dddd-dddd-dddd-dddddddddddd',
        action: 'Review requested',
        area: 'AML',
        specialist: 'Synthetic specialist',
        status: 'PENDING',
        evidence: null,
        conditions: null,
        actor: 'Synthetic staff',
        occurredAt: '2026-10-03T10:00:00Z',
      },
      {
        id: 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee',
        reviewId: 'dddddddd-dddd-dddd-dddd-dddddddddddd',
        action: 'Result recorded',
        area: 'AML',
        specialist: 'Synthetic specialist',
        status: 'HOLD',
        evidence: 'SYNTHETIC-EVIDENCE',
        conditions: null,
        actor: 'Synthetic partner',
        occurredAt: '2026-10-03T10:01:00Z',
      },
    ];
    expect(decodeAssessment({ ...context, selectedDecision: null, historical: false, specialistTimeline: history })
      .specialistTimeline).toHaveLength(2);
    expect(() => decodeAssessment({ ...context, selectedDecision: null, historical: false,
      specialistTimeline: [{ ...history[0], action: 'Directory mutation' }] })).toThrow();
  });
});
