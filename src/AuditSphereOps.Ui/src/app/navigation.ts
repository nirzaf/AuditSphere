import type { NavIconName } from './navigation-icons';

/** Navigation is a presentation aid; every destination remains authorized by the server. */
export type NavGroupId = 'work' | 'commercial' | 'planning' | 'execution' | 'review-delivery' | 'firm-finance' | 'reference-admin';
export interface NavItem { id: string; label: string; path: string; icon: NavIconName; exact?: boolean; section?: string }
export interface NavGroup { id: NavGroupId; title: string; icon: NavIconName; items: NavItem[] }
export const NAVIGATION: NavGroup[] = [
  { id: 'work', title: 'My Work & Portfolio', icon: 'layout-dashboard', items: [
    { id: 'my-work', label: 'My work', path: '/app/work', icon: 'list-checks' },
    { id: 'portfolio', label: 'Portfolio', path: '/app', icon: 'layout-dashboard', exact: true },
  ] },
  { id: 'commercial', title: 'Clients & Commercial', icon: 'user-round-plus', items: [
    { id: 'leads', label: 'Practice leads', path: '/app/practice/leads', icon: 'user-round-plus' },
    { id: 'pipeline', label: 'Opportunity pipeline', path: '/app/practice/pipeline', icon: 'kanban' },
  ] },
  { id: 'planning', title: 'Engagements & Planning', icon: 'briefcase-business', items: [
    { id: 'resources', label: 'Resources', path: '/app/practice/resources', icon: 'calendar-days' },
  ] },
  { id: 'execution', title: 'Work Execution', icon: 'notebook-pen', items: [
    { id: 'accounting', label: 'Accounting', path: '/app/accounting', icon: 'calculator', exact: true, section: 'Client Accounting' },
    { id: 'mappings', label: 'Mappings', path: '/app/accounting/mappings', icon: 'git-branch', section: 'Client Accounting' },
    { id: 'adjustments', label: 'Adjustments', path: '/app/accounting/journals', icon: 'notebook-pen', section: 'Client Accounting' },
    { id: 'differences', label: 'Differences', path: '/app/accounting/differences', icon: 'scale', section: 'Client Accounting' },
    { id: 'evidence', label: 'Evidence', path: '/app/accounting/evidence', icon: 'file-check-2', section: 'Client Accounting' },
    { id: 'fx-policies', label: 'FX rates & policies', path: '/app/accounting/currency-configuration', icon: 'coins', section: 'Client Accounting' },
    { id: 'fx-remeasurement', label: 'FX remeasurement', path: '/app/accounting/remeasurement', icon: 'refresh-cw', section: 'Client Accounting' },
    { id: 'consolidation', label: 'Group consolidation', path: '/app/consolidation', icon: 'layers', section: 'Group Consolidation' },
  ] },
  { id: 'review-delivery', title: 'Review, Reporting & Close', icon: 'clipboard-check', items: [
    { id: 'reviews', label: 'Package reviews', path: '/app/accounting/reviews', icon: 'clipboard-check' },
    { id: 'roll-forward', label: 'Roll-forward', path: '/app/accounting/rollforward', icon: 'calendar-sync' },
    { id: 'restatements', label: 'Restatements', path: '/app/accounting/restatements', icon: 'history' },
  ] },
  { id: 'firm-finance', title: 'Firm Finance & Time', icon: 'book-open', items: [
    { id: 'time', label: 'Time', path: '/app/practice/time', icon: 'timer' },
    { id: 'firm-ledger', label: 'Firm ledger', path: '/app/finance', icon: 'book-open', exact: true },
    { id: 'firm-books', label: 'Firm books', path: '/app/finance/books', icon: 'book-copy' },
    { id: 'firm-invoices', label: 'Firm invoices', path: '/app/finance/invoices', icon: 'receipt' },
    { id: 'fee-settlements', label: 'Fee settlements', path: '/app/finance/fee-agreements', icon: 'coins' },
    { id: 'analytics', label: 'Analytics', path: '/app/practice/analytics', icon: 'chart-no-axes-combined' },
  ] },
  { id: 'reference-admin', title: 'Reference & Administration', icon: 'shield-check', items: [
    { id: 'program-library', label: 'Program library', path: '/app/audit/library', icon: 'clipboard-list' },
    { id: 'technical-library', label: 'Technical library', path: '/app/library', icon: 'book-open-text' },
    { id: 'administration', label: 'Administration', path: '/app/administration', icon: 'shield-check', exact: true },
    { id: 'users-access', label: 'Users & Access', path: '/app/administration/users', icon: 'users-round' },
    { id: 'microsoft365', label: 'Microsoft 365', path: '/app/administration/microsoft365/tenant-connection', icon: 'cloud' },
    { id: 'operations', label: 'Operations', path: '/app/operations', icon: 'activity' },
  ] },
];

