import { exactDecimal, guidPattern } from './contracts';

/**
 * Small runtime decoder kit for /api/ui contracts. Responses are untrusted until decoded: a decoder either returns a
 * value of the declared shape or throws, and pages render an "unsupported response" state rather than guessing.
 * Decimals stay exact strings; nothing here converts money to JavaScript numbers.
 */
export type Decoder<T> = (value: unknown, path: string) => T;
export class DecodeError extends Error {}
const fail = (path: string, what: string): never => {
  throw new DecodeError(`${path || 'response'}: expected ${what}`);
};

export const str =
  (max = 20000): Decoder<string> =>
  (v, p) =>
    typeof v === 'string' && v.length <= max ? v : fail(p, 'text');
export const text = str();
export const guid: Decoder<string> = (v, p) => (typeof v === 'string' && guidPattern.test(v) ? v : fail(p, 'identifier'));
export const dec: Decoder<string> = (v, p) => (exactDecimal(v) ? v : fail(p, 'exact decimal string'));
export const int: Decoder<number> = (v, p) => (Number.isSafeInteger(v) ? (v as number) : fail(p, 'integer'));
export const nat: Decoder<number> = (v, p) => (Number.isSafeInteger(v) && (v as number) >= 0 ? (v as number) : fail(p, 'non-negative integer'));
export const bool: Decoder<boolean> = (v, p) => (typeof v === 'boolean' ? v : fail(p, 'boolean'));
export const date: Decoder<string> = (v, p) => (typeof v === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(v) ? v : fail(p, 'date'));
export const instant: Decoder<string> = (v, p) =>
  typeof v === 'string' && /^\d{4}-\d{2}-\d{2}T[\d:.]+(Z|[+-]\d{2}:\d{2})?$/.test(v) ? v : fail(p, 'timestamp');
/** Non-sensitive opaque concurrency tokens (row versions, generations, hashes). */
export const token: Decoder<string> = (v, p) => (typeof v === 'string' && /^[A-Za-z0-9+/=_:.-]{1,200}$/.test(v) ? v : fail(p, 'token'));
export const sha256: Decoder<string> = (v, p) => (typeof v === 'string' && /^[0-9a-f]{64}$/i.test(v) ? v : fail(p, 'SHA-256'));

export function oneOf<const T extends string>(...values: T[]): Decoder<T> {
  return (v, p) => (values.includes(v as T) ? (v as T) : fail(p, values.join('|')));
}
export function nullable<T>(d: Decoder<T>): Decoder<T | null> {
  return (v, p) => (v === null || v === undefined ? null : d(v, p));
}
export function arr<T>(d: Decoder<T>, max = 5000): Decoder<T[]> {
  return (v, p) => {
    if (!Array.isArray(v) || v.length > max) fail(p, `list of at most ${max}`);
    return (v as unknown[]).map((x, i) => d(x, `${p}[${i}]`));
  };
}
type Shape = Record<string, Decoder<unknown>>;
export type Decoded<S extends Shape> = { [K in keyof S]: ReturnType<S[K]> };
export function obj<S extends Shape>(shape: S): Decoder<Decoded<S>> {
  return (v, p) => {
    if (!v || typeof v !== 'object' || Array.isArray(v)) fail(p, 'object');
    const source = v as Record<string, unknown>;
    const out: Record<string, unknown> = {};
    for (const key of Object.keys(shape)) out[key] = shape[key](source[key], p ? `${p}.${key}` : key);
    return out as Decoded<S>;
  };
}
export function decode<T>(d: Decoder<T>, value: unknown): T {
  return d(value, '');
}

/** Formats an exact decimal string with grouping and fixed places without passing through floating point. */
export function money(value: string | null | undefined, places = 2): string {
  if (value === null || value === undefined || !exactDecimal(value)) return '—';
  const negative = value.startsWith('-');
  let [whole, fraction = ''] = (negative ? value.slice(1) : value).split('.');
  if (fraction.length > places) {
    // Round half away from zero on the decimal digits.
    const digits = (whole + fraction.slice(0, places)).split('').map(Number);
    if (Number(fraction[places]) >= 5) {
      let i = digits.length - 1;
      while (i >= 0) {
        if (digits[i] === 9) { digits[i] = 0; i--; } else { digits[i]++; break; }
      }
      if (i < 0) digits.unshift(1);
    }
    const joined = digits.join('');
    whole = joined.slice(0, joined.length - places) || '0';
    fraction = joined.slice(joined.length - places);
  }
  fraction = fraction.padEnd(places, '0');
  const grouped = whole.replace(/^0+(?=\d)/, '').replace(/\B(?=(\d{3})+(?!\d))/g, ',');
  const zero = /^[0,]*$/.test(grouped) && /^0*$/.test(fraction);
  return (negative && !zero ? '-' : '') + grouped + (places ? '.' + fraction : '');
}

/** Validates user decimal input before it is sent as an exact string; returns null when invalid. */
export function decimalInput(value: string, maxPlaces = 4): string | null {
  const v = value.trim().replace(/,/g, '');
  return new RegExp(`^-?\\d{1,20}(\\.\\d{1,${maxPlaces}})?$`).test(v) ? v : null;
}

/** Exact fraction string (0.25) → percentage text (25.00%) by shifting the decimal point, never through floating point. */
export function percent(fraction: string | null | undefined, places = 2): string {
  if (fraction === null || fraction === undefined || !exactDecimal(fraction)) return '—';
  const negative = fraction.startsWith('-');
  const [whole, frac = ''] = (negative ? fraction.slice(1) : fraction).split('.');
  const padded = frac.padEnd(2, '0');
  const shifted = (whole + padded.slice(0, 2)) + (padded.length > 2 ? '.' + padded.slice(2) : '');
  return money((negative ? '-' : '') + shifted, places) + '%';
}
