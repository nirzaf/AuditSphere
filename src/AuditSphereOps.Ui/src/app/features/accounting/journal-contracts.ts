import {
  arr,
  bool,
  date,
  dec,
  guid,
  instant,
  nat,
  nullable,
  obj,
  oneOf,
  sha256,
  str,
} from '../../core/decode';
export type JournalAction = 'UPDATE' | 'SUBMIT' | 'RETURN' | 'POST' | 'REVERSE';
export interface JournalFields {
  action: JournalAction;
  reason: string;
  evidenceReference: string;
  reversalNumber: string;
  lines: { accountCode: string; debit: string; credit: string }[];
}
const line = obj({ accountCode: str(32), debit: dec, credit: dec });
const action = oneOf('UPDATE', 'SUBMIT', 'RETURN', 'POST', 'REVERSE');
const operation = oneOf('CREATE', 'UPDATE', 'SUBMIT', 'RETURN', 'POST', 'REVERSE', 'MANAGEMENT');
const status = oneOf('Draft', 'Submitted', 'Returned', 'Posted', 'ReflectedInSource', 'Void');
const priorStatus = oneOf(
  'NOT_CREATED',
  'Draft',
  'Submitted',
  'Returned',
  'Posted',
  'ReflectedInSource',
  'Void',
);
export const decodeJournalReceipt = obj({
  id: guid,
  requestId: guid,
  requestHash: sha256,
  journalId: guid,
  resultJournalId: guid,
  action: operation,
  oldRevision: nat,
  newRevision: nat,
  oldStatus: priorStatus,
  newStatus: status,
  actorId: guid,
  reason: str(4000),
  evidenceReference: str(2000),
  createdAt: instant,
});
export const decodeManagementDecision = obj({
  id: guid,
  journalRevision: nat,
  decision: oneOf('ACCEPTED', 'REJECTED', 'PARTIAL'),
  evidenceMode: oneOf('SIGNED_IN', 'OFFLINE'),
  evidenceReference: str(2000),
  decidedByUserId: nullable(guid),
  decidedAt: instant,
});
export const decodeSourceReflection = obj({
  state: oneOf('UNKNOWN', 'NOT_REFLECTED', 'REFLECTED', 'PARTIALLY_REFLECTED', 'NOT_APPLICABLE'),
  evidence: str(2000),
  reviewedByUserId: nullable(guid),
  reviewedAt: nullable(instant),
  isExactRevision: bool,
});
const view = obj({
  journalId: guid,
  clientId: guid,
  engagementId: guid,
  journalNumber: str(32),
  status,
  revision: nat,
  reviewBasis: sha256,
  datasetId: guid,
  datasetRevision: nat,
  datasetDigest: sha256,
  periodId: nullable(guid),
  periodStart: str(10),
  periodEnd: str(10),
  bookId: nullable(guid),
  currency: str(3),
  purpose: str(40),
  origin: str(40),
  reason: str(4000),
  evidenceReference: str(2000),
  returnReason: nullable(str(2000)),
  preparerId: guid,
  supersedesId: nullable(guid),
  reversalOfId: nullable(guid),
  lines: arr(line, 500),
  totalDebit: dec,
  totalCredit: dec,
  blocker: nullable(str(2000)),
  canEdit: bool,
  canSubmit: bool,
  canReturn: bool,
  canPost: bool,
  canReverse: bool,
  historyCount: nat,
  historyPage: nat,
  history: arr(decodeJournalReceipt, 25),
  managementDecision: nullable(decodeManagementDecision),
  sourceReflection: nullable(decodeSourceReflection),
  canReconcileReflection: bool,
});
function units(s: string): bigint {
  if (!/^(0|[1-9][0-9]{0,18})(\.[0-9]{1,6})?$/.test(s))
    throw new Error('Unsupported journal precision');
  const [whole, fraction = ''] = s.split('.');
  return BigInt(whole) * 1000000n + BigInt(fraction.padEnd(6, '0'));
}
function totals(lines: ReturnType<typeof line>[], debit: string, credit: string) {
  if (
    lines.reduce((n, l) => n + units(l.debit), 0n) !== units(debit) ||
    lines.reduce((n, l) => n + units(l.credit), 0n) !== units(credit)
  )
    throw new Error('Mixed journal totals');
}
export function decodeJournalReview(raw: unknown, path = 'response') {
  const v = view(raw, path);
  totals(v.lines, v.totalDebit, v.totalCredit);
  if (
    v.revision < 1 ||
    v.datasetRevision < 1 ||
    v.historyCount > 1000 ||
    v.historyPage < 1 ||
    v.historyPage > Math.max(1, Math.ceil(v.historyCount / 25)) ||
    !/^[A-Z]{3}$/.test(v.currency) ||
    (v.canEdit && !['Draft', 'Returned'].includes(v.status)) ||
    (v.canSubmit && !['Draft', 'Returned'].includes(v.status)) ||
    (v.canReturn && v.status !== 'Submitted') ||
    (v.canPost && v.status !== 'Submitted') ||
    (v.canReverse && v.status !== 'Posted') ||
    (v.canReconcileReflection && v.status !== 'Posted') ||
    (v.blocker && (v.canEdit || v.canSubmit || v.canPost || v.canReturn || v.canReverse || v.canReconcileReflection)) ||
    v.history.some((e) => e.journalId !== v.journalId && e.resultJournalId !== v.journalId)
  )
    throw new Error('Unsupported journal review');
  if (v.periodId) {
    date(v.periodStart, path);
    date(v.periodEnd, path);
  }
  return v;
}
const preview = obj({
  action: operation,
  reviewBasis: sha256,
  requestHash: sha256,
  canProceed: bool,
  blocker: nullable(str(2000)),
  errors: arr(str(2000), 501),
  totalDebit: dec,
  totalCredit: dec,
  lines: arr(line, 500),
});
export function decodeJournalPreview(raw: unknown, path = 'response') {
  const v = preview(raw, path);
  totals(v.lines, v.totalDebit, v.totalCredit);
  if (
    v.canProceed &&
    (v.errors.length ||
      v.blocker ||
      units(v.totalDebit) !== units(v.totalCredit) ||
      units(v.totalDebit) === 0n)
  )
    throw new Error('Unsupported journal preview');
  return v;
}
export const decodeJournalLookup = obj({ found: bool, receipt: nullable(decodeJournalReceipt) });
const snapshot = obj({
  journalId: guid,
  revision: nat,
  status: priorStatus,
  reason: str(4000),
  evidenceReference: str(2000),
  lines: arr(line, 500),
});
export const decodeJournalHistory = obj({
  receipt: decodeJournalReceipt,
  before: snapshot,
  after: snapshot,
  management: nullable(decodeManagementDecision),
});
export function journalFields(raw: unknown): JournalFields | null {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return null;
  try {
    const d = obj({
      action,
      reason: str(4000),
      evidenceReference: str(2000),
      reversalNumber: str(32),
      lines: arr(obj({ accountCode: str(32), debit: str(21), credit: str(21) }), 500),
    })(raw, 'draft');
    return Object.keys(raw).length === 5 ? d : null;
  } catch {
    return null;
  }
}
