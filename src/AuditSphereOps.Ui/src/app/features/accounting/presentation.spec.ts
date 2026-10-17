import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SessionService } from '../../core/session';
import { StatementPresentation, decodePresentation, decodePresentationPin, longDate, presentationAmount } from './presentation';

const id = '11111111-1111-4111-8111-111111111111';
const hash64 = 'a'.repeat(64);
const row = (overrides: Record<string, unknown> = {}) => ({
  kind: 'LINE', code: 'CASH', label: 'Cash', depth: 1, noteReference: '7', current: '1090993', prior: '58372', ...overrides,
});
const payload = {
  status: 'UNAPPROVED_DRAFT',
  canPin: true,
  displayDecimals: 0,
  header: { entity: 'SYNTHETIC CLIENT', framework: 'IFRS', currency: 'QAR', currentPeriodEnd: '2025-12-31', priorPeriodEnd: '2024-12-31', reference: 'FSP-SYN' },
  manifest: {
    packageId: id, calculationHash: hash64, layoutVersionId: id, layoutHash: hash64,
    comparativePackageId: null, comparativeCalculationHash: null, builderVersion: 'auditsphere.statement-presentation.v6', displayPolicy: 'whole units',
    noteSourceManifestHash: hash64,
  },
  statements: [
    { code: 'FINANCIAL_POSITION', title: 'Statement of financial position', rows: [row(), row({ kind: 'HEADING', code: 'H', label: 'Assets', noteReference: null, current: null, prior: null })] },
    { code: 'CHANGES_IN_EQUITY', title: 'Statement of changes in equity', rows: [row({ code: 'EQ1', label: 'Closing balance', noteReference: null, current: null, prior: null })] },
    { code: 'CASH_FLOWS', title: 'Statement of cash flows', rows: [row({ code: 'CF1', label: 'Net change in cash', noteReference: null, current: null, prior: null })] },
  ],
  notes: [{
    number: 1, code: '7',
    current: { state: 'PREPARER_SUPPLIED_UNREVIEWED', text: 'Cash note content supplied by preparer.', rationale: null },
    comparative: { state: 'NOT_SUPPLIED', text: null, rationale: null },
    amounts: [{ faceDestinationCode: 'CASH', label: 'Cash and cash equivalents', statementSection: 'FINANCIAL_POSITION', currency: 'QAR', current: '1090993', comparative: '58372', currentEvidenceReference: 'synthetic-source', comparativeEvidenceReference: null }],
  }],
  checks: [
    { code: 'TB_BALANCED', passed: true, detail: 'The current balances net to zero.' },
    { code: 'CASH_FLOW_SOURCE_LINEAGE', passed: false, detail: 'Movement references are not source-resolved.' },
  ],
  warnings: [{ code: 'CASH_FLOW_LINEAGE_PENDING', detail: 'Cash-flow evidence is not source-verified.' }],
};

const savedPin = (revisionId: string) => ({
  revisionId, packageId: id, packageCalculationHash: hash64, periodId: null, bookId: null, basis: null,
  framework: 'IFRS', taxonomyVersion: 'TAX-v1', templateVersion: 'statements.v1', calculationEngineVersion: 'package.v1',
  currency: 'QAR', periodStart: '2025-01-01', periodEnd: '2025-12-31', layoutVersionId: id, layoutHash: hash64,
  comparativePackageId: null, comparativeCalculationHash: null, comparativeBasis: null, comparativeEvidenceReference: null,
  noteSourceManifestHash: hash64, builderVersion: 'builder.v1', rendererVersion: 'renderer.v1', displayPolicy: 'whole units',
  status: 'UNAPPROVED_DRAFT', revisionHash: hash64, snapshotSha256Hex: hash64, createdAt: '2026-10-12T00:00:00Z',
  sourceIsCurrent: false, artifactFormats: ['pdf', 'docx', 'xlsx'], predecessorRevisionId: null, compositionId: null,
  compositionManifestDigest: null,
});

async function presentationScreen(pins: unknown[]) {
  TestBed.configureTestingModule({ imports: [StatementPresentation], providers: [provideHttpClient(), provideHttpClientTesting()] });
  TestBed.inject(SessionService).current.set({ userId: id, firmId: id, generation: '1', staff: true });
  const http = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(StatementPresentation);
  fixture.componentRef.setInput('packageId', id);
  fixture.detectChanges();
  http.expectOne(`/api/ui/accounting/packages/${id}/presentation`).flush(payload);
  http.expectOne(`/api/ui/accounting/packages/${id}/presentation/pins`).flush(pins);
  fixture.detectChanges();
  return { fixture, http };
}

function savedBody(entity: string, amount: string) {
  return {
    ...payload,
    header: { ...payload.header, entity },
    statements: [{ code: 'FINANCIAL_POSITION', title: 'Saved statement', rows: [row({ label: `Saved cash ${entity}`, current: amount })] }],
    notes: [], checks: [], warnings: [],
  };
}

