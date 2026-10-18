export interface TeamMember {
  assignmentId: string;
  userId: string;
  name: string;
  levelLabel: string;
  authorizationRole: string;
  certified: boolean;
}
export interface BudgetRow {
  phase: string;
  riskArea: string;
  forecastMinutes: number;
  actualMinutes: number;
  forecastCost: string;
  actualCost: string;
}
export interface Budget {
  currency: string;
  id: string;
  version: string;
  rows: BudgetRow[];
  forecastMinutes: number;
  actualMinutes: number;
  forecastCost: string;
  actualCost: string;
}
export interface DraftLine {
  role: string;
  activity: string;
  phase: string;
  riskArea: string | null;
  minutes: number;
  cost: string;
}
export interface Draft {
  id: string;
  version: string;
  currency: string;
  canApprove: boolean;
  lines: DraftLine[];
}
export interface Planning {
  team: TeamMember[];
  budget: Budget | null;
  budgetState: string;
  latestBudgetVersion: string;
  draft: Draft | null;
  canManageStaffing: boolean;
  canPrepareBudget: boolean;
  candidates: { userId: string; name: string; department: string | null; certified: boolean }[];
}
export interface PlanningEditableBudget {
  currency: string;
  lines: {
    role: string;
    activity: string;
    phase: string;
    riskArea: string;
    forecastMinutes: number;
  }[];
}
/**
 * Budget variance for one phase or total, in signed minutes (STE 4.5.1). Positive means more time than budgeted. The label states the
 * direction in words, so the sign is never the only cue.
 */
export function budgetVarianceLabel(forecastMinutes: number, actualMinutes: number): string {
  const variance = actualMinutes - forecastMinutes;
  if (variance === 0) return '0 (on budget)';
  return variance > 0 ? `+${variance} (over budget)` : `${variance} (under budget)`;
}

export function editablePlanningBudget(raw: unknown): PlanningEditableBudget | null {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return null;
  const value = raw as Record<string, unknown>;
  if (
    Object.keys(value).sort().join(',') !== 'currency,lines' ||
    typeof value['currency'] !== 'string' ||
    !/^[A-Z]{3}$/.test(value['currency']) ||
    !Array.isArray(value['lines']) ||
    value['lines'].length < 1 ||
    value['lines'].length > 200
  )
    return null;
  const lines: PlanningEditableBudget['lines'] = [];
  for (const rawLine of value['lines']) {
    if (!rawLine || typeof rawLine !== 'object' || Array.isArray(rawLine)) return null;
    const line = rawLine as Record<string, unknown>;
    if (
      Object.keys(line).sort().join(',') !== 'activity,forecastMinutes,phase,riskArea,role' ||
      !['role', 'activity', 'phase'].every(
        (key) =>
          typeof line[key] === 'string' &&
          line[key].length >= 1 &&
          line[key].length <= 50 &&
          line[key] === line[key].trim() &&
          !/[\x00-\x1f\x7f]/.test(line[key]),
      ) ||
      typeof line['riskArea'] !== 'string' ||
      line['riskArea'].length > 120 ||
      /[\x00-\x1f\x7f]/.test(line['riskArea']) ||
      !Number.isSafeInteger(line['forecastMinutes']) ||
      Number(line['forecastMinutes']) < 1 ||
      Number(line['forecastMinutes']) > 10000000
    )
      return null;
    lines.push({
      role: line['role'] as string,
      activity: line['activity'] as string,
      phase: line['phase'] as string,
      riskArea: line['riskArea'],
      forecastMinutes: line['forecastMinutes'] as number,
    });
  }
  return { currency: value['currency'], lines };
}
export const planningGuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export function exactDecimal(value: unknown): value is string {
  return typeof value === 'string' && /^-?\d{1,29}(?:\.\d{1,28})?$/.test(value);
}
export function decodePlanning(value: unknown): Planning {
  if (!value || typeof value !== 'object') throw new Error('Invalid planning');
  const v = value as Record<string, unknown>;
  if (
    !Array.isArray(v['team']) ||
    v['team'].length > 1000 ||
    !['APPROVED', 'UNAVAILABLE'].includes(v['budgetState'] as string)
  )
    throw new Error('Invalid team');
  for (const t of v['team'])
    if (
      !t ||
      !['assignmentId', 'userId'].every((k) => typeof t[k] === 'string' && planningGuid.test(t[k])) ||
      !['name', 'levelLabel', 'authorizationRole'].every((k) => typeof t[k] === 'string') ||
      typeof t.certified !== 'boolean'
    )
      throw new Error('Invalid member');
  if (
    typeof v['canManageStaffing'] !== 'boolean' ||
    typeof v['canPrepareBudget'] !== 'boolean' ||
    !Array.isArray(v['candidates']) ||
    v['candidates'].length > 100
  )
    throw new Error('Invalid staffing permissions');
  for (const c of v['candidates'])
    if (
      !c ||
      typeof c.userId !== 'string' ||
      !planningGuid.test(c.userId) ||
      typeof c.name !== 'string' ||
      typeof c.certified !== 'boolean' ||
      (c.department !== null && typeof c.department !== 'string')
    )
      throw new Error('Invalid candidate');
  if (typeof v['latestBudgetVersion'] !== 'string' || !/^\d{1,19}$/.test(v['latestBudgetVersion']))
    throw new Error('Invalid revision');
  const draft = v['draft'] as Draft | null;
  if (draft !== null) {
    if (
      !draft ||
      !planningGuid.test(draft.id) ||
      typeof draft.version !== 'string' ||
      !/^\d{1,19}$/.test(draft.version) ||
      typeof draft.currency !== 'string' ||
      typeof draft.canApprove !== 'boolean' ||
      !Array.isArray(draft.lines) ||
      draft.lines.length > 200
    )
      throw new Error('Invalid draft');
    for (const line of draft.lines)
      if (
        !line ||
        !['role', 'activity', 'phase'].every(
          (k) => typeof (line as unknown as Record<string, unknown>)[k] === 'string',
        ) ||
        (line.riskArea !== null && typeof line.riskArea !== 'string') ||
        !Number.isSafeInteger(line.minutes) ||
        line.minutes < 1 ||
        !exactDecimal(line.cost)
      )
        throw new Error('Invalid draft line');
  }
  const budget = v['budget'] as Record<string, unknown> | null;
  if (budget !== null) {
    if (
      !budget ||
      typeof budget['currency'] !== 'string' ||
      typeof budget['id'] !== 'string' ||
      !planningGuid.test(budget['id']) ||
      typeof budget['version'] !== 'string' ||
      !/^\d{1,19}$/.test(budget['version']) ||
      !Array.isArray(budget['rows']) ||
      budget['rows'].length > 1000
    )
      throw new Error('Invalid budget');
    for (const row of [budget, ...budget['rows']]) {
      if (
        !row ||
        !['forecastMinutes', 'actualMinutes'].every(
          (k) => Number.isSafeInteger(row[k]) && Number(row[k]) >= 0,
        ) ||
        !exactDecimal(row['forecastCost']) ||
        !exactDecimal(row['actualCost'])
      )
        throw new Error('Invalid financial quantity');
    }
    for (const row of budget['rows'])
      if (typeof row.phase !== 'string' || typeof row.riskArea !== 'string')
        throw new Error('Invalid phase');
  }
  if ((v['budgetState'] === 'APPROVED') !== (budget !== null))
    throw new Error('Inconsistent budget state');
  return v as unknown as Planning;
}
