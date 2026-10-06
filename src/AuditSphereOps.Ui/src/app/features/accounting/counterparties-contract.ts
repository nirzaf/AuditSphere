import { guidPattern } from '../../core/contracts';
interface Party { id: string; clientId: string; legalName: string; displayName: string; role: string; address: string; country: string; taxIdentifier: string; contactDetails: string; paymentTerms: string; defaultCurrency: string; externalSystem: string; externalReference: string; createdByUserId: string; createdAt: string; revision: string; effectiveAmendmentId: string | null }
interface PartyList { clientId: string; role: string | null; page: number; pageSize: number; total: number; bookkeepingActive: boolean; counterparties: Party[] }
export function decodeCounterparties(value: unknown, client: string, role: string | null, page: number): PartyList {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('Invalid party list');
  const v = value as Record<string, unknown>;
  if (v['clientId'] !== client || v['role'] !== role || v['page'] !== page || v['pageSize'] !== 25 || typeof v['bookkeepingActive'] !== 'boolean' ||
      !Number.isSafeInteger(v['total']) || Number(v['total']) < 0 || !Array.isArray(v['counterparties']) || v['counterparties'].length > 25 || v['counterparties'].length > Number(v['total'])) throw new Error('Invalid scoped party list');
  const seen = new Set<string>();
  for (const item of v['counterparties']) {
    if (!item || typeof item !== 'object' || Array.isArray(item)) throw new Error('Invalid party');
    const p = item as Record<string, unknown>;
    if (p['clientId'] !== client || typeof p['id'] !== 'string' || !guidPattern.test(p['id']) || seen.has(p['id']) ||
        typeof p['createdByUserId'] !== 'string' || !guidPattern.test(p['createdByUserId']) ||
        !['CUSTOMER', 'SUPPLIER', 'BOTH'].includes(String(p['role'])) || (role && p['role'] !== role && !(role !== 'BOTH' && p['role'] === 'BOTH')) ||
        !['legalName', 'displayName', 'address', 'country', 'taxIdentifier', 'contactDetails', 'paymentTerms', 'defaultCurrency', 'externalSystem', 'externalReference', 'createdAt'].every(k => typeof p[k] === 'string') ||
        typeof p['revision'] !== 'string' || !/^[1-9][0-9]{0,18}$/.test(p['revision']) || (p['effectiveAmendmentId'] !== null && (typeof p['effectiveAmendmentId'] !== 'string' || !guidPattern.test(p['effectiveAmendmentId']))) ||
        !String(p['legalName']).trim() || !String(p['displayName']).trim() || !/^[A-Z]{2}$/.test(String(p['country'])) || !/^(?:[A-Z]{3})?$/.test(String(p['defaultCurrency']))) throw new Error('Invalid party profile');
    seen.add(p['id']);
  }
  return value as PartyList;
}
