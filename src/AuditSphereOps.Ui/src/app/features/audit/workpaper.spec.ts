import { describe, expect, it } from 'vitest';
import { decodeWorkpaper } from './workpaper';

const id1 = '11111111-1111-4111-8111-111111111111';
const id2 = '22222222-2222-4222-8222-222222222222';
const id3 = '33333333-3333-4333-8333-333333333333';

describe('Audit Workpaper Contracts', () => {
  it('decodes a valid workpaper payload', () => {
    const raw = {
      id: id1,
      engagementId: id2,
      index: 'C.01',
      title: 'Cash and Bank Testing',
      objective: 'Verify existence and completeness of bank balances at year end.',
      templateVersion: '1.0',
      procedure: 'Perform bank reconciliation testing for all open bank accounts.',
      linkedProcedureTitle: 'Bank reconciliations',
      revision: 1,
      status: 'WORKING',
      workPerformed: 'Tested reconciliation between GL and bank statements.',
      conclusion: 'Reconciliation is complete and free of material difference.',
      createdAt: '2026-10-01T08:00:00Z',
      submittedAt: null,
      submissions: [
        {
          revision: 1,
          submittedAt: '2026-10-01T12:00:00Z',
          conclusion: 'Initial review conclusion.',
        },
      ],
      draft: {
        workpaperId: id1,
        draftId: id3,
        baseWorkpaperRevision: 1,
        baseInputGeneration: 1,
        basePolicyGeneration: 1,
        draftRevision: 2,
        workPerformed: 'Tested reconciliation between GL and bank statements.',
        conclusion: 'Reconciliation is complete and free of material difference.',
        lastSaveId: id2,
        lastSavedAt: '2026-10-01T11:45:00Z',
        lifecycle: 'ACTIVE',
      },
    };

    const decoded = decodeWorkpaper(raw, 'workpaper');
    expect(decoded.id).toBe(id1);
    expect(decoded.engagementId).toBe(id2);
    expect(decoded.index).toBe('C.01');
    expect(decoded.status).toBe('WORKING');
    expect(decoded.submissions.length).toBe(1);
    expect(decoded.draft.draftRevision).toBe(2);
    expect(decoded.draft.lifecycle).toBe('ACTIVE');
  });

  it('rejects invalid workpaper payloads', () => {
    expect(() => decodeWorkpaper(null, 'workpaper')).toThrow();
    expect(() => decodeWorkpaper({}, 'workpaper')).toThrow();
  });
});
