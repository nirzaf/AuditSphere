import { Component, computed, input } from '@angular/core';
import { arr, decode, guid, obj, sha256, str } from '../../core/decode';

interface EvidenceEntry { kind: string; title: string; details: { label: string; value: string }[] }
const stored = obj({ linkId: guid, uploadIntentId: guid, pbcRequestId: guid, fileName: str(500), contentSha256: sha256 });
const physical = obj({ physicalLinkId: guid, physicalItemId: guid, fileIndex: str(500), boxReference: str(500),
  description: str(4000), locationAtSubmission: str(2000) });
const manual = obj({ manualReference: str(500), observation: str(4000) });
const legacy = obj({ kind: str(50), reference: str(2000) });

/** Projection of the retained version-one snapshot only; never reconstructs it from current links. */
export function summarizeSubmittedEvidence(raw: string): EvidenceEntry[] | null {
  if (raw.length > 50_000 || new TextEncoder().encode(raw).length > 50_000) return null;
  try {
    const value: unknown = JSON.parse(raw);
    const snapshot = decode(obj({ version: (v: unknown) => { if (v !== 1) throw new Error('Unsupported evidence version'); return 1; },
      selections: arr((v: unknown) => {
        if (!v || typeof v !== 'object' || Array.isArray(v)) throw new Error('Invalid evidence');
        const kind = (v as Record<string, unknown>)['kind'];
        if (kind === 'STORED_LINK') {
          const item = decode(stored, v);
          return { kind: 'Received file at submission', title: item.fileName, details: [
            { label: 'SHA-256', value: item.contentSha256 }, { label: 'Upload identity', value: item.uploadIntentId },
            { label: 'Request identity', value: item.pbcRequestId }, { label: 'Link identity', value: item.linkId }] };
        }
        if (kind === 'PHYSICAL_LINK') {
          const item = decode(physical, v);
          return { kind: 'Physical evidence at submission', title: item.fileIndex, details: [
            { label: 'Box reference', value: item.boxReference }, { label: 'Description', value: item.description },
            { label: 'Location at submission', value: item.locationAtSubmission },
            { label: 'Physical item identity', value: item.physicalItemId }, { label: 'Link identity', value: item.physicalLinkId }] };
        }
        if (kind === 'MANUAL_REFERENCE') {
          const item = decode(manual, v);
          return { kind: 'Manual observation at submission', title: item.manualReference,
            details: [{ label: 'What was observed', value: item.observation }] };
        }
        throw new Error('Unsupported evidence kind');
      }, 50), legacyReferences: arr(legacy, 500) }), value);
    if (snapshot.legacyReferences.some(item => item.kind !== 'LEGACY_REFERENCE')) return null;
    return [...snapshot.selections, ...snapshot.legacyReferences.map(item => ({ kind: 'Legacy reference — not verified source evidence',
      title: item.reference, details: [] }))];
  } catch { return null; }
}

@Component({
  selector: 'audit-procedure-submitted-evidence',
  template: `
    <section aria-label="Evidence retained at submission">
      <h4>Evidence retained at submission</h4>
      <p>This is the retained submission snapshot. It does not establish current source freshness or professional sufficiency.</p>
      @if (entries(); as items) {
        <ol>@for (item of items; track $index) {
          <li><p><strong>{{ item.kind }}</strong> · {{ item.title }}</p>
            <dl>@for (detail of item.details; track detail.label) { <dt>{{ detail.label }}</dt><dd>{{ detail.value }}</dd> }</dl>
          </li>
        } @empty { <li>No evidence selections were recorded in this snapshot.</li> }</ol>
      } @else { <p role="status">This retained evidence format cannot be summarized. Inspect the retained snapshot below.</p> }
    </section>
  `,
  styles: `:host { display:block; min-width:0; } section, li, dt, dd { overflow-wrap:anywhere; }
    ol { padding-inline-start:1.5rem; } li { margin-block:1rem; } dl { margin-block:.5rem; }
    dt { font-weight:600; } dd { margin-inline-start:0; white-space:pre-wrap; }`,
})
export class ProcedureSubmittedEvidence {
  readonly snapshot = input.required<string>();
  readonly entries = computed(() => summarizeSubmittedEvidence(this.snapshot()));
}
