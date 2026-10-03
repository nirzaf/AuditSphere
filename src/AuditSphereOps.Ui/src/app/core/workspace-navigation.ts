import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { NAVIGATION } from '../navigation';
import { SessionService } from './session';

/** Presentation links only; the API owns every record and action permission. */
@Component({
  selector: 'audit-workspace-navigation',
  imports: [RouterLink, RouterLinkActive],
  template: `
    <nav aria-label="Workspace navigation">
      @if (session.current()?.staff) {
        @for (group of nav; track group.title) {
          <h2>{{ group.title }}</h2>
          @for (item of group.items; track item.path) {
            <a [routerLink]="item.path" routerLinkActive="active"
              [routerLinkActiveOptions]="{ exact: item.exact ?? false }"
              ariaCurrentWhenActive="page">{{ item.label }}</a>
          }
        }
        <p>Your access follows current AuditSphere assignments.</p>
      } @else if (session.current()) {
        <h2>Your workspace</h2>
        <a routerLink="/portal" routerLinkActive="active" ariaCurrentWhenActive="page">Client portal</a>
        <p>Only your assigned requests and shared documents are available.</p>
      }
    </nav>
  `,
  styles: `
    :host { display: block; }
    nav { display: block; margin: 0; }
    h2 { font-size: .75rem; text-transform: uppercase; letter-spacing: .05em;
      margin: 1rem 0 .25rem; color: #315476; }
    a { display: block; padding: .75rem; min-height: 44px; box-sizing: border-box;
      color: #12385d; border-radius: 4px; overflow-wrap: anywhere; }
    a.active { font-weight: 600; background: #dbe7f2; }
    p { font-size: .875rem; margin-block-start: 2rem; }
  `,
})
export class WorkspaceNavigation {
  readonly session = inject(SessionService);
  readonly nav = NAVIGATION;
}
