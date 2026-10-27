import { Component, inject, input } from '@angular/core';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs';
import { NAVIGATION, navigationOwner, type NavGroupId } from '../navigation';
import { SessionService } from './session';
import { WorkspaceNavigationIcon } from './workspace-navigation-icon';

/** Presentation links only; the API owns every record and action permission. */
@Component({
  selector: 'audit-workspace-navigation',
  imports: [RouterLink, RouterLinkActive, WorkspaceNavigationIcon],
  template: `
    <nav class="workspace-navigation" [class.compact-navigation]="compactNavigation()" aria-label="Workspace navigation">
      @if (session.current()?.staff) {
        @for (group of nav; track group.id) {
          <h2 [class.current-section]="isCurrentGroup(group.id)"
            [attr.aria-current]="isCurrentGroup(group.id) ? 'location' : null"
            [attr.aria-label]="group.title"
            [attr.title]="compactNavigation() ? group.title : null">
            <span class="group-mark"><audit-nav-icon [name]="group.icon" /></span>
            <span class="group-label">{{ group.title }}</span>
          </h2>
          @for (item of group.items; track item.id; let index = $index) {
            @if (group.id === 'execution' && item.id === 'consolidation') {
              <h3 class="subsection">Audit Fieldwork</h3>
              <p class="section-hint">Open an authorized engagement for audit work.</p>
            }
            @if (item.section && (index === 0 || group.items[index - 1].section !== item.section)) {
              <h3 class="subsection">{{ item.section }}</h3>
            }
            <a [routerLink]="item.path" routerLinkActive="active"
              [routerLinkActiveOptions]="{ exact: item.exact ?? false }"
              ariaCurrentWhenActive="page"
              [attr.aria-label]="compactNavigation() ? item.label : null"
              [attr.title]="compactNavigation() ? item.label : null">
              <span class="item-mark"><audit-nav-icon [name]="item.icon" /></span>
              <span class="nav-label">{{ item.label }}</span>
            </a>
          }
        }
        <p class="access-note">Your access follows current AuditSphere assignments.</p>
      } @else if (session.current()) {
        <h2 aria-label="Your workspace" [attr.title]="compactNavigation() ? 'Your workspace' : null"><span class="group-mark"><audit-nav-icon [name]="'user-round'" /></span><span class="group-label">Your workspace</span></h2>
        <a routerLink="/portal" routerLinkActive="active" ariaCurrentWhenActive="page"
          [attr.aria-label]="compactNavigation() ? 'Client portal' : null"
          [attr.title]="compactNavigation() ? 'Client portal' : null">
          <span class="item-mark"><audit-nav-icon [name]="'globe'" /></span><span class="nav-label">Client portal</span>
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
    .subsection { margin: .7rem .75rem .15rem; color: var(--as-shell-muted, #cbd5e1); font-size: .75rem; }
    .section-hint { margin: .15rem .75rem .4rem; padding: 0; font-size: .75rem; }
    h2.current-section { color: white; border-inline-start: 3px solid #60a5fa; padding-inline-start: .5rem; }
    .group-mark, .item-mark { display: inline-grid; place-items: center; flex: none; }
    .group-mark { --nav-icon-size: 1rem; }
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
    .compact-navigation .group-mark { min-width: 2rem; min-height: 1.75rem; border: 1px solid var(--as-shell-border, #475569);
      border-radius: 6px; }
    .compact-navigation .group-label, .compact-navigation .nav-label, .compact-navigation .access-note,
    .compact-navigation .subsection, .compact-navigation .section-hint { display: none; }
    .compact-navigation a { justify-content: center; padding-inline: .25rem; }
    .compact-navigation .item-mark { width: 2.25rem; height: 2.25rem; border: 1px solid var(--as-shell-border, #475569);
      border-radius: 8px; }
    .compact-navigation a.active .item-mark { border-color: #bfdbfe; }
    @media (forced-colors: active) { a.active, h2.current-section { outline: 1px solid Highlight; } }
  `,
})
export class WorkspaceNavigation {
  readonly session = inject(SessionService);
  readonly compactNavigation = input(false);
  readonly nav = NAVIGATION;
  private readonly router = inject(Router);
  private readonly currentRoute = toSignal(this.router.events.pipe(map(() => this.router.url)), { initialValue: this.router.url });

  isCurrentGroup(id: NavGroupId): boolean { return navigationOwner(this.currentRoute())?.groupId === id; }
}
