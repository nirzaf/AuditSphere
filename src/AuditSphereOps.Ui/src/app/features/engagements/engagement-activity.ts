import { Component, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { arr, bool, date, guid, instant, int, nullable, obj, str } from '../../core/decode';
import { SHARED } from '../../core/ui';

const activityItemDecoder = obj({
  id: guid,
  source: int,
  kind: str(80),
  label: str(300),
  actorUserId: nullable(guid),
  atUtc: instant,
  currentStatus: nullable(str(50)),
  sourceHref: str(200),
});

const activityCursorDecoder = obj({ atUtc: instant, source: int, id: guid });
const activityPageDecoder = obj({
  engagementId: guid,
  from: date,
  to: date,
  snapshotAtUtc: instant,
  items: arr(activityItemDecoder, 100),
  hasMore: bool,
  nextCursor: nullable(activityCursorDecoder),
});

export type EngagementActivityItem = ReturnType<typeof activityItemDecoder>;
export type EngagementActivityCursor = ReturnType<typeof activityCursorDecoder>;
export type EngagementActivityPage = ReturnType<typeof activityPageDecoder>;

function utcDateValue(value: string): number | null {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return null;
  const time = Date.parse(`${value}T00:00:00.000Z`);
  return Number.isFinite(time) && new Date(time).toISOString().slice(0, 10) === value ? time : null;
}

function utcInstantValue(value: string): number | null {
  const time = Date.parse(value);
  return Number.isFinite(time) ? time : null;
}

export function activitySourceHrefAllowed(engagementId: string, href: string): boolean {
  const id = engagementId.toLowerCase();
  return new Set([
    `/app/engagements/${id}/pbc`,
    '/app/work',
    `/app/engagements/${id}/audit-fieldwork`,
    `/app/engagements/${id}/completion`,
  ]).has(href);
}

export function decodeEngagementActivity(value: unknown, expectedEngagementId: string,
  expectedFrom?: string, expectedTo?: string, path = ''): EngagementActivityPage {
  const page = activityPageDecoder(value, path);
  const expected = expectedEngagementId.toLowerCase();
  if (page.engagementId.toLowerCase() !== expected) throw new Error('Engagement context mismatch');
  if ((expectedFrom !== undefined && page.from !== expectedFrom) ||
      (expectedTo !== undefined && page.to !== expectedTo)) throw new Error('Activity date context mismatch');
  const from = utcDateValue(page.from);
  const to = utcDateValue(page.to);
  const snapshot = utcInstantValue(page.snapshotAtUtc);
  if (from === null || to === null || to < from || to - from >= 90 * 24 * 60 * 60 * 1000 || snapshot === null)
    throw new Error('Invalid activity date range');
  const endExclusive = to + 24 * 60 * 60 * 1000;
  for (const item of page.items) {
    const at = utcInstantValue(item.atUtc);
    if (item.source < 1 || item.source > 7 || !item.kind.trim() || !item.label.trim() ||
        (item.currentStatus !== null && !item.currentStatus.trim()) || at === null ||
        at < from || at >= endExclusive || at > snapshot ||
        item.id.toLowerCase() === '00000000-0000-0000-0000-000000000000' ||
        item.actorUserId?.toLowerCase() === '00000000-0000-0000-0000-000000000000' ||
        !activitySourceHrefAllowed(expected, item.sourceHref))
      throw new Error('Invalid activity item');
  }
  if (page.hasMore !== (page.nextCursor !== null)) throw new Error('Invalid activity cursor');
  if (page.nextCursor && (page.nextCursor.source < 1 || page.nextCursor.source > 7 ||
      page.nextCursor.id.toLowerCase() === '00000000-0000-0000-0000-000000000000' ||
      utcInstantValue(page.nextCursor.atUtc) === null))
    throw new Error('Invalid activity cursor');
  if (page.nextCursor) {
    const last = page.items[page.items.length - 1];
    if (!last || last.source !== page.nextCursor.source ||
        last.id.toLowerCase() !== page.nextCursor.id.toLowerCase() ||
        utcInstantValue(last.atUtc) !== utcInstantValue(page.nextCursor.atUtc))
      throw new Error('Invalid activity cursor');
  }
  return page;
}

export function engagementActivityUtcTime(value: string): string {
  const instantValue = utcInstantValue(value);
  return instantValue === null ? '—' : new Intl.DateTimeFormat('en-GB', {
    dateStyle: 'medium', timeStyle: 'short', timeZone: 'UTC',
  }).format(new Date(instantValue));
}

function defaultUtcDate(daysAgo: number): string {
  const date = new Date();
  date.setUTCHours(0, 0, 0, 0);
  date.setUTCDate(date.getUTCDate() - daysAgo);
  return date.toISOString().slice(0, 10);
}

@Component({
  selector: 'audit-engagement-activity',
  imports: [FormsModule, RouterLink, MatButtonModule, ...SHARED],
  template: `
    <section aria-labelledby="engagement-activity-heading">
      <h2 id="engagement-activity-heading">Activity</h2>
      <p>This view lists recorded facts; status badges are current observations, not a reconstructed status history.</p>
      <div class="activity-filters">
        <label>From (UTC)
          <input type="date" [ngModel]="from()" (ngModelChange)="changeFrom($event)" />
        </label>
        <label>To (UTC)
          <input type="date" [ngModel]="to()" (ngModelChange)="changeTo($event)" />
        </label>
        <button matButton type="button" (click)="refresh()">Refresh</button>
      </div>
      @if (rangeError()) {
        <p role="alert">{{ rangeError() }}</p>
      } @else if (activity.loading()) {
        <p role="status">Loading activity…</p>
      }
      @if (activity.error()) {
        <p role="alert">{{ activity.error() }} <button matButton type="button" (click)="refresh()">Retry</button></p>
      }
      @if (activity.data(); as page) {
        @if (page.items.length) {
          <ol aria-label="Recorded engagement activity">
            @for (item of page.items; track item.source + ':' + item.id) {
              <li>
                <time [attr.datetime]="item.atUtc">{{ utcTime(item.atUtc) }} UTC</time>
                <p>{{ item.label }}</p>
                <p><small>{{ sourceLabel(item.source) }} · {{ item.kind }}</small></p>
                @if (item.currentStatus !== null) {
                  <p>Current stored status: {{ item.currentStatus }}</p>
                }
                <a [routerLink]="item.sourceHref">Open source workspace</a>
              </li>
            }
          </ol>
        } @else {
          <p>No recorded activity in this date range.</p>
        }
        <nav aria-label="Activity pages">
          <button matButton type="button" (click)="next()" [disabled]="!page.hasMore || activity.loading()">
            Next
          </button>
        </nav>
      }
    </section>
  `,
})
export class EngagementActivity {
  readonly engagementId = input.required<string>();
  readonly from = signal(defaultUtcDate(89));
  readonly to = signal(defaultUtcDate(0));
  readonly snapshotAtUtc = signal<string | null>(null);
  readonly cursor = signal<EngagementActivityCursor | null>(null);
  readonly rangeError = signal('');
  private readonly api = inject(Api);
  private readonly requestUrl = computed(() => {
    const id = this.engagementId().toLowerCase();
    const from = this.from();
    const to = this.to();
    const fromMs = utcDateValue(from);
    const toMs = utcDateValue(to);
    const todayMs = utcDateValue(defaultUtcDate(0));
    if (fromMs === null || toMs === null || todayMs === null || fromMs > toMs || toMs > todayMs ||
        toMs - fromMs >= 90 * 24 * 60 * 60 * 1000) return null;
    const params = new URLSearchParams({ from, to, pageSize: '50' });
    const snapshot = this.snapshotAtUtc();
    const cursor = this.cursor();
    if (snapshot && cursor) {
      params.set('snapshotAtUtc', snapshot);
      params.set('afterAtUtc', cursor.atUtc);
      params.set('afterSource', String(cursor.source));
      params.set('afterId', cursor.id);
    }
    return `/api/ui/engagements/${id}/activity?${params.toString()}`;
  });
  readonly activity = this.api.resource(
    () => this.rangeError() ? null : this.requestUrl(),
    (value, path) => decodeEngagementActivity(value, this.engagementId(), this.from(), this.to(), path),
    'Activity is unavailable for your current role or engagement.',
  );
  readonly utcTime = engagementActivityUtcTime;

  changeFrom(value: string): void {
    this.from.set(value);
    this.resetPaging();
    this.validateRange();
  }

  changeTo(value: string): void {
    this.to.set(value);
    this.resetPaging();
    this.validateRange();
  }

  refresh(): void {
    const paged = this.snapshotAtUtc() !== null || this.cursor() !== null;
    this.resetPaging();
    if (!paged) this.activity.reload();
  }

  next(): void {
    const page = this.activity.data();
    if (!page?.hasMore || !page.nextCursor) return;
    this.snapshotAtUtc.set(page.snapshotAtUtc);
    this.cursor.set(page.nextCursor);
  }

  sourceLabel(source: number): string {
    return ({
      1: 'PBC communication',
      2: 'PBC upload',
      3: 'Operational task',
      4: 'Review note',
      5: 'Review note thread',
      6: 'Correspondence',
      7: 'Commercial notification',
    } as Record<number, string>)[source] ?? 'Recorded activity';
  }

  private resetPaging(): void {
    this.snapshotAtUtc.set(null);
    this.cursor.set(null);
  }

  private validateRange(): void {
    const from = utcDateValue(this.from());
    const to = utcDateValue(this.to());
    const today = utcDateValue(defaultUtcDate(0));
    this.rangeError.set(from === null || to === null || today === null || from > to || to > today ||
      to - from >= 90 * 24 * 60 * 60 * 1000
      ? 'Choose a UTC date range no longer than 90 days.'
      : '');
  }
}
