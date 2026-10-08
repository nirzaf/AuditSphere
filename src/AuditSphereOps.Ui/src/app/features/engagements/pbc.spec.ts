import { describe, expect, it } from 'vitest';
import { decodePbcInbox } from './pbc';

const id1 = '11111111-1111-4111-8111-111111111111';
const id2 = '22222222-2222-4222-8222-222222222222';
const id3 = '33333333-3333-4333-8333-333333333333';
const hash64 = 'a'.repeat(64);

describe('PBC Request Inbox Contracts', () => {
  it('decodes a valid PBC inbox payload', () => {
    const raw = {
      engagementId: id1,
      serviceRoute: 'AUDIT',
      periodStart: '2026-01-01',
      periodEnd: '2026-12-31',
      requests: [
        {
          id: id2,
          area: 'Cash and Bank',
          objective: 'Provide bank confirmation authorities and year-end statements.',
          dueDate: '2026-10-15',
          state: 'OPEN',
          revision: 1,
          intents: [
            {
              id: id3,
              fileName: 'qnb_bank_statement.pdf',
              receivedByteCount: 524288,
              declaredByteCount: 524288,
              state: 'RECEIVED',
              transferState: 'COMPLETED',
              declaredSha256Hex: hash64,
            },
          ],
          timeline: [
            {
              at: '2026-10-01T10:00:00Z',
              label: 'REQUEST_CREATED',
              body: 'PBC request sent to client finance team.',
            },
          ],
          canRequestMore: true,
        },
      ],
      clientOwners: [
        {
          id: id3,
          name: 'Jane Client',
          email: 'jane@client.test',
        },
      ],
      reviewers: [
        {
          id: id1,
          name: 'Audit Senior',
          email: 'senior@firm.test',
        },
      ],
    };

    const decoded = decodePbcInbox(raw, 'pbcInbox');
    expect(decoded.engagementId).toBe(id1);
    expect(decoded.requests.length).toBe(1);
    expect(decoded.requests[0].area).toBe('Cash and Bank');
    expect(decoded.requests[0].intents.length).toBe(1);
    expect(decoded.requests[0].intents[0].state).toBe('RECEIVED');
    expect(decoded.clientOwners[0].name).toBe('Jane Client');
    expect(decoded.reviewers[0].name).toBe('Audit Senior');
  });

  it('rejects invalid PBC payloads', () => {
    expect(() => decodePbcInbox(null, 'pbcInbox')).toThrow();
    expect(() => decodePbcInbox({}, 'pbcInbox')).toThrow();
  });
});
