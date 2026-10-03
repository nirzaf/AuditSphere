import { arr, bool, dec, guid, nat, nullable, obj, oneOf, sha256, str } from '../../core/decode';
import { decodeJournalReceipt, decodeManagementDecision } from './journal-contracts';
const view = obj({
  journalId: guid,
  clientId: guid,
  engagementId: guid,
  journalNumber: str(32),
  revision: nat,
  status: oneOf('Draft', 'Submitted', 'Returned', 'Posted', 'ReflectedInSource', 'Void'),
  purpose: str(40),
  datasetId: guid,
  datasetRevision: nat,
  datasetDigest: sha256,
  periodStart: str(10),
  periodEnd: str(10),
  currency: str(3),
  reason: str(4000),
  evidenceReference: str(2000),
  lines: arr(obj({ accountCode: str(32), debit: dec, credit: dec }), 500),
  totalDebit: dec,
  totalCredit: dec,
  reviewBasis: sha256,
  evidenceMode: oneOf('SIGNED_IN', 'OFFLINE'),
  canDecide: bool,
  blocker: nullable(str(2000)),
  management: nullable(decodeManagementDecision),
});
export function decodeJournalManagement(raw: unknown, path = 'response') {
  const v = view(raw, path);
  const units = (s: string) => {
    if (!/^(0|[1-9][0-9]{0,18})(\.[0-9]{1,6})?$/.test(s)) throw new Error('Unsupported amount');
    const [w, f = ''] = s.split('.');
    return BigInt(w) * 1000000n + BigInt(f.padEnd(6, '0'));
  };
  if (
    v.revision < 1 ||
    v.datasetRevision < 1 ||
    !/^[A-Z]{3}$/.test(v.currency) ||
    (v.canDecide &&
      (v.status !== 'Draft' ||
        v.blocker ||
        v.management ||
        units(v.totalDebit) !== units(v.totalCredit))) ||
    (v.management &&
      (v.management.journalRevision !== v.revision ||
        (v.management.evidenceMode === 'SIGNED_IN') !== !!v.management.decidedByUserId)) ||
    v.lines.reduce((s, l) => s + units(l.debit), 0n) !== units(v.totalDebit) ||
    v.lines.reduce((s, l) => s + units(l.credit), 0n) !== units(v.totalCredit)
  )
    throw new Error('Unsupported management context');
  return v;
}
export const decodeManagementPreview = obj({
  reviewBasis: sha256,
  requestHash: sha256,
  decision: oneOf('ACCEPTED', 'REJECTED', 'PARTIAL'),
  evidenceMode: oneOf('SIGNED_IN', 'OFFLINE'),
  reason: str(4000),
  evidenceReference: str(2000),
  canProceed: bool,
  blocker: nullable(str(2000)),
});
export const decodeManagementReceipt = obj({
  action: decodeJournalReceipt,
  management: decodeManagementDecision,
});
export const decodeManagementLookup = obj({
  found: bool,
  receipt: nullable(decodeManagementReceipt),
});
export interface ManagementFields {
  decision: 'ACCEPTED' | 'REJECTED' | 'PARTIAL';
  reason: string;
  evidenceReference: string;
}
export function managementFields(raw: unknown): ManagementFields | null {
  try {
    if (!raw || typeof raw !== 'object' || Object.keys(raw).length !== 3) return null;
    return obj({
      decision: oneOf('ACCEPTED', 'REJECTED', 'PARTIAL'),
      reason: str(4000),
      evidenceReference: str(2000),
    })(raw, 'draft');
  } catch {
    return null;
  }
}
export const decodePortalJournals = obj({
  items: arr(
    obj({
      id: guid,
      number: str(32),
      revision: nat,
      purpose: str(40),
      status: str(30),
      currency: str(3),
    }),
    25,
  ),
  page: nat,
  hasMore: bool,
});
