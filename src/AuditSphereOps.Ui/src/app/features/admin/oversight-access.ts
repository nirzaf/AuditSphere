import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { arr, bool, DecodeError, guid, instant, nullable, obj, oneOf, str } from '../../core/decode';
import { SHARED } from '../../core/ui';

const row = obj({ id: guid, actorUserId: guid, sessionEpoch: str(19), requestId: guid, operation: str(100),
  resourceKind: oneOf('FIRM', 'INVOICE', 'GROUP', 'CONSOLIDATION_SCOPE', 'EXTERNAL_PACK'), resourceId: nullable(guid),
  authorizedAt: instant, outcome: oneOf('DISCLOSURE_AUTHORIZED') });
const page = obj({ items: arr(row, 25), hasMore: bool, before: nullable(instant), beforeId: nullable(guid) });
export function decodeOversightAccess(value: unknown, path: string) {
  const result = page(value, path), last = result.items.at(-1);
  if (result.items.some(x => !/^[1-9]\d{0,18}$/.test(x.sessionEpoch) || BigInt(x.sessionEpoch) > 9223372036854775807n ||
      (x.resourceKind === 'FIRM') !== (x.resourceId === null)) ||
      result.hasMore && result.items.length !== 25 ||
      (last ? result.before !== last.authorizedAt || result.beforeId !== last.id : result.before !== null || result.beforeId !== null || result.hasMore))
    throw new DecodeError('Unsupported oversight access history.');
  return result;
}
type Cursor = { before: string; beforeId: string };

@Component({
  selector: 'audit-oversight-access', imports: [RouterLink, MatButtonModule, ...SHARED],
  template: `
    <nav aria-label="Administration location"><a routerLink="/app/administration">Administration</a> / Oversight access</nav>
    <audit-page-header title="Administrator oversight access" description="Retained authorization evidence for read-only administrative access. These events do not prove delivery, acknowledgement or professional review." />
    <p>Only current firm-wide Staff Administrators can view this history. Each request is recorded separately. Search text, document contents and credentials are not recorded here.</p>
    <button matButton="outlined" (click)="newest()">Refresh newest events</button>
    <audit-state [loading]="events.loading()" [error]="events.error()" label="oversight access history" />
    @if (events.data(); as p) {
      <div class="table-scroll"><table><caption>Oversight disclosures authorized for this firm</caption>
        <thead><tr><th>Authorized at (UTC)</th><th>Operation</th><th>Resource scope</th><th>Actor and session</th><th>Request evidence</th></tr></thead>
        <tbody>@for (r of p.items; track r.id) {
          <tr><td>{{ r.authorizedAt.slice(0, 19).replace('T', ' ') }}</td><td>{{ r.operation }}<small>Disclosure authorized</small></td>
            <td>{{ r.resourceKind }}<small>{{ r.resourceId ?? 'Current firm' }}</small></td>
            <td><code>{{ r.actorUserId }}</code><small>Session {{ r.sessionEpoch }}</small></td>
            <td><code>{{ r.requestId }}</code><small>Event {{ r.id }}</small></td></tr>
        } @empty { <tr><td colspan="5">No oversight access events in this page.</td></tr> }</tbody></table></div>
      <nav class="actions" aria-label="Oversight access pages">
        <button matButton (click)="newer()" [disabled]="events.loading() || !previous().length">Newer events</button>
        <button matButton (click)="older()" [disabled]="events.loading() || !p.hasMore">Older events</button>
      </nav>
    }
  `,
  styles: `small { display: block; } code { overflow-wrap: anywhere; }`,
})
export class OversightAccessHistory {
  private readonly api = inject(Api);
  readonly cursor = signal<Cursor | null>(null);
  readonly previous = signal<(Cursor | null)[]>([]);
  readonly events = this.api.resource(() => {
    const c = this.cursor();
    return '/api/ui/administration/oversight-access' + (c ? `?before=${encodeURIComponent(c.before)}&beforeId=${c.beforeId}` : '');
  }, decodeOversightAccess, 'Current firm-wide Administrator access is required.');
  older(): void {
    const p = this.events.data();
    if (this.events.loading() || !p?.hasMore || !p.before || !p.beforeId) return;
    this.previous.update(x => [...x, this.cursor()]);
    this.cursor.set({ before: p.before, beforeId: p.beforeId });
  }
  newer(): void {
    if (this.events.loading() || !this.previous().length) return;
    this.cursor.set(this.previous().at(-1) ?? null);
    this.previous.update(x => x.slice(0, -1));
  }
  newest(): void { this.cursor.set(null); this.previous.set([]); this.events.reload(); }
}
