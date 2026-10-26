import { Component, computed, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

export type AccessHistoryRow = {
  at: string; operation: string; target: string; change: string; reason: string; actor: string; result: string;
  microsoftOperation: string | null;
};
export type AccessCategory = 'ROLES' | 'MICROSOFT' | 'SITES' | 'OTHER';
export interface AccessHistoryEntry {
  key: string; at: string; ms: number; when: string; category: AccessCategory; event: string; operation: string;
  subject: string; subjectFull: string; summary: string; reason: string; actor: string; tags: string[];
  details: { label: string; value: string }[]; records: number; haystack: string;
}

export const CATEGORY_LABELS: Record<AccessCategory, string> = {
  ROLES: 'Roles and access', MICROSOFT: 'Microsoft 365 connection', SITES: 'Sites and folders', OTHER: 'Other',
};
const EVENT_LABELS: Record<string, string> = {
  ROLE_GRANTED: 'Role granted', ROLE_REVOKED: 'Role revoked', ROLE_INVITATION_COPIED: 'Invitation link copied',
  CAPABILITIES_VERIFIED: 'Capabilities verified', TENANT_CONSENT_VERIFIED: 'Tenant consent verified',
  TENANT_CONSENT_IDENTITY_RETURNED: 'Consent sign-in returned', TENANT_SETUP_METADATA_SAVED: 'Tenant setup saved',
  SELECTED_RESOURCE_DRAFT_SAVED: 'Site selection saved', SELECTED_RESOURCE_BOUNDARY_VERIFIED: 'Site boundary verified',
  FOLDER_TEMPLATE_SAVED: 'Folder template saved', FOLDER_TEMPLATE_APPROVED: 'Folder template approved',
};
const TAG_LABELS: Record<string, string> = {
  OWNER_DIRECTED_OPERATOR: 'Owner-directed', ADMIN_ACTION: 'Admin action', BOOTSTRAP: 'Bootstrap',
  APPROVED_ROSTER: 'Approved roster', VERIFIED_SIGN_IN: 'Verified sign-in', EXPIRY: 'Expiry', APPLIED: 'Applied',
  RECORDED: 'Recorded', VERIFIED: 'Verified', SAVED_LOCAL_METADATA: 'Saved locally', REDEEMING: 'Redeeming',
};
// Role order used by Users & Access role assignment; unknown roles follow alphabetically.
const ROLE_ORDER = ['Administrator', 'Partner', 'Manager', 'Senior', 'Staff', 'RelationshipManager', 'FinanceManager',
  'FinanceReviewer', 'ClientUser'];
const roleRank = (role: string) => { const i = ROLE_ORDER.indexOf(role); return i < 0 ? ROLE_ORDER.length : i; };
const HASH = /\b[0-9a-f]{64}\b/gi;
const GUID = /\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b/gi;
const CODE = /^[A-Z][A-Z0-9_]*$/;
const WHEN = new Intl.DateTimeFormat('en-GB', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit',
  minute: '2-digit', hourCycle: 'h23', timeZone: 'UTC' });

/** Shortens 64-hex digests and GUIDs to their first eight characters; the full value stays in the details. */
export function shorten(value: string): string {
  return value.replace(HASH, m => m.slice(0, 8) + '…').replace(GUID, m => m.slice(0, 8) + '…');
}

/** ROLE_GRANTED → "Role granted"; RelationshipManager → "Relationship Manager"; other text is returned as is. */
export function humanize(value: string): string {
  if (CODE.test(value)) { const text = value.toLowerCase().replace(/_/g, ' '); return text.charAt(0).toUpperCase() + text.slice(1); }
  return /^[A-Z][A-Za-z]+$/.test(value) ? value.replace(/([a-z])([A-Z])/g, '$1 $2') : value;
}

const humanizeList = (value: string) =>
  /^[A-Z0-9_]+(,[A-Z0-9_]+)+$/.test(value) ? value.split(',').map(humanize).join(', ') : humanize(value);

function categoryOf(operation: string): AccessCategory {
  if (/^(ROLE_|USER_|INVITATION|ACCESS_)/.test(operation)) return 'ROLES';
  if (/^(TENANT_|CAPABILIT|CONSENT)/.test(operation)) return 'MICROSOFT';
  if (/^(SELECTED_RESOURCE|FOLDER_TEMPLATE|SITE_)/.test(operation)) return 'SITES';
  return 'OTHER';
}

