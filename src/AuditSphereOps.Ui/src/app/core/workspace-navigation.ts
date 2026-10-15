import { Component, inject, input } from '@angular/core';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs';
import { NAVIGATION } from '../navigation';
import { SessionService } from './session';

/** Presentation links only; the API owns every record and action permission. */
@Component({
  selector: 'audit-workspace-navigation',
  imports: [RouterLink, RouterLinkActive],
  template: `
    <nav class="workspace-navigation" [class.compact-navigation]="compactNavigation()" aria-label="Workspace navigation">
      @if (session.current()?.staff) {
        @for (group of nav; track group.title) {
          <h2 [class.current-section]="isCurrentGroup(group.title)"
            [attr.aria-current]="isCurrentGroup(group.title) ? 'location' : null"
            [attr.aria-label]="group.title">
            <span class="group-mark" aria-hidden="true">{{ compactMark(group.title) }}</span>
            <span class="group-label">{{ group.title }}</span>
          </h2>
          @for (item of group.items; track item.path) {
            <a [routerLink]="item.path" routerLinkActive="active"
              [routerLinkActiveOptions]="{ exact: item.exact ?? false }"
              ariaCurrentWhenActive="page"
              [attr.aria-label]="compactNavigation() ? item.label : null"
              [attr.title]="compactNavigation() ? item.label : null">
              <span class="item-mark" aria-hidden="true">{{ compactMark(item.label) }}</span>
              <span class="nav-label">{{ item.label }}</span>
            </a>
          }
        }
        <p class="access-note">Your access follows current AuditSphere assignments.</p>
      } @else if (session.current()) {
        <h2 aria-label="Your workspace"><span class="group-mark" aria-hidden="true">YW</span><span class="group-label">Your workspace</span></h2>
        <a routerLink="/portal" routerLinkActive="active" ariaCurrentWhenActive="page"
          [attr.aria-label]="compactNavigation() ? 'Client portal' : null"
          [attr.title]="compactNavigation() ? 'Client portal' : null">
          <span class="item-mark" aria-hidden="true">CP</span><span class="nav-label">Client portal</span>
        </a>
        <p class="access-note">Only your assigned requests and shared documents are available.</p>
      }
    </nav>
  `,
  styles: `
    :host { display: block; }
    nav { display: block; margin: 0; }
    h2 { display: flex; align-items: center; gap: .55rem; font-size: .6875rem; text-transform: uppercase;
      letter-spacing: .08em; font-weight: 700; margin: 1.25rem 0 .25rem; padding-inline: .75rem;
      color: var(--as-shell-muted, #cbd5e1); }
    h2:first-of-type { margin-block-start: 0; }
    h2.current-section { color: white; border-inline-start: 3px solid #60a5fa; padding-inline-start: .5rem; }
    .group-mark, .item-mark { display: none; }
    a { display: flex; align-items: center; gap: .65rem; padding: .5rem .75rem; min-height: 44px;
      margin-block: 1px; box-sizing: border-box; color: var(--as-shell-muted, #cbd5e1); text-decoration: none;
      border: 1px solid transparent; border-radius: 8px; overflow-wrap: anywhere;
      transition: background-color .15s ease, border-color .15s ease, color .15s ease; }
    a:hover { background: var(--as-shell-raised, #1e293b); color: white; }
    a.active { font-weight: 600; background: var(--as-shell-active, #1d4ed8); color: white;
      box-shadow: inset 3px 0 0 #bfdbfe; }
    a:focus-visible { outline: 3px solid #fff; outline-offset: 2px; }
    p { font-size: .8125rem; line-height: 1.4; color: var(--as-shell-muted, #cbd5e1); padding-inline: .75rem;
      margin-block-start: 1.5rem; }
    .compact-navigation h2 { justify-content: center; padding-inline: 0; }
    .compact-navigation h2.current-section { border-inline: 0; padding-inline: 0; }
    .compact-navigation .group-mark { display: inline-grid; place-items: center; min-width: 2rem; min-height: 1.75rem;
      border: 1px solid var(--as-shell-border, #475569); border-radius: 6px; font-size: .625rem; }
    .compact-navigation .group-label, .compact-navigation .nav-label, .compact-navigation .access-note { display: none; }
    .compact-navigation a { justify-content: center; padding-inline: .25rem; }
    .compact-navigation .item-mark { display: inline-grid; place-items: center; width: 2.25rem; height: 2.25rem;
      border: 1px solid var(--as-shell-border, #475569); border-radius: 8px; color: white; font-size: .75rem; font-weight: 700; }
    .compact-navigation a.active .item-mark { border-color: #bfdbfe; }
    @media (forced-colors: active) { a.active { outline: 1px solid Highlight; } }
  `,
})
export class WorkspaceNavigation {
  readonly session = inject(SessionService);
  readonly compactNavigation = input(false);
  readonly nav = NAVIGATION;
  private readonly router = inject(Router);
  private readonly currentRoute = toSignal(this.router.events.pipe(map(() => this.router.url)), { initialValue: this.router.url });

  compactMark(label: string): string {
    return label.split(/\s+/).filter(word => word !== '&').slice(0, 2).map(word => word[0]).join('').toUpperCase();
  }

  isCurrentGroup(title: string): boolean {
    const path = this.currentRoute().split(/[?#]/, 1)[0];
    const under = (root: string): boolean => path === root || path.startsWith(`${root}/`);
    if (title === 'Administration') return under('/app/administration') || path === '/app/operations';
    if (title === 'Accounting') return ['/app/accounting', '/app/consolidation', '/app/finance'].some(under);
    if (title === 'Audit') return under('/app/audit') || under('/app/library') ||
      /^\/app\/engagements\/[^/]+\/(audit-plan|audit-fieldwork|completion)(?:\/|$)/.test(path);
    if (title === 'Practice') return under('/app/practice') || under('/app/clients') || path === '/app' ||
      (under('/app/engagements') && !/^\/app\/engagements\/[^/]+\/(audit-plan|audit-fieldwork|completion)(?:\/|$)/.test(path));
    return false;
  }
}
