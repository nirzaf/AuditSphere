import { describe, expect, it } from 'vitest';
import { decode } from '../../core/decode';
import { portalRequest, portalWorkspace, revision } from './contracts';
import { clientPackage } from './package';
describe('client portal contracts', () => {
  it('preserves exact byte/revision strings and refuses numeric or signed values', () => {
    expect(decode(revision, '9223372036854775807')).toBe('9223372036854775807');
    for (const bad of [42, '-1', '1.1', '1e6']) expect(() => decode(revision, bad)).toThrow();
  });
  it('does not accept partial staff DTOs as portal responses', () => {
    expect(() => decode(portalRequest, { id: crypto.randomUUID(), area: 'Private workpaper' })).toThrow();
    expect(() => decode(portalWorkspace, { requests: [], packages: [] })).toThrow();
  });
  it('refuses floating point package totals', () => {
    const sample = { packageId: crypto.randomUUID(), framework: 'IFRS', periodStart: '2026-01-01', periodEnd: '2026-12-31', currency: 'QAR', templateVersion: '1', packageHash: 'a'.repeat(64),
      hasCashFlow: true, hasDisclosures: true, managementDecision: 'PENDING', managementDecidedAt: null, statementTotals: [{ statementSection: 'Assets', amount: '9007199254740993.25' }] };
    expect(decode(clientPackage, sample).statementTotals[0].amount).toBe('9007199254740993.25');
    expect(() => decode(clientPackage, { ...sample, statementTotals: [{ statementSection: 'Assets', amount: 1.25 }] })).toThrow();
  });
  it('maps request states to user-friendly status labels according to AS-COMP-09', async () => {
    const { clientRequestStatusLabel } = await import('./contracts');
    expect(clientRequestStatusLabel('SENT')).toBe('Pending Upload');
    expect(clientRequestStatusLabel('ACKNOWLEDGED')).toBe('Pending Upload');
    expect(clientRequestStatusLabel('PARTIALLY_RECEIVED')).toBe('Pending Upload');
    expect(clientRequestStatusLabel('RECEIVED')).toBe('Under Review');
    expect(clientRequestStatusLabel('UNDER_REVIEW')).toBe('Under Review');
    expect(clientRequestStatusLabel('RESUBMITTED')).toBe('Under Review');
    expect(clientRequestStatusLabel('ACCEPTED')).toBe('Approved');
    expect(clientRequestStatusLabel('CLOSED')).toBe('Approved');
    expect(clientRequestStatusLabel('CLARIFICATION_REQUIRED')).toBe('Rejected / Re-upload Required');
  });
  it('decodes portalRequest with optional clarificationReason', () => {
    const req = {
      id: crypto.randomUUID(), area: 'Cash', objective: 'Bank confirmation', instructions: 'Upload statements',
      requestedFormat: 'PDF', dueDate: '2027-01-31', state: 'CLARIFICATION_REQUIRED', revision: '2',
      firstSignIn: { completed: true, identityPath: 'EXTERNAL_IDENTITY', canComplete: false, message: 'Done' },
      canWrite: true, canManageDelegations: false, canDelegate: false, delegations: [], candidates: [], uploads: [], conversation: [],
      clarificationReason: 'Pages 3 and 4 are missing from the statement scan.'
    };
    const decoded = decode(portalRequest, req);
    expect(decoded.clarificationReason).toBe('Pages 3 and 4 are missing from the statement scan.');
    expect(decoded.canManageDelegations).toBe(false);
    expect(decoded.state).toBe('CLARIFICATION_REQUIRED');
    expect(decoded.acceptance).toBeNull();
  });
  it('decodes exact acceptance receipt strings and rejects malformed provided receipts', () => {
    const request = {
      id: '50000000-0000-4000-8000-000000000001', area: 'Cash', objective: 'Bank confirmation', instructions: 'Upload statements',
      requestedFormat: 'PDF', dueDate: '2027-01-31', state: 'ACCEPTED', revision: '3',
      firstSignIn: { completed: true, identityPath: 'EXTERNAL_IDENTITY', canComplete: false, message: 'Done' },
      canWrite: false, canManageDelegations: false, canDelegate: false, delegations: [], candidates: [], uploads: [], conversation: [],
      clarificationReason: null, hasMoreUploads: false, hasMoreConversation: false,
    };
    const acceptance = {
      id: '50000000-0000-4000-8000-000000000011', uploadIntentId: '50000000-0000-4000-8000-000000000012',
      acceptedAt: '2026-10-21T09:34:56+00:00', acceptedRequestRevision: '9223372036854775807',
      sourceRequestRevision: '9007199254740993', uploadIntentRevision: '12', fileName: 'support.pdf',
      byteCount: '9007199254740993', sha256: 'b'.repeat(64),
    };
    const decoded = decode(portalRequest, { ...request, acceptance });
    expect(decoded.acceptance).toEqual(acceptance);
    expect(decode(portalRequest, request).acceptance).toBeNull();

    const malformed: Array<[string, unknown]> = [
      ['id', 'not-a-guid'], ['uploadIntentId', null], ['acceptedAt', '2026-10-21T12:34:56+03:00'],
      ['acceptedRequestRevision', '0'], ['sourceRequestRevision', '01'],
      ['uploadIntentRevision', '9223372036854775808'], ['fileName', 42], ['byteCount', '0'],
      ['byteCount', 9007199254740992], ['sha256', 'not-a-hash'],
    ];
    for (const [field, value] of malformed) {
      expect(() => decode(portalRequest, { ...request, acceptance: { ...acceptance, [field]: value } })).toThrow();
    }
  });
});
