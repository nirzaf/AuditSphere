import type { NavIconName } from './navigation-icons';

/**
 * Staff shell navigation. Links are navigation aids only; every page's data is authorized by the server.
 * Groups follow the firm / client-accounting / group-consolidation boundaries: firm books sit with Practice,
 * and group consolidation has its own workspace.
 */
export interface NavItem { label: string; path: string; icon: NavIconName; exact?: boolean }
export interface NavGroup { title: string; icon: NavIconName; items: NavItem[] }
export const NAVIGATION: NavGroup[] = [
  { title: 'Administration', icon: 'shield-check', items: [
    { label: 'Administration', path: '/app/administration', icon: 'shield-check', exact: true },
    { label: 'Users & Access', path: '/app/administration/users', icon: 'users-round' },
    { label: 'Microsoft 365', path: '/app/administration/microsoft365/tenant-connection', icon: 'cloud' },
    { label: 'Operations', path: '/app/operations', icon: 'activity' },
  ] },
  { title: 'Practice', icon: 'briefcase-business', items: [
    { label: 'Portfolio', path: '/app', icon: 'layout-dashboard', exact: true },
    { label: 'My work', path: '/app/work', icon: 'list-checks' },
    { label: 'Practice leads', path: '/app/practice/leads', icon: 'user-round-plus' },
    { label: 'Opportunity pipeline', path: '/app/practice/pipeline', icon: 'kanban' },
    { label: 'Resources', path: '/app/practice/resources', icon: 'calendar-days' },
    { label: 'Time', path: '/app/practice/time', icon: 'timer' },
    { label: 'Firm ledger', path: '/app/finance', icon: 'book-open', exact: true },
    { label: 'Firm books', path: '/app/finance/books', icon: 'book-copy' },
    { label: 'Invoices', path: '/app/finance/invoices', icon: 'receipt' },
    { label: 'Fee settlements', path: '/app/finance/fee-agreements', icon: 'coins' },
    { label: 'Analytics', path: '/app/practice/analytics', icon: 'chart-no-axes-combined' },
  ] },
  { title: 'Accounting', icon: 'calculator', items: [
    { label: 'Accounting', path: '/app/accounting', icon: 'calculator', exact: true },
    { label: 'Mappings', path: '/app/accounting/mappings', icon: 'git-branch' },
    { label: 'Adjustments', path: '/app/accounting/journals', icon: 'notebook-pen' },
    { label: 'Differences', path: '/app/accounting/differences', icon: 'scale' },
    { label: 'Evidence', path: '/app/accounting/evidence', icon: 'file-check-2' },
    { label: 'Package reviews', path: '/app/accounting/reviews', icon: 'clipboard-check' },
    { label: 'Roll-forward', path: '/app/accounting/rollforward', icon: 'calendar-sync' },
    { label: 'Restatements', path: '/app/accounting/restatements', icon: 'history' },
    { label: 'FX rates & policies', path: '/app/accounting/currency-configuration', icon: 'coins' },
    { label: 'FX remeasurement', path: '/app/accounting/remeasurement', icon: 'refresh-cw' },
  ] },
  { title: 'Consolidation', icon: 'layers', items: [
    { label: 'Group consolidation', path: '/app/consolidation', icon: 'layers' },
  ] },
  { title: 'Audit', icon: 'file-search', items: [
    { label: 'Program library', path: '/app/audit/library', icon: 'clipboard-list' },
    { label: 'Technical library', path: '/app/library', icon: 'book-open-text' },
  ] },
];
