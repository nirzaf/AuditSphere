import { Decoder, arr, bool, dec, guid, instant, int, nat, nullable, obj, oneOf, sha256, str } from '../../core/decode';

export const planningAction = oneOf('CALCULATE_MATERIALITY', 'ROUND_MATERIALITY', 'APPROVE_MATERIALITY', 'CREATE_RISK', 'ASSESS_RISK', 'ASSIGN_RISK_OWNER', 'CLEAR_RISK', 'LINK_PROCEDURE_RISK');
export type PlanningAction = ReturnType<typeof planningAction>;
const score: Decoder<number> = (raw, path) => { const n = int(raw, path); if (n < 1 || n > 3) throw new Error(`${path}: expected score 1 to 3`); return n; };
const exactEpoch: Decoder<string> = (raw, path) => { if (typeof raw !== 'string' || !/^(0|[1-9][0-9]{0,18})$/.test(raw)) throw new Error(`${path}: expected exact epoch`); return raw; };
const fieldsShape = obj({ action: planningAction,
  calculate: nullable(obj({ benchmarkKind: str(50), destinationCode: nullable(str(100)), ratePercent: dec, performancePercent: dec, trivialPercent: dec, rationale: str(4000), qualitativeConsiderations: nullable(str(4000)) })),
  round: nullable(obj({ assessmentId: guid, planningMateriality: dec, tolerableError: dec, sadThreshold: dec, rationale: str(4000) })),
  approve: nullable(obj({ assessmentId: guid })),
  createRisk: nullable(obj({ area: str(200), assertion: str(100), description: str(4000), drivers: str(4000), significanceDecision: oneOf('NORMAL', 'SIGNIFICANT'), response: str(4000), controlsConsidered: nullable(str(4000)) })),
  assessRisk: nullable(obj({ riskId: guid, likelihood: score, magnitude: score, fraud: bool, criticalEstimate: bool, rationale: str(4000) })),
  assignRiskOwner: nullable(obj({ riskId: guid, expectedAssessmentId: guid, ownerUserId: guid })),
  clearRisk: nullable(obj({ riskId: guid, expectedAssessmentId: guid, note: str(4000) })),
  linkProcedureRisk: nullable(obj({ procedureId: guid, riskId: nullable(guid), expectedRiskId: nullable(guid) })) });
export const planningPayloadKey = { CALCULATE_MATERIALITY: 'calculate', ROUND_MATERIALITY: 'round', APPROVE_MATERIALITY: 'approve', CREATE_RISK: 'createRisk', ASSESS_RISK: 'assessRisk', ASSIGN_RISK_OWNER: 'assignRiskOwner', CLEAR_RISK: 'clearRisk', LINK_PROCEDURE_RISK: 'linkProcedureRisk' } as const;
export const decodePlanningFields: Decoder<ReturnType<typeof fieldsShape>> = (raw, path) => {
  const fields = fieldsShape(raw, path), key = planningPayloadKey[fields.action];
  if (!fields[key] || Object.entries(fields).filter(([k, v]) => k !== 'action' && v !== null).length !== 1) throw new Error(`${path}: expected exactly the selected action payload`);
  return fields;
};
export type PlanningFields = ReturnType<typeof decodePlanningFields>;
export const decodePlanningMateriality = obj({ assessmentId: guid, calculationId: nullable(guid), roundingDecisionId: nullable(guid), preparedByUserId: guid,
  state: oneOf('DRAFT', 'APPROVED', 'STALE'), benchmarkKind: str(100), destinationCode: nullable(str(100)), currency: str(3), benchmarkAmount: dec, ratePercent: dec, performancePercent: dec, trivialPercent: dec,
  computedPlanningMateriality: dec, computedTolerableError: dec, computedSadThreshold: dec, planningMateriality: dec, tolerableError: dec, sadThreshold: dec,
  rationale: str(4000), qualitativeConsiderations: nullable(str(4000)), roundingRationale: nullable(str(4000)), roundedByUserId: nullable(guid), mappingId: nullable(guid), mappingVersion: nullable(exactEpoch), datasetId: nullable(guid),
  datasetDigest: nullable(sha256), policyVersion: str(100), approvedByUserId: nullable(guid), approvedAt: nullable(instant) });