/** The role a role-change record grants or revokes, from either "Prior → New" or "NONE -> Role@SCOPE". */
function roleOf(row: AccessHistoryRow): { role: string; scope: string } | null {
  if (row.operation !== 'ROLE_GRANTED' && row.operation !== 'ROLE_REVOKED') return null;
  const parts = row.change.split(/→|->/).map(x => x.trim());
  if (parts.length !== 2) return null;
  const side = row.operation === 'ROLE_REVOKED' ? parts[0] || parts[1] : parts[1] || parts[0];
  if (!side || side === 'NONE' || side === '-') return null;
  const [role, scope = ''] = side.split('@');
  return { role: role.trim(), scope: scope.trim() };
}

function changeSummary(change: string): string {
  const parts = change.split(/→|->/).map(x => x.trim());
  if (parts.length !== 2) return shorten(humanizeList(change.trim() || '—'));
  const [before, after] = parts;
  if (!after || after === '-') return before && before !== '-' && before !== 'NONE' ? `Cleared ${shorten(humanizeList(before))}` : '—';
  if (!before || before === '-' || before === 'NONE') return shorten(humanizeList(after));
  if (before === after) return `Unchanged · ${shorten(after)}`;
  return `${shorten(humanizeList(before))} → ${shorten(humanizeList(after))}`;
}

type Draft = {
  rows: AccessHistoryRow[]; operation: string; target: string; actor: string; lastMs: number;
  roles: Map<string, string> | null;
};
const BATCH_WINDOW_MS = 5000;

/**
 * Turns the merged audit rows (role evidence and Microsoft 365 administration events, newest first) into concise entries.
 * The two records written for one role change, and role changes made together for one person, become one entry; identical
 * consecutive records are counted rather than repeated. Every original value remains in the entry's details and search text.
 */
export function summarizeAccessHistory(rows: readonly AccessHistoryRow[]): AccessHistoryEntry[] {
  const drafts: Draft[] = [];
  for (const row of rows) {
    const ms = Date.parse(row.at);
    const role = roleOf(row);
    const last = drafts[drafts.length - 1];
    const sameEvent = !!last && last.operation === row.operation && last.target === row.target && last.actor === row.actor &&
      Math.abs(last.lastMs - ms) <= BATCH_WINDOW_MS &&
      (role ? last.roles !== null : last.roles === null && last.rows[0].change === row.change &&
        last.rows[0].reason === row.reason && last.rows[0].result === row.result);
    if (sameEvent) {
      last.rows.push(row); last.lastMs = ms;
      if (role && !last.roles!.has(role.role)) last.roles!.set(role.role, role.scope);
      else if (role && role.scope && !last.roles!.get(role.role)) last.roles!.set(role.role, role.scope);
      continue;
    }
    drafts.push({ rows: [row], operation: row.operation, target: row.target, actor: row.actor, lastMs: ms,
      roles: role ? new Map([[role.role, role.scope]]) : null });
  }
  return drafts.map((d, index) => entryFrom(d, index));
}

