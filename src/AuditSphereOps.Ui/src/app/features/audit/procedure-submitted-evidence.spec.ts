import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';
import { ProcedureSubmittedEvidence, summarizeSubmittedEvidence } from './procedure-submitted-evidence';

const id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const uploadId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
const requestId = 'cccccccc-cccc-cccc-cccc-cccccccccccc';
const physicalLinkId = 'dddddddd-dddd-dddd-dddd-dddddddddddd';
const itemId = 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee';
const hash = 'a'.repeat(64);
const snapshot = () => ({ version: 1, selections: [
  { kind: 'STORED_LINK', linkId: id, uploadIntentId: uploadId, pbcRequestId: requestId, fileName: 'retained-bank.csv', contentSha256: hash },
  { kind: 'PHYSICAL_LINK', physicalLinkId, physicalItemId: itemId, fileIndex: 'FILE-1', boxReference: 'BOX-2',
    description: 'Signed statement', locationAtSubmission: 'Archive shelf A' },
  { kind: 'MANUAL_REFERENCE', manualReference: 'BANK-REC-1', observation: 'Compared the retained closing balance.' },
], legacyReferences: [{ kind: 'LEGACY_REFERENCE', reference: 'Old free-text index' }] });

describe('Submitted procedure evidence', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('renders only the retained evidence identities and distinguishes physical/manual/legacy observations', async () => {
    TestBed.configureTestingModule({ imports: [ProcedureSubmittedEvidence] });
    const fixture = TestBed.createComponent(ProcedureSubmittedEvidence);
    fixture.componentRef.setInput('snapshot', JSON.stringify(snapshot()));
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const region = root.querySelector('[aria-label="Evidence retained at submission"]')!;
    expect(region.querySelectorAll('li').length).toBe(4);
    expect(region.textContent).toContain('retained-bank.csv');
    expect(region.textContent).toContain(hash);
    expect(Array.from(region.querySelectorAll('li')[0].querySelectorAll('dd')).map(item => item.textContent))
      .toEqual([hash, uploadId, requestId, id]);
    expect(Array.from(region.querySelectorAll('li')[1].querySelectorAll('dd')).map(item => item.textContent))
      .toEqual(['BOX-2', 'Signed statement', 'Archive shelf A', itemId, physicalLinkId]);
    expect(region.textContent).toContain('Archive shelf A');
    expect(region.textContent).toContain('Compared the retained closing balance.');
    expect(region.textContent).toContain('Legacy reference — not verified source evidence');
    expect(region.textContent).toContain('does not establish current source freshness or professional sufficiency');
    expect(region.querySelector('a,button,input')).toBeNull();

    fixture.componentRef.setInput('snapshot', '{"version":2}');
    await fixture.whenStable();
    expect(root.querySelectorAll('li').length).toBe(0);
    expect(root.textContent).not.toContain('retained-bank.csv');
    expect(root.querySelector('[role="status"]')?.textContent).toContain('cannot be summarized');
    fixture.destroy();
  });

  it('refuses malformed, oversized or unknown snapshots as a whole instead of displaying a partial selection', () => {
    const valid = snapshot();
    expect(summarizeSubmittedEvidence(JSON.stringify(valid))).toHaveLength(4);
    for (const raw of [
      '{', JSON.stringify({ ...valid, version: 2 }),
      JSON.stringify({ ...valid, selections: [...valid.selections, { kind: 'UNKNOWN' }] }),
      JSON.stringify({ ...valid, selections: [{ ...valid.selections[0], contentSha256: 'unverified' }] }),
      JSON.stringify({ ...valid, selections: Array.from({ length: 51 }, () => valid.selections[0]) }),
      JSON.stringify({ ...valid, legacyReferences: [{ kind: 'STORED_LINK', reference: 'not a verified file' }] }),
      JSON.stringify({ ...valid, padding: 'é'.repeat(25_000) }),
    ]) expect(summarizeSubmittedEvidence(raw)).toBeNull();
  });
});
