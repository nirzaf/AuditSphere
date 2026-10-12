import { describe, expect, it } from 'vitest';
import { COMMUNICATION_STATUS_LABELS, decodeEngagementCommunications } from './engagement-communications';

const engagementId = '0d3b6a53-7d0a-4b47-9d53-3a2c5c1f4b11';
const entry = (overrides: Record<string, unknown> = {}) => ({
  id: '5a1c1f6e-2f0b-4a0e-8a53-0a1b2c3d4e5f',
  source: 'PBC',
  kind: 'PBC request email',
  subject: 'Bank confirmations',
  recipient: 'liaison@example.test',
  status: 'PROVIDER_ACCEPTED',
  recordedAtUtc: '2026-10-12T07:00:00Z',
  deliveredAtUtc: null,
  sourceLink: `/app/engagements/${engagementId}/pbc`,
  ...overrides,
});

describe('engagement communications contract', () => {
  it('decodes a recorded timeline', () => {
    const timeline = decodeEngagementCommunications({ engagementId, totalCount: 1, entries: [entry()] });
    expect(timeline.entries[0].status).toBe('PROVIDER_ACCEPTED');
  });

  it('refuses an unknown status and a link that leaves the application', () => {
    expect(() => decodeEngagementCommunications({ engagementId, totalCount: 1, entries: [entry({ status: 'READ' })] })).toThrow();
    expect(() =>
      decodeEngagementCommunications({ engagementId, totalCount: 1, entries: [entry({ sourceLink: 'https://example.test/x' })] }),
    ).toThrow();
    expect(() =>
      decodeEngagementCommunications({ engagementId, totalCount: 1, entries: [entry({ sourceLink: '/app/../admin' })] }),
    ).toThrow();
  });

  it('never words provider acceptance or dispatch as delivery', () => {
    expect(COMMUNICATION_STATUS_LABELS['PROVIDER_ACCEPTED']).not.toMatch(/deliver/i);
    expect(COMMUNICATION_STATUS_LABELS['DISPATCHED']).not.toMatch(/deliver/i);
    expect(COMMUNICATION_STATUS_LABELS['RECORDED']).not.toMatch(/deliver/i);
  });
});
