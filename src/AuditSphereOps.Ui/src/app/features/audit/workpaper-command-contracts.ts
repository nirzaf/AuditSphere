import { Decoder, arr, bool, guid, instant, nullable, obj, oneOf, sha256, str } from '../../core/decode';

export const workpaperRevision: Decoder<string> = (value, path) => {
  if (typeof value !== 'string' || !/^(0|[1-9][0-9]{0,18})$/.test(value) || BigInt(value) > 9223372036854775807n)
    throw new Error(`${path}: expected exact revision or actor epoch`);
  return value;
};
const positiveRevision: Decoder<string> = (value, path) => {
  const revision = workpaperRevision(value, path);
  if (revision === '0') throw new Error(`${path}: expected positive revision`);
  return revision;
};
export const workpaperAction = oneOf('SAVE_DRAFT', 'DISCARD_DRAFT', 'SUBMIT');
const saveFields = obj({ expectedDraftRevision: workpaperRevision, baseWorkpaperRevision: positiveRevision,
  baseInputGeneration: positiveRevision, basePolicyGeneration: positiveRevision, workPerformed: str(100000), conclusion: str(20000) });
const submitFields = obj({ expectedRevision: positiveRevision, expectedDraftRevision: positiveRevision,
  draftSaveId: guid, workPerformed: str(100000), conclusion: str(20000) });
