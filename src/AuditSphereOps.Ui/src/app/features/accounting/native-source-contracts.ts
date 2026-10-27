import { arr, bool, date, DecodeRefusal, guid, instant, nat, nullable, obj, oneOf, sha256, str } from '../../core/decode';

export const NATIVE_BOOK_SCOPE = 'PERIOD_LEDGER_NO_BOOK' as const;
export function exactRevision(value: unknown, path = 'revision'): string {
  if (typeof value !== 'string' || !/^[1-9]\d{0,18}$/.test(value) || BigInt(value) > 9223372036854775807n)
    throw new Error(`Invalid ${path}`);
  return value;
}
function sequence(value: unknown): string {
  if (typeof value !== 'string' || !/^(0|[1-9]\d{0,18})$/.test(value) || BigInt(value) > 9223372036854775807n)
    throw new Error('Invalid posting sequence');
  return value;
}
function amount(value: unknown): string {
  if (typeof value !== 'string' || !/^\d{1,29}(?:\.\d{1,6})?$/.test(value)) throw new Error('Invalid exact native amount');
  return value;
}
function units(value: string): bigint {
  const [whole, fraction = ''] = value.split('.');
  return BigInt(whole) * 1000000n + BigInt(fraction.padEnd(6, '0'));
}
const period = obj({ id: guid, revision: exactRevision, code: str(100), start: date, end: date,
  basis: str(100), currency: str(3), status: str(50) });
export type NativePeriod = ReturnType<typeof period>;
const context = obj({ clientId: guid, name: str(2000), profile: nullable(obj({ sourceMode: str(50), currency: str(3) })),
  periods: arr(period, 100), hasMorePeriods: bool });
export function decodeNativeContext(raw: unknown, clientId: string) {
  const c = context(raw, 'context');
  if (c.clientId === clientId && c.profile?.sourceMode !== 'NATIVE_BOOKKEEPING')
    throw new DecodeRefusal(c.profile
      ? 'This client\'s books are not kept natively in AuditSphere, so native trial balance sources and opening balances do not apply. Use its imported sources instead.'
      : 'This client has no accounting profile yet. Set it up for native bookkeeping in the accounting workspace before creating native sources or opening balances.');
  if (c.clientId !== clientId || c.profile?.sourceMode !== 'NATIVE_BOOKKEEPING' ||
      !/^[A-Z]{3}$/.test(c.profile.currency) || new Set(c.periods.map(p => p.id)).size !== c.periods.length ||
      c.periods.some(p => p.start > p.end || !/^[A-Z]{3}$/.test(p.currency))) throw new Error('Unsupported native context');
  return c;
}
export const decodeNativeEngagement = obj({ engagementId: guid, clientId: guid,
  datasets: arr(obj({ id: guid, periodCode: str(200), currency: str(3), importedAt: instant }), 50) });
const row = obj({ accountId: guid, accountCode: str(100), accountName: str(2000), openingDebit: amount,
  openingCredit: amount, periodDebit: amount, periodCredit: amount, closingDebit: amount, closingCredit: amount });
const preview = obj({ reviewBasis: sha256, sourceIdentityHash: sha256, manifestHash: sha256, generatedRowsHash: sha256,
  rowCount: nat, bookScope: oneOf(NATIVE_BOOK_SCOPE), activityOnly: bool, currency: str(3), postingSequence: sequence,
  trialBalance: obj({ fromDate: date, toDate: date, source: str(1000), openingDebit: amount, openingCredit: amount,
    periodDebit: amount, periodCredit: amount, closingDebit: amount, closingCredit: amount, rows: arr(row, 250000) }) });
