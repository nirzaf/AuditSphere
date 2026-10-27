import { Component, Pipe, PipeTransform, inject, input } from '@angular/core';
import { Router } from '@angular/router';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { money, percent } from './decode';
import { NAVIGATION } from '../navigation';
import type { NavIconName } from '../navigation-icons';
import { WorkspaceNavigationIcon } from './workspace-navigation-icon';

const UTC_TIME = new Intl.DateTimeFormat('en-GB', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit',
  minute: '2-digit', hourCycle: 'h23', timeZone: 'UTC' });

/** A stored status, role or reason code in plain words: NOT_VERIFIED → "Not verified"; RelationshipManager → "Relationship Manager". */
export function codeLabel(value: string | null | undefined): string {
  if (!value) return '—';
  if (/^[A-Z][A-Z0-9]*(_[A-Z0-9]+)*$/.test(value) && value.length > 1) {
    const words = value.toLowerCase().replace(/_/g, ' ');
    return words.charAt(0).toUpperCase() + words.slice(1);
  }
  return /^[A-Z][a-z]+(?:[A-Z][a-z]+)+$/.test(value) ? value.replace(/([a-z])([A-Z])/g, '$1 $2') : value;
}

/** A stored instant as "26 Oct 2026, 18:08 UTC"; unparseable input is shown unchanged rather than invented. */
export function utcTime(value: string | null | undefined): string {
  if (!value) return '—';
  const ms = Date.parse(value);
  return Number.isNaN(ms) ? value : UTC_TIME.format(ms) + ' UTC';
}

@Pipe({ name: 'codeLabel' })
export class CodeLabelPipe implements PipeTransform {
  transform(value: string | null | undefined): string { return codeLabel(value); }
}

@Pipe({ name: 'utcTime' })
export class UtcTimePipe implements PipeTransform {
  transform(value: string | null | undefined): string { return utcTime(value); }
}

/** Exact decimal string → grouped display; never converts through floating point. */
@Pipe({ name: 'money' })
export class MoneyPipe implements PipeTransform {
  transform(value: string | null | undefined, places = 2): string {
    return money(value, places);
  }
}

@Pipe({ name: 'percent' })
export class PercentPipe implements PipeTransform {
  transform(value: string | null | undefined, places = 2): string {
    return percent(value, places);
  }
}

