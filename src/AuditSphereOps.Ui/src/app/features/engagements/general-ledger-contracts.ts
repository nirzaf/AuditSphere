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
  sha256,
  str,
} from '../../core/decode';
const selected = obj({
  decisionId: guid,
  sourceKind: str(10),
  trialBalanceDatasetId: nullable(guid),
  importBatchId: nullable(guid),
  sourceIdentityHash: sha256,
  acceptedByUserId: guid,
  acceptedAt: instant,
  inputGeneration: nat,
});
const summary = obj({
  id: guid,
  periodId: guid,
  periodCode: str(100),
  bookId: nullable(guid),
  entity: str(500),
  currency: str(10),
  rowCount: nat,
  sourceHash: sha256,
  importedAt: instant,
});
const catalogue = obj({
  clientId: guid,
  engagementId: guid,
  items: arr(summary, 20),
  totalCount: nat,
  page: nat,
  pageSize: nat,
});
const context = obj({
  batchId: guid,
  clientId: guid,
  engagementId: guid,
  periodId: guid,
  periodCode: str(100),
  bookId: nullable(guid),
  entity: str(500),
  currency: str(10),
  importState: str(30),
  rawFileSha256: sha256,
  sourceHash: sha256,
  profileVersion: str(100),
  parserVersion: str(100),
  importedByUserId: guid,
  importedAt: instant,
  selected: nullable(selected),
});
export const filterShape = obj({
  accountCodePrefix: str(100),
  postedFrom: nullable(date),
  postedTo: nullable(date),
  stableJournalId: str(200),
  counterparty: str(200),
});
const line = obj({
  lineId: guid,
  stableJournalId: str(200),
  stableLineId: str(200),
  postingDate: date,
  accountCode: str(100),
  debit: dec,
  credit: dec,
  functionalAmount: dec,
  originalCurrency: str(10),
  originalAmount: dec,
  counterparty: str(200),
  branch: str(100),
  costCentre: str(100),
  department: str(100),
  project: str(100),
});
const rows = obj({
  items: arr(line, 100),
  totalCount: nat,
  page: nat,
  pageSize: nat,
  totalDebit: dec,
  totalCredit: dec,
  balanced: bool,
});
const source = obj({ context, filter: filterShape, rows, journalLineLimit: nat });
const journalLine = obj({
  lineId: guid,
  stableLineId: str(200),
  accountCode: str(100),
  debit: dec,
  credit: dec,
  functionalAmount: dec,
  originalCurrency: str(10),
  originalAmount: dec,
  counterparty: str(200),
});
const journal = obj({
  context,
  journalLineLimit: nat,
  journal: obj({
    stableJournalId: str(200),
    postingDate: date,
    documentNumber: str(200),
    currency: str(10),
    isManual: bool,
    isYearEnd: bool,
    reversalReference: nullable(str(200)),
    lines: arr(journalLine, 1000),
    lineCount: nat,
    totalDebit: dec,
    totalCredit: dec,
    balanced: bool,
  }),
});
function validContext(c: ReturnType<typeof context>) {
  const s = c.selected;
  if (
    c.importState !== 'SEALED' ||
    (s &&
      (s.sourceKind !== 'GL' ||
        s.importBatchId === null ||
        s.trialBalanceDatasetId !== null ||
        s.inputGeneration < 1 ||
        (s.importBatchId === c.batchId && s.sourceIdentityHash !== c.sourceHash)))
  )
    throw new Error('Unsupported ledger context');
}
export function decodeLedgerCatalogue(raw: unknown) {
  const c = catalogue(raw, '');
  if (
    c.page < 1 ||
    c.pageSize !== 20 ||
    c.items.length > c.totalCount ||
    new Set(c.items.map((x) => x.id)).size !== c.items.length
  )
    throw new Error('Unsupported ledger catalogue');
  return c;
}
export function decodeLedgerSource(raw: unknown) {
  const s = source(raw, '');
  validContext(s.context);
  if (
    s.journalLineLimit !== 1000 ||
    s.rows.page < 1 ||
    s.rows.pageSize !== 100 ||
    s.rows.items.length > s.rows.totalCount ||
    new Set(s.rows.items.map((x) => x.lineId)).size !== s.rows.items.length ||
    (s.filter.postedFrom && s.filter.postedTo && s.filter.postedFrom > s.filter.postedTo)
  )
    throw new Error('Unsupported ledger source');
  return s;
}
export function decodeLedgerJournal(raw: unknown) {
  const j = journal(raw, '');
  validContext(j.context);
  if (
    j.journalLineLimit !== 1000 ||
    j.journal.lineCount !== j.journal.lines.length ||
    new Set(j.journal.lines.map((x) => x.lineId)).size !== j.journal.lines.length
  )
    throw new Error('Unsupported journal detail');
  return j;
}