const discardFields = obj({ expectedDraftRevision: positiveRevision });
const fieldsShape = obj({ action: workpaperAction, save: nullable(saveFields), submit: nullable(submitFields), discard: nullable(discardFields) });
export const decodeWorkpaperCommandFields: Decoder<ReturnType<typeof fieldsShape>> = (value, path) => {
  const fields = fieldsShape(value, path);
  if ((fields.save !== null) !== (fields.action === 'SAVE_DRAFT') ||
    (fields.submit !== null) !== (fields.action === 'SUBMIT') || (fields.discard !== null) !== (fields.action === 'DISCARD_DRAFT'))
    throw new Error(`${path}: expected exactly one action payload`);
  const content = fields.save ?? fields.submit;
  if (content && (content.workPerformed !== content.workPerformed.trim() || content.conclusion !== content.conclusion.trim() ||
    /[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/.test(content.workPerformed + content.conclusion)))
    throw new Error(`${path}: expected canonical bounded editor content`);
  if (fields.submit && (!fields.submit.workPerformed || !fields.submit.conclusion)) throw new Error(`${path}: submission content is required`);
  return fields;
};
export const decodeWorkpaperOwnedDraft = obj({ id: guid, baseWorkpaperRevision: positiveRevision,
  baseInputGeneration: positiveRevision, basePolicyGeneration: positiveRevision, draftRevision: positiveRevision,
  lastSaveId: guid, workPerformed: str(100000), conclusion: str(20000), lifecycle: oneOf('ACTIVE', 'DISCARDED', 'CONSUMED'), lastSavedAt: instant });
export const decodeWorkpaperCommandSnapshot = obj({ workpaperId: guid, clientId: guid, engagementId: guid,
  index: str(20000), title: str(20000), procedureId: nullable(guid), revision: positiveRevision, status: oneOf('WORKING', 'SUBMITTED_SNAPSHOT'),
  inputGeneration: positiveRevision, policyGeneration: positiveRevision, workPerformed: nullable(str(100000)),
  conclusion: nullable(str(20000)), submittedAt: nullable(instant), draft: nullable(decodeWorkpaperOwnedDraft) });
const stateShape = obj({ workpaper: decodeWorkpaperCommandSnapshot, reviewBasis: sha256,
  canSaveDraft: bool, canDiscardDraft: bool, canSubmit: bool, blockers: arr(str(4000), 100) });
export const decodeWorkpaperCommandState: Decoder<ReturnType<typeof stateShape>> = (value, path) => {
  const state = stateShape(value, path), w = state.workpaper;
  if ((state.blockers.length > 0 || w.status !== 'WORKING') && (state.canSaveDraft || state.canDiscardDraft || state.canSubmit) ||
    (state.canDiscardDraft || state.canSubmit) && w.draft?.lifecycle !== 'ACTIVE' || state.canSaveDraft && w.draft?.lifecycle === 'CONSUMED')
    throw new Error(`${path}: inconsistent workpaper capabilities`);
  return state;
};
const previewShape = obj({ requestId: guid, requestHash: sha256, reviewBasis: sha256, canonicalInput: decodeWorkpaperCommandFields,
  before: decodeWorkpaperCommandSnapshot, canProceed: bool, blockers: arr(str(4000), 100), summary: str(20000) });
export const decodeWorkpaperCommandPreview: Decoder<ReturnType<typeof previewShape>> = (value, path) => {
  const preview = previewShape(value, path), fields = preview.canonicalInput, draft = preview.before.draft;
  if (fields.action === 'SAVE_DRAFT' || !draft || draft.lifecycle !== 'ACTIVE' ||
    (fields.submit?.expectedDraftRevision ?? fields.discard?.expectedDraftRevision) !== draft.draftRevision ||
    preview.canProceed !== (preview.blockers.length === 0) || preview.canProceed && preview.before.status !== 'WORKING') throw new Error(`${path}: inconsistent final action preview`);
  const submit = fields.submit;
  if (submit && (submit.expectedRevision !== preview.before.revision || submit.draftSaveId !== draft.lastSaveId ||
    submit.workPerformed !== draft.workPerformed || submit.conclusion !== draft.conclusion ||
    draft.baseWorkpaperRevision !== preview.before.revision || draft.baseInputGeneration !== preview.before.inputGeneration ||
    draft.basePolicyGeneration !== preview.before.policyGeneration)) throw new Error(`${path}: submission differs from acknowledged draft and basis`);
  return preview;
};
export type WorkpaperCommandFields = ReturnType<typeof decodeWorkpaperCommandFields>;
export type WorkpaperCommandSnapshot = ReturnType<typeof decodeWorkpaperCommandSnapshot>;
export type WorkpaperCommandPreview = ReturnType<typeof decodeWorkpaperCommandPreview>;
export type WorkpaperOwnedDraft = ReturnType<typeof decodeWorkpaperOwnedDraft>;

export const workpaperActions = ['SAVE_DRAFT', 'DISCARD_DRAFT', 'SUBMIT'] as const;
export type WorkpaperAction = typeof workpaperActions[number];
const saveIntentShape = obj({ requestId: guid, requestHash: sha256, canonicalInput: decodeWorkpaperCommandFields });
export const decodeWorkpaperSaveIntent: Decoder<ReturnType<typeof saveIntentShape>> = (value, path) => {
  const intent = saveIntentShape(value, path);
  if (!intent.canonicalInput.save) throw new Error(`${path}: expected read-only save preparation`);
  return intent;
};
// Compare exact instants, including sub-millisecond precision, without depending on a database timezone.
function instantIdentity(value: string): string {
  const match = /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,9}))?(Z|[+-]\d{2}:\d{2})$/.exec(value);
  if (!match) throw new Error('Unsupported workpaper timestamp precision');
  const whole = Date.parse(match[1] + match[3]);
  if (!Number.isSafeInteger(whole)) throw new Error('Invalid workpaper timestamp');
  return (BigInt(whole) * 1000000n + BigInt((match[2] ?? '').padEnd(9, '0'))).toString();
}
export function sameWorkpaperSnapshot(a: WorkpaperCommandSnapshot, b: WorkpaperCommandSnapshot): boolean {
  const normalized = (value: WorkpaperCommandSnapshot) => {
    // Object insertion order is not snapshot identity. Decode both sides into the same complete contract order.
    const s = decodeWorkpaperCommandSnapshot(value, 'snapshot');
    return { ...s, submittedAt: s.submittedAt && instantIdentity(s.submittedAt),
      draft: s.draft && { ...s.draft, lastSavedAt: instantIdentity(s.draft.lastSavedAt) } };
  };
  return JSON.stringify(normalized(a)) === JSON.stringify(normalized(b));
}
export const sameWorkpaperFields = (a: WorkpaperCommandFields, b: WorkpaperCommandFields) => JSON.stringify(a) === JSON.stringify(b);
const eventShape = obj({ id: guid, actorId: guid, actorEpoch: workpaperRevision, action: workpaperAction, createdAt: instant,
  before: decodeWorkpaperCommandSnapshot, after: decodeWorkpaperCommandSnapshot });
