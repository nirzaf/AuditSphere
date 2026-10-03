export interface BudgetPreparationFields {
  currency: string;
  expectedVersion: string;
  lines: {
    role: string;
    activity: string;
    forecastMinutes: number;
    phase: string | null;
    riskArea: string | null;
  }[];
}
export interface BudgetPreparationPreview {
  engagementId: string;
  requestId: string;
  requestHash: string;
  reviewBasis: string;
  fields: BudgetPreparationFields;
  lines: {
    role: string;
    activity: string;
    phase: string;
    riskArea: string | null;
    forecastMinutes: number;
    rateCardId: string;
    ratePerHour: string;
    forecastCost: string;
  }[];
  forecastCost: string;
}
export interface BudgetPreparationReceipt {
  id: string;
  budgetId: string;
  engagementId: string;
  actorId: string;
  requestId: string;
  requestHash: string;
  reviewBasis: string;
  preview: BudgetPreparationPreview;
  createdAt: string;
}
const guid = (v: unknown): v is string =>
  typeof v === 'string' &&
  /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(v) &&
  v !== '00000000-0000-0000-0000-000000000000';
const hash = (v: unknown): v is string => typeof v === 'string' && /^[a-f0-9]{64}$/.test(v);
const money = (v: unknown): v is string =>
  typeof v === 'string' && /^\d{1,29}(?:\.\d{1,28})?$/.test(v);
const text = (v: unknown, limit: number): v is string =>
  typeof v === 'string' &&
  v.length >= 1 &&
  v.length <= limit &&
  v === v.trim() &&
  !/[\x00-\x1f\x7f]/.test(v);
export function decodeBudgetPreparationPreview(raw: unknown): BudgetPreparationPreview {
  if (!raw || typeof raw !== 'object') throw new Error('Unsupported preview');
  const p = raw as BudgetPreparationPreview,
    f = p.fields;
  if (
    !guid(p.engagementId) ||
    !guid(p.requestId) ||
    !hash(p.requestHash) ||
    !hash(p.reviewBasis) ||
    !f ||
    !/^[A-Z]{3}$/.test(f.currency) ||
    typeof f.expectedVersion !== 'string' ||
    !/^\d{1,19}$/.test(f.expectedVersion) ||
    BigInt(f.expectedVersion) > 9223372036854775807n ||
    !Array.isArray(f.lines) ||
    f.lines.length < 1 ||
    f.lines.length > 200 ||
    !Array.isArray(p.lines) ||
    p.lines.length !== f.lines.length ||
    !money(p.forecastCost)
  )
    throw new Error('Unsupported preview');
  for (let i = 0; i < f.lines.length; i++) {
    const l = f.lines[i],
      r = p.lines[i];
    if (
      !l ||
      !r ||
      !text(l.role, 50) ||
      !text(l.activity, 50) ||
      (l.phase !== null &&
        !['PLANNING', 'FIELDWORK', 'COMPLETION', 'REPORTING'].includes(l.phase)) ||
      (l.riskArea !== null && !text(l.riskArea, 120)) ||
      !Number.isSafeInteger(l.forecastMinutes) ||
      l.forecastMinutes < 1 ||
      l.forecastMinutes > 10000000 ||
      r.role !== l.role ||
      r.activity !== l.activity ||
      r.phase !== (l.phase ?? 'UNASSIGNED') ||
      r.riskArea !== l.riskArea ||
      r.forecastMinutes !== l.forecastMinutes ||
      !guid(r.rateCardId) ||
      !money(r.ratePerHour) ||
      !money(r.forecastCost)
    )
      throw new Error('Unsupported rate snapshot');
  }
  return p;
}
export function decodeBudgetPreparationReceipt(raw: unknown): BudgetPreparationReceipt {
  if (!raw || typeof raw !== 'object') throw new Error('Unsupported receipt');
  const r = raw as BudgetPreparationReceipt;
  if (
    ![r.id, r.budgetId, r.engagementId, r.actorId, r.requestId].every(guid) ||
    !hash(r.requestHash) ||
    !hash(r.reviewBasis) ||
    typeof r.createdAt !== 'string' ||
    !Number.isFinite(Date.parse(r.createdAt))
  )
    throw new Error('Unsupported receipt');
  const p = decodeBudgetPreparationPreview(r.preview);
  if (
    p.engagementId !== r.engagementId ||
    p.requestId !== r.requestId ||
    p.requestHash !== r.requestHash ||
    p.reviewBasis !== r.reviewBasis
  )
    throw new Error('Inconsistent receipt');
  return r;
}
export function decodeBudgetPreparationLookup(raw: unknown): {
  found: boolean;
  receipt: BudgetPreparationReceipt | null;
} {
  if (!raw || typeof raw !== 'object') throw new Error('Unsupported lookup');
  const v = raw as { found: unknown; receipt: unknown };
  if (typeof v.found !== 'boolean' || (!v.found && v.receipt !== null))
    throw new Error('Unsupported lookup');
  return { found: v.found, receipt: v.found ? decodeBudgetPreparationReceipt(v.receipt) : null };
}