describe('Statement presentation contract', () => {
  it('keeps exact amounts as strings and the draft status', () => {
    const decoded = decodePresentation(payload, 'presentation');
    expect(decoded.statements[0].rows[0].current).toBe('1090993');
    expect(decoded.notes[0].amounts[0].comparative).toBe('58372');
    expect(decoded.status).toBe('UNAPPROVED_DRAFT');
  });

  it('rejects numeric money, unknown statuses and missing manifests', () => {
    expect(() => decodePresentation({ ...payload, statements: [{ code: 'X', title: 'X', rows: [row({ current: 1090993 })] }] }, 'p')).toThrow();
    expect(() => decodePresentation({ ...payload, status: 'INVALID_STATUS' }, 'p')).toThrow();
    expect(decodePresentation({ ...payload, status: 'CANDIDATE' }, 'p').status).toBe('CANDIDATE');
    expect(() => decodePresentation({ ...payload, manifest: undefined }, 'p')).toThrow();
    expect(() => decodePresentation({ ...payload, notes: [{ ...payload.notes[0], amounts: [{ ...payload.notes[0].amounts[0], current: 1090993 }] }] }, 'p')).toThrow();
  });

  it('decodes composition identity and refuses a malformed composition digest', () => {
    const pin = {
      revisionId: id, packageId: id, packageCalculationHash: hash64, periodId: null, bookId: null, basis: null,
      framework: 'IFRS', taxonomyVersion: 'TAX-v1', templateVersion: 'statements.v1', calculationEngineVersion: 'package.v1',
      currency: 'QAR', periodStart: '2025-01-01', periodEnd: '2025-12-31', layoutVersionId: id, layoutHash: hash64,
      comparativePackageId: null, comparativeCalculationHash: null, comparativeBasis: null, comparativeEvidenceReference: null,
      noteSourceManifestHash: hash64, builderVersion: 'builder.v1', rendererVersion: 'renderer.v1', displayPolicy: 'whole units',
      status: 'CANDIDATE', revisionHash: hash64, snapshotSha256Hex: hash64, createdAt: '2026-10-15T00:00:00Z', sourceIsCurrent: true,
      artifactFormats: ['pdf', 'docx', 'xlsx'], predecessorRevisionId: null, compositionId: id, compositionManifestDigest: hash64,
    };
    expect(decodePresentationPin(pin, 'pin').compositionId).toBe(id);
    expect(() => decodePresentationPin({ ...pin, compositionManifestDigest: 'bad' }, 'pin')).toThrow();
  });

  it('formats amounts as the server text does: grouped, brackets for negatives, dash for zero', () => {
    expect(presentationAmount('3818172')).toBe('3,818,172');
    expect(presentationAmount('-508247')).toBe('(508,247)');
    expect(presentationAmount('0')).toBe('–');
    expect(presentationAmount(null)).toBe('Not available');
    expect(presentationAmount('1234.5', 2)).toBe('1,234.50');
    expect(longDate('2025-12-31')).toBe('31 December 2025');
  });
});

