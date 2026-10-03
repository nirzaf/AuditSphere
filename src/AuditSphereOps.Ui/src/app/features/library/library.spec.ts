import { describe, expect, it } from 'vitest';
import { decodeCatalogue, decodeEntry, decodeHits } from './library';

const id1 = '11111111-1111-4111-8111-111111111111';
const id2 = '22222222-2222-4222-8222-222222222222';
const hash64 = 'a'.repeat(64);

describe('Technical Library Contracts', () => {
  it('decodes a valid technical library catalogue', () => {
    const raw = [
      {
        id: id1,
        code: 'IFRS-15',
        title: 'Revenue from Contracts with Customers',
        category: 'IFRS',
        audience: 'FIRM_WIDE',
        publishedVersion: 2,
        hasDraft: false,
      },
    ];

    const decoded = decodeCatalogue(raw, 'catalogue');
    expect(decoded.length).toBe(1);
    expect(decoded[0].code).toBe('IFRS-15');
    expect(decoded[0].publishedVersion).toBe(2);
  });

  it('decodes a valid technical library entry', () => {
    const raw = {
      id: id1,
      code: 'IFRS-15',
      title: 'Revenue from Contracts with Customers',
      category: 'IFRS',
      version: {
        id: id2,
        version: 2,
        status: 'PUBLISHED',
        effectiveFrom: '2026-01-01',
        sourceReference: 'IASB Official Standard 2026',
        contentSha256: hash64,
        body: 'Full standard content...',
      },
      history: [
        {
          id: id2,
          version: 2,
          status: 'PUBLISHED',
          effectiveFrom: '2026-01-01',
          sourceReference: 'IASB Official Standard 2026',
          contentSha256: hash64,
          body: 'Full standard content...',
        },
      ],
    };

    const decoded = decodeEntry(raw, 'entry');
    expect(decoded.id).toBe(id1);
    expect(decoded.version.version).toBe(2);
    expect(decoded.history.length).toBe(1);
  });

  it('decodes search hits', () => {
    const raw = [
      {
        documentId: id1,
        code: 'IFRS-15',
        title: 'Revenue from Contracts with Customers',
        category: 'IFRS',
        version: 2,
        effectiveFrom: '2026-01-01',
        snippet: '...performance obligations satisfied over time...',
      },
    ];

    const decoded = decodeHits(raw, 'hits');
    expect(decoded.length).toBe(1);
    expect(decoded[0].code).toBe('IFRS-15');
  });

  it('rejects invalid payloads', () => {
    expect(() => decodeCatalogue(null, 'catalogue')).toThrow();
    expect(() => decodeEntry(null, 'entry')).toThrow();
  });
});
