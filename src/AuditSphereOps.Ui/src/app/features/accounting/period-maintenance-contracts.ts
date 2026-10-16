import { Decoder, arr, bool, date, dec, guid, instant, nat, nullable, obj, oneOf, sha256, str, text } from '../../core/decode';
import { PendingRequestReference } from '../../core/tab-drafts';
import { decodeClosedPeriods } from './maintenance';

export const maintenanceAction = oneOf('ROLL_FORWARD', 'CREATE_RESTATEMENT', 'APPROVE_RESTATEMENT');
const epochString: Decoder<string> = (raw, path) => {
  if (typeof raw !== 'string' || !/^(0|[1-9][0-9]{0,18})$/.test(raw)) throw new Error(`${path}: expected exact session epoch string`);
  return raw;
};
const rollInput = obj({ clientId: guid, priorPeriodId: guid, periodCode: str(100), startDate: date, endDate: date,
  basis: str(100), currency: str(3), sourceHash: sha256, priorClosingAmount: dec, currentOpeningAmount: dec,
  evidenceReference: str(2000), sourcePackageId: nullable(guid), expectedPriorRevision: nat });
const restatementInput = obj({ clientId: guid, periodId: guid, originalPackageId: guid, revisedPackageId: guid,
  revisedBasis: str(100), reason: str(4000), evidenceReference: str(2000), changeType: oneOf('RECLASSIFIED', 'RESTATED_ERROR', 'POLICY_TRANSITION', 'PROSPECTIVE_ESTIMATE_CHANGE'), affectedPeriods: str(500) });
export const decodeMaintenanceIntent = obj({ requestId: guid, reviewBasis: sha256, action: maintenanceAction,
  rollForward: nullable(rollInput), restatement: nullable(restatementInput), restatementId: nullable(guid), reviewed: bool,
  expectedRequestHash: nullable(sha256) });
export const decodeMaintenanceState = obj({ clientId: guid, action: maintenanceAction, periodId: guid,
  restatementId: nullable(guid), reviewBasis: sha256, canProceed: bool, blockers: arr(str(2000), 100) });
export const decodeMaintenanceApprovalDetail = obj({ restatementId: guid, clientId: guid, engagementId: guid, periodId: guid,
  periodCode: str(100), periodStart: date, periodEnd: date, currency: str(3), originalPackageId: guid, originalPackageHash: sha256,
  revisedPackageId: guid, revisedPackageHash: sha256, revisedBasis: str(100),
  changeType: oneOf('RECLASSIFIED', 'RESTATED_ERROR', 'POLICY_TRANSITION', 'PROSPECTIVE_ESTIMATE_CHANGE'),
  affectedPeriods: str(500), reason: str(4000), evidenceReference: str(2000), status: oneOf('SUBMITTED', 'APPROVED'),
  createdByUserId: guid, createdAt: instant });
const previewShape = obj({ requestId: guid, requestHash: sha256, state: decodeMaintenanceState,
  canonicalInput: decodeMaintenanceIntent, summary: str(8000), approvalDetail: nullable(decodeMaintenanceApprovalDetail) });
export const decodeMaintenancePreview: Decoder<ReturnType<typeof previewShape>> = (raw, path) => {
  const preview = previewShape(raw, path), detail = preview.approvalDetail;
  if (preview.state.action === 'APPROVE_RESTATEMENT') {
    if (!detail || preview.canonicalInput.action !== 'APPROVE_RESTATEMENT' ||
      detail.clientId !== preview.state.clientId || detail.periodId !== preview.state.periodId ||
      detail.restatementId !== preview.state.restatementId || detail.restatementId !== preview.canonicalInput.restatementId ||
      preview.canonicalInput.rollForward !== null || preview.canonicalInput.restatement !== null ||
      detail.originalPackageId === detail.revisedPackageId || detail.originalPackageHash === detail.revisedPackageHash ||
      detail.periodStart > detail.periodEnd || !/^[A-Z]{3}$/.test(detail.currency) || !detail.periodCode.trim() ||
      !detail.revisedBasis.trim() || !detail.reason.trim() || !detail.evidenceReference.trim() ||
      (preview.state.canProceed && detail.status !== 'SUBMITTED')) throw new Error(`${path}: approval evidence does not match the reviewed intent`);
  } else if (detail !== null) throw new Error(`${path}: unexpected approval evidence`);
  return preview;
};
export const decodeMaintenanceReceipt = obj({ id: guid, clientId: guid, actorId: guid, actorEpoch: epochString,
  requestId: guid, requestHash: sha256, action: maintenanceAction, reviewBasis: sha256, createdAt: instant,
  result: obj({ periodId: guid, restatementId: nullable(guid), status: oneOf('DRAFT', 'SUBMITTED', 'APPROVED') }) });
export const decodeMaintenanceLookup = obj({ found: bool, receipt: nullable(decodeMaintenanceReceipt) });
export const decodeMaintenanceContext = obj({ periods: decodeClosedPeriods, restatements: arr(obj({ id: guid, clientId: guid,
  clientName: text, periodCode: text, revisedBasis: text, status: text, evidenceReference: text, canReview: bool }), 20000) });
export type MaintenanceAction = ReturnType<typeof maintenanceAction>;
export type MaintenanceIntent = ReturnType<typeof decodeMaintenanceIntent>;
export type MaintenancePreview = ReturnType<typeof decodeMaintenancePreview>;
export type MaintenanceReceipt = ReturnType<typeof decodeMaintenanceReceipt>;
export type PendingMaintenance = { clientId: string; action: MaintenanceAction; reference: PendingRequestReference;
  expected?: { reviewBasis: string; periodId: string; restatementId: string | null } };


/** Canonicalization may change decimal scale, never financial value. No IEEE-754 conversion is used. */
export function sameMaintenanceIntent(left: MaintenanceIntent, right: MaintenanceIntent): boolean {
  const decimalValue = (value: string): string => {
    const negative = value.startsWith('-');
    const [integer, fraction = ''] = (negative ? value.slice(1) : value).split('.');
    const whole = integer.replace(/^0+(?=\d)/, ''), tail = fraction.replace(/0+$/, '');
    return (negative && (whole !== '0' || tail !== '') ? '-' : '') + whole + (tail ? '.' + tail : '');
  };
  const comparable = (intent: MaintenanceIntent) => ({ ...intent, rollForward: intent.rollForward ? { ...intent.rollForward,
    priorClosingAmount: decimalValue(intent.rollForward.priorClosingAmount), currentOpeningAmount: decimalValue(intent.rollForward.currentOpeningAmount) } : null });
  return JSON.stringify(comparable(left)) === JSON.stringify(comparable(right));
}
