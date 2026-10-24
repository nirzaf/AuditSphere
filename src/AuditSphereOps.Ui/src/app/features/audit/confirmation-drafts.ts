import type { ConfirmationBookedSourcePin } from './confirmation-contracts';

export type ConfirmationCreateModel = {
  areaCode: string;
  sourceRecordId: string;
  bookedAmount: string;
  currency: string;
  bookedSource?: ConfirmationBookedSourcePin | null;
  confirmationDate: string;
  respondent: string;
  contactValidationSource: string;
  reviewed: boolean;
};
export type ConfirmationBatchCaseModel = {
  sourceRecordId: string;
  bookedAmount: string;
  bookedSource?: ConfirmationBookedSourcePin | null;
  respondent: string;
  contactValidationSource: string;
};
export type ConfirmationBatchModel = {
  areaCode: string;
  currency: string;
  confirmationDate: string;
  procedureId: string;
  cases: ConfirmationBatchCaseModel[];
  reviewed: boolean;
};
export const emptyCreate = (): ConfirmationCreateModel => ({
  areaCode: '',
  sourceRecordId: '',
  bookedAmount: '',
  currency: '',
  confirmationDate: '',
  respondent: '',
  contactValidationSource: '',
  reviewed: false,
});
export const emptyBatchCase = (): ConfirmationBatchCaseModel => ({
  sourceRecordId: '',
  bookedAmount: '',
  respondent: '',
  contactValidationSource: '',
});
export const emptyBatch = (): ConfirmationBatchModel => ({
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
function strings<const T extends Record<string, number>>(
  raw: unknown,
  fields: T,
): { [K in keyof T]: string } | null {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return null;
  const o = raw as Record<string, unknown>,
    keys = Object.keys(fields) as (keyof T & string)[];
  if (Object.keys(o).some((k) => !keys.includes(k))) return null;
  const result: Record<string, string> = {};
  for (const k of keys) {
    if (typeof o[k] !== 'string' || (o[k] as string).length > fields[k]) return null;
    result[k] = o[k] as string;
  }
  return result as { [K in keyof T]: string };
}
function bookedSource(raw: unknown): ConfirmationBookedSourcePin | null | undefined | false {
  if (raw === undefined) return undefined;
  if (raw === null) return null;
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return false;
  const pin = raw as Record<string, unknown>;
  return Object.keys(pin).length === 3 &&
    pin['kind'] === 'BANK_RECONCILIATION' &&
    typeof pin['reconciliationId'] === 'string' &&
    /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(pin['reconciliationId']) &&
    typeof pin['expectedSnapshotSha256'] === 'string' &&
    /^[0-9a-f]{64}$/.test(pin['expectedSnapshotSha256'])
    ? {
        kind: 'BANK_RECONCILIATION',
        reconciliationId: pin['reconciliationId'],
        expectedSnapshotSha256: pin['expectedSnapshotSha256'],
      }
    : false;
}
export function createDraft(raw: unknown): Omit<ConfirmationCreateModel, 'reviewed'> | null {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return null;
  const { bookedSource: rawSource, ...fields } = raw as Record<string, unknown>;
  const source = bookedSource(rawSource),
    value = strings(fields, {
      areaCode: 40,
      sourceRecordId: 200,
      bookedAmount: 40,
      currency: 3,
      confirmationDate: 10,
      respondent: 500,
      contactValidationSource: 2000,
    });
  if (!value || source === false) return null;
  return source === undefined
    ? value
    : { ...value, bookedSource: source };
}
export function batchDraft(raw: unknown): Omit<ConfirmationBatchModel, 'reviewed'> | null {
  if (!raw || typeof raw !== 'object') return null;
  const { cases, ...shared } = raw as Record<string, unknown>;
  const head = strings(shared, {
    areaCode: 40,
    currency: 3,
    confirmationDate: 10,
    procedureId: 36,
  });
  if (!head || !Array.isArray(cases) || cases.length < 1 || cases.length > 100) return null;
  const rows = cases.map((c) => {
    if (!c || typeof c !== 'object' || Array.isArray(c)) return null;
    const { bookedSource: rawSource, ...fields } = c as Record<string, unknown>,
      source = bookedSource(rawSource),
      value = strings(fields, {
        sourceRecordId: 200,
        bookedAmount: 40,
        respondent: 500,
        contactValidationSource: 2000,
      });
    if (!value || source === false) return null;
    return source === undefined ? value : { ...value, bookedSource: source };
  });
  const validRows = rows.filter((c): c is NonNullable<typeof c> => c !== null);
  return validRows.length === rows.length ? { ...head, cases: validRows } : null;
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
