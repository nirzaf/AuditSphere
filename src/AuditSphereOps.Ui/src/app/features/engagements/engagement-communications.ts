import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import { guidPattern } from '../../core/contracts';
import { arr, decode, guid, instant, int, nullable, obj, oneOf, str } from '../../core/decode';
import { clientUtcTime } from '../clients/client-contracts';

const entryDecoder = obj({
  id: guid,
  source: oneOf('PBC', 'CORRESPONDENCE', 'COMMERCIAL'),
  kind: str(200),
  subject: str(1000),
  recipient: str(1000),
  status: oneOf('QUEUED', 'DISPATCHED', 'PROVIDER_ACCEPTED', 'DELIVERED', 'FAILED', 'UNKNOWN', 'RECORDED'),
  recordedAtUtc: instant,
  deliveredAtUtc: nullable(instant),
  sourceLink: str(500),
});
const timelineDecoder = obj({ engagementId: guid, totalCount: int, entries: arr(entryDecoder, 200) });

export function decodeEngagementCommunications(value: unknown) {
  const timeline = decode(timelineDecoder, value);
  // Only internal application routes may be rendered as links.
  for (const entry of timeline.entries)
    if (!/^\/app(?:\/[A-Za-z0-9_~-]{1,100}){0,8}$/.test(entry.sourceLink)) throw new Error('Unsupported source link');
  return timeline;
}
export type EngagementCommunications = ReturnType<typeof decodeEngagementCommunications>;

/** What each recorded status does and does not establish. Provider acceptance is never shown as delivery. */
export const COMMUNICATION_STATUS_LABELS: Record<string, string> = {
  QUEUED: 'Queued, not sent',
  DISPATCHED: 'Handed to the mail provider, outcome not confirmed',
  PROVIDER_ACCEPTED: 'Accepted by the mail provider',
  DELIVERED: 'Delivery confirmed',
  FAILED: 'Failed',
  UNKNOWN: 'Outcome unknown',
  RECORDED: 'Dispatch recorded, no provider receipt',
};

/**
 * Engagement communication timeline (PM-07). It lists the outbound mail the server recorded for this engagement and links
 * each row to its source screen. It is read-only and changes no request, review or delivery state.
 */
@Component({
  selector: 'audit-engagement-communications',
  imports: [RouterLink, MatButtonModule],
  template: `
    <section aria-label="Engagement communications">
      <h2>Communications</h2>
      <p>Outbound mail recorded for this engagement. A status shows what was recorded, not whether the recipient read the message.</p>
      @if (loading()) { <p role="status">Loading communications…</p> }
      @if (error()) { <p role="alert">{{ error() }} <button matButton type="button" (click)="load()">Retry</button></p> }
      @if (data(); as timeline) {
        @if (timeline.entries.length) {
          @if (timeline.totalCount > timeline.entries.length) {
            <p>Showing the newest {{ timeline.entries.length }} of {{ timeline.totalCount }} recorded communications.</p>
          }
          <div class="table-scroll"><table>
            <caption>Recorded outbound communications, newest first</caption>
            <thead><tr><th>Recorded (UTC)</th><th>Type</th><th>Subject</th><th>Recipient</th><th>Status</th><th>Source</th></tr></thead>
            <tbody>@for (entry of timeline.entries; track entry.id) {
              <tr>
                <td>{{ utcTime(entry.recordedAtUtc) }}</td>
                <td>{{ entry.kind }}</td>
                <td>{{ entry.subject || '—' }}</td>
                <td>{{ entry.recipient || '—' }}</td>
                <td>{{ statusLabel(entry.status) }}@if (entry.deliveredAtUtc) { <small> ({{ utcTime(entry.deliveredAtUtc) }})</small> }</td>
                <td><a [routerLink]="entry.sourceLink">Open source</a></td>
              </tr>
            }</tbody>
          </table></div>
        } @else {
          <p>No outbound communication is recorded for this engagement.</p>
        }
      }
    </section>
  `,
})
export class EngagementCommunicationsPanel {
  readonly engagementId = input.required<string>();
  readonly data = signal<EngagementCommunications | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly utcTime = clientUtcTime;

  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private request?: Subscription;
  private destroyed = false;

  constructor() {
    effect(() => {
      const id = this.engagementId();
      this.session.invalidation();
      const staff = this.session.current()?.staff;
      untracked(() => {
        this.request?.unsubscribe();
        this.data.set(null);
        this.error.set('');
        this.loading.set(false);
        if (staff && id) this.load();
      });
    });
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.request?.unsubscribe();
    });
  }

  statusLabel(status: string): string {
    return COMMUNICATION_STATUS_LABELS[status] ?? 'Outcome unknown';
  }

  load(): void {
    const id = this.engagementId()?.toLowerCase();
    if (!id || !guidPattern.test(id)) {
      this.error.set('Invalid engagement identifier.');
      return;
    }
    this.request?.unsubscribe();
    this.loading.set(true);
    this.error.set('');
    const generation = this.session.invalidation();
    this.request = this.http
      .get<unknown>(`/api/ui/engagements/${id}/communications`)
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (this.destroyed || generation !== this.session.invalidation()) return;
          try {
            const timeline = decodeEngagementCommunications(value);
            if (timeline.engagementId.toLowerCase() !== id) throw new Error('Engagement context mismatch');
            this.data.set(timeline);
          } catch {
            this.data.set(null);
            this.error.set('Unsupported communications response format.');
          }
          this.loading.set(false);
        },
        error: (err) => {
          if (this.destroyed || generation !== this.session.invalidation()) return;
          this.data.set(null);
          this.loading.set(false);
          if (err.status === 401) {
            this.session.clear();
            return;
          }
          // The timeline is limited to engagement management; other staff simply do not see it.
          this.error.set(err.status === 403 ? '' : 'Communications unavailable.');
        },
      });
  }
}