describe('Statement presentation screen', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
  });

  it('shows the draft, pins the displayed layout, and downloads bytes bound to the saved revision', async () => {
    TestBed.configureTestingModule({ imports: [StatementPresentation], providers: [provideHttpClient(), provideHttpClientTesting()] });
    TestBed.inject(SessionService).current.set({ userId: id, firmId: id, generation: '1', staff: true });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(StatementPresentation);
    fixture.componentRef.setInput('packageId', id);
    fixture.detectChanges();
    http.expectOne(`/api/ui/accounting/packages/${id}/presentation`).flush(payload);
    http.expectOne(`/api/ui/accounting/packages/${id}/presentation/pins`).flush([]);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Unapproved presentation draft');
    expect(host.textContent).toContain('1,090,993');
    expect(host.textContent).toContain('As at 31 December 2025');
    expect(host.textContent).toContain('Statement of changes in equity');
    expect(host.textContent).toContain('Statement of cash flows');
    expect(host.textContent).toContain('Year ended 31 December 2025');
    expect(host.textContent).toContain('Notes and disclosures');
    expect(host.textContent).toContain('Cash note content supplied by preparer.');
    expect(host.textContent).toContain('Current preparer reference, not independently verified: synthetic-source');
    expect(host.textContent).toContain('2 checks · 1 failed');
    expect(host.textContent).toContain('Readiness and rounding notes');
    expect(host.querySelector('tr.heading')).not.toBeNull();
    const revisionId = '22222222-2222-4222-8222-222222222222';
    const pin = {
      revisionId, packageId: id, packageCalculationHash: hash64, periodId: id, bookId: id, basis: 'ACCRUAL',
      framework: 'IFRS', taxonomyVersion: 'NAQAHA-2025', templateVersion: 'statements.v1',
      calculationEngineVersion: 'package-calculation.v1', currency: 'QAR', periodStart: '2025-01-01', periodEnd: '2025-12-31',
      layoutVersionId: id, layoutHash: hash64,
      comparativePackageId: null, comparativeCalculationHash: null, comparativeBasis: null, comparativeEvidenceReference: null,
      noteSourceManifestHash: hash64,
      builderVersion: 'auditsphere.statement-presentation.v6', rendererVersion: 'auditsphere.statement-renderer.v3',
      displayPolicy: 'whole units', status: 'UNAPPROVED_DRAFT', revisionHash: hash64, snapshotSha256Hex: hash64,
      createdAt: '2026-10-12T00:00:00Z', sourceIsCurrent: true, artifactFormats: [], predecessorRevisionId: null,
    };
    const download = fixture.componentInstance.download('xlsx');
    const pinsUrl = `/api/ui/accounting/packages/${id}/presentation/pins`;
    const createPin = await vi.waitFor(() => http.expectOne(pinsUrl));
    expect(createPin.request.body).toEqual({ layoutVersionId: id });
    createPin.flush({ value: pin });
    const refreshedPins = await vi.waitFor(() => http.expectOne(pinsUrl));
    refreshedPins.flush([pin]);
    const exported = await vi.waitFor(() => http.expectOne(`/api/ui/accounting/packages/${id}/presentation/pins/${revisionId}/artifacts`));
    expect(exported.request.body).toEqual({ format: 'xlsx' });
    exported.flush(new Blob(['x']), { headers: {
      'Content-Disposition': 'attachment; filename="statement.xlsx"',
      'X-Statement-Presentation-Revision': revisionId,
      'X-Statement-Presentation-SHA256': hash64,
    } });
    await download;
    (await vi.waitFor(() => http.expectOne(pinsUrl))).flush([{ ...pin, artifactFormats: ['xlsx'] }]);
    fixture.destroy();
  });

  it('renders the exact body for either of two selected saved revisions', async () => {
    const revA = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
    const revB = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
    const { fixture, http } = await presentationScreen([savedPin(revA), savedPin(revB)]);
    const buttons = () => [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')];
    buttons().find((button) => button.textContent?.includes('View saved snapshot'))!.click();
    fixture.detectChanges();
    http.expectOne(`/api/ui/accounting/packages/${id}/presentation/pins/${revA}`).flush(savedBody('Revision A', '101'));
    await vi.waitFor(() => expect(fixture.componentInstance.savedSnapshot()?.header.entity).toBe('Revision A'));
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Saved cash Revision A');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Revision A');

    buttons().find((button) => button.textContent?.includes('View saved snapshot'))!.click();
    fixture.detectChanges();
    http.expectOne(`/api/ui/accounting/packages/${id}/presentation/pins/${revB}`).flush(savedBody('Revision B', '202'));
    await vi.waitFor(() => expect(fixture.componentInstance.savedSnapshot()?.header.entity).toBe('Revision B'));
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Saved cash Revision B');
    expect(text).not.toContain('Saved cash Revision A');
    expect(text).toContain(`revision ${revB}`);
    fixture.destroy();
  });

  it('ignores an out-of-order response and clears a denied saved-revision selection', async () => {
    const revA = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
    const revB = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
    const deniedRevision = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';
    const { fixture, http } = await presentationScreen([savedPin(revA), savedPin(revB), savedPin(deniedRevision)]);
    const viewButton = (revision: string) => [...(fixture.nativeElement as HTMLElement).querySelectorAll('article[aria-label="Saved statement presentation draft"]')]
      .find((article) => article.textContent?.includes(revision))?.querySelector('button');
    viewButton(revA)!.click();
    fixture.detectChanges();
    const requestA = http.expectOne(`/api/ui/accounting/packages/${id}/presentation/pins/${revA}`);
    viewButton(revB)!.click();
    fixture.detectChanges();
    const requestB = http.expectOne(`/api/ui/accounting/packages/${id}/presentation/pins/${revB}`);
    requestB.flush(savedBody('Newer revision', '202'));
    await vi.waitFor(() => expect(fixture.componentInstance.savedSnapshot()?.header.entity).toBe('Newer revision'));
    requestA.flush(savedBody('Late old revision', '101'));
    await vi.waitFor(() => expect(fixture.componentInstance.savedSnapshot()?.header.entity).toBe('Newer revision'));
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Saved cash Newer revision');
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Saved cash Late old revision');

    viewButton(deniedRevision)!.click();
    fixture.detectChanges();
    http.expectOne(`/api/ui/accounting/packages/${id}/presentation/pins/${deniedRevision}`)
      .flush({ message: 'Access denied.' }, { status: 403, statusText: 'Forbidden' });
    await vi.waitFor(() => expect(fixture.componentInstance.selectedPin()).toBeNull());
    fixture.detectChanges();
    expect(fixture.componentInstance.savedSnapshot()).toBeNull();
    expect(fixture.componentInstance.savedSnapshotError()).toContain('selection was cleared');
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Saved snapshot body · immutable revision');
    fixture.destroy();
  });
});