export type NativePreview = ReturnType<typeof preview>;
export interface NativeSourceIntent {
  requestId: string; clientId: string; engagementId: string; periodId: string; expectedPeriodRevision: string;
  asOfDate: string; legalEntityKey: string; evidenceReference: string; allowActivityOnly: boolean; reviewBasis: string;
}
export function decodeNativePreview(raw: unknown, p: NativePeriod, intent: NativeSourceIntent): NativePreview {
  const v = preview(raw, 'preview'), tb = v.trialBalance;
  if (v.rowCount < 1 || v.rowCount !== tb.rows.length || v.currency !== p.currency ||
      tb.fromDate !== p.start || tb.toDate !== intent.asOfDate || (v.activityOnly && !intent.allowActivityOnly) ||
      new Set(tb.rows.map(r => r.accountCode)).size !== tb.rows.length) throw new Error('Native preview scope mismatch');
  const pairs = [['openingDebit', 'openingCredit'], ['periodDebit', 'periodCredit'], ['closingDebit', 'closingCredit']] as const;
  for (const [debit, credit] of pairs) {
    if (units(tb[debit]) !== units(tb[credit]) ||
        tb.rows.reduce((total, r) => total + units(r[debit]), 0n) !== units(tb[debit]) ||
        tb.rows.reduce((total, r) => total + units(r[credit]), 0n) !== units(tb[credit])) throw new Error('Invalid native controls');
  }
  if (tb.rows.some(r => units(r.openingDebit) - units(r.openingCredit) + units(r.periodDebit) - units(r.periodCredit) !==
      units(r.closingDebit) - units(r.closingCredit))) throw new Error('Invalid native row rollforward');
  return v;
}
const source = obj({ datasetId: guid, clientId: guid, engagementId: guid, periodId: guid, asOfDate: date,
  legalEntityKey: str(200), bookScope: oneOf(NATIVE_BOOK_SCOPE), sourceKind: oneOf('Native'), activityOnly: bool,
  rowCount: nat, sourceIdentityHash: sha256, manifestHash: sha256, generatedRowsHash: sha256,
  validationStatus: oneOf('Pending', 'Accepted', 'Rejected'), isCurrent: bool, currentnessBlocker: nullable(str(4000)),
  capturedByUserId: guid, createdAt: instant });
export type NativeSource = ReturnType<typeof source>;
export function decodeNativeSource(raw: unknown, clientId: string, datasetId?: string): NativeSource {
  const v = source(raw, 'source');
  if (v.clientId !== clientId || datasetId && v.datasetId !== datasetId || v.rowCount < 1 || v.rowCount > 250000 ||
      v.isCurrent === (v.currentnessBlocker !== null) || !v.legalEntityKey.trim()) throw new Error('Native source context mismatch');
  return v;
}
const artifact = obj({ datasetId: guid, fileName: str(200), contentType: oneOf('application/json'),
  generatedRowsJson: str(32 * 1024 * 1024), generatedRowsHash: sha256,
  manifestJson: str(16 * 1024 * 1024), manifestHash: sha256, sourceIdentityHash: sha256,
  isCurrent: bool, currentnessBlocker: nullable(str(4000)) });
export function decodeNativeArtifact(raw: unknown, s: NativeSource) {
  const v = artifact(raw, 'artifact');
  if (v.datasetId !== s.datasetId || v.fileName !== `auditsphere-native-tb-${s.datasetId}.json` ||
      v.generatedRowsHash !== s.generatedRowsHash || v.manifestHash !== s.manifestHash ||
      v.sourceIdentityHash !== s.sourceIdentityHash || v.isCurrent === (v.currentnessBlocker !== null))
    throw new Error('Generated artifact identity mismatch');
  return v;
}
const pending = obj({ requestId: guid, clientId: guid, engagementId: guid, periodId: guid, expectedPeriodRevision: exactRevision,
  asOfDate: date, legalEntityKey: str(200), evidenceReference: str(2000), allowActivityOnly: bool, reviewBasis: sha256 });
export function nativePendingIntent(raw: unknown): NativeSourceIntent | null {
  try {
    if (!raw || typeof raw !== 'object' || Object.keys(raw).length !== 10) return null;
    const v = pending(raw, 'pending');
    return v.legalEntityKey.trim() === v.legalEntityKey && v.legalEntityKey.length > 0 &&
      v.evidenceReference.trim() === v.evidenceReference && v.evidenceReference.length > 0 ? v : null;
  } catch { return null; }
}
