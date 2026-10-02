import {
  arr,
  bool,
  date,
  decode,
  guid,
  instant,
  int,
  nullable,
  obj,
  oneOf,
  sha256,
  str,
} from '../../core/decode';
const meta = {
  id: guid,
  firmId: guid,
  code: str(100),
  status: oneOf('DRAFT', 'APPROVED'),
  createdByUserId: guid,
  createdAt: instant,
  approvedByUserId: nullable(guid),
  approvedAt: nullable(instant),
};
const set = obj({
  ...meta,
  version: int,
  source: str(2000),
  effectiveFrom: nullable(date),
  effectiveTo: nullable(date),
});
const policy = obj({
  ...meta,
  functionalCurrency: str(3),
  presentationCurrency: str(3),
  closingRateRule: str(100),
  averageRateRule: str(100),
  historicalRateRule: str(100),
});
export function exactRate(value: string): boolean {
  return /^\d{1,12}(\.\d{1,6})?$/.test(value) && /[1-9]/.test(value);
}
const rate = obj({
  id: guid,
  firmId: guid,
  rateSetVersionId: guid,
  fromCurrency: str(3),
  toCurrency: str(3),
  rateDate: date,
  rateType: str(100),
  rate: str(40),
  direction: str(20),
  createdAt: instant,
});
export const catalogueDecoder = obj({
  revision: sha256,
  rateSets: arr(set, 1000),
  policies: arr(policy, 1000),
  canWrite: bool,
});
const setReview = obj({
  set,
  rates: arr(rate, 1000),
  revision: sha256,
  canWrite: bool,
  canApprove: bool,
});
export function decodeSet(raw: unknown) {
  const r = decode(setReview, raw);
  if (
    r.rates.some((x) => x.firmId !== r.set.firmId || x.rateSetVersionId !== r.set.id) ||
    r.set.version < 1
  )
    throw new Error('Unsupported rate context');
  return r;
}
export const policyDecoder = obj({ policy, revision: sha256, canApprove: bool });
export type SetReview = ReturnType<typeof decodeSet>;
export type PolicyReview = ReturnType<typeof policyDecoder>;
export const emptyCurrencyIntent = () => ({
  code: '',
  source: '',
  version: '1',
  effectiveFrom: '',
  effectiveTo: '',
  fromCurrency: '',
  toCurrency: '',
  date: '',
  purpose: 'CLOSING',
  rate: '',
});
export type CurrencyIntent = ReturnType<typeof emptyCurrencyIntent>;
/** Only bounded editable intent; never restore review assent or a client-controlled revision. */
export function currencyDraft(raw: unknown): CurrencyIntent | null {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return null;
  const bounds: Record<keyof CurrencyIntent, number> = {
    code: 100,
    source: 2000,
    version: 10,
    effectiveFrom: 10,
    effectiveTo: 10,
    fromCurrency: 3,
    toCurrency: 3,
    date: 10,
    purpose: 20,
    rate: 40,
  };
  const r = raw as Record<string, unknown>;
  if (Object.keys(r).some((k) => !(k in bounds))) return null;
  const value = emptyCurrencyIntent();
  for (const key of Object.keys(bounds) as (keyof CurrencyIntent)[]) {
    if (typeof r[key] !== 'string' || (r[key] as string).length > bounds[key]) return null;
    value[key] = r[key] as string;
  }
  return value;
}