function entryFrom(d: Draft, index: number): AccessHistoryEntry {
  const first = d.rows[0];
  const ms = Date.parse(first.at);
  const results = unique(d.rows.map(r => r.result));
  const tags = unique(results.map(r => TAG_LABELS[r] ?? (CODE.test(r) && r.length <= 24 ? humanize(r) : '')).filter(Boolean));
  const conciseTags = tags.length > 1 ? tags.filter(t => t !== 'Applied') : tags;
  // A reason that only repeats a source or result code adds nothing next to its tag.
  const reasons = unique(d.rows.map(r => r.reason.trim()).filter(r => r && r !== '-'));
  const prose = reasons.filter(r => !CODE.test(r));
  const reason = (prose.length ? prose : reasons.map(humanize)).join(' ') || 'No reason recorded.';
  let event = EVENT_LABELS[d.operation] ?? humanize(d.operation);
  let summary: string;
  if (d.roles) {
    const roles = [...d.roles.keys()].sort((a, b) => roleRank(a) - roleRank(b) || a.localeCompare(b));
    const scopes = unique([...d.roles.values()].filter(Boolean));
    if (roles.length > 1) event = `${roles.length} ${d.operation === 'ROLE_REVOKED' ? 'roles revoked' : 'roles granted'}`;
    summary = roles.map(humanize).join(', ') +
      (scopes.length === 1 ? ` · ${scopes[0] === 'FIRM_WIDE' ? 'firm-wide' : humanize(scopes[0]).toLowerCase()}` : '');
  } else summary = changeSummary(first.change);
  const references = unique(results.filter(r => !TAG_LABELS[r] && !(CODE.test(r) && r.length <= 24)));
  const details = [
    { label: 'Reason', value: reason },
    { label: 'Recorded', value: unique(d.rows.map(r => r.at)).join(', ') },
    { label: 'Event code', value: d.operation },
    { label: 'Subject', value: first.target || '—' },
    { label: 'Recorded changes', value: unique(d.rows.map(r => r.change)).join('; ') },
    ...(references.length ? [{ label: 'Result', value: references.join('; ') }] : []),
    ...unique(d.rows.map(r => r.microsoftOperation ?? '').filter(Boolean)).map(value => ({ label: 'Microsoft operation', value })),
    { label: 'Log records', value: String(d.rows.length) },
  ];
  const subject = first.target && first.target !== '-' ? shorten(first.target) : '—';
  const when = Number.isNaN(ms) ? first.at : WHEN.format(ms);
  const haystack = [event, d.operation, CATEGORY_LABELS[categoryOf(d.operation)], subject, first.target, summary, reason,
    d.actor, ...tags, ...results, ...d.rows.map(r => r.change), ...d.rows.map(r => r.microsoftOperation ?? ''), when]
    .join(' ').toLowerCase();
  return { key: `${first.at}|${d.operation}|${index}`, at: first.at, ms, when, category: categoryOf(d.operation), event,
    operation: d.operation, subject, subjectFull: first.target, summary, reason, actor: d.actor, tags: conciseTags, details,
    records: d.rows.length, haystack };
}

function unique<T>(values: T[]): T[] { return [...new Set(values)]; }

const PAGE = 25;
const PERIODS = [['ALL', 'All time'], ['7', 'Last 7 days'], ['30', 'Last 30 days'], ['90', 'Last 90 days']] as const;

