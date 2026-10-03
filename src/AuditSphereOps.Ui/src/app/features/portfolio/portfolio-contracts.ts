import { Injectable, inject } from '@angular/core';
import { SessionService } from '../../core/session';
import { arr, bool, decode, guid, instant, nat, obj, str } from '../../core/decode';

export interface ClientRow { id: string; name: string; engagements: number; }
export interface PortfolioPage { items: ClientRow[]; total: number; page: number; pageSize: number; }
const pageDecoder = obj({ items: arr(obj({ id: guid, name: str(2000), engagements: nat }), 100), total: nat, page: nat, pageSize: nat });
export function decodePortfolio(value: unknown): PortfolioPage {
  const v = decode(pageDecoder, value);
  if (v.page > 10000 || v.pageSize < 1 || v.pageSize > 100 || v.items.length > v.pageSize || v.items.length > v.total)
    throw new Error('Invalid page bounds');
  return v;
}
const revision = (v: unknown): string => {
  if (typeof v !== 'string' || !/^[1-9][0-9]{0,18}$/.test(v)) throw new Error('Invalid exact revision');
  return v;
};
const workspaceDecoder = obj({
  search: str(100), clients: (v: unknown) => decodePortfolio(v),
  metrics: obj({ clients: nat, engagements: nat, openHolds: nat, pendingOperations: nat, readyCandidates: nat, issuedReleases: nat }),
  hasActiveMicrosoftConfiguration: bool, recentLimit: nat, candidateTotal: nat, packageTotal: nat,
  candidates: arr(obj({ id: guid, clientId: guid, engagementId: guid, clientName: str(2000), targetKind: str(50), targetRevision: revision, status: str(200), createdAt: instant }), 25),
  packages: arr(obj({ id: guid, clientId: guid, engagementId: guid, clientName: str(2000), framework: str(200), periodStart: str(100), periodEnd: str(100), status: str(200) }), 25),
});
export function decodePortfolioWorkspace(value: unknown) {
  const v = decode(workspaceDecoder, value);
  if (v.recentLimit !== 25 || v.candidates.length > v.candidateTotal || v.packages.length > v.packageTotal ||
    v.clients.total > v.metrics.clients || v.metrics.readyCandidates > v.candidateTotal)
    throw new Error('Invalid portfolio bounds');
  return v;
}
/** Paging only the already authorized recent window; never requests or implies a larger record set. */
export function portfolioWindow<T>(rows: readonly T[], requestedPage: number, pageSize: number) {
  if (rows.length > 25 || !Number.isSafeInteger(requestedPage) || requestedPage < 0 || (pageSize !== 10 && pageSize !== 25))
    throw new Error('Invalid recent window');
  const page = Math.min(requestedPage, Math.max(0, Math.ceil(rows.length / pageSize) - 1));
  return { items: rows.slice(page * pageSize, (page + 1) * pageSize), total: rows.length, page, pageSize };
}
export interface PortfolioLocation { search: string; page: number; pageSize: number; selected: string | null; }
export function portfolioLocation(get: (key: string) => string | null): PortfolioLocation | null {
  const search = get('search') ?? '', raw = get('page') ?? '0', selected = get('selected'), size = get('pageSize') ?? '25';
  if (search.length > 100 || !/^(0|[1-9][0-9]{0,4})$/.test(raw) || Number(raw) > 10000) return null;
  if (size !== '10' && size !== '25') return null;
  if (selected && !/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(selected)) return null;
  return { search: search.trim(), page: Number(raw), pageSize: Number(size), selected };
}

/** Memory-only return navigation, owned by the exact local session. No protected result is cached. */
@Injectable({ providedIn: 'root' })
export class PortfolioNavigation {
  private readonly session = inject(SessionService);
  private owner = '';
  private saved: PortfolioLocation | null = null;
  private key(): string {
    const s = this.session.current();
    return s?.staff ? `${s.firmId}:${s.userId}:${s.generation}:${this.session.invalidation()}` : '';
  }
  read(): PortfolioLocation | null {
    if (!this.key() || this.owner !== this.key()) { this.saved = null; this.owner = ''; }
    return this.saved;
  }
  remember(location: PortfolioLocation): void { this.owner = this.key(); this.saved = this.owner ? { ...location } : null; }
  params(): Record<string, string | number | null> { return { ...(this.read() ?? { search: '', page: 0, pageSize: 25, selected: null }) }; }
}
