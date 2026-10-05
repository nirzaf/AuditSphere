import type { CanDeactivateFn } from '@angular/router';
import type { EngagementDetail } from './engagement';

export const engagementNavigationGuard: CanDeactivateFn<EngagementDetail> = (
  component,
  _route,
  _state,
  next,
) => component.confirmNavigation(next.url);
