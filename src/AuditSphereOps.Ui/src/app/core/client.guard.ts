import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { SessionService } from './session';
/** Navigation aid; all portal endpoints independently authorize current client scope and participation. */
export const clientGuard: CanActivateFn = async () => {
  const session = inject(SessionService);
  const router = inject(Router);
  if (!(await session.refresh())) return false;
  return session.current()?.staff ? router.createUrlTree(['/app']) : true;
};
