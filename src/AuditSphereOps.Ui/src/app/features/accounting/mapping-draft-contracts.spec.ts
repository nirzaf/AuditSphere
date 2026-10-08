import {
  decodeMappingEditor,
  emptySplit,
  emptyMappingDraft,
  mappingEditableDraft,
  parseMappingPaste,
  splitError,
} from './mapping-draft-contracts';

describe('Exact bounded mapping editor fields', () => {
  const split = {
    destinationCode: 'CASH',
    fraction: '1',
    rationale: 'Synthetic reviewed allocation',
    auditArea: '',
  };
  it('sums fractions with decimal integers and never rounds or coerces edit text', () => {
    expect(splitError([split], ['CASH'])).toBe('');
    expect(
      splitError(
        [
          { ...split, fraction: '0.123456' },
          { ...split, destinationCode: 'OTHER', fraction: '0.876544' },
        ],
        ['CASH', 'OTHER'],
      ),
    ).toBe('');
    for (const fraction of ['1e0', '1,0', '0.1234567', '-1', '0', '1.000001', '', ' 1'])
      expect(splitError([{ ...split, fraction }], ['CASH'])).not.toBe('');
    expect(splitError([{ ...split, fraction: '0.999999' }], ['CASH'])).toContain('exactly 1');
    expect(splitError([split, split], ['CASH'])).toContain('distinct');
    expect(splitError([split], [])).toContain('approved');
  });
  it('stores only bounded editable fields, preserves partial text and rejects credentials, assent and duplicate identities', () => {
    const draft = {
      ...emptyMappingDraft(),
      changes: [{ accountCode: '1000', splits: [split] }],
      active: { accountCode: '2000', splits: [emptySplit()] },
    };
    expect(mappingEditableDraft(draft)).toEqual(draft);
    for (const key of ['reviewed', 'revision', 'accessToken', 'requestId', 'amount', 'files'])
      expect(mappingEditableDraft({ ...draft, [key]: 'excluded' })).toBeNull();
    expect(
      mappingEditableDraft({ ...draft, changes: [draft.changes[0], draft.changes[0]] }),
    ).toBeNull();
    expect(
      mappingEditableDraft({ ...draft, changes: Array(201).fill(draft.changes[0]) }),
    ).toBeNull();
    expect(
      mappingEditableDraft({
        ...draft,
        active: { accountCode: '1000', splits: Array(21).fill(split) },
      }),
    ).toBeNull();
  });
  it('parses paste into an explicit grouped preview and bounds every identity and field', () => {
    const changes = parseMappingPaste(
      '1000\tCASH\t0.5\tReviewed first split\n1000\tOTHER\t0.5\tReviewed second split\tCash',
    );
    expect(changes.length).toBe(1);
    expect(changes[0].splits.map((s) => s.fraction)).toEqual(['0.5', '0.5']);
    expect(splitError(changes[0].splits, ['CASH', 'OTHER'])).toBe('');
    expect(() => parseMappingPaste('1000,CASH,1,rationale')).toThrow();
    expect(() => parseMappingPaste('x'.repeat(100001))).toThrow();
    expect(() =>
      parseMappingPaste(
        Array.from({ length: 201 }, (_, i) => `${i}\tCASH\t1\tSynthetic rationale`).join('\n'),
      ),
    ).toThrow();
    expect(() => decodeMappingEditor({ sourceAccountCount: 20001 })).toThrow();
  });
});
