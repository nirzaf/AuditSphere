import { arr, bool, date, guid, instant, nat, nullable, obj, sha256, str } from '../../core/decode';
const identity = (v: unknown, p: string) => {
  const id = guid(v, p);
  if (id === '00000000-0000-0000-0000-000000000000') throw new Error('Unsupported identity');
  return id;
};
export interface EngagementFields {
  serviceRoute: string;
  serviceProfile: string;
  periodStart: string;
  periodEnd: string;
  annualSourceEngagementId?: string;
  annualTemplateRevisionId?: string;
}
export const emptyEngagement = (): EngagementFields => ({
  serviceRoute: '',
  serviceProfile: '',
  periodStart: '',
  periodEnd: '',
});
const fields = obj({
  serviceRoute: str(50),
  serviceProfile: str(100),
  periodStart: str(10),
  periodEnd: str(10),
});
export function engagementFields(raw: unknown): EngagementFields | null {
  try {
    if (!raw || typeof raw !== 'object' || Object.keys(raw).some((k) =>
      !['serviceRoute', 'serviceProfile', 'periodStart', 'periodEnd', 'annualSourceEngagementId', 'annualTemplateRevisionId'].includes(k))) return null;
    const f = fields(raw, 'fields');
    if (Object.values(f).some((s) => /[\u0000-\u001f\u007f]/.test(s))) return null;
    const source = (raw as { annualSourceEngagementId?: unknown }).annualSourceEngagementId;
    const template = (raw as { annualTemplateRevisionId?: unknown }).annualTemplateRevisionId;
    if (template != null && source == null) return null;
    return { ...f, ...(source == null ? {} : { annualSourceEngagementId: identity(source, 'annualSourceEngagementId') }),
      ...(template == null ? {} : { annualTemplateRevisionId: identity(template, 'annualTemplateRevisionId') }) };
  } catch {
    return null;
  }
}
const validDate = (s: string) =>
  /^\d{4}-\d{2}-\d{2}$/.test(s) &&
  s.slice(0, 4) !== '0000' &&
  Number.isFinite(Date.parse(s)) &&
  new Date(s).toISOString().slice(0, 10) === s;
