// Synthetic finding contracts shared only by specs.
import { FindingFields, decodeFindingFields } from './finding-command-contracts';
export const findingTestId = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
export const findingTestBasis = 'b'.repeat(64), findingTestHash = 'a'.repeat(64);
export const findingTestSnapshot = (n = 1) => ({ findingId: findingTestId(n), clientId: findingTestId(21), engagementId: findingTestId(20), revision: '1', createdByUserId: findingTestId(89),
  findingType: 'Synthetic deficiency', impactDescription: 'Synthetic impact', monetaryAmount: '10.000000', corrected: true, managementResponse: `Response ${n}`, status: 'CORRECTED',
  letterDesignatedAt: null as string | null, letterDesignatedByUserId: null as string | null, letterRecommendation: null as string | null, createdAt: '2026-10-16T00:00:00Z' });
export const findingTestState = (n = 1) => ({ finding: findingTestSnapshot(n), reviewBasis: findingTestBasis, canRecordResponse: true, canDesignate: true, responseBlockers: [], designationBlockers: [] });
export const findingTestPreview = (request: { requestId: string; fields: unknown }, n = 1) => ({ requestId: request.requestId, requestHash: findingTestHash, expectedRevision: '1', reviewBasis: findingTestBasis,
  canonicalInput: decodeFindingFields(request.fields, ''), before: findingTestSnapshot(n), nextRevision: '2', canProceed: true, blockers: [], summary: 'Exact synthetic amendment' });
export const findingTestReceipt = (p: ReturnType<typeof findingTestPreview>, actorId = findingTestId(90), actorEpoch = '1') => {
  const fields: FindingFields = p.canonicalInput;
  const after = { ...p.before, revision: '2', ...(fields.response ? { managementResponse: fields.response.managementResponse, corrected: fields.response.corrected,
    status: fields.response.corrected ? 'CORRECTED' : 'EVALUATED' } : fields.designation ? { letterRecommendation: fields.designation.recommendation,
    letterDesignatedAt: '2026-10-16T01:00:00Z', letterDesignatedByUserId: actorId } : { letterRecommendation: null, letterDesignatedAt: null, letterDesignatedByUserId: null }) };
  return { id: findingTestId(40), findingId: p.before.findingId, actorId, actorEpoch, requestId: p.requestId, requestHash: p.requestHash, reviewBasis: p.reviewBasis,
    action: fields.action, createdAt: '2026-10-16T01:00:00Z', preview: p,
    amendment: { id: findingTestId(41), actorId, actorEpoch, action: fields.action, createdAt: '2026-10-16T01:00:00Z', before: p.before, after } };
};
