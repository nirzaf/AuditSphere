import { BehaviorSubject } from 'rxjs';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { ClientOperationalOpeningBalances, NativeSuccessorOpeningPage, decodeOpeningBalance, decodeSuccessorPreview, decodeSuccessorView } from './operational-opening-balances';
import { SessionService } from '../../core/session';

const client = '11111111-1111-4111-8111-111111111111';
const periodId = '22222222-2222-4222-8222-222222222222';
const chart = '33333333-3333-4333-8333-333333333333';
const maker = '44444444-4444-4444-8444-444444444444';
const reviewer = '55555555-5555-4555-8555-555555555555';
const openingId = '66666666-6666-4666-8666-666666666666';
const period = { id: periodId, revision: '8', code: 'FY2026', start: '2026-01-01', end: '2026-12-31', currency: 'QAR', status: 'OPEN' };
const response = (approvedByUserId: string | null = null) => ({ id: openingId, clientId: client, periodId, chartVersionId: chart,
  periodRevision: '8', asOfDate: period.start, currency: 'QAR', evidenceReference: 'CUTOVER-TB-2025', evidenceSha256: 'a'.repeat(64),
  manifestSha256: 'b'.repeat(64), createdByUserId: maker, createdAt: '2026-10-07T10:00:00Z', approvedByUserId,
  approvedAt: approvedByUserId ? '2026-10-07T11:00:00Z' : null,
  lines: [{ accountCode: '1000', accountName: 'Cash', debit: '100.000000', credit: '0.000000' },
    { accountCode: '3000', accountName: 'Capital', debit: '0.000000', credit: '100.000000' }], openItems: [] });

const sourceId = '77777777-7777-4777-8777-777777777777';
const priorId = '88888888-8888-4888-8888-888888888888';
const engagementId = '99999999-9999-4999-8999-999999999999';
const successorBase = `/api/ui/accounting/clients/${client}/operational-opening-successors`;
const catalogUrl = successorBase + `/catalog/${periodId}?page=0&pageSize=5`;
const exactRevision = '9007199254740993';
const catalog = (origin = 'NONE', requestId = openingId, canApprove = false) => ({ clientId: client, periodId,
  periodRevision: exactRevision, priorPeriodId: priorId, priorPeriodRevision: '4', priorPeriodCode: 'FY2025', priorPeriodStatus: 'CLOSED',
  origin, existingOpeningId: origin === 'NONE' ? null : openingId,
  retainedSuccessor: origin === 'NATIVE_SUCCESSOR' ? { openingId, engagementId, requestId } : null,
  canPrepare: origin === 'NONE', canApprove, page: 0, pageSize: 5, total: 1,
  sources: [{ datasetId: sourceId, engagementId, legalEntityKey: 'TEST-ENTITY', sourceIdentityHash: 'a'.repeat(64),
    validationStatus: 'Accepted', eligible: true, blocker: null }], blocker: null });
const preview = () => ({ reviewBasis: 'e'.repeat(64), sourceIdentityHash: 'a'.repeat(64), prospectiveOpeningManifestHash: 'b'.repeat(64),
  periodId, periodRevision: exactRevision, priorPeriodId: priorId, priorPeriodRevision: '4', asOfDate: period.start,
  currency: 'QAR', basis: 'IFRS', bookScope: 'PERIOD_LEDGER_NO_BOOK', populationHash: 'd'.repeat(64), ready: true, blockers: [],
  lines: response().lines.map((line, index) => ({ ...line, accountId: index === 0 ? chart : openingId })),
  controls: [{ role: 'AR', configurationId: maker, decisionId: client, accountId: priorId, closingDebit: '0.000000', closingCredit: '0.000000' },
    { role: 'AP', configurationId: reviewer, decisionId: periodId, accountId: sourceId, closingDebit: '0.000000', closingCredit: '0.000000' }] });
