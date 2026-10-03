import { routes } from '../app.routes';

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Explicit Angular route ownership. A navigation hint never authorizes its destination's data. */
export function workspaceRoute(href: string): string | null {
  if (!/^\/(app|portal|setup)(?:\/|$)/.test(href) || /[\\\s?#%]/.test(href)) return null;
  const parts = href.slice(1).split('/');
  const owned = routes.some((route) => {
    if (!route.path) return false;
    const pattern = route.path.split('/');
    return pattern.length === parts.length && pattern.every((part, index) =>
      part === ':id' ? uuid.test(parts[index]) : part === ':kind' ?
        ['ECL', 'INVENTORY', 'SPECIALIST', 'ANALYTICAL', 'JOURNAL_RISK'].includes(parts[index]) : part === parts[index]);
  });
  return owned ? href : null;
}

export function migratedHref(href: string, base = '/ui/'): string | null {
  const route = workspaceRoute(href);
  return route && (base === '/' || base === '/ui/') ? (base === '/' ? '' : '/ui') + route : null;
}

/** The host emits one of these two bases; it changes navigation, never API authority. */
export function presentationBase(): '/' | '/ui/' {
  return typeof document !== 'undefined' && document.querySelector('base')?.getAttribute('href') === '/' ? '/' : '/ui/';
}
export function workspaceSignInHref(path: string, query: string, base: '/' | '/ui/'): string {
  const native = base === '/' ? path : path.startsWith('/ui/') ? path.slice(3) : '';
  const destination = workspaceRoute(native) ? path + query : migratedHref('/app', base)!;
  return '/auth/sign-in?returnUrl=' + encodeURIComponent(destination);
}
