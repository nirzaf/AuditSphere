/** Staff shell navigation. Links are navigation aids only; every page's data is authorized by the server. */
export interface NavItem { label: string; path: string; exact?: boolean }
export interface NavGroup { title: string; items: NavItem[] }
export const NAVIGATION: NavGroup[] = [
  { title: 'Administration', items: [ { label: 'Administration', path: '/app/administration', exact: true },
    { label: 'Users & Access', path: '/app/administration/users' },
    { label: 'Microsoft 365', path: '/app/administration/microsoft365/tenant-connection' }, { label: 'Operations', path: '/app/operations' } ] },
  { title: 'Practice', items: [
    { label: 'Portfolio', path: '/app', exact: true },
    { label: 'Practice leads', path: '/app/practice/leads' },
    { label: 'Resources', path: '/app/practice/resources' },
    { label: 'Time', path: '/app/practice/time' },
    { label: 'Analytics', path: '/app/practice/analytics' },
  ] },
  { title: 'Accounting', items: [
    { label: 'Accounting', path: '/app/accounting', exact: true },
    { label: 'Mappings', path: '/app/accounting/mappings' },
    { label: 'Adjustments', path: '/app/accounting/journals' },
    { label: 'Differences', path: '/app/accounting/differences' },
    { label: 'Evidence', path: '/app/accounting/evidence' },
    { label: 'Package reviews', path: '/app/accounting/reviews' },
    { label: 'Roll-forward', path: '/app/accounting/rollforward' },
    { label: 'Restatements', path: '/app/accounting/restatements' },
    { label: 'FX rates & policies', path: '/app/accounting/currency-configuration' },
    { label: 'FX remeasurement', path: '/app/accounting/remeasurement' },
    { label: 'Group consolidation', path: '/app/consolidation' },
    { label: 'Firm ledger', path: '/app/finance', exact: true },
    { label: 'Firm books', path: '/app/finance/books' },
  ] },
  { title: 'Audit', items: [
    { label: 'Program library', path: '/app/audit/library' },
    { label: 'Technical library', path: '/app/library' },
  ] },
];
