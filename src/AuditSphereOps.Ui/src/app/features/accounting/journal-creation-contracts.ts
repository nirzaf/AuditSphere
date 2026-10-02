import { arr, bool, date, guid, nat, nullable, obj, oneOf, sha256, str } from '../../core/decode';
const purpose = oneOf(
  'REPORTING_ADJUSTMENT',
  'PRESENTATION_RECLASSIFICATION',
  'CLIENT_BOOK_CORRECTION',
);
const origin = oneOf('AUDIT_PROPOSED', 'CLIENT_REQUESTED', 'MANAGEMENT_PROVIDED', 'IMPORTED');
const context = obj({
  datasetId: guid,
  clientId: guid,
  engagementId: guid,
  datasetRevision: nat,
  datasetDigest: sha256,
  periodId: nullable(guid),
  periodCode: str(100),
  periodStart: str(10),
  periodEnd: str(10),
  bookId: nullable(guid),
  bookCode: str(100),
  currency: str(3),
  basis: str(100),
  entity: str(500),
  reviewBasis: sha256,
  canCreate: bool,
  blocker: nullable(str(2000)),
});
export function decodeJournalCreation(raw: unknown, path = 'response') {
  const c = context(raw, path);
  if (
    c.datasetRevision < 1 ||
    !/^[A-Z]{3}$/.test(c.currency) ||
    (c.canCreate && (!c.periodId || c.blocker))
  )
    throw new Error('Unsupported journal source');
  if (c.periodId) {
    date(c.periodStart, path);
    date(c.periodEnd, path);
  }
  return c;
}
const fields = obj({
  journalNumber: str(32),
  purpose,
  origin,
  reason: str(4000),
  evidenceReference: str(2000),
  supersedesId: str(36),
  supersedesRevision: str(12),
  lines: arr(obj({ accountCode: str(32), debit: str(20), credit: str(20) }), 500),
});
export type JournalCreationFields = ReturnType<typeof fields>;
export function journalCreationFields(raw: unknown): JournalCreationFields | null {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw) || Object.keys(raw).length !== 8)
    return null;
  try {
    return fields(raw, 'draft');
  } catch {
    return null;
  }
}
