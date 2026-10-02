export const emptyCreate = () => ({
  areaCode: '',
  sourceRecordId: '',
  bookedAmount: '',
  currency: '',
  confirmationDate: '',
  respondent: '',
  contactValidationSource: '',
  reviewed: false,
});
export const emptyBatchCase = () => ({
  sourceRecordId: '',
  bookedAmount: '',
  respondent: '',
  contactValidationSource: '',
});
export const emptyBatch = () => ({
  areaCode: '',
  currency: '',
  confirmationDate: '',
  procedureId: '',
  cases: [emptyBatchCase()],
  reviewed: false,
});
export const emptyAction = () => ({
  reference: '',
  origin: '',
  channel: '',
  confirmedAmount: '',
  authenticityAssessment: '',
  decision: 'AGREED',
  purpose: '',
  evidence: '',
  conclusion: '',
  critical: false,
  rationale: '',
  reviewed: false,
});
export type DraftKind = 'create' | 'batch' | 'action';
export function intent<T extends { reviewed: boolean }>(model: T): Omit<T, 'reviewed'> {
  const { reviewed, ...value } = model;
  return value;
}
function strings(raw: unknown, fields: Record<string, number>): Record<string, string> | null {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return null;
  const o = raw as Record<string, unknown>,
    keys = Object.keys(fields);
  if (Object.keys(o).some((k) => !keys.includes(k))) return null;
  const result: Record<string, string> = {};
  for (const k of keys) {
    if (typeof o[k] !== 'string' || (o[k] as string).length > fields[k]) return null;
    result[k] = o[k] as string;
  }
  return result;
}
export function createDraft(raw: unknown): Omit<ReturnType<typeof emptyCreate>, 'reviewed'> | null {
  return strings(raw, {
    areaCode: 40,
    sourceRecordId: 200,
    bookedAmount: 40,
    currency: 3,
    confirmationDate: 10,
    respondent: 500,
    contactValidationSource: 2000,
  }) as ReturnType<typeof createDraft>;
}
export function batchDraft(raw: unknown): Omit<ReturnType<typeof emptyBatch>, 'reviewed'> | null {
  if (!raw || typeof raw !== 'object') return null;
  const { cases, ...shared } = raw as Record<string, unknown>;
  const head = strings(shared, {
    areaCode: 40,
    currency: 3,
    confirmationDate: 10,
    procedureId: 36,
  });
  if (!head || !Array.isArray(cases) || cases.length < 1 || cases.length > 100) return null;
  const rows = cases.map((c) =>
    strings(c, {
      sourceRecordId: 200,
      bookedAmount: 40,
      respondent: 500,
      contactValidationSource: 2000,
    }),
  );
  return rows.every((c) => c !== null)
    ? ({ ...head, cases: rows } as ReturnType<typeof batchDraft>)
    : null;
}
export function actionDraft(raw: unknown): Omit<ReturnType<typeof emptyAction>, 'reviewed'> | null {
  if (!raw || typeof raw !== 'object') return null;
  const { critical, ...fields } = raw as Record<string, unknown>;
  const value = strings(fields, {
    reference: 500,
    origin: 40,
    channel: 40,
    confirmedAmount: 40,
    authenticityAssessment: 4000,
    decision: 30,
    purpose: 2000,
    evidence: 20000,
    conclusion: 4000,
    rationale: 4000,
  });
  return value &&
    typeof critical === 'boolean' &&
    ['AGREED', 'DIFFERENCE', 'NO_RESPONSE', 'ALTERNATIVE_REQUIRED'].includes(value['decision'])
    ? ({ ...value, critical } as ReturnType<typeof actionDraft>)
    : null;
}
