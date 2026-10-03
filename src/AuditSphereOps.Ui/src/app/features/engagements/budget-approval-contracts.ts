export interface BudgetApprovalPreview {
  engagementId: string;
  requestId: string;
  requestHash: string;
  reviewBasis: string;
  fields: { budgetId: string; expectedVersion: string };
  currency: string;
  lines: {
    role: string;
    activity: string;
    phase: string;
    riskArea: string | null;
    minutes: number;
    cost: string;
  }[];
  forecastCost: string;
  engagementGeneration: string;
  clientGeneration: string;
}
export interface BudgetApprovalReceipt {
  id: string;
  budgetId: string;
  engagementId: string;
  actorId: string;
  requestId: string;
  requestHash: string;
  reviewBasis: string;
  preview: BudgetApprovalPreview;
  createdAt: string;
}
const guid = (v: unknown): v is string =>
  typeof v === 'string' &&
  /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(v) &&
  v !== '00000000-0000-0000-0000-000000000000';
const hash = (v: unknown): v is string => typeof v === 'string' && /^[a-f0-9]{64}$/.test(v);
const money = (v: unknown): v is string =>
  typeof v === 'string' && /^\d{1,29}(?:\.\d{1,28})?$/.test(v);
const revision = (v: unknown): v is string =>
  typeof v === 'string' && /^\d{1,19}$/.test(v) && BigInt(v) <= 9223372036854775807n;
const text = (v: unknown, limit: number): v is string =>
  typeof v === 'string' &&
  v.length >= 1 &&
  v.length <= limit &&
  v === v.trim() &&
  !/[\x00-\x1f\x7f]/.test(v);
export function decodeBudgetApprovalPreview(raw: unknown): BudgetApprovalPreview {
  if (!raw || typeof raw !== 'object') throw new Error('Unsupported approval preview');
  const p = raw as BudgetApprovalPreview;
  if (
    !guid(p.engagementId) ||
    !guid(p.requestId) ||
    !hash(p.requestHash) ||
    !hash(p.reviewBasis) ||
    !p.fields ||
    !guid(p.fields.budgetId) ||
    !revision(p.fields.expectedVersion) ||
    BigInt(p.fields.expectedVersion) < 1n ||
    typeof p.currency !== 'string' ||
    !/^[A-Z]{3}$/.test(p.currency) ||
    !revision(p.engagementGeneration) ||
    !revision(p.clientGeneration) ||
    !money(p.forecastCost) ||
    !Array.isArray(p.lines) ||
    p.lines.length < 1 ||
    p.lines.length > 200
  )
    throw new Error('Unsupported approval preview');
  for (const l of p.lines)
    if (
      !l ||
      !text(l.role, 50) ||
      !text(l.activity, 50) ||
      !text(l.phase, 50) ||
      (l.riskArea !== null && !text(l.riskArea, 120)) ||
      !Number.isSafeInteger(l.minutes) ||
      l.minutes < 1 ||
      l.minutes > 10000000 ||
      !money(l.cost)
    )
      throw new Error('Unsupported draft line');
  return p;
}
export function decodeBudgetApprovalReceipt(raw: unknown): BudgetApprovalReceipt {
  if (!raw || typeof raw !== 'object') throw new Error('Unsupported approval receipt');
  const r = raw as BudgetApprovalReceipt;
  if (
    ![r.id, r.budgetId, r.engagementId, r.actorId, r.requestId].every(guid) ||
    !hash(r.requestHash) ||
    !hash(r.reviewBasis) ||
    typeof r.createdAt !== 'string' ||
    !Number.isFinite(Date.parse(r.createdAt))
  )
    throw new Error('Unsupported approval receipt');
  const p = decodeBudgetApprovalPreview(r.preview);
  if (
    p.fields.budgetId !== r.budgetId ||
    p.engagementId !== r.engagementId ||
    p.requestId !== r.requestId ||
    p.requestHash !== r.requestHash ||
    p.reviewBasis !== r.reviewBasis
  )
    throw new Error('Inconsistent approval receipt');
  return r;
}
export function decodeBudgetApprovalLookup(raw: unknown): {
  found: boolean;
  receipt: BudgetApprovalReceipt | null;
} {
  if (!raw || typeof raw !== 'object') throw new Error('Unsupported approval lookup');
  const v = raw as { found: unknown; receipt: unknown };
  if (typeof v.found !== 'boolean' || (!v.found && v.receipt !== null))
    throw new Error('Unsupported approval lookup');
  return { found: v.found, receipt: v.found ? decodeBudgetApprovalReceipt(v.receipt) : null };
}
