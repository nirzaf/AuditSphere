import { describe, expect, it } from 'vitest';
import { decode } from '../../core/decode';
import { setupPreview, setupReceiptLookup, storedSetupFields } from './tenant-setup-contracts';

const fields = { tenantDisplayName: 'Synthetic label', mailState: 'CONFIGURED', recordsState: 'NOT_CONFIGURED' };
const preview = { requestId: '11111111-1111-4111-8111-111111111111', draftId: '22222222-2222-4222-8222-222222222222',
  expectedRevision: '9007199254740993', requestHash: 'a'.repeat(64), reviewBasis: 'b'.repeat(64), before: fields, fields };
describe('tenant setup contracts', () => {
  it('preserves exact revisions beyond JavaScript integer precision', () => {
    expect(decode(setupPreview, preview).expectedRevision).toBe('9007199254740993');
  });
  it('rejects malformed and overflowing revisions and unknown states', () => {
    for (const expectedRevision of ['0', '01', '9223372036854775807', 2])
      expect(() => decode(setupPreview, { ...preview, expectedRevision })).toThrow();
    expect(storedSetupFields({ ...fields, mailState: 'VERIFIED' })).toBeNull();
    expect(storedSetupFields({ ...fields, tenantDisplayName: 'x'.repeat(301) })).toBeNull();
    expect(storedSetupFields({ ...fields, tenantDisplayName: 'unsafe\u001f' })).toBeNull();
  });
  it('rejects contradictory lookup results', () => {
    expect(decode(setupReceiptLookup, { found: false, receipt: null })).toEqual({ found: false, receipt: null });
    expect(() => decode(setupReceiptLookup, { found: true, receipt: null })).toThrow();
  });
});
