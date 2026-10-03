import { describe, expect, it } from 'vitest';
import { routes } from '../app.routes';
import { NAVIGATION } from '../navigation';
import { migratedHref, workspaceRoute, workspaceSignInHref } from './navigation';

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
  it('preserves exact canonical and preview deep links during sign-in', () => {
    const path='/app/clients/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
    expect(migratedHref(path,'/')).toBe(path);
    expect(migratedHref(path,'/unowned/')).toBeNull();
    expect(workspaceSignInHref(path,'?tab=engagements','/')).toBe('/auth/sign-in?returnUrl='+encodeURIComponent(path+'?tab=engagements'));
    expect(workspaceSignInHref('/ui'+path,'','/ui/')).toBe('/auth/sign-in?returnUrl='+encodeURIComponent('/ui'+path));
    expect(workspaceSignInHref('/portal','','/')).toBe('/auth/sign-in?returnUrl=%2Fportal');
    expect(workspaceSignInHref('/api/ui/session','','/')).toBe('/auth/sign-in?returnUrl=%2Fapp');
    expect(workspaceSignInHref('/app/no-such-page','','/ui/')).toBe('/auth/sign-in?returnUrl=%2Fui%2Fapp');
  });
});
