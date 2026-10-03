import { bool, date, guid, nat, nullable, obj, text } from '../../core/decode';
import {
  ResourceFormKind,
  ResourceModel,
  resourceMinutes,
  validResourceDate,
} from './resource-editor';
const hash = (v: unknown): string => {
  if (typeof v !== 'string' || !/^[a-f0-9]{64}$/.test(v))
    throw new Error('Unsupported planning proof');
  return v;
};
const bounded =
  (maximum: number) =>
  (v: unknown): string => {
    const s = text(v, '');
    if (!s.trim() || s.length > maximum || /[\x00-\x1f\x7f]/.test(s))
      throw new Error('Unsupported planning text');
    return s;
  };
const calendar = (v: unknown) => {
  const s = date(v, '');
  if (!validResourceDate(s)) throw new Error('Unsupported date');
  return s;
};
const rawFields = obj({
  kind: bounded(20),
  userId: guid,
  engagementId: nullable(guid),
  department: nullable(bounded(100)),
  skills: nullable(bounded(500)),
  weeklyCapacityMinutes: nullable(nat),
  targetUtilizationPercent: nullable(bounded(20)),
  name: nullable(bounded(100)),
  expiresOn: nullable(calendar),
  startDate: nullable(calendar),
  endDate: nullable(calendar),
  availabilityKind: nullable(bounded(30)),
  minutesPerDay: nullable(nat),
  weekStart: nullable(calendar),
  plannedMinutes: nullable(nat),
});
export type PlanningFields = ReturnType<typeof rawFields>;
export function decodePlanningFields(raw: unknown): PlanningFields {
  const f = rawFields(raw, ''),
    shaped = emptyPlanningFields(f.kind, f.userId);
  switch (f.kind) {
    case 'PROFILE':
      if (
        !f.department ||
        f.weeklyCapacityMinutes === null ||
        f.weeklyCapacityMinutes > 4800 ||
        f.targetUtilizationPercent === null ||
        !/^\d{1,3}(\.\d{1,2})?$/.test(f.targetUtilizationPercent) ||
        Number(f.targetUtilizationPercent) > 100
      )
        throw new Error('Unsupported profile');
      Object.assign(shaped, {
        department: f.department,
        skills: f.skills,
        weeklyCapacityMinutes: f.weeklyCapacityMinutes,
        targetUtilizationPercent: f.targetUtilizationPercent,
      });
      break;
    case 'CERTIFICATION':
      if (!f.name) throw new Error('Unsupported certification');
      Object.assign(shaped, { name: f.name, expiresOn: f.expiresOn });
      break;
    case 'AVAILABILITY':
      if (
        !f.startDate ||
        !f.endDate ||
        f.endDate < f.startDate ||
        f.minutesPerDay === null ||
        f.minutesPerDay < 1 ||
        f.minutesPerDay > 1440 ||
        !['LEAVE', 'TRAINING', 'PUBLIC_HOLIDAY'].includes(f.availabilityKind ?? '')
      )
        throw new Error('Unsupported availability');
      Object.assign(shaped, {
        startDate: f.startDate,
        endDate: f.endDate,
        minutesPerDay: f.minutesPerDay,
        availabilityKind: f.availabilityKind,
      });
      break;
    case 'ALLOCATION':
      if (!f.engagementId || !f.weekStart || f.plannedMinutes === null || f.plannedMinutes > 4800)
        throw new Error('Unsupported allocation');
      Object.assign(shaped, {
        engagementId: f.engagementId,
        weekStart: f.weekStart,
        plannedMinutes: f.plannedMinutes,
      });
      break;
    default:
      throw new Error('Unsupported action');
  }
  if (JSON.stringify(f) !== JSON.stringify(shaped)) throw new Error('Mixed planning action');
  return f;
}
export function emptyPlanningFields(kind: string, userId: string): PlanningFields {
  return {
    kind,
    userId,
    engagementId: null,
    department: null,
    skills: null,
    weeklyCapacityMinutes: null,
    targetUtilizationPercent: null,
    name: null,
    expiresOn: null,
    startDate: null,
    endDate: null,
    availabilityKind: null,
    minutesPerDay: null,
    weekStart: null,
    plannedMinutes: null,
  };
}
export function planningFields(kind: ResourceFormKind, m: ResourceModel): PlanningFields {
  const f = emptyPlanningFields(kind.toUpperCase(), m.user);
  switch (kind) {
    case 'profile':
      Object.assign(f, {
        department: m.department.trim(),
        skills: m.skills.trim() || null,
        weeklyCapacityMinutes: resourceMinutes(m.capacity),
        targetUtilizationPercent: m.target.trim(),
      });
      break;
    case 'certification':
      Object.assign(f, { name: m.name.trim(), expiresOn: m.expires || null });
      break;
    case 'availability':
      Object.assign(f, {
        startDate: m.from,
        endDate: m.to,
        availabilityKind: m.kind,
        minutesPerDay: resourceMinutes(m.hours, 24),
      });
      break;
    case 'allocation':
      Object.assign(f, {
        engagementId: m.engagement,
        weekStart: m.week,
        plannedMinutes: resourceMinutes(m.hours),
      });
      break;
  }
  return f;
}
const preview = obj({
  requestId: guid,
  requestHash: hash,
  reviewBasis: hash,
  fields: decodePlanningFields,
  before: obj({
    userName: bounded(200),
    department: nullable(bounded(100)),
    skills: nullable(text),
    weeklyCapacityMinutes: nullable(nat),
    targetUtilizationPercent: nullable(text),
    plannedMinutes: nullable(nat),
    engagementLabel: nullable(bounded(1000)),
    weekStart: nullable(calendar),
    capacityMinutes: nullable(nat),
    unavailableMinutes: nullable(nat),
    totalPlannedMinutes: nullable(nat),
  }),
  effect: bounded(2000),
});
export const decodePlanningPreview = (v: unknown) => preview(v, '');
export type PlanningPreview = ReturnType<typeof preview>;
const receipt = obj({
  id: guid,
  actorId: guid,
  targetUserId: guid,
  requestId: guid,
  requestHash: hash,
  reviewBasis: hash,
  kind: bounded(20),
  resourceId: nullable(guid),
  preview,
  createdAt: bounded(100),
});
export function decodePlanningReceipt(raw: unknown) {
  const r = receipt(raw, '');
  if (
    r.preview.requestId !== r.requestId ||
    r.preview.requestHash !== r.requestHash ||
    r.preview.reviewBasis !== r.reviewBasis ||
    r.preview.fields.userId !== r.targetUserId ||
    r.preview.fields.kind !== r.kind ||
    (!r.resourceId && !(r.kind === 'ALLOCATION' && r.preview.fields.plannedMinutes === 0)) ||
    !Number.isFinite(Date.parse(r.createdAt))
  )
    throw new Error('Unsupported planning receipt');
  return r;
}
export type PlanningReceipt = ReturnType<typeof decodePlanningReceipt>;
const lookup = obj({ found: bool, receipt: nullable(decodePlanningReceipt) });
export const decodePlanningLookup = (v: unknown) => {
  const r = lookup(v, '');
  if (r.found !== !!r.receipt) throw new Error('Unsupported planning lookup');
  return r;
};
export function planningSummary(p: PlanningPreview): { label: string; value: string }[] {
  const f = p.fields,
    b = p.before,
    items = [{ label: 'Team member', value: b.userName }];
  const add = (label: string, value: unknown) =>
    items.push({ label, value: value === null ? 'Not recorded' : String(value) });
  switch (f.kind) {
    case 'PROFILE':
      add('Previous department', b.department);
      add('Proposed department', f.department);
      add('Previous skills', b.skills);
      add('Proposed skills', f.skills);
      add('Previous weekly capacity (minutes)', b.weeklyCapacityMinutes);
      add('Proposed weekly capacity (minutes)', f.weeklyCapacityMinutes);
      add('Previous target %', b.targetUtilizationPercent);
      add('Proposed target %', f.targetUtilizationPercent);
      break;
    case 'CERTIFICATION':
      add('Certification', f.name);
      add('Expires', f.expiresOn);
      break;
    case 'AVAILABILITY':
      add('From', f.startDate);
      add('To', f.endDate);
      add('Kind', f.availabilityKind);
      add('Minutes per day', f.minutesPerDay);
      break;
    case 'ALLOCATION':
      add('Engagement', b.engagementLabel);
      add('Week of', f.weekStart);
      add('Previous allocation (minutes)', b.plannedMinutes);
      add('Proposed allocation (minutes)', f.plannedMinutes);
      add('Current capacity (minutes)', b.capacityMinutes);
      add('Current unavailable (minutes)', b.unavailableMinutes);
      add('Current total planned (minutes)', b.totalPlannedMinutes);
      break;
  }
  return items;
}
