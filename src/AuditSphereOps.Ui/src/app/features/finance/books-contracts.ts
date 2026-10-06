import { bool, guid, obj, sha256, text } from '../../core/decode';

export interface ExpenseCreateReference { requestId: string; requestHash: string }
export function expenseCreateReference(raw: unknown): ExpenseCreateReference | null {
  try {
    if (!raw || typeof raw !== 'object' || Array.isArray(raw) || Object.keys(raw).length !== 2) return null;
    const v = raw as Record<string, unknown>;
    return { requestId: guid(v['requestId'], 'requestId'), requestHash: sha256(v['requestHash'], 'requestHash') };
  } catch { return null; }
}

const receipt = obj({ expenseId: guid, actorId: guid, requestId: guid, requestHash: sha256, status: text });
const lookup = obj({ found: bool, receipt: (value, path) => value === null ? null : receipt(value, path) });
export function decodeExpenseCreateLookup(raw: unknown, path = '') {
  const value = lookup(raw, path);
  if (value.found !== (value.receipt !== null)) throw new Error('Unsupported expense receipt state');
  return value;
}
export function decodeExpenseId(raw: unknown, path = ''): string { return guid(raw, path); }

function part(value: string): string {
  return `${new TextEncoder().encode(value).length}:${value}`;
}

/** Fingerprints the exact normalized form and source bytes without retaining the file in browser storage. */
export async function hashExpenseCreation(input: {
  firmId: string; actorId: string; requestId: string; date: string; category: string; payee: string;
  description: string; amount: string; currency: string; expenseAccountId: string; paymentAccountId: string; file: File;
}): Promise<string> {
  const amount = input.amount.trim();
  const [whole, fraction = ''] = amount.split('.');
  const canonicalAmount = `${whole.replace(/^0+(?=\d)/, '')}.${fraction.padEnd(2, '0')}`;
  const bytes = await input.file.arrayBuffer();
  const evidenceHash = Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', bytes)))
    .map(b => b.toString(16).padStart(2, '0')).join('');
  const values = [
    'firm-expense-create-v1', input.firmId.toLowerCase(), input.actorId.toLowerCase(), input.requestId.toLowerCase(),
    input.date, input.category.trim().toUpperCase(), input.payee.trim(), input.description.trim(), canonicalAmount,
    input.currency.trim().toUpperCase(), input.expenseAccountId.toLowerCase(), input.paymentAccountId.toLowerCase(),
    input.file.name.trim(), input.file.type.trim() || 'application/octet-stream', evidenceHash,
  ];
  return Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(values.map(part).join('')))))
    .map(b => b.toString(16).padStart(2, '0')).join('');
}