export const decodePlanningRisk = obj({ riskId: guid, area: str(200), assertion: str(100), description: str(4000), drivers: str(4000), significanceDecision: oneOf('NORMAL', 'SIGNIFICANT'), response: str(4000), controlsConsidered: nullable(str(4000)),
  assessmentId: nullable(guid), likelihood: nullable(score), magnitude: nullable(score), fraud: bool, criticalEstimate: bool, band: nullable(oneOf('GREEN', 'AMBER', 'RED')), assessmentRationale: nullable(str(4000)), assessedByUserId: nullable(guid),
  clearanceId: nullable(guid), clearedByUserId: nullable(guid), clearanceNote: nullable(str(4000)), ownerAssignmentId: nullable(guid), ownerUserId: nullable(guid), ownerStaffingLevel: nullable(str(100)) });
export const decodePlanningProcedure = obj({ procedureId: guid, riskId: nullable(guid), status: str(100), resultRevision: exactEpoch });
export const decodePlanningState = obj({ engagementId: guid, clientId: guid, action: planningAction, reviewBasis: sha256, canProceed: bool, blockers: arr(str(2000), 100),
  source: nullable(obj({ mappingId: guid, mappingVersion: exactEpoch, datasetId: guid, datasetDigest: sha256, currency: str(3), benchmarks: arr(obj({ kind: str(50), destinationCode: nullable(str(100)), label: str(200), amount: nullable(dec), lineCount: nat }), 500) })),
  materiality: nullable(decodePlanningMateriality), risk: nullable(decodePlanningRisk), procedure: nullable(decodePlanningProcedure), owners: arr(obj({ userId: guid, name: str(300), staffingLevel: str(100) }), 500) });
const previewShape = obj({ requestId: guid, requestHash: sha256, reviewBasis: sha256, canonicalInput: decodePlanningFields, state: decodePlanningState, summary: str(8000),
  materialityOutcome: nullable(obj({ benchmarkAmount: dec, planningMateriality: dec, tolerableError: dec, sadThreshold: dec, currency: str(3) })), riskBandOutcome: nullable(oneOf('GREEN', 'AMBER', 'RED')) });
export const decodePlanningPreview: Decoder<ReturnType<typeof previewShape>> = (raw, path) => {
  const p = previewShape(raw, path), f = p.canonicalInput, s = p.state;
  if (s.action !== f.action || p.reviewBasis !== s.reviewBasis ||
    (f.round && s.materiality?.assessmentId !== f.round.assessmentId) || (f.approve && s.materiality?.assessmentId !== f.approve.assessmentId) ||
    (f.assessRisk && s.risk?.riskId !== f.assessRisk.riskId) ||
    (f.assignRiskOwner && (s.risk?.riskId !== f.assignRiskOwner.riskId || s.risk?.assessmentId !== f.assignRiskOwner.expectedAssessmentId)) ||
    (f.clearRisk && (s.risk?.riskId !== f.clearRisk.riskId || s.risk?.assessmentId !== f.clearRisk.expectedAssessmentId)) ||
    (f.linkProcedureRisk && (s.procedure?.procedureId !== f.linkProcedureRisk.procedureId || s.procedure?.riskId !== f.linkProcedureRisk.expectedRiskId || (f.linkProcedureRisk.riskId !== null && s.risk?.riskId !== f.linkProcedureRisk.riskId)))) throw new Error(`${path}: preview identity mismatch`);
  return p;
};
const receiptShape = obj({ id: guid, actorId: guid, actorEpoch: exactEpoch, engagementId: guid, clientId: guid, requestId: guid, requestHash: sha256, reviewBasis: sha256, action: planningAction, createdAt: instant,
  preview: decodePlanningPreview, result: obj({ resultId: guid, materiality: nullable(decodePlanningMateriality), risk: nullable(decodePlanningRisk), procedure: nullable(decodePlanningProcedure), summary: str(8000) }) });
