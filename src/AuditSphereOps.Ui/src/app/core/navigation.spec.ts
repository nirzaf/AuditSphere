import { describe, expect, it } from 'vitest';
import { routes } from '../app.routes';
import { NAVIGATION } from '../navigation';
import { migratedHref, workspaceRoute } from './navigation';

describe('Angular workspace navigation ownership', () => {
  it('owns every sidebar destination and every declared parameterized route', () => {
    for (const group of NAVIGATION) for (const item of group.items) {
      expect(workspaceRoute(item.path)).toBe(item.path);
    }
    for (const route of routes) {
      if (!route.path) continue;
      const path = '/' + route.path.replaceAll(':id', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa').replaceAll(':kind', 'ECL');
      expect(migratedHref(path)).toBe('/ui' + path);
    }
  });
  it('never falls back to an unowned server route', () => {
    for (const path of ['/app/no-such-page', '/auth/sign-out', '/api/ui/session', '/app/%2fadmin', '/app/../auth', '/app/clients/not-an-id', '/app/accounting/evidence/UNKNOWN/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa']) {
      expect(workspaceRoute(path)).toBeNull();
    }
  });
});
