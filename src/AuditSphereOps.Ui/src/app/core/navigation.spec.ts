import { describe, expect, it } from 'vitest';
import { routes } from '../app.routes';
import { NAVIGATION, navigationOwner } from '../navigation';
import { NAV_ICON_PATHS } from '../navigation-icons';
import { migratedHref, workspaceRoute, workspaceSignInHref } from './navigation';

describe('Angular workspace navigation ownership', () => {
  it('orders every destination by lifecycle without mixing the financial domains', () => {
    expect(NAVIGATION.map(group => group.title)).toEqual([
      'My Work & Portfolio', 'Clients & Commercial', 'Engagements & Planning', 'Work Execution',
      'Review, Reporting & Close', 'Firm Finance & Time', 'Reference & Administration',
    ]);
    for (const group of NAVIGATION) {
      expect(NAV_ICON_PATHS[group.icon].length).toBeGreaterThan(0);
      for (const item of group.items) expect(NAV_ICON_PATHS[item.icon].length).toBeGreaterThan(0);
    }
    const groupOf = (path: string) => NAVIGATION.find(group => group.items.some(item => item.path === path))?.title;
    expect(groupOf('/app/finance')).toBe('Firm Finance & Time');
    expect(groupOf('/app/finance/books')).toBe('Firm Finance & Time');
    expect(groupOf('/app/consolidation')).toBe('Work Execution');
    expect(groupOf('/app/accounting/remeasurement')).toBe('Work Execution');
    expect(groupOf('/app/accounting/reviews')).toBe('Review, Reporting & Close');
    const paths = NAVIGATION.flatMap(group => group.items.map(item => item.path));
    expect(new Set(paths).size).toBe(paths.length);
    expect(new Set(NAVIGATION.map(group => group.id)).size).toBe(NAVIGATION.length);
    const ids = NAVIGATION.flatMap(group => group.items.map(item => item.id));
    expect(new Set(ids).size).toBe(ids.length);
    expect(paths.slice().sort()).toEqual([
      '/app', '/app/work', '/app/practice/leads', '/app/practice/pipeline', '/app/practice/resources',
      '/app/practice/time', '/app/finance', '/app/finance/books', '/app/finance/invoices',
      '/app/finance/fee-agreements', '/app/practice/analytics', '/app/accounting', '/app/accounting/mappings',
      '/app/accounting/journals', '/app/accounting/differences', '/app/accounting/evidence',
      '/app/accounting/reviews', '/app/accounting/rollforward', '/app/accounting/restatements',
      '/app/accounting/currency-configuration', '/app/accounting/remeasurement', '/app/consolidation',
      '/app/audit/library', '/app/library', '/app/administration', '/app/administration/users',
      '/app/administration/microsoft365/tenant-connection', '/app/operations',
    ].sort());
  });
  it('assigns contextual audit and accounting routes to a single stable owner', () => {
    const engagement = '/app/engagements/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
    for (const [path, group] of [
      [engagement, 'planning'], [engagement + '/audit-plan', 'planning'],
      [engagement + '/pbc', 'execution'], [engagement + '/confirmations', 'execution'],
      [engagement + '/general-ledger/upload', 'execution'], [engagement + '/statements', 'review-delivery'],
      ['/app/accounting/reviews', 'review-delivery'], ['/app/consolidation/scopes/abc', 'execution'],
      ['/app/finance/invoices?page=2', 'firm-finance'],
    ]) expect(navigationOwner(path)?.groupId).toBe(group);
  });
  it('assigns every declared staff workspace route an owner', () => {
    for (const route of routes.filter(item => item.path === 'app' || item.path?.startsWith('app/'))) {
      const path = '/' + route.path!.replace(/:kind/g, 'ECL')
        .replace(/:[^/]+/g, 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa');
      expect(navigationOwner(path), path).not.toBeNull();
    }
  });
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
