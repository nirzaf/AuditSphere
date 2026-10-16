import { decodeWorkpaperCommandFields, WorkpaperCommandSnapshot } from './workpaper-command-contracts';

export const workpaperId = '11111111-1111-4111-8111-111111111111';
export const engagementId = '22222222-2222-4222-8222-222222222222';
export const draftId = '33333333-3333-4333-8333-333333333333';
export const requestId = '44444444-4444-4444-8444-444444444444';
export const requestHash = 'a'.repeat(64);
export const basis = 'b'.repeat(64);
export const at = '2026-10-16T10:00:00Z';
export function detail(id = workpaperId) {
  return { id, engagementId, index: 'C.01', title: 'Cash and Bank Testing', objective: 'Verify balances.', templateVersion: '1.0',
    procedure: 'Reconciliation testing.', linkedProcedureTitle: null, revision: 1, status: 'WORKING', workPerformed: null,
    conclusion: null, createdAt: at, submittedAt: null, submissions: [], draft: { workpaperId: id, draftId,
      baseWorkpaperRevision: 1, baseInputGeneration: 1, basePolicyGeneration: 1, draftRevision: 2,
      workPerformed: 'Worked.', conclusion: 'Clear.', lastSaveId: engagementId, lastSavedAt: at, lifecycle: 'ACTIVE' } };
}
export function snapshot(id = workpaperId): WorkpaperCommandSnapshot {
  return { workpaperId: id, clientId: workpaperId, engagementId, index: 'C.01', title: 'Cash and Bank Testing', procedureId: null,
    revision: '1', status: 'WORKING', inputGeneration: '1', policyGeneration: '1', workPerformed: null, conclusion: null, submittedAt: null,
    draft: { id: draftId, baseWorkpaperRevision: '1', baseInputGeneration: '1', basePolicyGeneration: '1', draftRevision: '2',
      lastSaveId: engagementId, workPerformed: 'Worked.', conclusion: 'Clear.', lastSavedAt: at, lifecycle: 'ACTIVE' } };
}
export const commandState = (s = snapshot()) => ({ workpaper: s, reviewBasis: basis, canSaveDraft: true, canSubmit: true, canDiscardDraft: true, blockers: [] });
export function commandPreview(body: { requestId: string; fields: unknown; reviewBasis?: string | null }, before = snapshot()) {
  return { requestId: body.requestId, requestHash, reviewBasis: body.reviewBasis ?? basis,
    canonicalInput: decodeWorkpaperCommandFields(body.fields, 'fixture'), before, canProceed: true, blockers: [], summary: 'Exact original workpaper effect.' };
}
export function commandReceipt(body: { requestId: string; fields: unknown; reviewBasis?: string | null }, before = snapshot()) {
  const fields = decodeWorkpaperCommandFields(body.fields, 'fixture'), previous = before.draft;
  const after: WorkpaperCommandSnapshot = fields.save ? { ...before, draft: { id: previous?.id ?? draftId,
    baseWorkpaperRevision: fields.save.baseWorkpaperRevision, baseInputGeneration: fields.save.baseInputGeneration,
    basePolicyGeneration: fields.save.basePolicyGeneration, draftRevision: previous ? (BigInt(previous.draftRevision) + 1n).toString() : '1',
    lastSaveId: body.requestId, workPerformed: fields.save.workPerformed, conclusion: fields.save.conclusion, lifecycle: 'ACTIVE', lastSavedAt: at } }
    : { ...before, draft: { ...previous!, lifecycle: fields.submit ? 'CONSUMED' : 'DISCARDED' },
      ...(fields.submit ? { revision: (BigInt(before.revision) + 1n).toString(), status: 'SUBMITTED_SNAPSHOT' as const,
        workPerformed: fields.submit.workPerformed, conclusion: fields.submit.conclusion, submittedAt: at } : {}) };
  return { id: requestId, workpaperId: before.workpaperId, actorId: workpaperId, actorEpoch: '1', requestId: body.requestId, requestHash,
    action: fields.action, createdAt: at, preview: fields.save ? null : commandPreview(body, before), commandEvent: {
      id: requestId, actorId: workpaperId, actorEpoch: '1', action: fields.action, createdAt: at, before, after } };
}
export const saveFields = { action: 'SAVE_DRAFT', save: { expectedDraftRevision: '2', baseWorkpaperRevision: '1',
  baseInputGeneration: '1', basePolicyGeneration: '1', workPerformed: 'Updated work.', conclusion: 'Clear.' } };
