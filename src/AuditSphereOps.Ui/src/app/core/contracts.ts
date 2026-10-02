/** Exact decimal wire values stay strings; financial values are never coerced to JavaScript numbers. */
export function exactDecimal(value: unknown): value is string {
  return typeof value === 'string' && /^-?\d{1,29}(?:\.\d{1,28})?$/.test(value);
}
export const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
