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

const allocation = obj({
  sourceAccountCode: str(100),
  destinationCode: str(100),
  statementSection: str(100),
  fraction: dec,
  rationale: str(2000),
  auditArea: nullable(str(100)),
  residualPolicy: nullable(str(100)),
});
const editorShape = obj({
  baseMappingId: guid,
  clientId: guid,
  engagementId: guid,
  datasetId: guid,
  baseVersion: nat,
  inputGeneration: nat,
  revision: sha256,
  taxonomyVersion: str(100),
  chartVersionId: nullable(guid),
  periodStart: date,
  periodEnd: date,
  sourceAccountCount: nat,
  filteredCount: nat,
  page: nat,
  pageSize: nat,
  accounts: arr(
    obj({
      accountCode: str(100),
      accountName: str(500),
      amount: dec,
      currency: str(30),
      allocations: arr(allocation, 5000),
    }),
    25,
  ),
  destinations: arr(obj({ code: str(100), name: str(500), statementSection: str(100) }), 5000),
  canCreate: bool,
  blocker: nullable(str(2000)),
});
export function decodeMappingEditor(raw: unknown, path = '') {
  const p = editorShape(raw, path);
  if (
    p.baseVersion < 1 ||
    p.page < 1 ||
    p.pageSize !== 25 ||
    p.sourceAccountCount > 20000 ||
    p.filteredCount > p.sourceAccountCount ||
    p.accounts.length > p.filteredCount ||
    new Set(p.accounts.map((x) => x.accountCode)).size !== p.accounts.length ||
    new Set(p.destinations.map((x) => x.code)).size !== p.destinations.length ||
    p.canCreate === !!p.blocker ||
    p.accounts.some(
      (x) => !x.accountCode || x.allocations.some((a) => a.sourceAccountCode !== x.accountCode),
    )
  )
    throw new Error('Unsupported mapping editor');
  return p;
}
export type MappingEditor = ReturnType<typeof decodeMappingEditor>;
export type MappingAccount = MappingEditor['accounts'][number];
export const decodeMappingPreview = obj({
  baseMappingId: guid,
  requestId: guid,
  baseRevision: sha256,
  revision: sha256,
  requestHash: sha256,
  changedAccountCount: nat,
  allocationCount: nat,
  changes: arr(
    obj({ accountCode: str(100), before: arr(allocation, 5000), after: arr(allocation, 20) }),
    200,
  ),
  canCreate: bool,
  blocker: nullable(str(2000)),
});
export type MappingPreview = ReturnType<typeof decodeMappingPreview>;
export const decodeMappingReceipt = obj({
  baseMappingId: guid,
  requestId: guid,
  mappingId: guid,
  version: nat,
  status: oneOf('DRAFT', 'APPROVED'),
  requestHash: sha256,
  createdBy: guid,
  createdAt: instant,
});
export type MappingReceipt = ReturnType<typeof decodeMappingReceipt>;
export interface DraftSplit {
  destinationCode: string;
  fraction: string;
  rationale: string;
  auditArea: string;
}
export interface BatchChange {
  accountCode: string;
  splits: DraftSplit[];
}
export interface MappingEditableDraft {
  changes: BatchChange[];
  active: BatchChange | null;
  selected: string[];
  batch: { destinationCode: string; rationale: string; auditArea: string };
  paste: string;
  localBatch: BatchChange[];
}
export function emptyMappingDraft(): MappingEditableDraft {
  return {
    changes: [],
    active: null,
    selected: [],
    batch: { destinationCode: '', rationale: '', auditArea: '' },
    paste: '',
    localBatch: [],
  };
}
const exactKeys = (v: Record<string, unknown>, keys: string[]) =>
  Object.keys(v).length === keys.length && keys.every((k) => k in v);
