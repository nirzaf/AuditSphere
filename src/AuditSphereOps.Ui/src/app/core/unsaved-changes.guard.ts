import type { CanDeactivateFn } from '@angular/router';
import type { NavigationProtected } from './unsaved-changes';

export const unsavedChangesGuard: CanDeactivateFn<NavigationProtected> = (component) =>
  component.confirmNavigation();