export interface NavigationOwner { groupId: NavGroupId; icon: NavIconName }
/** Context routes have no global link; assign them to their existing work area without inventing destinations. */
const CONTEXT_OWNERS: ReadonlyArray<{ pattern: RegExp; owner: NavigationOwner }> = [
  { pattern: /^\/app\/engagements\/[^/]+\/(?:general-ledger|tb-intake)(?:\/|$)/, owner: { groupId: 'execution', icon: 'calculator' } },
  { pattern: /^\/app\/engagements\/[^/]+\/(?:audit-fieldwork|confirmations|pbc)(?:\/|$)/, owner: { groupId: 'execution', icon: 'file-search' } },
  { pattern: /^\/app\/engagements\/[^/]+\/audit-plan(?:\/|$)/, owner: { groupId: 'planning', icon: 'file-search' } },
  { pattern: /^\/app\/engagements\/[^/]+\/(?:financial-package|statements)(?:\/|$)/, owner: { groupId: 'review-delivery', icon: 'calculator' } },
  { pattern: /^\/app\/engagements\/[^/]+\/completion(?:\/|$)/, owner: { groupId: 'review-delivery', icon: 'file-search' } },
  { pattern: /^\/app\/engagements(?:\/|$)/, owner: { groupId: 'planning', icon: 'briefcase-business' } },
  { pattern: /^\/app\/clients\/[^/]+\/engagements(?:\/|$)/, owner: { groupId: 'planning', icon: 'briefcase-business' } },
  { pattern: /^\/app\/(?:clients|assessments)(?:\/|$)/, owner: { groupId: 'commercial', icon: 'briefcase-business' } },
  { pattern: /^\/app\/audit\/plans(?:\/|$)/, owner: { groupId: 'planning', icon: 'file-search' } },
  { pattern: /^\/app\/(?:audit|findings)(?:\/|$)/, owner: { groupId: 'execution', icon: 'file-search' } },
  { pattern: /^\/app\/(?:reviews|releases|completion|records)(?:\/|$)/, owner: { groupId: 'review-delivery', icon: 'file-search' } },
  { pattern: /^\/app\/accounting\/(?:packages|periods)(?:\/|$)/, owner: { groupId: 'review-delivery', icon: 'calculator' } },
  { pattern: /^\/app\/accounting(?:\/|$)/, owner: { groupId: 'execution', icon: 'calculator' } },
  { pattern: /^\/app\/practice\/(?:workflow-templates|reminder-rules)(?:\/|$)/, owner: { groupId: 'planning', icon: 'briefcase-business' } },
  { pattern: /^\/app\/practice\/(?:invoices|rate-cards)(?:\/|$)/, owner: { groupId: 'firm-finance', icon: 'receipt' } },
  { pattern: /^\/app\/practice(?:\/|$)/, owner: { groupId: 'commercial', icon: 'briefcase-business' } },
  { pattern: /^\/app\/finance(?:\/|$)/, owner: { groupId: 'firm-finance', icon: 'book-open' } },
  { pattern: /^\/app\/administration(?:\/|$)/, owner: { groupId: 'reference-admin', icon: 'shield-check' } },
  { pattern: /^\/app\/profile(?:\/|$)/, owner: { groupId: 'reference-admin', icon: 'user-round' } },
  { pattern: /^\/app\/overview(?:\/|$)/, owner: { groupId: 'work', icon: 'layout-dashboard' } },
];

/** Resolve one owner for a current route; this hint does not grant access or retain context. */
export function navigationOwner(url: string): NavigationOwner | null {
  const path = url.split(/[?#]/, 1)[0].replace(/^\/ui(?=\/)/, '');
  let best: { item: NavItem; groupId: NavGroupId } | null = null;
  for (const group of NAVIGATION) for (const item of group.items) {
    if ((path === item.path || (!item.exact && path.startsWith(item.path + '/'))) &&
        (!best || item.path.length > best.item.path.length)) best = { item, groupId: group.id };
  }
  if (best) return { groupId: best.groupId, icon: best.item.icon };
  return CONTEXT_OWNERS.find(rule => rule.pattern.test(path))?.owner ?? null;
}