export function validEngagement(f: EngagementFields): boolean {
  return (
    !!engagementFields(f) &&
    f.serviceRoute.trim().length >= 2 &&
    !!f.serviceProfile.trim() &&
    validDate(f.periodStart) &&
    validDate(f.periodEnd) &&
    f.periodStart <= f.periodEnd
  );
}
const canonical = (v: unknown, path = '') => {
  const f = engagementFields(v);
  if (!f) throw new Error('Unsupported engagement fields');
  if (!validEngagement(f) || Object.values(f).some((s) => s !== s.trim()))
    throw new Error('Unsupported engagement intent');
  date(f.periodStart, path);
  date(f.periodEnd, path);
  return f;
};
const generation = (v: unknown): string => {
  if (typeof v !== 'string' || !/^[1-9]\d{0,18}$/.test(v) || BigInt(v) > 9223372036854775807n)
    throw new Error('Unsupported generation');
  return v;
};
const timestamp = (v: unknown, p: string) => {
  const s = instant(v, p);
  if (!Number.isFinite(Date.parse(s))) throw new Error('Unsupported time');
  return s;
};
const annualMilestoneKind = (v: unknown, p: string) => {
  if (v === 'FIELDWORK_START' || v === 'DRAFT_REPORT' || v === 'FINAL_REPORT' || v === 'STATUTORY_CUTOFF') return v;
  throw new Error(`Unsupported milestone kind at ${p}`);
};
const annualMilestoneSuggestionState = (v: unknown, p: string) => {
  if (v === 'AVAILABLE' || v === 'MISSING' || v === 'RESTRICTED' || v === 'UNAVAILABLE') return v;
  throw new Error(`Unsupported milestone suggestion state at ${p}`);
};
const signedDayOffset = (v: unknown, p: string) => {
  if (Number.isSafeInteger(v)) return v as number;
  throw new Error(`Unsupported milestone offset at ${p}`);
};
const annualMilestoneSuggestion = obj({
  kind: annualMilestoneKind,
  sourceDate: date,
  offsetDaysFromSourcePeriodEnd: signedDayOffset,
  suggestedDate: nullable(date),
  sourcePlanRevision: nat,
  notice: str(300),
});
export const decodeEngagementCreationState = obj({
  clientId: identity,
  clientName: str(500),
  clientGeneration: generation,
  reviewBasis: sha256,
});
const annualPreparation = obj({
  clientId: identity, sourceEngagementId: identity, sourcePeriodStart: date, sourcePeriodEnd: date,
  fields: canonical, copiedReferences: arr(str(200), 10), requiresFreshWork: arr(str(200), 10),
  contactSuggestions: arr(obj({ id: identity, name: str(2000), role: str(200), primary: bool }), 10),
  historicalMappingState: (value: unknown, path: string) => {
    if (value !== 'AVAILABLE' && value !== 'RESTRICTED') throw new Error(`Unsupported mapping reference state at ${path}`);
    return value;
  },
  historicalMappings: arr(obj({ id: identity, version: generation, taxonomyVersion: str(100), periodStart: date, periodEnd: date }), 5),
  taskTemplateSuggestions: arr(obj({ templateId: identity, revisionId: identity, version: nat, name: str(160), contentDigest: sha256 }), 3),
  milestoneSuggestions: arr(annualMilestoneSuggestion, 4),
  milestoneSuggestionState: annualMilestoneSuggestionState,
});
export const decodeAnnualPreparation = (value: unknown, path = '') => {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error(`Unsupported annual preparation at ${path}`);
  const raw = value as Record<string, unknown>;
  const annual = annualPreparation({
    ...raw,
    milestoneSuggestions: raw['milestoneSuggestions'] ?? [],
    milestoneSuggestionState: raw['milestoneSuggestionState'] ?? 'MISSING',
  }, path);
  if ((annual.historicalMappingState === 'RESTRICTED' && annual.historicalMappings.length !== 0) ||
    annual.historicalMappings.some(mapping => mapping.periodStart !== annual.sourcePeriodStart || mapping.periodEnd !== annual.sourcePeriodEnd) ||
    new Set(annual.historicalMappings.map(mapping => mapping.id)).size !== annual.historicalMappings.length ||
    new Set(annual.contactSuggestions.map(contact => contact.id)).size !== annual.contactSuggestions.length ||
    new Set(annual.taskTemplateSuggestions.map(template => template.revisionId)).size !== annual.taskTemplateSuggestions.length ||
    annual.taskTemplateSuggestions.some(template => template.version < 1) ||
    (annual.fields.annualTemplateRevisionId && !annual.taskTemplateSuggestions.some(template => template.revisionId === annual.fields.annualTemplateRevisionId)) ||
    (annual.milestoneSuggestionState !== 'AVAILABLE' && annual.milestoneSuggestions.length !== 0) ||
    new Set(annual.milestoneSuggestions.map(milestone => milestone.kind)).size !== annual.milestoneSuggestions.length ||
    annual.milestoneSuggestions.some(milestone => milestone.sourcePlanRevision < 1 ||
      (milestone.kind === 'STATUTORY_CUTOFF' && milestone.suggestedDate !== null)))
    throw new Error('Invalid annual reference context');
  return annual;
};
export const decodeEngagementCreationPreview = obj({
  clientId: identity,
  requestId: identity,
  reviewBasis: sha256,
  requestHash: sha256,
  fields: canonical,
  existingEngagementId: nullable(identity),
});
export const decodeEngagementCreationReceipt = obj({
  id: identity,
  clientId: identity,
  engagementId: identity,
  actorId: identity,
  requestId: identity,
  requestHash: sha256,
  reviewBasis: sha256,
  clientGeneration: generation,
  engagementGeneration: one,
  fields: canonical,
  createdAt: timestamp,
});
function one(v: unknown): string {
  if (v !== '1') throw new Error('Unsupported initial revision');
  return v;
}
const lookup = obj({ found: bool, receipt: nullable(decodeEngagementCreationReceipt) });
export function decodeEngagementCreationLookup(raw: unknown, path = '') {
  const l = lookup(raw, path);
  if (l.found !== (l.receipt !== null)) throw new Error('Unsupported receipt state');
  return l;
}