/** Searchable, concise view of the firm's access and Microsoft 365 administration audit trail. */
@Component({
  selector: 'audit-access-history',
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  styles: [`
    .history-head { display: flex; flex-wrap: wrap; align-items: baseline; justify-content: space-between; gap: .25rem 1rem; }
    .history-head h2 { margin: 0; }
    .history-filters { display: grid; grid-template-columns: minmax(14rem, 2fr) repeat(2, minmax(10rem, 1fr)) auto; gap: .75rem;
      align-items: center; margin: 1rem 0 .5rem; }
    .history-filters button { white-space: nowrap; }
    @media (max-width: 760px) { .history-filters { grid-template-columns: 1fr; } .history-filters button { justify-self: start; } }
    /* Narrow screens scroll the table inside its card rather than squeezing the change text into a sliver. */
    table { width: 100%; min-width: 54rem; }
    th:nth-child(4) { min-width: 16rem; }
    td { vertical-align: top; }
    .event { display: block; font-weight: 600; color: var(--as-ink, #12385d); }
    .meta { display: flex; flex-wrap: wrap; gap: .25rem; margin-top: .25rem; }
    .tag { padding: .05rem .45rem; border-radius: 999px; background: var(--as-surface-sunken, #f6f8fb);
      border: 1px solid var(--as-line, #dfe6ee); font-size: .75rem; color: var(--as-ink-soft, #33506e); white-space: nowrap; }
    .category { font-size: .75rem; color: var(--as-text-muted, #52667a); }
    .reason { display: -webkit-box; -webkit-line-clamp: 1; -webkit-box-orient: vertical; overflow: hidden;
      margin-top: .2rem; color: var(--as-text-muted, #52667a); font-size: .8125rem; }
    .subject { min-width: 7rem; }
    .detail-row td { background: var(--as-surface-sunken, #f6f8fb); }
    .detail-row dd { overflow-wrap: anywhere; }
  `],
  template: `
    <section class="panel" aria-labelledby="access-history-heading">
      <div class="history-head">
        <h2 id="access-history-heading">Access history</h2>
        <span class="hint">{{ entries().length }} events from {{ rows().length }} log records · newest first · times in UTC</span>
      </div>
      <div class="history-filters" role="search" aria-label="Filter access history">
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Search history</mat-label>
          <input matInput type="search" name="historySearch" [ngModel]="query()" (ngModelChange)="update(query, $event)"
            maxlength="100" placeholder="Person, role, event or reason" />
        </mat-form-field>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Activity</mat-label>
          <select matNativeControl name="historyCategory" [ngModel]="category()" (ngModelChange)="update(category, $event)">
            <option value="ALL">All activity</option>
            @for (c of categories; track c[0]) { <option [value]="c[0]">{{ c[1] }}</option> }
          </select>
        </mat-form-field>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Period</mat-label>
          <select matNativeControl name="historyPeriod" [ngModel]="period()" (ngModelChange)="update(period, $event)">
            @for (p of periods; track p[0]) { <option [value]="p[0]">{{ p[1] }}</option> }
          </select>
        </mat-form-field>
        <button matButton type="button" (click)="clear()" [disabled]="!filtering()">Clear filters</button>
      </div>
      <p class="hint" role="status">{{ filtering() ? 'Showing ' + matches().length + ' of ' + entries().length + ' events.' : '' }}</p>
      @if (shown().length) {
        <div class="table-scroll"><table>
          <caption class="sr-only">Access history events, newest first</caption>
          <thead><tr><th scope="col">When (UTC)</th><th scope="col">Event</th><th scope="col">Subject</th>
            <th scope="col">Change</th><th scope="col">By</th><th scope="col"><span class="sr-only">Details</span></th></tr></thead>
          <tbody>
            @for (e of shown(); track e.key) {
              <tr>
                <td class="nowrap">{{ e.when }}</td>
                <td><span class="event">{{ e.event }}</span>
                  <span class="meta"><span class="category">{{ categoryLabels[e.category] }}</span>
                    @for (t of e.tags; track t) { <span class="tag">{{ t }}</span> }</span></td>
                <td class="subject" [attr.title]="e.subjectFull">{{ e.subject }}</td>
                <td>{{ e.summary }}<span class="reason" [attr.title]="e.reason">{{ e.reason }}</span></td>
                <td>{{ e.actor }}</td>
                <td><button matButton type="button" (click)="toggle(e.key)" [attr.aria-expanded]="open() === e.key"
                  [attr.aria-controls]="'access-history-detail-' + $index"
                  [attr.aria-label]="(open() === e.key ? 'Hide details for ' : 'Show details for ') + e.event + ', ' + e.when">Details</button></td>
              </tr>
              @if (open() === e.key) {
                <tr class="detail-row" [id]="'access-history-detail-' + $index"><td colspan="6"><dl class="facts">
                  @for (d of e.details; track d.label) { <dt>{{ d.label }}</dt><dd>{{ d.value }}</dd> }
                </dl></td></tr>
              }
            }
          </tbody>
        </table></div>
        @if (matches().length > shown().length) {
          <button matButton type="button" (click)="limit.set(limit() + page)">Show {{ page }} more</button>
        }
      } @else {
        <p>{{ entries().length ? 'No events match these filters.' : 'No access changes recorded.' }}</p>
      }
    </section>
  `,
})
export class AccessHistory {
  readonly rows = input.required<readonly AccessHistoryRow[]>();
  readonly page = PAGE;
  readonly periods = PERIODS;
  readonly categoryLabels = CATEGORY_LABELS;
  readonly categories = Object.entries(CATEGORY_LABELS) as [AccessCategory, string][];
  readonly query = signal('');
  readonly category = signal<'ALL' | AccessCategory>('ALL');
  readonly period = signal<(typeof PERIODS)[number][0]>('ALL');
  readonly limit = signal(PAGE);
  readonly open = signal<string | null>(null);
  readonly entries = computed(() => summarizeAccessHistory(this.rows()));
  readonly filtering = computed(() => !!this.query().trim() || this.category() !== 'ALL' || this.period() !== 'ALL');
  readonly matches = computed(() => {
    const terms = this.query().trim().toLowerCase().split(/\s+/).filter(Boolean);
    const category = this.category(), period = this.period();
    const since = period === 'ALL' ? -Infinity : Date.now() - Number(period) * 86_400_000;
    return this.entries().filter(e => (category === 'ALL' || e.category === category) && !(e.ms < since) &&
      terms.every(t => e.haystack.includes(t)));
  });
  readonly shown = computed(() => this.matches().slice(0, this.limit()));

  update<T>(target: { set(value: T): void }, value: T): void {
    target.set(value);
    this.limit.set(PAGE);
    this.open.set(null);
  }
  clear(): void {
    this.query.set(''); this.category.set('ALL'); this.period.set('ALL'); this.limit.set(PAGE); this.open.set(null);
  }
  toggle(key: string): void { this.open.set(this.open() === key ? null : key); }
}
