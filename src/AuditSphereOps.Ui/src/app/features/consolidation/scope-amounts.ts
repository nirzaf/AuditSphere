const CONSOLIDATION_SCALE = 1_000_000n;
export const MAX_NUMERIC_19_6 = BigInt('9'.repeat(19));
const MAX_OWNERSHIP_PERCENT_SCALED = 100n * CONSOLIDATION_SCALE;

export function parseConsolidationAmount(value: string): bigint | null {
  const input = value.trim();
  const parts = /^(\d+)(?:\.(\d{1,6}))?$/.exec(input);
  if (!parts || input.length > 64) return null;
  const scaled = BigInt(parts[1]) * CONSOLIDATION_SCALE + BigInt((parts[2] ?? '').padEnd(6, '0') || '0');
  return scaled <= MAX_NUMERIC_19_6 ? scaled : null;
}

export function parseSignedConsolidationAmount(value: string): bigint | null {
  const input = value.trim();
  const parts = /^(-?)(\d{1,13})(?:\.(\d{1,6}))?$/.exec(input);
  if (!parts || input.length > 21) return null;
  const magnitude = BigInt(parts[2]) * CONSOLIDATION_SCALE + BigInt((parts[3] ?? '').padEnd(6, '0') || '0');
  if (magnitude > MAX_NUMERIC_19_6) return null;
  return parts[1] ? -magnitude : magnitude;
}

export function formatScaledAmount(value: bigint): string {
  return `${value / CONSOLIDATION_SCALE}.${(value % CONSOLIDATION_SCALE).toString().padStart(6, '0')}`;
}

/** Ownership is already expressed in percentage points by the API; preserve the value and append its unit. */
export function formatOwnershipPercent(value: string): string {
  const scaled = parseConsolidationAmount(value);
  if (scaled === null || scaled > MAX_OWNERSHIP_PERCENT_SCALED) return '—';
  const [whole, fraction = ''] = value.trim().split('.');
  const normalizedWhole = whole.replace(/^0+(?=\d)/, '') || '0';
  const normalizedFraction = fraction.replace(/0+$/, '');
  return `${normalizedWhole}${normalizedFraction ? `.${normalizedFraction}` : ''}%`;
}

export function isValidOwnershipPercent(value: string): boolean {
  const scaled = parseConsolidationAmount(value);
  return scaled !== null && scaled <= MAX_OWNERSHIP_PERCENT_SCALED;
}