const successorView = (approvedByUserId: string | null = null) => ({ opening: { ...response(approvedByUserId), periodRevision: exactRevision },
  sourceIdentityHash: 'a'.repeat(64), proofHash: 'c'.repeat(64), isCurrent: true, currentnessBlocker: null,
  proofJson: JSON.stringify({ Version: 'native-opening-successor.v1', ClientId: client, OpeningId: openingId,
    OpeningManifestHash: 'b'.repeat(64), SourceIdentityHash: 'a'.repeat(64), SourceDatasetId: sourceId, PriorPeriodId: priorId, EngagementId: engagementId,
    Facts: { TargetPeriodId: periodId, PopulationHash: 'd'.repeat(64), Controls: preview().controls.map(c => ({ Role: c.role,
      ConfigurationId: c.configurationId, DecisionId: c.decisionId, AccountId: c.accountId, ClosingDebit: c.closingDebit, ClosingCredit: c.closingCredit })) } }) });
const originalOpenItemId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const originalDocumentId = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const counterpartyId = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';
const submissionId = 'dddddddd-dddd-4ddd-8ddd-dddddddddddd';
const draftId = 'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee';
const journalId = 'ffffffff-ffff-4fff-8fff-ffffffffffff';
const carriedItem = () => ({ originalKind: 'SALES_INVOICE', originalOpenItemId, originalDocumentId, role: 'AR', counterpartyId,
  counterpartyName: 'Original customer', reference: 'INV-EXACT-2025', referenceKind: 'DRAFT_REFERENCE', accountId: priorId,
  accountCode: '1100', currency: 'QAR', originalAmount: '100.000000', baselineAppliedAmount: '0.000000', baselineOpenAmount: '100.000000',
  sourceAsOfDate: null, documentDate: '2025-01-10', accountingDate: '2025-12-31', dueDate: null, sourceFactsHash: 'f'.repeat(64) });
const carrySourceFacts = () => ({ Version: 'native-successor-invoice-item-source.v1', FirmId: maker, ClientId: client,
  OriginalKind: 'SALES_INVOICE', OriginalOpenItemId: originalOpenItemId, OriginalDocumentId: originalDocumentId,
  SubmissionId: submissionId, DraftId: draftId, OriginalOpeningId: null, SourceAsOfDate: null,
  AccountingDate: '2025-12-31', JournalId: journalId, Currency: 'QAR', CounterpartyId: counterpartyId,
  CounterpartyName: 'Original customer', Reference: 'INV-EXACT-2025', ReferenceKind: 'DRAFT_REFERENCE', DueDate: null,
  OriginalAmount: '100.000000', Role: 'AR', AccountId: priorId, AccountCode: '1100' });
