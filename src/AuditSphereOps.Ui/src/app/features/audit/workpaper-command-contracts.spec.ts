import { describe, expect, it } from 'vitest';
import { decode } from '../../core/decode';
import { decodeWorkpaperCommandFields, decodeWorkpaperCommandPreview, decodeWorkpaperCommandSnapshot, sameWorkpaperSnapshot, workpaperRevision } from './workpaper-command-contracts';

const id = '11111111-1111-4111-8111-111111111111';
const hash = 'a'.repeat(64);
const draft = { id, baseWorkpaperRevision: '9007199254740993', baseInputGeneration: '2', basePolicyGeneration: '3',
  draftRevision: '4', lastSaveId: id, workPerformed: 'Original work', conclusion: 'Original conclusion', lifecycle: 'ACTIVE',
  lastSavedAt: '2026-10-16T10:00:00Z' };
const before = { workpaperId: id, clientId: id, engagementId: id, index: 'W1', title: 'Workpaper', procedureId: null,
  revision: draft.baseWorkpaperRevision, status: 'WORKING', inputGeneration: '2', policyGeneration: '3',
  workPerformed: null, conclusion: null, submittedAt: null, draft };
const fields = { action: 'SUBMIT', save: null, discard: null, submit: { expectedRevision: before.revision,
  expectedDraftRevision: draft.draftRevision, draftSaveId: id, workPerformed: draft.workPerformed, conclusion: draft.conclusion } };
const preview = { requestId: id, requestHash: hash, reviewBasis: hash, canonicalInput: fields, before,
  canProceed: true, blockers: [], summary: 'Freeze this acknowledged draft.' };

describe('Workpaper reviewed command contract', () => {
  it('compares complete decoded snapshots independently of root and nested object key order', () => {
    const original = decodeWorkpaperCommandSnapshot(before, 'snapshot');
    const reordered = Object.fromEntries(Object.entries(original).reverse()) as typeof original;
    reordered.draft = Object.fromEntries(Object.entries(original.draft!).reverse()) as NonNullable<typeof original.draft>;
    expect(sameWorkpaperSnapshot(original, reordered)).toBe(true);
    expect(sameWorkpaperSnapshot(reordered, original)).toBe(true);
    expect(sameWorkpaperSnapshot(original, { ...reordered, draft: { ...reordered.draft, workPerformed: 'Changed work' } })).toBe(false);
    expect(sameWorkpaperSnapshot(original, { ...reordered, draft: { ...reordered.draft, conclusion: 'Changed conclusion' } })).toBe(false);
    expect(sameWorkpaperSnapshot(original, { ...reordered, clientId: '22222222-2222-4222-8222-222222222222' })).toBe(false);
    expect(sameWorkpaperSnapshot(original, { ...reordered, revision: '9007199254740994' })).toBe(false);
  });
  it('keeps exact sub-millisecond timestamp identity while accepting equivalent timezone offsets', () => {
    const original = decodeWorkpaperCommandSnapshot({ ...before, draft: { ...draft, lastSavedAt: '2026-10-16T10:00:00.123456789Z' } }, 'snapshot');
    const equivalent = { ...original, draft: { ...original.draft!, lastSavedAt: '2026-10-16T13:00:00.123456789+03:00' } };
    expect(sameWorkpaperSnapshot(original, equivalent)).toBe(true);
    expect(sameWorkpaperSnapshot(original, { ...equivalent, draft: { ...equivalent.draft, lastSavedAt: '2026-10-16T13:00:00.123456788+03:00' } })).toBe(false);
  });
  it('retains exact revisions above the JavaScript integer range', () => {
    expect(decode(decodeWorkpaperCommandPreview, preview).before.revision).toBe('9007199254740993');
    expect(decode(workpaperRevision, '9223372036854775807')).toBe('9223372036854775807');
  });
  it.each([9007199254740992, '9223372036854775808', '-1', '01', '1.0'])('refuses an inexact or invalid revision %s', value => {
    expect(() => decode(workpaperRevision, value)).toThrow();
  });
  it('refuses mixed action payloads and noncanonical content', () => {
    expect(() => decode(decodeWorkpaperCommandFields, { ...fields, discard: { expectedDraftRevision: '4' } })).toThrow();
    expect(() => decode(decodeWorkpaperCommandFields, { ...fields, submit: { ...fields.submit, workPerformed: ' Original work ' } })).toThrow();
  });
  it.each([
    { ...draft, lastSaveId: '22222222-2222-4222-8222-222222222222' },
    { ...draft, draftRevision: '5' },
    { ...draft, workPerformed: 'Changed work' },
    { ...draft, conclusion: 'Changed conclusion' },
    { ...draft, baseInputGeneration: '1' },
    { ...draft, basePolicyGeneration: '2' },
    { ...draft, baseWorkpaperRevision: '9007199254740992' },
    { ...draft, lifecycle: 'DISCARDED' },
  ])('refuses a preview that does not freeze the exact acknowledged draft', changed => {
    expect(() => decode(decodeWorkpaperCommandPreview, { ...preview, before: { ...before, draft: changed } })).toThrow();
  });
  it('refuses autosave masquerading as an assented final-action preview', () => {
    const save = { expectedDraftRevision: '4', baseWorkpaperRevision: before.revision, baseInputGeneration: '2',
      basePolicyGeneration: '3', workPerformed: draft.workPerformed, conclusion: draft.conclusion };
    expect(() => decode(decodeWorkpaperCommandPreview, { ...preview, canonicalInput: { action: 'SAVE_DRAFT', save } })).toThrow();
  });
  it('retains an exact discard preview without claiming its retained content was deleted', () => {
    const decoded = decode(decodeWorkpaperCommandPreview, { ...preview,
      canonicalInput: { action: 'DISCARD_DRAFT', discard: { expectedDraftRevision: '4' } } });
    expect(decoded.before.draft?.workPerformed).toBe(draft.workPerformed);
    expect(decoded.canonicalInput.discard?.expectedDraftRevision).toBe('4');
  });
});
