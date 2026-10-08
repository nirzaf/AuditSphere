import { describe, expect, it } from 'vitest';
import { decodeDocuments } from './documents';
const id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const document = {
  id,
  quotationId: id,
  kind: 'QUOTATION',
  templateVersion: 'v1',
  profileVersion: '1',
  fileName: 'quotation.docx',
  contentType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  sha256: 'a'.repeat(64),
  createdAt: '2026-10-02T00:00:00Z',
};
const workspace = {
  proposalId: id,
  quotationId: id,
  profileVersion: '2',
  firmName: 'Firm',
  history: 'Reviewed history',
  credentials: 'Reviewed credentials',
  methodology: 'Reviewed methodology',
  commonBlockers: [],
  letterBlockers: ['Record client acceptance.'],
  tenderBlockers: [],
  documents: [document],
};
describe('Commercial document contract', () => {
  it('keeps historical profile and immutable artifact identity', () => {
    const result = decodeDocuments(workspace);
    expect(result.profileVersion).toBe('2');
    expect(result.documents[0].profileVersion).toBe('1');
    expect(result.letterBlockers).toHaveLength(1);
  });
  it('rejects forged IDs, incomplete hashes and numeric revisions', () => {
    for (const change of [{ id: 'guessed' }, { sha256: 'a'.repeat(12) }, { profileVersion: 1 }])
      expect(() =>
        decodeDocuments({ ...workspace, documents: [{ ...document, ...change }] }),
      ).toThrow();
    expect(() => decodeDocuments({ ...workspace, profileVersion: 2 })).toThrow();
  });
  it('bounds artifact history and blocker messages', () => {
    expect(() => decodeDocuments({ ...workspace, documents: Array(101).fill(document) })).toThrow();
    expect(() =>
      decodeDocuments({ ...workspace, commonBlockers: Array(21).fill('Blocked') }),
    ).toThrow();
  });
});
