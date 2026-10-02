import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { SessionService } from './session';

/** Navigation aid only: every HTTP read independently authorizes current scope. */
export const staffGuard: CanActivateFn = async () => {
  const session = inject(SessionService);
  const router = inject(Router);
  if (!(await session.refresh())) return false;
  return session.current()?.staff === true ? true : router.createUrlTree(['/portal']);
};
