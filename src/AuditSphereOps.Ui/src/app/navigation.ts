import type { NavIconName } from './navigation-icons';

/**
 * Staff shell navigation. Links are navigation aids only; every page's data is authorized by the server.
 * Groups follow the firm / client-accounting / group-consolidation boundaries: firm books sit with Practice,
 * and group consolidation has its own workspace.
 */
export interface NavItem { label: string; path: string; icon: NavIconName; exact?: boolean }
export interface NavGroup { title: string; icon: NavIconName; items: NavItem[] }
export const NAVIGATION: NavGroup[] = [
  { title: 'Administration', icon: 'shield', items: [
    { label: 'Administration', path: '/app/administration', icon: 'shield', exact: true },
    { label: 'Users & Access', path: '/app/administration/users', icon: 'users' },
    { label: 'Microsoft 365', path: '/app/administration/microsoft365/tenant-connection', icon: 'cloud' },
    { label: 'Operations', path: '/app/operations', icon: 'activity' },
  ] },
  { title: 'Practice', icon: 'briefcase', items: [
    { label: 'Portfolio', path: '/app', icon: 'grid', exact: true },
    { label: 'My work', path: '/app/work', icon: 'check-square' },
    { label: 'Practice leads', path: '/app/practice/leads', icon: 'user-plus' },
    { label: 'Resources', path: '/app/practice/resources', icon: 'calendar' },
    { label: 'Time', path: '/app/practice/time', icon: 'clock' },
    { label: 'Firm ledger', path: '/app/finance', icon: 'book-open', exact: true },
    { label: 'Firm books', path: '/app/finance/books', icon: 'receipt' },
    { label: 'Analytics', path: '/app/practice/analytics', icon: 'bar-chart' },
  ] },
  { title: 'Accounting', icon: 'calculator', items: [
    { label: 'Accounting', path: '/app/accounting', icon: 'calculator', exact: true },
    { label: 'Mappings', path: '/app/accounting/mappings', icon: 'git-branch' },
    { label: 'Adjustments', path: '/app/accounting/journals', icon: 'sliders' },
    { label: 'Differences', path: '/app/accounting/differences', icon: 'plus-minus' },
    { label: 'Evidence', path: '/app/accounting/evidence', icon: 'file-text' },
    { label: 'Package reviews', path: '/app/accounting/reviews', icon: 'clipboard-check' },
    { label: 'Roll-forward', path: '/app/accounting/rollforward', icon: 'trending-up' },
    { label: 'Restatements', path: '/app/accounting/restatements', icon: 'history' },
    { label: 'FX rates & policies', path: '/app/accounting/currency-configuration', icon: 'currency-exchange' },
    { label: 'FX remeasurement', path: '/app/accounting/remeasurement', icon: 'refresh-cw' },
  ] },
  { title: 'Consolidation', icon: 'layers', items: [
    { label: 'Group consolidation', path: '/app/consolidation', icon: 'layers' },
  ] },
  { title: 'Audit', icon: 'search', items: [
    { label: 'Program library', path: '/app/audit/library', icon: 'list-checks' },
    { label: 'Technical library', path: '/app/library', icon: 'book' },
  ] },
];
