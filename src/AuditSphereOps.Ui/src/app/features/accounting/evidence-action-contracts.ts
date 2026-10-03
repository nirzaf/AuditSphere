import { arr, bool, guid, instant, nat, nullable, obj, oneOf, sha256, str } from '../../core/decode';

export const evidenceKind = oneOf('ECL', 'INVENTORY', 'SPECIALIST', 'ANALYTICAL', 'JOURNAL_RISK');
const action = oneOf('LINK', 'REVIEW');
const decision = oneOf('APPROVED', 'CHANGES_REQUIRED', 'REJECTED', 'ESCALATED');
const receipt = obj({ id: guid, requestId: guid, requestHash: sha256, kind: evidenceKind, evidenceId: guid,
  action, actorId: guid, resultId: nullable(guid), linkId: nullable(guid), decision: str(30), reason: str(4000),
  evidenceReference: str(2000), createdAt: instant });
export function decodeEvidenceReceipt(raw: unknown, path = 'receipt') {
  const r = receipt(raw, path);
  if (!r.reason.trim() || !r.evidenceReference.trim() || (r.action === 'LINK' ? !r.resultId || !r.linkId || r.decision !== '' :
    r.resultId !== null || r.linkId !== null || !['APPROVED', 'CHANGES_REQUIRED', 'REJECTED', 'ESCALATED'].includes(r.decision)))
    throw new Error('Unsupported evidence action receipt');
  return r;
}
const state = obj({ kind: evidenceKind, evidenceId: guid, reviewBasis: sha256, canLink: bool, canReview: bool,
  decisions: arr(decision, 3), blockers: arr(str(2000), 30), page: nat, count: nat, hasMore: bool, history: arr(decodeEvidenceReceipt, 25) });
export function decodeEvidenceActions(raw: unknown, path = 'response') {
  const r = state(raw, path);
  if (r.count > 1000 || r.page >= 40 || r.hasMore !== (r.count > (r.page + 1) * 25) ||
    r.history.length !== Math.min(25, Math.max(0, r.count - r.page * 25)) ||
    new Set(r.history.map(x => x.id)).size !== r.history.length || new Set(r.decisions).size !== r.decisions.length ||
    (r.canReview !== (r.decisions.length > 0)) ||
    (r.kind === 'JOURNAL_RISK' ? r.decisions.some(x => x !== 'ESCALATED') : r.decisions.includes('ESCALATED')) ||
    r.history.some(x => x.kind !== r.kind || x.evidenceId !== r.evidenceId)) throw new Error('Unsupported evidence action context');
  return r;
}
export const decodeEvidencePreview = obj({ kind: evidenceKind, evidenceId: guid, action, reviewBasis: sha256,
  requestHash: sha256, canProceed: bool, blockers: arr(str(2000), 20) });
const lookup = obj({ found: bool, receipt: nullable(decodeEvidenceReceipt) });
export function decodeEvidenceLookup(raw: unknown, path = 'response') {
  const r = lookup(raw, path); if (r.found !== (r.receipt !== null)) throw new Error('Unsupported evidence receipt lookup'); return r;
}
const candidate = obj({ resultId: guid, workpaperId: guid, procedureId: guid, procedureCode: str(200), status: oneOf('SUBMITTED', 'REVIEWED'),
  revision: nat, inputGeneration: nat, currentIndependentReview: bool, alreadyLinked: bool, resultBasis: sha256 });
const candidates = obj({ kind: evidenceKind, evidenceId: guid, reviewBasis: sha256, page: nat, count: nat, hasMore: bool, rows: arr(candidate, 25) });
export function decodeEvidenceProcedures(raw: unknown, path = 'response') {
  const r = candidates(raw, path);
  if (r.count > 500 || r.page >= 20 || r.hasMore !== (r.count > (r.page + 1) * 25) ||
    r.rows.length !== Math.min(25, Math.max(0, r.count - r.page * 25)) || new Set(r.rows.map(x => x.resultId)).size !== r.rows.length ||
    r.rows.some(x => !x.revision || !x.inputGeneration || (x.currentIndependentReview && x.status !== 'REVIEWED')))
    throw new Error('Unsupported scoped procedure page');
  return r;
}
export interface EvidenceActionFields { action: 'LINK' | 'REVIEW'; resultId: string | null; resultBasis: string | null; decision: string; reason: string; evidenceReference: string; }
export function evidenceActionFields(raw: unknown): EvidenceActionFields | null {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return null;
  try { const r = obj({ action, resultId: nullable(guid), resultBasis: nullable(sha256), decision: str(30), reason: str(4000), evidenceReference: str(2000) })(raw, "fields");
    if (Object.keys(raw).length !== 6 || (r.action === 'LINK' ? r.decision !== '' || (r.resultId === null) !== (r.resultBasis === null) :
      r.resultId !== null || r.resultBasis !== null || (r.decision !== '' && !['APPROVED', 'CHANGES_REQUIRED', 'REJECTED', 'ESCALATED'].includes(r.decision)))) return null;
    return r;
  } catch { return null; }
}
