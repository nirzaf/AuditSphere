import { describe, expect, it } from 'vitest';
import { decodeOperations } from './operations';

const id1 = '11111111-1111-4111-8111-111111111111';
const id2 = '22222222-2222-4222-8222-222222222222';

describe('Durable Operations Console Contracts', () => {
  it('decodes a valid operations console payload', () => {
    const raw = {
      operatingMode: 'LOCAL_ONLY',
      retryableStates: ['RETRY_WAIT', 'FAILED', 'DEAD_LETTER'],
      operations: [
        {
          id: id1,
          kind: 'GL_COMPLETENESS_CALCULATION',
          status: 'COMPLETED',
          attemptCount: 1,
          nextAttemptAt: null,
          errorCode: null,
          cancellationDisposition: null,
        },
        {
          id: id2,
          kind: 'PBC_DOCUMENT_TRANSFER',
          status: 'RETRY_WAIT',
          attemptCount: 2,
          nextAttemptAt: '2026-10-03T05:00:00Z',
          errorCode: 'NETWORK_TIMEOUT',
          cancellationDisposition: null,
        },
      ],
    };

    const decoded = decodeOperations(raw, 'operations');
    expect(decoded.operatingMode).toBe('LOCAL_ONLY');
    expect(decoded.retryableStates).toContain('RETRY_WAIT');
    expect(decoded.operations.length).toBe(2);
    expect(decoded.operations[0].status).toBe('COMPLETED');
    expect(decoded.operations[1].errorCode).toBe('NETWORK_TIMEOUT');
  });

  it('rejects invalid operations payloads', () => {
    expect(() => decodeOperations(null, 'operations')).toThrow();
    expect(() => decodeOperations({}, 'operations')).toThrow();
  });
});