/** The sidebar icon of the area a page belongs to, so each page header carries the same visual cue as its menu item. */
export function pageIconFor(url: string): NavIconName {
  const path = url.split(/[?#]/, 1)[0].replace(/^\/ui(?=\/)/, '');
  let best: { path: string; icon: NavIconName } | null = null;
  for (const group of NAVIGATION) for (const item of group.items) {
    const matches = path === item.path || (!item.exact && path.startsWith(item.path + '/'));
    if (matches && (!best || item.path.length > best.path.length)) best = item;
  }
  if (best) return best.icon;
  // Engagement sub-pages that the sidebar files under Accounting or Audit take that area's icon.
  if (/^\/app\/engagements\/[^/]+\/(general-ledger|tb-intake|financial-package|statements)(\/|$)/.test(path)) return 'calculator';
  if (/^\/app\/engagements\/[^/]+\/(audit-plan|audit-fieldwork|completion|confirmations|pbc)(\/|$)/.test(path)) return 'file-search';
  if (/^\/app\/(engagements|clients|assessments)(\/|$)/.test(path)) return 'briefcase-business';
  if (/^\/app\/(audit|findings|reviews|releases|completion|records)(\/|$)/.test(path)) return 'file-search';
  if (path.startsWith('/app/accounting')) return 'calculator';
  if (path.startsWith('/app/consolidation')) return 'layers';
  if (path.startsWith('/app/profile')) return 'user-round';
  if (path.startsWith('/portal')) return 'globe';
  return 'layout-dashboard';
}

@Component({
  selector: 'audit-page-header',
  imports: [WorkspaceNavigationIcon],
  template: `<header>
    <span class="page-icon" aria-hidden="true"><audit-nav-icon [name]="icon" /></span>
    <div class="page-header-text">
      @if (eyebrow()) { <p class="eyebrow">{{ eyebrow() }}</p> }
      <h1>{{ title() }}</h1>
      @if (description()) { <p>{{ description() }}</p> }
    </div>
    <div class="page-header-actions"><ng-content /></div>
  </header>`,
})
export class PageHeader {
  readonly icon = pageIconFor(inject(Router, { optional: true })?.url ?? '');
  readonly title = input.required<string>();
  readonly eyebrow = input('');
  readonly description = input('');
}

/** Loading / error / empty states shared by every migrated page. */
@Component({
  selector: 'audit-state',
  imports: [MatProgressBarModule],
  template: `@if (loading()) { <mat-progress-bar mode="indeterminate" [attr.aria-label]="'Loading ' + label()" /> }
    @if (error()) { <section role="alert" class="state-error"><p>{{ error() }}</p></section> }`,
})
export class StateBlock {
  readonly loading = input(false);
  readonly error = input('');
  readonly label = input('content');
}

@Component({
  selector: 'audit-status',
  template: `<span class="status-chip" [attr.data-status]="value()" [attr.data-tone]="tone()">{{ label() }}</span>`,
  styles: [`
    :host {
      display: inline-block;
      --status-info-ink: #124b74;
      --status-info-fill: #e8f2fb;
      --status-success-ink: #175b34;
      --status-success-fill: #e8f5ec;
      --status-warning-ink: #674800;
      --status-warning-fill: #fff3d6;
      --status-error-ink: #8f1f17;
      --status-error-fill: #fdecea;
      --status-neutral-ink: #12385d;
      --status-neutral-fill: #e3ebf3;
    }
    .status-chip {
      display: inline-flex;
      min-height: 1.5rem;
      max-width: 100%;
      align-items: center;
      box-sizing: border-box;
      padding: 0.125rem 0.55rem;
      border: 1px solid transparent;
      border-radius: 999px;
      font-size: 0.8125rem;
      font-weight: 600;
      line-height: 1.25;
      text-transform: capitalize;
      overflow-wrap: anywhere;
      vertical-align: middle;
    }
    [data-tone='info'] { color: var(--status-info-ink); background: var(--status-info-fill); border-color: #c3d8ec; }
    [data-tone='success'] { color: var(--status-success-ink); background: var(--status-success-fill); border-color: #c7e1cf; }
    [data-tone='warning'] { color: var(--status-warning-ink); background: var(--status-warning-fill); border-color: #ead49a; }
    [data-tone='error'] { color: var(--status-error-ink); background: var(--status-error-fill); border-color: #eac4c1; }
    [data-tone='neutral'] { color: var(--status-neutral-ink); background: var(--status-neutral-fill); border-color: #d4deea; }
  `],
})
export class StatusChip {
  readonly value = input.required<string>();
  protected tone(): 'info' | 'success' | 'warning' | 'error' | 'neutral' {
    const status = this.value().trim().toUpperCase();
    if (SUCCESS_STATUSES.has(status)) return 'success';
    if (ERROR_STATUSES.has(status)) return 'error';
    if (WARNING_STATUSES.has(status)) return 'warning';
    if (INFO_STATUSES.has(status)) return 'info';
    return 'neutral';
  }
  protected label(): string {
    return this.value().replace(/_/g, ' ').toLowerCase();
  }
}

const INFO_STATUSES = new Set(['DRAFT', 'LEADNEW', 'NEW', 'PENDING', 'OPEN', 'SUBMITTED', 'RESUBMITTED',
  'AWAITING REVIEW', 'IN PROGRESS', 'INPROGRESS', 'NOT_VERIFIED', 'NOT_OBSERVED', 'PARTIAL', 'INFO', 'AUTHORIZED',
  'DISPATCHING', 'REQUESTED', 'NOT_SENT', 'COPIED', 'FUTURE']);
const SUCCESS_STATUSES = new Set(['APPROVED', 'ACTIVE', 'READY', 'ISSUED', 'COMPLETED', 'QUALIFIED', 'VERIFIED',
  'REVIEW COMPLETE', 'POSTED', 'RECONCILED', 'ACCEPTED', 'CLEARED', 'DONE', 'OK', 'BOUND', 'ENABLED',
  'CONFIGURED', 'PROVIDER_ACCEPTED', 'CONSENT_VERIFIED']);
const ERROR_STATUSES = new Set(['REJECTED', 'BLOCKED', 'CANCELLED', 'VOID', 'FAILED', 'DENIED', 'UNAVAILABLE',
  'DECLINED', 'NOT_GRANTED', 'BLOCKED_EXTERNAL', 'CONFLICT_REQUIRES_REVIEW', 'DISABLED', 'REVOKED']);
const WARNING_STATUSES = new Set(['LOCKED', 'SUPERSEDED', 'ON HOLD', 'HOLD', 'STALE', 'EXPIRED', 'CONDITIONS',
  'ACCEPTEDWITHCONDITIONS', 'ACCEPTED_WITH_CONDITIONS', 'DEFERRED', 'UNKNOWN', 'ATTENTION', 'NOT_CONFIGURED',
  'NOT_CONNECTED', 'NO_ACCESS', 'RETURNED_UNVERIFIED', 'IDENTITY_PENDING', 'REQUIRED', 'REVIEW_REQUIRED',
  'EXECUTION_REQUIRED', 'EXECUTION_REVIEW_REQUIRED']);

@Component({
  selector: 'audit-command-message',
  template: `@if (message()) { <p [attr.role]="failed() ? 'alert' : 'status'" [class.error-text]="failed()" class="command-result">{{ message() }}</p> }`,
})
export class CommandMessage {
  readonly message = input('');
  readonly failed = input(false);
}

export const SHARED = [MoneyPipe, PercentPipe, CodeLabelPipe, UtcTimePipe, PageHeader, StateBlock, StatusChip, CommandMessage] as const;
