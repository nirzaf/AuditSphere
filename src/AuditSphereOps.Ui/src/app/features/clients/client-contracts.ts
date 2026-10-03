import { arr, bool, decode, guid, instant, nat, nullable, obj, oneOf, str } from '../../core/decode';

export interface ClientLocation { engagementPage: number; engagementPageSize: number; contactPage: number; contactPageSize: number; }
export const defaultClientLocation: ClientLocation = { engagementPage: 0, engagementPageSize: 10, contactPage: 0, contactPageSize: 10 };
export function clientLocation(get: (key: string) => string | null): ClientLocation | null {
  const out = { ...defaultClientLocation };
  for (const key of ['engagementPage', 'contactPage'] as const) {
    const raw = get(key) ?? '0';
    if (!/^(0|[1-9][0-9]{0,4})$/.test(raw) || Number(raw) > 10000) return null;
    out[key] = Number(raw);
  }
  for (const key of ['engagementPageSize', 'contactPageSize'] as const) {
    const raw = get(key) ?? '10';
    if (!['10', '25', '50'].includes(raw)) return null;
    out[key] = Number(raw);
  }
  return out;
}
const revision = (v: unknown): string => {
  if (typeof v !== 'string' || !/^(0|[1-9][0-9]{0,18})$/.test(v) || BigInt(v) > 9223372036854775807n)
    throw new Error('Invalid exact generation');
  return v;
};
const decoder = obj({
  id: guid, name: str(2000), status: str(100), commercialName: nullable(str(2000)),
  registrationNumber: nullable(str(2000)), jurisdiction: nullable(str(200)), createdAt: instant,
  safetyGeneration: revision, canManageContacts: bool, canCreateEngagement: bool,
  metrics: obj({ engagements: nat, workBlocked: nat, contacts: nat }),
  paging: obj({ engagementPage: nat, engagementPageSize: nat, contactPage: nat, contactPageSize: nat }),
  engagements: arr(obj({ id: guid, serviceRoute: str(200), status: str(100), periodStart: str(100), periodEnd: str(100), professionalWorkBlocked: bool }), 50),
  contacts: arr(obj({ id: guid, name: str(2000), email: str(254), role: str(200), primary: bool }), 50),
  portalIntent: nullable(obj({ state: oneOf('AWAITING_ACCEPTANCE', 'READY_TO_INVITE', 'INVITED'), recipientEmail: str(254), updatedAt: instant, contactHasClientAccess: bool })),
});
export function decodeClient(value: unknown) {
  const v = decode(decoder, value);
  if (!clientLocation((key) => String(v.paging[key as keyof ClientLocation])) ||
    !Number.isFinite(Date.parse(v.createdAt)) || v.metrics.workBlocked > v.metrics.engagements ||
    v.engagements.length > v.paging.engagementPageSize || v.contacts.length > v.paging.contactPageSize ||
    v.engagements.length > v.metrics.engagements || v.contacts.length > v.metrics.contacts ||
    new Set(v.engagements.map(e => e.id.toLowerCase())).size !== v.engagements.length ||
    new Set(v.contacts.map(c => c.id.toLowerCase())).size !== v.contacts.length)
    throw new Error('Invalid client projection bounds');
  return v;
}
export type Client = ReturnType<typeof decodeClient>;
export function clientUtcTime(value: string): string {
  const date = new Date(value);
  return Number.isFinite(date.getTime()) ? date.toISOString().slice(0, 16).replace('T', ' ') + ' UTC' : 'Unsupported timestamp';
}
export function portalIntentExplanation(state: string): string {
  switch (state) {
    case 'AWAITING_ACCEPTANCE': return 'Recorded at conversion. Acceptance and Partner activation are required before invitation.';
    case 'READY_TO_INVITE': return 'An engagement is active. An administrator can invite this contact with reviewed client-scoped access.';
    case 'INVITED': return 'Portal onboarding was recorded. Current identity, grants and first-sign-in requirements are checked separately on every portal request.';
    default: return 'Portal intent is unavailable. Refresh before taking an action.';
  }
}
