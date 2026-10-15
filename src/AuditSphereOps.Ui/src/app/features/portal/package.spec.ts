import { describe, expect, it } from 'vitest';
import { clientDecisionIntent, clientDecisionRequest, clientPackage, ClientDecisionForm, ClientPackageView } from './package';

const hash = 'a'.repeat(64);
const digest = 'b'.repeat(64);

function view(overrides: Partial<Record<string, unknown>> = {}): ClientPackageView {
  return clientPackage({
    packageId: '11111111-1111-4111-8111-111111111111', framework: 'IFRS', periodStart: '2025-01-01', periodEnd: '2025-12-31',
    currency: 'QAR', templateVersion: 'ifrs-v1', packageHash: hash, hasCashFlow: true, hasDisclosures: true,
    managementDecision: 'PENDING', managementDecidedAt: null, statementTotals: [{ statementSection: 'ASSETS', amount: '100.000000' }],
    composition: {
      presentationRevisionId: '22222222-2222-4222-8222-222222222222', compositionId: '33333333-3333-4333-8333-333333333333',
      compositionManifestDigest: digest, pdfSha256Hex: 'c'.repeat(64), wordSha256Hex: 'd'.repeat(64), excelSha256Hex: 'e'.repeat(64),
      selectedAt: '2026-10-15T00:00:00Z',
    },
    latestDecisionId: null,
    ...overrides,
  }, '$');
}

const form: ClientDecisionForm = { decision: 'APPROVED', evidenceReference: 'Board minute 4', comment: '', reviewed: true };

describe('client management decision on the exact composition (F-02)', () => {
  it('names the exact composition the manager reviewed, with its original intent and the decision chain it saw', () => {
    const body = clientDecisionRequest(view(), form, '44444444-4444-4444-8444-444444444444');
    expect(body).toEqual({
      decision: 'APPROVED', evidenceReference: 'Board minute 4', comment: '', reviewed: true, expectedHash: hash,
      presentationRevisionId: '22222222-2222-4222-8222-222222222222',
      presentationCompositionId: '33333333-3333-4333-8333-333333333333', compositionManifestDigest: digest,
      requestId: '44444444-4444-4444-8444-444444444444', expectedLatestDecisionId: null,
    });
  });

  it('sends no composition identity when only the package awaits acknowledgement', () => {
    const body = clientDecisionRequest(view({ composition: null }), form, '44444444-4444-4444-8444-444444444444');
    expect(body['presentationCompositionId']).toBeNull();
    expect(body['presentationRevisionId']).toBeNull();
    expect(body['compositionManifestDigest']).toBeNull();
  });

  it('keeps the request identity for a retry of the same decision and renews it when the report or decision changes', () => {
    const first = clientDecisionIntent(null, view(), form, 'first');
    expect(clientDecisionIntent(first, view(), form, 'second')).toBe(first);
    expect(clientDecisionIntent(first, view(), { ...form, decision: 'CHANGES_REQUIRED' }, 'second').requestId).toBe('second');
    const changed = view({ composition: { ...view().composition!, compositionManifestDigest: 'f'.repeat(64) } });
    expect(clientDecisionIntent(first, changed, form, 'third').requestId).toBe('third');
    expect(clientDecisionIntent(first, view({ latestDecisionId: '55555555-5555-4555-8555-555555555555' }), form, 'fourth').requestId).toBe('fourth');
  });

  it('refuses a composition without a lowercase SHA-256 manifest digest', () => {
    expect(() => view({ composition: { ...view().composition!, compositionManifestDigest: 'not-a-digest' } })).toThrow();
  });
});
