import { arr, bool, dec, guid, instant, nat, nullable, obj, oneOf, sha256, str } from '../../core/decode';
import { decodeJournalCreation } from './journal-creation-contracts';

const option = obj({ journalId: guid, journalNumber: str(32), revision: nat,
  purpose: oneOf('REPORTING_ADJUSTMENT', 'PRESENTATION_RECLASSIFICATION', 'CLIENT_BOOK_CORRECTION'),
  layer: oneOf('REPORTING'),
  reflectionState: oneOf('UNKNOWN', 'NOT_REFLECTED', 'REFLECTED', 'PARTIALLY_REFLECTED', 'NOT_APPLICABLE'),
  canInclude: bool, blocker: nullable(str(2000)) });
export const decodePlanCreation = obj({ source: decodeJournalCreation, reviewBasis: sha256,
  journals: arr(option, 25), page: nat, hasMore: bool });
const preview = obj({ action: oneOf('CREATE', 'FINALIZE'), reviewBasis: sha256, requestHash: sha256,
  canProceed: bool, blocker: nullable(str(2000)), journals: arr(option, 100),
  debits: nullable(dec), credits: nullable(dec), appliedCount: nullable(nat), resultHash: nullable(sha256) });
export function decodePlanCommandPreview(raw: unknown, path = 'preview') {
  const p = preview(raw, path);
  if ((p.canProceed && p.blocker) || (p.action === 'CREATE' &&
      (p.debits !== null || p.credits !== null || p.appliedCount !== null || p.resultHash !== null)) ||
      (p.action === 'FINALIZE' && (p.journals.length !== 0 || (p.canProceed ?
       p.debits === null || p.credits === null || p.appliedCount === null || !p.resultHash :
       p.debits !== null || p.credits !== null || p.appliedCount !== null || p.resultHash !== null))) ||
      (p.canProceed && p.journals.some(j => !j.canInclude)) ||
      p.journals.some(j => j.revision < 1 || (j.canInclude && (j.blocker ||
       ['UNKNOWN', 'PARTIALLY_REFLECTED'].includes(j.reflectionState)))) ||
      new Set(p.journals.map(j => j.journalId)).size !== p.journals.length)
    throw new Error('Unsupported plan preview');
  return p;
}
const receipt = obj({ id: guid, requestId: guid, requestHash: sha256, planId: guid, datasetId: guid,
  action: oneOf('CREATE', 'FINALIZE'), actorId: guid, reason: str(4000), evidenceReference: str(2000),
  status: oneOf('Draft', 'Finalized'), resultHash: nullable(sha256), debits: nullable(dec),
  credits: nullable(dec), appliedCount: nullable(nat), createdAt: instant });
export function decodePlanCommandReceipt(raw: unknown, path = 'receipt') {
  const r = receipt(raw, path);
  if (!r.reason.trim() || !r.evidenceReference.trim() || (r.action === 'CREATE' ?
      r.status !== 'Draft' || r.resultHash !== null || r.debits !== null || r.credits !== null || r.appliedCount !== null :
      r.status !== 'Finalized' || !r.resultHash || r.debits === null || r.credits === null || r.appliedCount === null))
    throw new Error('Unsupported retained plan receipt');
  return r;
}
const lookup = obj({ found: bool, receipt: nullable(decodePlanCommandReceipt) });
export function decodePlanCommandLookup(raw: unknown, path = 'lookup') {
  const r = lookup(raw, path); if (r.found !== (r.receipt !== null)) throw new Error('Unsupported plan receipt lookup'); return r;
}
export const decodePlanHistory = obj({ items: arr(decodePlanCommandReceipt, 25), page: nat, hasMore: bool });
const fields = obj({ reason: str(4000), evidenceReference: str(2000),
  journals: arr(obj({ journalId: guid, revision: nat }), 100) });
export type PlanFields = ReturnType<typeof fields>;
export function planFields(raw: unknown): PlanFields | null {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw) || Object.keys(raw).length !== 3) return null;
  try { const r = fields(raw, 'draft'); return r.journals.some(j => j.revision < 1) ||
    new Set(r.journals.map(j => j.journalId)).size !== r.journals.length ? null : r; } catch { return null; }
}