export const decodePlanningReceipt: Decoder<ReturnType<typeof receiptShape>> = (raw, path) => {
  const r = receiptShape(raw, path), p = r.preview;
  if (r.requestId !== p.requestId || r.requestHash !== p.requestHash || r.reviewBasis !== p.reviewBasis || r.action !== p.state.action || r.engagementId !== p.state.engagementId || r.clientId !== p.state.clientId) throw new Error(`${path}: receipt identity mismatch`);
  const f = p.canonicalInput, result = r.result;
  if (f.calculate && (!result.materiality || result.materiality.assessmentId !== result.resultId) ||
    f.round && (!result.materiality || result.materiality.assessmentId !== result.resultId || !result.materiality.roundingDecisionId) ||
    f.approve && (result.resultId !== f.approve.assessmentId || result.materiality?.assessmentId !== f.approve.assessmentId || result.materiality?.state !== 'APPROVED' || result.materiality?.approvedByUserId !== r.actorId) ||
    f.createRisk && result.risk?.riskId !== result.resultId ||
    f.assessRisk && (result.risk?.riskId !== f.assessRisk.riskId || result.risk?.assessmentId !== result.resultId) ||
    f.assignRiskOwner && (result.risk?.riskId !== f.assignRiskOwner.riskId || result.risk?.assessmentId !== f.assignRiskOwner.expectedAssessmentId || result.risk?.ownerUserId !== f.assignRiskOwner.ownerUserId || result.risk?.ownerAssignmentId !== result.resultId) ||
    f.clearRisk && (result.risk?.riskId !== f.clearRisk.riskId || result.risk?.assessmentId !== f.clearRisk.expectedAssessmentId || result.risk?.clearanceId !== result.resultId || result.risk?.clearedByUserId !== r.actorId) ||
    f.linkProcedureRisk && (result.procedure?.procedureId !== f.linkProcedureRisk.procedureId || result.resultId !== f.linkProcedureRisk.procedureId || result.procedure?.riskId !== f.linkProcedureRisk.riskId)) throw new Error(`${path}: receipt result differs from reviewed target`);
  return r;
};
export const decodePlanningLookup = obj({ found: bool, receipt: nullable(decodePlanningReceipt) });
export type PlanningPreview = ReturnType<typeof decodePlanningPreview>;
export type PlanningReceipt = ReturnType<typeof decodePlanningReceipt>;
/** Canonical exact decimal scale is semantic, never compared through Number. */
export function samePlanningFields(a: PlanningFields, b: PlanningFields): boolean {
  const normalize = (fields: PlanningFields) => {
    const decimal = (s: string) => { const negative = s.startsWith('-'); const [i, f = ''] = (negative ? s.slice(1) : s).split('.'); const whole = i.replace(/^0+(?=\d)/, ''), tail = f.replace(/0+$/, ''); return (negative && (whole !== '0' || tail) ? '-' : '') + whole + (tail ? '.' + tail : ''); };
    return { ...fields, calculate: fields.calculate ? { ...fields.calculate, ratePercent: decimal(fields.calculate.ratePercent), performancePercent: decimal(fields.calculate.performancePercent), trivialPercent: decimal(fields.calculate.trivialPercent) } : null,
      round: fields.round ? { ...fields.round, planningMateriality: decimal(fields.round.planningMateriality), tolerableError: decimal(fields.round.tolerableError), sadThreshold: decimal(fields.round.sadThreshold) } : null };
  };
  return JSON.stringify(normalize(a)) === JSON.stringify(normalize(b));
}
