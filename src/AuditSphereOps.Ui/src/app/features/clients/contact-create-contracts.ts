import { arr, bool, guid, instant, nullable, obj, sha256, str } from '../../core/decode';
export interface ContactFields { name: string; email: string; role: string; primary: boolean }
export const emptyContact = (): ContactFields => ({ name: '', email: '', role: '', primary: false });
const fields = obj({ name: str(200), email: str(254), role: str(200), primary: bool });
export function contactFields(raw: unknown): ContactFields | null {
  try {
    if (!raw || typeof raw !== 'object' || Object.keys(raw).length !== 4) return null;
    const v = fields(raw, 'fields');
    return [v.name,v.email,v.role].some(s => /[\u0000-\u001f\u007f]/.test(s)) ? null : v;
  } catch { return null; }
}
export function validContact(v: ContactFields): boolean {
  return !!contactFields(v) && !!v.name.trim() && !!v.role.trim() && /^[^\s@]+@[^\s@]+$/.test(v.email.trim());
}
const generation = (v: unknown): string => {
  if (typeof v !== 'string' || !/^[1-9]\d{0,18}$/.test(v) || BigInt(v) > 9223372036854775807n) throw new Error('Unsupported generation');
  return v;
};
const primary = obj({ id: guid, name: str(200) });
export const decodeContactState = obj({ clientId: guid, clientName: str(500), safetyGeneration: generation, reviewBasis: sha256, currentPrimary: arr(primary,50) });
export const decodeContactPreview = obj({ clientId: guid, requestId: guid, reviewBasis: sha256, requestHash: sha256, fields, replacedPrimary: arr(primary,50) });
export const decodeContactReceipt = obj({ id: guid, clientId: guid, contactId: guid, actorId: guid, requestId: guid, requestHash: sha256,
  reviewBasis: sha256, resultGeneration: generation, createdAt: instant });
const lookup = obj({ found: bool, receipt: nullable(decodeContactReceipt) });
export function decodeContactLookup(raw: unknown, path = '') {
  const v = lookup(raw,path); if (v.found !== (v.receipt !== null)) throw new Error('Unsupported receipt state'); return v;
}