function change(raw: unknown): BatchChange | null {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return null;
  const r = raw as Record<string, unknown>;
  if (
    !exactKeys(r, ['accountCode', 'splits']) ||
    typeof r['accountCode'] !== 'string' ||
    !r['accountCode'] ||
    r['accountCode'].length > 100 ||
    !Array.isArray(r['splits']) ||
    r['splits'].length < 1 ||
    r['splits'].length > 20
  )
    return null;
  const splits: DraftSplit[] = [];
  for (const v of r['splits']) {
    if (
      !v ||
      typeof v !== 'object' ||
      Array.isArray(v) ||
      !exactKeys(v, ['destinationCode', 'fraction', 'rationale', 'auditArea']) ||
      Object.entries({ destinationCode: 100, fraction: 40, rationale: 2000, auditArea: 100 }).some(
        ([k, max]) => typeof v[k] !== 'string' || v[k].length > max,
      )
    )
      return null;
    splits.push({
      destinationCode: v.destinationCode,
      fraction: v.fraction,
      rationale: v.rationale,
      auditArea: v.auditArea,
    });
  }
  return { accountCode: r['accountCode'], splits };
}
/** Explicit convenience fields only; never authorization, source balances, review or an executable request. */
export function mappingEditableDraft(raw: unknown): MappingEditableDraft | null {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return null;
  const r = raw as Record<string, unknown>;
  if (
    !exactKeys(r, ['changes', 'active', 'selected', 'batch', 'paste', 'localBatch']) ||
    !Array.isArray(r['changes']) ||
    r['changes'].length > 200
  )
    return null;
  if (
    !Array.isArray(r['selected']) ||
    r['selected'].length > 200 ||
    r['selected'].some((x) => typeof x !== 'string' || !x || x.length > 100) ||
    new Set(r['selected']).size !== r['selected'].length ||
    typeof r['paste'] !== 'string' ||
    r['paste'].length > 100000 ||
    !Array.isArray(r['localBatch']) ||
    r['localBatch'].length > 200
  )
    return null;
  const batch = r['batch'] as Record<string, unknown> | null;
  if (
    !batch ||
    typeof batch !== 'object' ||
    Array.isArray(batch) ||
    !exactKeys(batch, ['destinationCode', 'rationale', 'auditArea']) ||
    Object.entries({ destinationCode: 100, rationale: 2000, auditArea: 100 }).some(
      ([key, max]) => typeof batch[key] !== 'string' || (batch[key] as string).length > max,
    )
  )
    return null;
  const localBatch = r['localBatch'].map(change);
  if (
    localBatch.some((x) => x === null) ||
    new Set(localBatch.map((x) => x!.accountCode)).size !== localBatch.length
  )
    return null;
  const changes = r['changes'].map(change);
  if (
    changes.some((x) => x === null) ||
    new Set(changes.map((x) => x!.accountCode)).size !== changes.length
  )
    return null;
  const active = r['active'] === null ? null : change(r['active']);
  return r['active'] !== null && !active
    ? null
    : {
        changes: changes as BatchChange[],
        active,
        selected: [...r['selected']] as string[],
        batch: {
          destinationCode: batch['destinationCode'] as string,
          rationale: batch['rationale'] as string,
          auditArea: batch['auditArea'] as string,
        },
        paste: r['paste'],
        localBatch: localBatch as BatchChange[],
      };
}
export const emptySplit = (): DraftSplit => ({
  destinationCode: '',
  fraction: '1',
  rationale: '',
  auditArea: '',
});
export function splitError(splits: DraftSplit[], destinations: string[]): string {
  if (
    splits.length < 1 ||
    splits.length > 20 ||
    new Set(splits.map((s) => s.destinationCode)).size !== splits.length
  )
    return 'Choose one to 20 distinct destinations.';
  let sum = 0n;
  for (const s of splits) {
    if (
      !destinations.includes(s.destinationCode) ||
      !s.rationale.trim() ||
      s.rationale.length > 2000 ||
      s.auditArea.length > 100
    )
      return 'Choose an approved destination and enter a bounded rationale for every split.';
    if (!/^[01](\.\d{1,6})?$/.test(s.fraction))
      return 'Use exact fractions with up to six decimal places; no exponent or locale separators.';
    const [whole, part = ''] = s.fraction.split('.');
    const n = BigInt(whole) * 1000000n + BigInt(part.padEnd(6, '0'));
    if (n <= 0n || n > 1000000n)
      return 'Every fraction must be greater than zero and no greater than one.';
    sum += n;
  }
  return sum === 1000000n
    ? ''
    : 'Split fractions must total exactly 1. The server rechecks the complete mapping.';
}
/** Paste is explicit text input, parsed into a bounded preview; it never applies edits or calls the server. */
export function parseMappingPaste(text: string): BatchChange[] {
  if (!text.trim() || text.length > 100000)
    throw new Error('Paste one to 200 bounded source-account changes.');
  const lines = text.trim().split(/\r?\n/);
  if (lines.length > 4000) throw new Error('Paste at most 4,000 explicit allocation rows.');
  const map = new Map<string, DraftSplit[]>();
  for (const line of lines) {
    const cells = line.split('\t');
    if (cells.length < 4 || cells.length > 5)
      throw new Error(
        'Each row needs account, destination, exact fraction, rationale and optional audit area, separated by tabs.',
      );
    const [accountCode, destinationCode, fraction, rationale, auditArea = ''] = cells;
    const splits = map.get(accountCode) ?? [];
    splits.push({ destinationCode, fraction, rationale, auditArea });
    map.set(accountCode, splits);
  }
  const safe = mappingEditableDraft({
    ...emptyMappingDraft(),
    changes: [...map].map(([accountCode, splits]) => ({ accountCode, splits })),
    active: null,
  });
  if (!safe) throw new Error('Pasted changes exceed the account, split or field limits.');
  return safe.changes;
}
