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
import { NATIVE_BOOK_SCOPE, NativeSource } from './native-source-contracts';

export interface NativeLedgerIntent {
  requestId: string;
  clientId: string;
  engagementId: string;
  trialBalanceDatasetId: string;
  expectedTrialBalanceSourceHash: string;
  evidenceReference: string;
  reviewBasis: string;
}
function sequence(value: unknown): string {
  if (
    typeof value !== 'string' ||
    !/^(0|[1-9]\d{0,18})$/.test(value) ||
    BigInt(value) > 9223372036854775807n
  )
    throw new Error('Invalid exact native posting sequence');
  return value;
}
function amount(value: unknown): string {
  const v = dec(value, 'native amount');
  if (!/^-?\d{1,29}(?:\.\d{1,6})?$/.test(v)) throw new Error('Unsupported native amount precision');
  return v;
}
function units(value: string): bigint {
  const negative = value.startsWith('-');
  const [whole, fraction = ''] = (negative ? value.slice(1) : value).split('.');
  return (negative ? -1n : 1n) * (BigInt(whole) * 1000000n + BigInt(fraction.padEnd(6, '0')));
}
const opening = obj({ openingId: guid, manifestHash: sha256 });
const line = obj({
  journalId: guid,
  lineId: guid,
  clientAccountId: guid,
  journalNumber: str(200),
  postingDate: date,
  accountCode: str(100),
  debit: amount,
  credit: amount,
  originalCurrency: str(3),
  originalAmount: amount,
  functionalAmount: amount,
  currency: str(3),
  sourceUser: str(2000),
  sourceSystem: str(2000),
  isManual: nullable(bool),
  isYearEnd: nullable(bool),
});
const sourceFacts = {
  trialBalanceDatasetId: guid,
  periodId: guid,
  asOfDate: date,
  currency: str(3),
  legalEntityKey: str(200),
  bookScope: oneOf(NATIVE_BOOK_SCOPE),
  originKind: oneOf('NATIVE_LEDGER'),
  sourceIdentityHash: sha256,
  manifestHash: sha256,
  generatedRowsHash: sha256,
  transactionCount: nat,
  lineCount: nat,
  totalDebit: amount,
  totalCredit: amount,
  postingSequence: sequence,
  nativeOpening: opening,
  unavailableMetadata: arr(str(2000), 100),
};
const preview = obj({ ...sourceFacts, reviewBasis: sha256, rows: arr(line, 250000) });
export type NativeLedgerPreview = ReturnType<typeof preview>;
function validFacts(v: ReturnType<typeof preview> | ReturnType<typeof source>) {
  if (
    v.lineCount < 1 ||
    v.lineCount > 250000 ||
    v.transactionCount < 1 ||
    v.transactionCount > v.lineCount ||
    !/^[A-Z]{3}$/.test(v.currency) ||
    !v.legalEntityKey.trim() ||
    units(v.totalDebit) < 0n ||
    units(v.totalDebit) !== units(v.totalCredit)
  )
    throw new Error('Invalid native ledger controls');
}
export function decodeNativeLedgerPreview(raw: unknown, tb: NativeSource): NativeLedgerPreview {
  const v = preview(raw, 'native ledger preview');
  validFacts(v);
  if (
    v.trialBalanceDatasetId !== tb.datasetId ||
    v.periodId !== tb.periodId ||
    v.asOfDate !== tb.asOfDate ||
    v.legalEntityKey !== tb.legalEntityKey ||
    v.rows.length !== v.lineCount ||
    new Set(v.rows.map((r) => r.lineId)).size !== v.lineCount ||
    new Set(v.rows.map((r) => r.journalId)).size !== v.transactionCount ||
    v.rows.some(
      (r) =>
        r.postingDate > v.asOfDate ||
        r.currency !== v.currency ||
        units(r.debit) < 0n ||
        units(r.credit) < 0n,
    ) ||
    v.rows.reduce((sum, r) => sum + units(r.debit), 0n) !== units(v.totalDebit) ||
    v.rows.reduce((sum, r) => sum + units(r.credit), 0n) !== units(v.totalCredit)
  )
    throw new Error('Native ledger preview source mismatch');
  return v;
}
const source = obj({
  ...sourceFacts,
  importBatchId: guid,
  clientId: guid,
  engagementId: guid,
  status: oneOf('SEALED'),
  isCurrent: bool,
  currentnessBlocker: nullable(str(4000)),
  capturedByUserId: guid,
  createdAt: instant,
});
export type NativeLedgerSource = ReturnType<typeof source>;
export function decodeNativeLedgerSource(
  raw: unknown,
  clientId: string,
  importBatchId?: string,
): NativeLedgerSource {
  const v = source(raw, 'native ledger source');
  validFacts(v);
  if (
    v.clientId !== clientId ||
    (importBatchId && v.importBatchId !== importBatchId) ||
    v.isCurrent === (v.currentnessBlocker !== null)
  )
    throw new Error('Native ledger source scope mismatch');
  return v;
}
const artifact = obj({
  importBatchId: guid,
  fileName: str(200),
  contentType: oneOf('application/json'),
  generatedRowsJson: str(32 * 1024 * 1024),
  generatedRowsHash: sha256,
  manifestJson: str(16 * 1024 * 1024),
  manifestHash: sha256,
  sourceIdentityHash: sha256,
  isCurrent: bool,
  currentnessBlocker: nullable(str(4000)),
});
export function decodeNativeLedgerArtifact(raw: unknown, s: NativeLedgerSource) {
  const v = artifact(raw, 'native ledger artifact');
  if (
    v.importBatchId !== s.importBatchId ||
    v.fileName !== `native-gl-${s.importBatchId}.json` ||
    v.generatedRowsHash !== s.generatedRowsHash ||
    v.manifestHash !== s.manifestHash ||
    v.sourceIdentityHash !== s.sourceIdentityHash ||
    v.isCurrent === (v.currentnessBlocker !== null)
  )
    throw new Error('Native ledger artifact identity mismatch');
  return v;
}
const pending = obj({
  requestId: guid,
  clientId: guid,
  engagementId: guid,
  trialBalanceDatasetId: guid,
  expectedTrialBalanceSourceHash: sha256,
  evidenceReference: str(300),
  reviewBasis: sha256,
});
export function nativeLedgerPendingIntent(raw: unknown): NativeLedgerIntent | null {
  try {
    if (!raw || typeof raw !== 'object' || Object.keys(raw).length !== 7) return null;
    const v = pending(raw, 'pending native ledger request');
    return v.evidenceReference.trim() === v.evidenceReference && v.evidenceReference.length > 0
      ? v
      : null;
  } catch {
    return null;
  }
}
