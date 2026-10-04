import { arr, bool, decode, guid, instant, nat, nullable, obj, str } from '../../core/decode';
export interface EngagementLocation {
  holdPage: number;
  holdPageSize: number;
}
export function engagementLocation(get: (key: string) => string | null): EngagementLocation | null {
  const page = get('holdPage') ?? '0',
    size = get('holdPageSize') ?? '10';
  if (
    !/^(0|[1-9][0-9]{0,4})$/.test(page) ||
    Number(page) > 10000 ||
    !['10', '25', '50'].includes(size)
  )
    return null;
  return { holdPage: Number(page), holdPageSize: Number(size) };
}
const revision = (v: unknown): string => {
  if (typeof v !== 'string' || !/^[1-9][0-9]{0,18}$/.test(v) || BigInt(v) > 9223372036854775807n)
    throw new Error('Invalid exact generation');
  return v;
};
const decoder = obj({
  id: guid,
  clientId: guid,
  clientName: str(2000),
  serviceRoute: str(200),
  serviceProfileId: str(200),
  status: str(100),
  periodStart: str(100),
  periodEnd: str(100),
  createdAt: instant,
  generation: revision,
  professionalWorkBlocked: bool,
  canActivate: bool,
  canViewClientProfile: bool,
  canPrepareAccounting: bool,
  holdMetrics: obj({ total: nat, active: nat, released: nat }),
  paging: obj({ holdPage: nat, holdPageSize: nat }),
  holds: arr(
    obj({
      id: guid,
      kind: str(200),
      reason: str(20000),
      released: bool,
      createdAt: instant,
      releasedAt: nullable(instant),
    }),
    50,
  ),
});
export function decodeEngagement(value: unknown) {
  const v = decode(decoder, value);
  if (
    !engagementLocation((k) => String(v.paging[k as keyof EngagementLocation])) ||
    v.holdMetrics.total !== v.holdMetrics.active + v.holdMetrics.released ||
    v.holds.length > v.paging.holdPageSize ||
    v.holds.length > v.holdMetrics.total ||
    new Set(v.holds.map((h) => h.id.toLowerCase())).size !== v.holds.length ||
    !Number.isFinite(Date.parse(v.createdAt)) ||
    v.holds.some(
      (h) =>
        !Number.isFinite(Date.parse(h.createdAt)) ||
        (h.releasedAt !== null && !Number.isFinite(Date.parse(h.releasedAt))),
    )
  )
    throw new Error('Invalid engagement projection bounds');
  return v;
}
export type Engagement = ReturnType<typeof decodeEngagement>;