const receiptShape = obj({ id: guid, workpaperId: guid, actorId: guid, actorEpoch: workpaperRevision, requestId: guid,
  requestHash: sha256, action: workpaperAction, createdAt: instant, preview: nullable(decodeWorkpaperCommandPreview), commandEvent: eventShape });
export const decodeWorkpaperCommandReceipt: Decoder<ReturnType<typeof receiptShape>> = (value, path) => {
  const receipt = receiptShape(value, path), e = receipt.commandEvent, before = e.before, after = e.after, p = receipt.preview;
  if (receipt.workpaperId !== before.workpaperId || receipt.workpaperId !== after.workpaperId ||
    receipt.actorId !== e.actorId || receipt.actorEpoch !== e.actorEpoch || receipt.action !== e.action)
    throw new Error(`${path}: inconsistent actor, target or action receipt`);
  if (receipt.action === 'SAVE_DRAFT') {
    const d = after.draft;
    if (p || !d || before.status !== 'WORKING' || d.lifecycle !== 'ACTIVE' || d.lastSaveId !== receipt.requestId ||
      d.baseWorkpaperRevision !== before.revision || d.baseInputGeneration !== before.inputGeneration || d.basePolicyGeneration !== before.policyGeneration ||
      before.draft && (before.draft.id !== d.id || before.draft.lifecycle === 'CONSUMED') ||
      BigInt(d.draftRevision) !== (before.draft ? BigInt(before.draft.draftRevision) + 1n : 1n) ||
      !sameWorkpaperSnapshot(after, { ...before, draft: d })) throw new Error(`${path}: inconsistent original save event`);
  } else {
    if (!p || !p.canProceed || p.requestId !== receipt.requestId || p.requestHash !== receipt.requestHash ||
      p.canonicalInput.action !== receipt.action || !sameWorkpaperSnapshot(p.before, before) || !before.draft || !after.draft)
      throw new Error(`${path}: receipt differs from reviewed final action`);
    const expectedDraft = { ...before.draft, lifecycle: receipt.action === 'SUBMIT' ? 'CONSUMED' as const : 'DISCARDED' as const };
    const expected: WorkpaperCommandSnapshot = receipt.action === 'SUBMIT'
      ? { ...before, draft: expectedDraft, revision: (BigInt(before.revision) + 1n).toString(), status: 'SUBMITTED_SNAPSHOT',
        workPerformed: p.canonicalInput.submit!.workPerformed, conclusion: p.canonicalInput.submit!.conclusion, submittedAt: after.submittedAt }
      : { ...before, draft: expectedDraft };
    if (receipt.action === 'SUBMIT' && !after.submittedAt || !sameWorkpaperSnapshot(after, expected))
      throw new Error(`${path}: receipt differs from exact proposed effect`);
  }
  return receipt;
};
const lookupShape = obj({ found: bool, receipt: nullable(decodeWorkpaperCommandReceipt) });
export const decodeWorkpaperCommandLookup: Decoder<ReturnType<typeof lookupShape>> = (value, path) => {
  const lookup = lookupShape(value, path);
  if (lookup.found !== !!lookup.receipt) throw new Error(`${path}: inconsistent receipt lookup`);
  return lookup;
};
export type WorkpaperCommandReceipt = ReturnType<typeof decodeWorkpaperCommandReceipt>;
