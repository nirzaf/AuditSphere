import { describe, expect, it } from 'vitest';
import { decodeProgramLibrary, decodeSectionPage } from './library';

const id1 = '11111111-1111-4111-8111-111111111111';
const id2 = '22222222-2222-4222-8222-222222222222';
const hash64 = 'c'.repeat(64);

describe('Audit Program Library Contracts', () => {
  it('decodes a valid program library payload', () => {
    const raw = {
      versions: [
        {
          programVersionId: id1,
          programCode: 'CORE-AUDIT',
          version: '2026.1',
          status: 'PUBLISHED',
          sourceHash: hash64,
          procedureCount: 150,
          sectionCount: 12,
          approvedAt: '2026-09-15T09:00:00Z',
        },
      ],
      selectedVersion: {
        programVersionId: id1,
        programCode: 'CORE-AUDIT',
        version: '2026.1',
        status: 'PUBLISHED',
        sourceHash: hash64,
        procedureCount: 150,
        sectionCount: 12,
        approvedAt: '2026-09-15T09:00:00Z',
      },
      sections: [
        {
          sectionNumber: 1,
          sectionTitle: 'Planning and Risk Assessment',
          procedureCount: 15,
          assertions: ['Completeness', 'Accuracy'],
        },
      ],
    };

    const decoded = decodeProgramLibrary(raw, 'library');
    expect(decoded.versions.length).toBe(1);
    expect(decoded.versions[0].programCode).toBe('CORE-AUDIT');
    expect(decoded.selectedVersion?.version).toBe('2026.1');
    expect(decoded.sections[0].sectionNumber).toBe(1);
    expect(decoded.sections[0].assertions).toContain('Completeness');
  });

  it('decodes a valid section procedures page payload', () => {
    const raw = {
      sectionNumber: 1,
      sectionTitle: 'Planning and Risk Assessment',
      totalCount: 1,
      page: 1,
      pageSize: 50,
      items: [
        {
          procedureId: id2,
          sourceProcedureId: 'AWP-01',
          sectionNumber: 1,
          sectionTitle: 'Planning and Risk Assessment',
          ordinal: 1,
          sourceWording: 'Inquire with management regarding business risks and operational changes.',
          applicabilityCondition: 'Always applicable',
          expectedEvidence: 'Meeting minutes and notes of inquiry',
        },
      ],
    };

    const decoded = decodeSectionPage(raw, 'sectionPage');
    expect(decoded.sectionNumber).toBe(1);
    expect(decoded.items.length).toBe(1);
    expect(decoded.items[0].sourceProcedureId).toBe('AWP-01');
    expect(decoded.items[0].sourceWording).toContain('Inquire with management');
  });

  it('rejects invalid library payloads', () => {
    expect(() => decodeProgramLibrary(null, 'library')).toThrow();
    expect(() => decodeProgramLibrary({}, 'library')).toThrow();
    expect(() => decodeSectionPage(null, 'sectionPage')).toThrow();
  });
});
