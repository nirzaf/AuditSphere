import { Component, computed, input } from '@angular/core';
import { NAV_ICON_PATHS, type NavIconName } from '../navigation-icons';

/** Decorative sidebar icon. The link or heading it sits in carries the accessible name. */
@Component({
  selector: 'audit-nav-icon',
  template: `
    <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
      @for (d of paths(); track $index) { <path [attr.d]="d" /> }
    </svg>
  `,
  styles: `
    :host { display: inline-flex; flex: none; }
    svg { width: var(--nav-icon-size, 1.25rem); height: var(--nav-icon-size, 1.25rem); fill: none; stroke: currentColor;
      stroke-width: 1.75; stroke-linecap: round; stroke-linejoin: round; }
  `,
})
export class WorkspaceNavigationIcon {
  readonly name = input.required<NavIconName>();
  readonly paths = computed(() => NAV_ICON_PATHS[this.name()]);
}