const successorControlStateJson = (accountClientId = client) => JSON.stringify([
  { Role: 'AP', ConfigurationId: reviewer, DecisionId: periodId, AccountId: sourceId, ClosingDebit: '0.000000', ClosingCredit: '0.000000',
    AccountHash: 'a'.repeat(64), ConfigurationHash: 'b'.repeat(64), DecisionHash: 'c'.repeat(64),
    AccountJson: JSON.stringify({ Id: sourceId, FirmId: maker, ClientId: client, ChartVersionId: chart, AccountCode: '2100' }),
    ConfigurationJson: JSON.stringify({ Id: reviewer, FirmId: maker, ClientId: client, ChartVersionId: chart, AccountId: sourceId, Role: 'AP' }),
    DecisionJson: JSON.stringify({ Id: periodId, FirmId: maker, ClientId: client, ConfigurationId: reviewer, Decision: 'APPROVE' }) },
  { Role: 'AR', ConfigurationId: maker, DecisionId: client, AccountId: priorId, ClosingDebit: '100.000000', ClosingCredit: '0.000000',
    AccountHash: 'd'.repeat(64), ConfigurationHash: 'e'.repeat(64), DecisionHash: 'f'.repeat(64),
    AccountJson: JSON.stringify({ Id: priorId, FirmId: maker, ClientId: accountClientId, ChartVersionId: chart, AccountCode: '1100' }),
    ConfigurationJson: JSON.stringify({ Id: maker, FirmId: maker, ClientId: client, ChartVersionId: chart, AccountId: priorId, Role: 'AR' }),
    DecisionJson: JSON.stringify({ Id: client, FirmId: maker, ClientId: client, ConfigurationId: maker, Decision: 'APPROVE' }) },
]);
const carriedPreview = (items = [carriedItem()], closingDebit = '100.000000') => {
  const result = preview();
  result.controls[0] = { ...result.controls[0], closingDebit };
  return { ...result, carriedPopulation: { version: 'native-successor-carried-population.v1', count: items.length,
    populationHash: result.populationHash, items } };
};
const successorViewV2 = (sourceFacts = carrySourceFacts(), count = 1, controlStateJson = successorControlStateJson()) => {
  const carry = carriedPreview();
  const publicItem = carriedItem();
  const rawItem = { OriginalKind: publicItem.originalKind, OriginalOpenItemId: publicItem.originalOpenItemId,
    OriginalDocumentId: publicItem.originalDocumentId, OriginalOpeningId: null, SubmissionId: submissionId, DraftId: draftId,
    JournalId: journalId, Role: publicItem.role, CounterpartyId: publicItem.counterpartyId, CounterpartyName: publicItem.counterpartyName,
    Reference: publicItem.reference, ReferenceKind: publicItem.referenceKind, AccountId: publicItem.accountId, AccountCode: publicItem.accountCode,
    Currency: publicItem.currency, OriginalAmount: publicItem.originalAmount, BaselineAppliedAmount: publicItem.baselineAppliedAmount,
    BaselineOpenAmount: publicItem.baselineOpenAmount, SourceAsOfDate: null, DocumentDate: publicItem.documentDate,
    AccountingDate: publicItem.accountingDate, DueDate: null, SourceFactsJson: JSON.stringify(sourceFacts), SourceFactsHash: publicItem.sourceFactsHash };
  return { opening: { ...response(), periodRevision: exactRevision }, sourceIdentityHash: 'a'.repeat(64), proofHash: 'c'.repeat(64),
    isCurrent: true, currentnessBlocker: null, proofJson: JSON.stringify({ Version: 'native-opening-successor.v2', FirmId: maker, ClientId: client,
      OpeningId: openingId, OpeningManifestHash: 'b'.repeat(64), SourceIdentityHash: 'a'.repeat(64), SourceDatasetId: sourceId,
      PriorPeriodId: priorId, EngagementId: engagementId, Facts: { TargetPeriodId: periodId, PriorPeriodId: priorId,
        PriorEnd: '2025-12-31', Currency: 'QAR', PopulationHash: carry.populationHash,
        Controls: carry.controls.map(c => ({ Role: c.role, ConfigurationId: c.configurationId, DecisionId: c.decisionId,
          AccountId: c.accountId, ClosingDebit: c.closingDebit, ClosingCredit: c.closingCredit })),
        CarriedPopulation: { Version: 'native-successor-carried-population.v1', Count: count,
          PopulationHash: carry.populationHash, ControlStateHash: '1'.repeat(64), ControlStateJson: controlStateJson, Items: [rawItem] } } }) };
};

