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

export function migratedHref(href: string): string | null {
  const route = workspaceRoute(href);
  return route ? '/ui' + route : null;
}