describe('Reviewed client opening balances', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [ClientOperationalOpeningBalances], providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
      { provide: ActivatedRoute, useValue: { paramMap: new BehaviorSubject(convertToParamMap({ clientId: client })),
        snapshot: { queryParamMap: convertToParamMap({}) } } }] });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => { http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); });
  async function setup(userId = maker) {
    TestBed.inject(SessionService).current.set({ userId, firmId: client, generation: '1', staff: true });
    const fixture = TestBed.createComponent(ClientOperationalOpeningBalances);
    fixture.componentRef.setInput('clientId', client); fixture.componentRef.setInput('currency', 'QAR'); fixture.componentRef.setInput('periods', [period]); fixture.detectChanges();
    const select = fixture.nativeElement.querySelector('#opening-period') as HTMLSelectElement;
    select.value = periodId; select.dispatchEvent(new Event('change')); fixture.detectChanges();
    http.expectOne(`/api/ui/accounting/clients/${client}/operational-opening-balances/${periodId}`).flush(null);
    http.expectOne(catalogUrl).flush(catalog());
    http.expectOne(`/api/ui/accounting/clients/${client}/counterparties?page=0&pageSize=25`).flush({ clientId: client, role: null, page: 0, pageSize: 25, total: 0, bookkeepingActive: true, counterparties: [] });
    await fixture.whenStable(); fixture.detectChanges();
    return fixture;
  }

  it('validates a balanced client-period manifest and rejects tampered context', () => {
    expect(decodeOpeningBalance(response(), client, period)?.manifestSha256).toBe('b'.repeat(64));
    expect(() => decodeOpeningBalance({ ...response(), clientId: periodId }, client, period)).toThrow();
    expect(() => decodeOpeningBalance({ ...response(), periodRevision: '7' }, client, period)).not.toThrow();
    expect(() => decodeOpeningBalance({ ...response(), lines: [{ ...response().lines[0], credit: '1.000000' }, response().lines[1]] }, client, period)).toThrow();
    expect(() => decodeOpeningBalance({ ...response(), manifestSha256: 'bad' }, client, period)).toThrow();
  });

  it('creates from the exact period revision and recovers the immutable manifest by reading it back', async () => {
    const fixture = await setup(); const c = fixture.componentInstance;
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('table tbody tr').length).toBe(2);
    expect(fixture.nativeElement.querySelector("input[aria-label='Opening account code row 1']")).toBeTruthy();
    c.evidenceReference = 'CUTOVER-TB-2025'; c.evidenceSha256 = 'a'.repeat(64);
    c.lines.set([{ accountCode: '1000', debit: '100', credit: '0' }, { accountCode: '3000', debit: '0', credit: '100' }]); c.reviewed.set(true);
    expect(c.balanced()).toBe(true); c.create();
    const create = http.expectOne({ method: 'POST', url: `/api/ui/accounting/clients/${client}/operational-opening-balances` });
    expect(create.request.body).toMatchObject({ periodId, periodRevision: '8', asOfDate: period.start, currency: 'QAR', reviewed: true });
    expect(create.request.body.openItems).toEqual([]);
    create.flush({ id: openingId });
    const read = http.expectOne(`/api/ui/accounting/clients/${client}/operational-opening-balances/${periodId}`);
    read.flush(response()); http.expectOne(catalogUrl).flush(catalog('MANUAL')); await fixture.whenStable(); fixture.detectChanges();
    expect(c.opening()?.manifestSha256).toBe('b'.repeat(64));
    expect(fixture.nativeElement.textContent).toContain('Awaiting independent review');
    const buttons = fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>;
    expect(Array.from(buttons).some(b => b.textContent?.includes('Approve opening snapshot'))).toBe(false);
  });

  it('lets a different reviewer approve the exact manifest and then displays retained approval evidence', async () => {
    const fixture = await setup(reviewer); const c = fixture.componentInstance;
    c.load(); http.expectOne(`/api/ui/accounting/clients/${client}/operational-opening-balances/${periodId}`).flush(response());
    http.expectOne(catalogUrl).flush(catalog('MANUAL')); await fixture.whenStable(); fixture.detectChanges();
    c.approvalReviewed.set(true); c.approve(c.opening()!);
    const approve = http.expectOne({ method: 'POST', url: `/api/ui/accounting/clients/${client}/operational-opening-balances/${openingId}/approve` });
    expect(approve.request.body).toEqual({ periodRevision: '8', manifestSha256: 'b'.repeat(64), reviewed: true });
    approve.flush(null);
    http.expectOne(`/api/ui/accounting/clients/${client}/operational-opening-balances/${periodId}`).flush(response(reviewer));
    http.expectOne(catalogUrl).flush(catalog('MANUAL')); await fixture.whenStable(); fixture.detectChanges();
    expect(c.opening()?.approvedByUserId).toBe(reviewer);
    expect(fixture.nativeElement.textContent).toContain('Independently approved');
  });

  it('preserves v1 proof bytes and strictly decodes complete v2 carried originals', () => {
    const legacy = successorView();
    const decodedLegacy = decodeSuccessorView(legacy, client, period);
    expect(decodedLegacy.proofJson).toBe(legacy.proofJson);
    expect(decodedLegacy.carriedPopulation).toBeNull();
    expect(decodeSuccessorPreview(preview()).carriedPopulation).toBeNull();
    const forgedV1 = JSON.parse(legacy.proofJson) as Record<string, unknown>;
    (forgedV1['Facts'] as Record<string, unknown>)['CarriedPopulation'] = { Version: 'native-successor-carried-population.v1' };
    expect(() => decodeSuccessorView({ ...legacy, proofJson: JSON.stringify(forgedV1) }, client, period)).toThrow();

    const validPreview = carriedPreview();
    const decodedPreview = decodeSuccessorPreview(validPreview);
    expect(decodedPreview.carriedPopulation?.items[0]).toMatchObject({ reference: 'INV-EXACT-2025', dueDate: null,
      originalAmount: '100.000000', baselineAppliedAmount: '0.000000', baselineOpenAmount: '100.000000' });
    expect(() => decodeSuccessorPreview({ ...validPreview, carriedPopulation: {
      ...validPreview.carriedPopulation, items: [{ ...validPreview.carriedPopulation.items[0], originalAmount: '99.000000' }] } })).toThrow();
    expect(() => decodeSuccessorPreview({ ...validPreview, carriedPopulation: {
      ...validPreview.carriedPopulation, count: 2 } })).toThrow();

    const retained = successorViewV2();
    expect(decodeSuccessorView(retained, client, period).carriedPopulation?.items[0].originalOpenItemId).toBe(originalOpenItemId);
    expect(() => decodeSuccessorView(successorViewV2({ ...carrySourceFacts(), ClientId: reviewer }), client, period)).toThrow();
    expect(() => decodeSuccessorView(successorViewV2(carrySourceFacts(), 1, successorControlStateJson(reviewer)), client, period)).toThrow();
    expect(() => decodeSuccessorView(successorViewV2(carrySourceFacts(), 2), client, period)).toThrow();
  });

  it('shows paged carried originals in preview and retained successor history', async () => {
    const fixture = await setup(); const c = fixture.componentInstance;
    c.setSuccessorMode(true); c.successorSourceId = sourceId; c.successorEvidence = 'EXACT-CLOSED-SOURCE';
    const previewItems = Array.from({ length: 101 }, (_, index) => {
      const suffix = index.toString().padStart(12, '0');
      return { ...carriedItem(), originalOpenItemId: `a0000000-0000-4000-8000-${suffix}`,
        originalDocumentId: `b0000000-0000-4000-8000-${suffix}`, reference: `INV-PAGE-${suffix}`,
        originalAmount: '1.000000', baselineOpenAmount: '1.000000' };
    });
    const preparing = c.previewSuccessor(null);
    http.expectOne(successorBase + '/preview').flush({ value: carriedPreview(previewItems, '101.000000') }); await preparing; fixture.detectChanges();
    const tables = fixture.nativeElement.querySelectorAll('table') as NodeListOf<HTMLTableElement>;
    const firstPage = Array.from(tables).find(table =>
      table.querySelector('caption')?.textContent?.trim() === 'Original-item carry preview');
    expect(firstPage?.querySelectorAll('tbody tr').length).toBe(100);
    expect(firstPage?.textContent).toContain('INV-PAGE-000000000099');
    expect(firstPage?.textContent).not.toContain('INV-PAGE-000000000100');
    expect(fixture.nativeElement.textContent).toContain('Checked in this preview');
    expect(fixture.nativeElement.querySelector('mat-paginator[aria-label="Successor preview original-item pages"]')).toBeTruthy();
    c.successorCarryPage.set(1); fixture.detectChanges();
    const secondPage = Array.from(tables).find(table =>
      table.querySelector('caption')?.textContent?.trim() === 'Original-item carry preview');
    expect(secondPage?.querySelectorAll('tbody tr').length).toBe(1);
    expect(secondPage?.textContent).toContain('INV-PAGE-000000000100');
    expect(secondPage?.textContent).not.toContain('INV-PAGE-000000000099');

    const loading = c.loadSuccessorCatalog();
    http.expectOne(catalogUrl).flush(catalog('NATIVE_SUCCESSOR', openingId));
    await Promise.resolve(); await Promise.resolve();
    http.expectOne(successorBase + '/' + openingId).flush(successorViewV2()); await loading; fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Retained original items');
    expect(fixture.nativeElement.textContent).toContain('INV-EXACT-2025');
    expect(fixture.nativeElement.textContent).toContain('Current');
    expect(fixture.nativeElement.querySelector('mat-paginator[aria-label="Retained original-item pages"]')).toBeTruthy();
  });

  it('previews exact accepted predecessor identities and preserves the original uncertain creation request for explicit recovery', async () => {
    const fixture = await setup(); const c = fixture.componentInstance;
    c.setSuccessorMode(true); c.successorSourceId = sourceId; c.successorEvidence = 'EXACT-CLOSED-SOURCE';
    const ready = c.previewSuccessor(null);
    const previewRequest = http.expectOne(successorBase + '/preview');
    expect(previewRequest.request.body).toMatchObject({ clientId: client, engagementId, periodId,
      expectedPeriodRevision: exactRevision, priorPeriodId: priorId, expectedPriorPeriodRevision: '4',
      nativeTrialBalanceDatasetId: sourceId, expectedSourceIdentityHash: 'a'.repeat(64), reviewed: false });
    previewRequest.flush({ value: preview() }); await ready; fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('100.000000');
    expect(fixture.nativeElement.textContent).toContain('PERIOD_LEDGER_NO_BOOK');
    await c.createSuccessor(); http.expectNone(successorBase + '/create');
    c.successorReviewed.set(true); const send = c.createSuccessor();
    const first = http.expectOne(successorBase + '/create'); const original = first.request.body;
    expect(original.reviewBasis).toBe('e'.repeat(64)); expect(original.expectedPeriodRevision).toBe(exactRevision);
    first.error(new ProgressEvent('network')); await send;
    expect(c.successorUncertain()).toBe(true); expect(c.manualReady()).toBe(false);
    const refresh = c.loadSuccessorCatalog(); http.expectOne(catalogUrl).flush(catalog()); await refresh;
    expect(c.successorPending()?.request.requestId).toBe(original.requestId);
    http.expectNone(successorBase + '/create');
    c.successorReviewed.set(true); await c.createSuccessor(); http.expectNone(successorBase + '/create');
    // Readable catalogue with no eligible source on this page must not authorize replay.
    const offPage = c.loadSuccessorCatalog();
    http.expectOne(catalogUrl).flush({ ...catalog(), canPrepare: false, total: 0, sources: [] }); await offPage;
    const denied = c.validateSuccessorRecovery();
    const deniedPreview = http.expectOne(successorBase + '/preview');
    expect(deniedPreview.request.body).toEqual({ ...original, reviewed: false });
    deniedPreview.flush({ message: 'The engagement file is frozen.' }, { status: 409, statusText: 'Conflict' }); await denied;
    expect(c.successorRecoveryReady()).toBe(false); expect(c.successorPending()?.request).toEqual(original);
    c.successorReviewed.set(true); await c.createSuccessor(); http.expectNone(successorBase + '/create');
    const changed = c.validateSuccessorRecovery();
    http.expectOne(successorBase + '/preview').flush({ value: { ...preview(), reviewBasis: 'f'.repeat(64) } }); await changed;
    expect(c.successorRecoveryReady()).toBe(false); expect(c.successorPending()?.request).toEqual(original);
    const exact = c.validateSuccessorRecovery();
    const originalPreview = http.expectOne(successorBase + '/preview');
    expect(originalPreview.request.body).toEqual({ ...original, reviewed: false });
    originalPreview.flush({ value: preview() }); await exact;
    expect(c.successorRecoveryReady()).toBe(true); expect(c.successorReviewed()).toBe(false);
    await c.createSuccessor(); http.expectNone(successorBase + '/create');
    c.successorReviewed.set(true); const recover = c.createSuccessor();
    const retry = http.expectOne(successorBase + '/create'); expect(retry.request.body).toEqual(original);
    retry.flush({ value: successorView() });
    await Promise.resolve(); await Promise.resolve();
    http.expectOne(catalogUrl).flush(catalog('NATIVE_SUCCESSOR', original.requestId));
    await Promise.resolve(); await Promise.resolve();
    http.expectOne(successorBase + '/' + openingId).flush(successorView()); await recover; fixture.detectChanges();
    expect(c.successorPending()).toBeNull(); expect(c.successorUncertain()).toBe(false);
    expect(c.opening()?.approvedByUserId).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Awaiting independent review');
    expect(fixture.nativeElement.textContent).not.toContain('Approve opening snapshot');
  });

  it('requires independent assent to both retained proof and manifest and refuses missing or mismatched proof', async () => {
    expect(() => decodeSuccessorView({ ...successorView(), proofHash: null }, client, period)).toThrow();
    expect(() => decodeSuccessorView({ ...successorView(), sourceIdentityHash: 'f'.repeat(64) }, client, period)).toThrow();
    const fixture = await setup(reviewer); const c = fixture.componentInstance;
    const load = c.loadSuccessorCatalog(); http.expectOne(catalogUrl).flush(catalog('NATIVE_SUCCESSOR', openingId, true));
    await Promise.resolve(); await Promise.resolve(); http.expectOne(successorBase + '/' + openingId).flush(successorView()); await load; fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('Approve opening snapshot');
    expect(fixture.nativeElement.querySelector(`a[href="/app/accounting/clients/${client}/native-sources/${sourceId}"]`)).toBeTruthy();
    await c.approveSuccessor(); http.expectNone(successorBase + '/' + openingId + '/approve');
    c.successorApprovalReviewed.set(true); const approve = c.approveSuccessor();
    const sent = http.expectOne(successorBase + '/' + openingId + '/approve');
    expect(sent.request.body).toMatchObject({ openingId, clientId: client, engagementId, expectedPeriodRevision: exactRevision,
      expectedOpeningManifestHash: 'b'.repeat(64), expectedSuccessorProofHash: 'c'.repeat(64), reviewed: true });
    sent.flush({ value: true }); await Promise.resolve(); await Promise.resolve();
    http.expectOne(catalogUrl).flush(catalog('NATIVE_SUCCESSOR', openingId, false)); await Promise.resolve(); await Promise.resolve();
    http.expectOne(successorBase + '/' + openingId).flush(successorView(reviewer)); await approve; fixture.detectChanges();
    expect(c.opening()?.approvedByUserId).toBe(reviewer); expect(c.successorPending()).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Independently approved');
  });

  it('keeps unsupported candidates blocked and clears a late preview when the actor changes', async () => {
    const fixture = await setup(); const c = fixture.componentInstance;
    c.setSuccessorMode(true); c.successorSourceId = sourceId; c.successorEvidence = 'EXACT-CLOSED-SOURCE';
    const reload = c.loadSuccessorCatalog();
    http.expectOne(catalogUrl).flush({ ...catalog(), sources: [{ ...catalog().sources[0], eligible: false,
      blocker: 'Nonzero individual P&L closing account; no supported successor.' }] }); await reload; fixture.detectChanges();
    await c.previewSuccessor(null); http.expectNone(successorBase + '/preview');
    expect(fixture.nativeElement.textContent).toContain('Nonzero individual P&L closing account');
    const refresh = c.loadSuccessorCatalog(); http.expectOne(catalogUrl).flush(catalog()); await refresh;
    const previewRead = c.previewSuccessor(null); const pending = http.expectOne(successorBase + '/preview');
    const session = TestBed.inject(SessionService); session.current.set(null); session.invalidation.update(n => n + 1);
    fixture.detectChanges(); pending.flush({ value: preview() }); await previewRead; fixture.detectChanges();
    expect(c.successorPreview()).toBeNull(); expect(c.successorCatalog()).toBeNull(); expect(c.opening()).toBeNull();
    expect(c.successorBusy()).toBe(false); expect(fixture.nativeElement.textContent).not.toContain('100.000000');
  });

  it('loads the route client through the existing authorized native context without selecting or activating a target', async () => {
    TestBed.inject(SessionService).current.set({ userId: maker, firmId: client, generation: '1', staff: true });
    const fixture = TestBed.createComponent(NativeSuccessorOpeningPage); fixture.detectChanges(); TestBed.tick();
    http.expectOne(`/api/ui/accounting/clients/${client}`).flush({ clientId: client, name: 'Native client',
      profile: { sourceMode: 'NATIVE_BOOKKEEPING', currency: 'QAR' }, periods: [{ ...period, basis: 'IFRS', status: 'DRAFT' }], hasMorePeriods: false });
    await fixture.whenStable(); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Native client');
    expect((fixture.nativeElement.querySelector('#opening-period') as HTMLSelectElement).value).toBe('');
    http.expectNone(r => r.method === 'POST');
    http.expectNone(catalogUrl);
  });

});
