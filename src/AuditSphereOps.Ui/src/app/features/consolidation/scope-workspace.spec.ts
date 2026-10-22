import { signal, type WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { describe, expect, it } from 'vitest';
import { vi } from 'vitest';
import { Api } from '../../core/api';
import { SessionService } from '../../core/session';
import {
  decodeScopeWorkspace,
  decodeConsolidationJournal,
  decodeConsolidationComponent,
  decodeIntercompanyExceptionsPage,
  decodeIntercompanyMatchOptions,
  decodeScopeSummary,
  ScopeWorkspace,
} from './scope-contracts';
import { ConsolidationScopeWorkspace } from './scope-workspace';
import { formatOwnershipPercent } from './scope-amounts';

const id1 = '11111111-1111-4111-8111-111111111111';
const id2 = '22222222-2222-4222-8222-222222222222';
const id3 = '33333333-3333-4333-8333-333333333333';
const hash64 = 'a'.repeat(64);
const scopeUrl = `/api/ui/consolidation/scopes/${id1}`;
const actor = id1;
const matchId1 = '44444444-4444-4444-8444-444444444444';
const matchId2 = '55555555-5555-4555-8555-555555555555';
const matchId3 = '66666666-6666-4666-8666-666666666666';

function intercompanyOptionsPayload(overrides: Record<string, unknown> = {}) {
  return {
    scopeVersionId: id1, groupId: id2, pinnedGroupRevision: 1, currentGroupRevision: 1, reportingCurrency: 'QAR',
    periodStartDate: '2026-01-01', periodEndDate: '2026-12-31', membershipBlocker: null,
    members: [
      { clientId: id2, legalName: 'Synthetic Seller Ltd', hasApprovedComponent: true },
      { clientId: id3, legalName: 'Synthetic Buyer Ltd', hasApprovedComponent: true },
    ],
    memberPaging: { totalCount: Array.isArray(overrides['members']) ? overrides['members'].length : 2, page: 1, pageSize: 25 },
    memberSearch: '', resolvedSeller: null, resolvedBuyer: null,
    membersTruncated: false, taxonomyCodes: ['ASSETS.IC_AR', 'LIABILITIES.IC_AP'], taxonomyCodesTruncated: false,
    ...overrides,
  };
}

function intercompanyMatch(matchId: string, status: string, createdByUserId: string, reviewedByUserId: string | null = null) {
  return {
    matchId, sellerClientId: id2, buyerClientId: id3, accountNature: 'RECEIVABLE_PAYABLE', matchMode: 'ONE_TO_ONE',
    periodCode: '2026-12', currency: 'QAR', transactionReference: `IC-${matchId.slice(0, 4)}`,
    sellerAmount: '1200.123456', buyerAmount: '-1200.000000', matchedAmount: '1200.000000', difference: '0.123456',
    unmatchedResidual: '0.123456',
    differenceReason: 'Timing difference per signed confirmation.', status, outsidePerimeterReview: false,
    evidenceReference: 'SIGNED-IC-STATEMENT-01', sellerTaxonomyCode: 'ASSETS.IC_AR',
    buyerTaxonomyCode: 'LIABILITIES.IC_AP', matchGroupReference: '', createdByUserId,
    createdAt: '2026-10-01T11:00:00Z', reviewedByUserId,
    reviewedAt: reviewedByUserId ? '2026-10-01T11:30:00Z' : null,
  };
}

function intercompanyPagePayload(
  items: unknown[], currentGroupRevision = 1, page = 1, totalCount = items.length,
  totalUnmatchedDifference = totalCount === 0 ? '0.000000' : '0.123456',
  totalGrossUnmatchedExposure = totalCount === 0 ? '0.000000' : '0.123456',
) {
  return {
    scopeVersionId: id1, groupId: id2, pinnedGroupRevision: 1, currentGroupRevision,
    items, totalCount, page, pageSize: 50, totalUnmatchedDifference, totalGrossUnmatchedExposure,
  };
}

function workspacePayload(overrides: Record<string, unknown> = {}) {
  const payload = {
    scope: {
      id: id1, groupId: id2, groupName: 'Synthetic Group', groupCode: 'SYN-GRP', version: 1,
      groupRevision: 1, periodId: id3, reportingCurrency: 'QAR', method: 'PROPORTIONATE',
      status: 'APPROVED', openingBasis: 'OPENING_BALANCE', isAdvanced: false,
    },
    canPrepare: true,
    canReview: true,
    canApproveScope: false,
    scopeApprovalBlocker: null,
    currentUserId: actor,
    components: [],
    eligiblePackages: [],
    journals: [],
    runs: [],
    latestReport: { scopeVersionId: id1, runId: null, state: 'NO_RUN', approvedAt: null, lines: [] },
    componentReadiness: [],
    intercompanyExceptions: [],
    ...overrides,
  };
  const page = (value: unknown) => Array.isArray(value)
    ? { items: value, totalCount: value.length, page: 1, pageSize: 100 }
    : value;
  const readResult = (value: unknown) => value && typeof value === 'object' && 'available' in value
    ? value
    : { available: true, value, errorCode: null, message: null };
  const rawExceptions = payload.intercompanyExceptions;
  const exceptionPage = Array.isArray(rawExceptions)
    ? { scopeVersionId: id1, groupId: id2, pinnedGroupRevision: 1, currentGroupRevision: 1,
      items: rawExceptions, totalCount: rawExceptions.length, page: 1, pageSize: 100,
      totalUnmatchedDifference: '0.000000', totalGrossUnmatchedExposure: '0.000000' }
    : rawExceptions;
  return {
    ...payload,
    componentPaging: overrides['componentPaging'] ?? { totalCount: payload.components.length, page: 1, pageSize: 100 },
    readinessPaging: overrides['readinessPaging'] ?? (Array.isArray(payload.componentReadiness)
      ? { totalCount: payload.componentReadiness.length, page: 1, pageSize: 100 }
      : (payload.componentReadiness as unknown as { available: boolean; value: unknown[] | null }).available
        ? { totalCount: (payload.componentReadiness as unknown as { value: unknown[] }).value.length, page: 1, pageSize: 100 } : null),
    resolvedComponent: null,
    eligiblePackages: page(payload.eligiblePackages),
    journals: page(payload.journals),
    runs: page(payload.runs),
    latestReport: readResult(payload.latestReport),
    componentReadiness: readResult(payload.componentReadiness),
    intercompanyExceptions: readResult(exceptionPage),
  };
}

function journal(id: string, number: string, status: string, createdByUserId: string) {
  return {
    id, journalNumber: number, journalType: 'INTERCOMPANY_ELIMINATION', currency: 'QAR',
    totalDebits: '0.000000', totalCreditsAbs: '0.000000', evidenceReference: 'SYN-REF',
    status, returnReason: status === 'Returned' ? 'Correct the source reference.' : null,
    createdByUserId, createdAt: '2026-10-01T11:00:00Z', approvedByUserId: null, approvedAt: null,
    lines: [],
  };
}

function run(id: string, status: string, createdByUserId: string) {
  return {
    id, engineVersion: '1.0', runHash: hash64, signedTotal: '0.000000', status,
    createdByUserId, createdAt: '2026-10-01T12:00:00Z', approvedByUserId: null, approvedAt: null,
  };
}

function consolidationComponent(id: string, clientId: string) {
  return {
    id, clientId, clientLegalName: `Synthetic entity ${clientId.slice(0, 4)}`, sourceType: 'Package',
    packageId: id, externalComponentPackId: null, packageHash: hash64,
    periodBasis: '2026-01-01..2026-12-31', taxonomyVersion: 'IFRS-2026', mappingVersion: '1', currency: 'QAR',
    ownershipPercent: '100.000000', controlMethod: 'CONTROLLED', status: 'APPROVED',
    submittedByUserId: id2, submittedAt: '2026-10-01T10:00:00Z', approvedByUserId: id3,
    approvedAt: '2026-10-01T10:30:00Z', canApprove: false, approvalBlocker: null,
  };
}

describe('Consolidation Scope Workspace Contracts', () => {
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let workspaceData: WritableSignal<ReturnType<typeof decodeScopeWorkspace> | null>;
  let api: { resource: ReturnType<typeof vi.fn>; command: ReturnType<typeof vi.fn> };
  let resource: {
    data: WritableSignal<ScopeWorkspace | null>;
    loading: WritableSignal<boolean>;
    error: WritableSignal<string>;
    reload: ReturnType<typeof vi.fn>;
  };
  let intercompanyOptionsData: WritableSignal<ReturnType<typeof decodeIntercompanyMatchOptions> | null>;
  let intercompanyMatchesData: WritableSignal<ReturnType<typeof decodeIntercompanyExceptionsPage> | null>;
  let intercompanyOptionsResource: {
    data: WritableSignal<ReturnType<typeof decodeIntercompanyMatchOptions> | null>;
    loading: WritableSignal<boolean>;
    error: WritableSignal<string>;
    reload: ReturnType<typeof vi.fn>;
  };
  let intercompanyMatchesResource: {
    data: WritableSignal<ReturnType<typeof decodeIntercompanyExceptionsPage> | null>;
    loading: WritableSignal<boolean>;
    error: WritableSignal<string>;
    reload: ReturnType<typeof vi.fn>;
  };

  beforeEach(() => {
    params = new BehaviorSubject(convertToParamMap({ id: id1 }));
    workspaceData = signal<ReturnType<typeof decodeScopeWorkspace> | null>(null);
    resource = { data: workspaceData, loading: signal(false), error: signal(''), reload: vi.fn() };
    intercompanyOptionsData = signal<ReturnType<typeof decodeIntercompanyMatchOptions> | null>(null);
    intercompanyMatchesData = signal(decodeIntercompanyExceptionsPage(intercompanyPagePayload([]), 'root'));
    intercompanyOptionsResource = { data: intercompanyOptionsData, loading: signal(false), error: signal(''), reload: vi.fn() };
    intercompanyMatchesResource = { data: intercompanyMatchesData, loading: signal(false), error: signal(''), reload: vi.fn() };
    let resourceIndex = 0;
    api = {
      resource: vi.fn((_url, decoder) => {
        resourceIndex++;
        if (resourceIndex === 1 || decoder === decodeScopeWorkspace) return resource;
        return resourceIndex === 2 ? intercompanyOptionsResource : resourceIndex === 3 ? intercompanyMatchesResource
          : { data: signal(null), loading: signal(false), error: signal(''), reload: vi.fn() };
      }),
      command: vi.fn().mockResolvedValue({ ok: true, value: true }),
    };
    TestBed.configureTestingModule({
      imports: [ConsolidationScopeWorkspace],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap: params } },
        { provide: Api, useValue: api },
        { provide: SessionService, useValue: { invalidation: signal(0), current: signal({ userId: actor, firmId: id2, generation: '1', staff: true }) } },
      ],
    });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
    vi.restoreAllMocks();
  });

  function beginIntercompanyOptionsRefresh() {
    intercompanyOptionsData.set(null);
    intercompanyOptionsResource.error.set('');
    intercompanyOptionsResource.loading.set(true);
  }

  function publishCurrentIntercompanyOptions(raw: unknown) {
    const decoder = api.resource.mock.calls[1][1] as (value: unknown, path: string) => ReturnType<typeof decodeIntercompanyMatchOptions>;
    const options = decoder(raw, 'root');
    intercompanyOptionsData.set(options);
    intercompanyOptionsResource.error.set('');
    intercompanyOptionsResource.loading.set(false);
    return options;
  }

  function open(raw: unknown = workspacePayload()) {
    workspaceData.set(decodeScopeWorkspace(raw, 'root'));
    const fixture = TestBed.createComponent(ConsolidationScopeWorkspace);
    fixture.detectChanges();
    publishCurrentIntercompanyOptions(intercompanyOptionsPayload());
    fixture.detectChanges();
    return { fixture, component: fixture.componentInstance };
  }

  it('opens external pack history only for the active scope and clears it on a route change', () => {
    const { fixture, component } = open();
    expect(api.resource.mock.calls.filter(([url]) => (url as () => string | null)()?.includes('?eligiblePackagesPage='))).toHaveLength(1);
    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('audit-consolidation-external-packs')).toBeNull();
    const firstChildCall = api.resource.mock.calls.length;
    api.resource.mockImplementation(() => ({
      data: signal(null), loading: signal(false), error: signal(''), reload: vi.fn(),
    }));
    const openButton = Array.from(page.querySelectorAll('button'))
      .find(button => button.textContent?.trim() === 'Open external component packs')!;
    openButton.click();
    fixture.detectChanges();
    expect(page.querySelector('audit-consolidation-external-packs')).not.toBeNull();
    expect(openButton.getAttribute('aria-expanded')).toBe('true');
    const urls = api.resource.mock.calls.slice(firstChildCall).map(([url]) => url());
    expect(urls.some(url => url?.startsWith(`${scopeUrl}/external-packs?`))).toBe(true);

    const oldWorkspace = component.ws.data()!;
    component.intercompanyForm.sellerClientId = id2;
    component.intercompanyForm.buyerClientId = id3;
    component.intercompanyForm.sellerAmount = '1200.000000';
    component.intercompanyForm.reviewed = true;
    component.journalNumber = 'EJ-OLD-SCOPE';
    component.journalEvidence = 'OLD-SCOPE-EVIDENCE';
    component.journalLines.set([{ taxonomyCode: 'OLD', debit: '1', credit: '1', description: 'old scope' }]);
    params.next(convertToParamMap({ id: id2 }));
    fixture.detectChanges();
    expect(component.intercompanyForm.sellerClientId).toBe('');
    expect(component.intercompanyForm.buyerClientId).toBe('');
    expect(component.intercompanyForm.sellerAmount).toBe('');
    expect(component.intercompanyForm.reviewed).toBe(false);
    expect(component.journalNumber).toBe('');
    expect(component.journalEvidence).toBe('');
    expect(component.journalLines()).toHaveLength(2);
    expect(page.querySelector('audit-consolidation-external-packs')).toBeNull();
    component.toggleExternalPacks(oldWorkspace);
    fixture.detectChanges();
    expect(page.querySelector('audit-consolidation-external-packs')).toBeNull();
  });

  it('decodes a complete scope workspace structure', () => {
    const raw = {
      scope: {
        id: id1,
        groupId: id2,
        groupName: 'Acme Holdings Group',
        groupCode: 'ACME-GRP',
        version: 1,
        groupRevision: 1,
        periodId: id3,
        reportingCurrency: 'QAR',
        method: 'PROPORTIONATE',
        status: 'APPROVED',
        openingBasis: 'OPENING_BALANCE',
        isAdvanced: false,
      },
      canPrepare: true,
      canReview: true,
      canApproveScope: false,
      scopeApprovalBlocker: null,
      currentUserId: id1,
      componentPaging: { totalCount: 1, page: 1, pageSize: 100 },
      readinessPaging: { totalCount: 1, page: 1, pageSize: 100 },
      resolvedComponent: null,
      components: [
        {
          id: id2,
          clientId: id3,
          clientLegalName: 'Acme Subsidiary QSTP-LLC',
          sourceType: 'Package',
          packageId: id1,
          externalComponentPackId: null,
          packageHash: hash64,
          periodBasis: '2026-01-01..2026-12-31',
          taxonomyVersion: 'IFRS-2026',
          mappingVersion: '1',
          currency: 'QAR',
          ownershipPercent: '100',
          controlMethod: 'CONTROLLED',
          status: 'APPROVED',
          submittedByUserId: id2,
          submittedAt: '2026-10-01T10:00:00Z',
          approvedByUserId: id1,
          approvedAt: '2026-10-01T10:30:00Z',
          canApprove: false,
          approvalBlocker: null,
        },
      ],
      eligiblePackages: {
        items: [{
          packageId: id1,
          clientId: id3,
          clientLegalName: 'Acme Subsidiary QSTP-LLC',
          engagementId: id2,
          periodBasis: '2026-01-01..2026-12-31',
          taxonomyVersion: 'IFRS-2026',
          mappingVersion: '1',
          currency: 'QAR',
          calculationHash: hash64,
        }], totalCount: 1, page: 1, pageSize: 100,
      },
      journals: {
        items: [{
          id: id1,
          journalNumber: 'EJ-001',
          journalType: 'INTERCOMPANY_ELIMINATION',
          currency: 'QAR',
          totalDebits: '1500.00',
          totalCreditsAbs: '1500.00',
          evidenceReference: 'IC-RECON-01',
          status: 'SUBMITTED',
          returnReason: null,
          createdByUserId: id2,
          createdAt: '2026-10-01T11:00:00Z',
          approvedByUserId: null,
          approvedAt: null,
          lines: [
            {
              id: id1,
              taxonomyCode: 'BS-100',
              debit: '1500.00',
              credit: '0.00',
              description: 'Eliminate IC AR',
              intercompanyMatchId: null,
            },
            {
              id: id2,
              taxonomyCode: 'BS-200',
              debit: '0.00',
              credit: '1500.00',
              description: 'Eliminate IC AP',
              intercompanyMatchId: null,
            },
          ],
        }], totalCount: 1, page: 1, pageSize: 100,
      },
      runs: {
        items: [{
          id: id1,
          engineVersion: '1.0',
          runHash: hash64,
          signedTotal: '0.00',
          status: 'APPROVED',
          createdByUserId: id2,
          createdAt: '2026-10-01T12:00:00Z',
          approvedByUserId: id1,
          approvedAt: '2026-10-01T12:30:00Z',
        }], totalCount: 1, page: 1, pageSize: 100,
      },
      latestReport: { available: true, errorCode: null, message: null, value: {
        scopeVersionId: id1,
        runId: id1,
        state: 'CURRENT_APPROVED',
        approvedAt: '2026-10-01T12:30:00Z',
        lines: [
          {
            component: 'Sub 1',
            taxonomyCode: 'BS-100',
            componentAmount: '1500.00',
            alignmentAmount: '0.00',
            eliminationAmount: '-1500.00',
            consolidatedAmount: '0.00',
            currency: 'QAR',
          },
        ],
      } },
      componentReadiness: { available: true, errorCode: null, message: null, value: [
        {
          clientId: id3,
          memberName: 'Acme Subsidiary QSTP-LLC',
          componentState: 'APPROVED',
          componentId: id2,
          currency: 'QAR',
          periodBasis: '2026-01-01..2026-12-31',
          taxonomyVersion: 'IFRS-2026',
          mappingVersion: '1',
          currencyCompatible: true,
          mismatchReason: null,
        },
      ] },
      intercompanyExceptions: { available: true, errorCode: null, message: null, value: {
        scopeVersionId: id1, groupId: id2, pinnedGroupRevision: 1, currentGroupRevision: 1,
        items: [], totalCount: 0, page: 1, pageSize: 100,
        totalUnmatchedDifference: '0.000000', totalGrossUnmatchedExposure: '0.000000',
      } },
    };

    const decoded = decodeScopeWorkspace(raw, 'root');
    expect(decoded.scope.groupCode).toBe('ACME-GRP');
    expect(decoded.canApproveScope).toBe(false);
    expect(decoded.scopeApprovalBlocker).toBeNull();
    expect(decoded.components.length).toBe(1);
    expect(() => decodeScopeWorkspace({ ...raw, componentPaging: { totalCount: 2, page: 1, pageSize: 100 } }, 'root')).toThrow();
    expect(() => decodeScopeWorkspace({ ...raw, componentPaging: { totalCount: 1, page: 1, pageSize: 101 } }, 'root')).toThrow();
    expect(() => decodeScopeWorkspace({ ...raw, readinessPaging: null }, 'root')).toThrow();
    const refusedReadiness = { available: false, value: null, errorCode: 'readiness.denied', message: 'Readiness refused.' };
    expect(() => decodeScopeWorkspace({ ...raw, componentReadiness: refusedReadiness }, 'root')).toThrow();
    expect(decodeScopeWorkspace({ ...raw, componentReadiness: refusedReadiness, readinessPaging: null }, 'root').componentReadiness.available).toBe(false);
    expect(decoded.components[0].clientLegalName).toBe('Acme Subsidiary QSTP-LLC');
    expect(decoded.components[0].canApprove).toBe(false);
    expect(decoded.components[0].approvalBlocker).toBeNull();
    expect(decoded.eligiblePackages.items.length).toBe(1);
    expect(decoded.journals.items.length).toBe(1);
    expect(decoded.journals.items[0].lines.length).toBe(2);
    expect(decoded.journals.items[0].totalDebits).toBe('1500.00');
    expect(decoded.runs.items.length).toBe(1);
    expect(decoded.latestReport.value?.lines[0].consolidatedAmount).toBe('0.00');
    expect(() => decodeScopeWorkspace({ ...raw, scope: { ...raw.scope, status: 'Draft' } }, 'root')).toThrow();
    expect(() => decodeScopeWorkspace({ ...raw, canApproveScope: undefined }, 'root')).toThrow();
    expect(() => decodeScopeWorkspace({ ...raw, canApproveScope: 'true' }, 'root')).toThrow();
    const workspaceWithoutApprovalBlocker: Record<string, unknown> = { ...raw };
    delete workspaceWithoutApprovalBlocker['scopeApprovalBlocker'];
    expect(() => decodeScopeWorkspace(workspaceWithoutApprovalBlocker, 'root')).toThrow();
    expect(() => decodeScopeWorkspace({ ...raw, scopeApprovalBlocker: undefined }, 'root')).toThrow();
    expect(() => decodeScopeWorkspace({ ...raw, scopeApprovalBlocker: 1 }, 'root')).toThrow();
    expect(() => decodeScopeWorkspace({ ...raw, canApproveScope: true, scopeApprovalBlocker: 'blocked' }, 'root')).toThrow();
    expect(() => decodeScopeWorkspace({ ...raw, scope: { ...raw.scope, status: 'DRAFT' }, canReview: true,
      canApproveScope: false, scopeApprovalBlocker: null }, 'root')).toThrow();
    expect(() => decodeScopeWorkspace({ ...raw, components: [{ ...raw.components[0], canApprove: undefined }] }, 'root')).toThrow();
    expect(() => decodeScopeWorkspace({ ...raw, components: [{ ...raw.components[0], canApprove: 'true' }] }, 'root')).toThrow();
    const componentWithoutBlocker: Record<string, unknown> = { ...raw.components[0] };
    delete componentWithoutBlocker['approvalBlocker'];
    expect(() => decodeScopeWorkspace({ ...raw, components: [componentWithoutBlocker] }, 'root')).toThrow();
    expect(() => decodeScopeWorkspace({ ...raw, components: [{ ...raw.components[0], approvalBlocker: undefined }] }, 'root')).toThrow();
    expect(() => decodeScopeWorkspace({ ...raw, components: [{ ...raw.components[0], canApprove: true, approvalBlocker: 'blocked' }] }, 'root')).toThrow();
    expect(() => decodeScopeWorkspace({ ...raw, components: [{ ...raw.components[0], status: 'SUBMITTED', submittedByUserId: id2,
      canApprove: false, approvalBlocker: null }] }, 'root')).toThrow();
    expect(() => decodeScopeWorkspace({ ...raw, journals: { ...raw.journals, items: [{ ...raw.journals.items[0], status: 'Submitted' }] } }, 'root')).toThrow();
    expect(() => decodeScopeWorkspace({ ...raw, runs: { ...raw.runs, items: [{ ...raw.runs.items[0], status: 'VERIFIED' }] } }, 'root')).toThrow();
    expect(() => decodeScopeWorkspace({ ...raw, componentReadiness: { ...raw.componentReadiness, value: [{ ...raw.componentReadiness.value[0], componentState: 'READY' }] } }, 'root')).toThrow();
  });

  it('rejects invalid or missing identifiers and malformed decoders', () => {
    expect(() =>
      decodeScopeSummary(
        {
          id: 'bad-uuid',
          groupId: id2,
          groupName: 'Group',
          groupCode: 'GRP',
          version: 1,
          groupRevision: 1,
          periodId: id3,
          reportingCurrency: 'QAR',
          method: 'METHOD',
          status: 'DRAFT',
          openingBasis: 'BASIS',
          isAdvanced: false,
        },
        'root',
      ),
    ).toThrow();

    expect(() =>
      decodeConsolidationJournal(
        {
          id: id1,
          journalNumber: 'EJ-001',
          journalType: 'IC',
          currency: 'QAR',
          totalDebits: 'not-a-decimal',
          totalCreditsAbs: '1500.00',
          evidenceReference: 'REF',
          status: 'SUBMITTED',
          returnReason: null,
          createdByUserId: id2,
          createdAt: '2026-10-01T11:00:00Z',
          approvedByUserId: null,
          approvedAt: null,
          lines: [],
        },
        'root',
      ),
    ).toThrow();
  });

  it('renders and dispatches perimeter approval for the actual DRAFT state', () => {
    const draftScope = workspacePayload().scope;
    const blocker = 'The perimeter has unresolved scope changes.';
    const blocked = workspacePayload({
      scope: { ...draftScope, status: 'DRAFT' },
      canApproveScope: false,
      scopeApprovalBlocker: blocker,
    });
    const { fixture, component } = open(blocked);
    const blockedButton = [...fixture.nativeElement.querySelectorAll('button')]
      .find((item: HTMLButtonElement) => item.textContent?.includes('Approve perimeter')) as HTMLButtonElement;
    expect(blockedButton.disabled).toBe(true);
    expect(blockedButton.getAttribute('aria-describedby')).toBe('scope-approval-blocker');
    expect(fixture.nativeElement.querySelector('#scope-approval-blocker')?.textContent).toContain(blocker);
    component.approveScope(id1);
    expect(api.command).not.toHaveBeenCalled();

    const payload = workspacePayload({
      scope: { ...draftScope, status: 'DRAFT' },
      canApproveScope: true,
      scopeApprovalBlocker: null,
    });
    workspaceData.set(decodeScopeWorkspace(payload, 'root'));
    fixture.detectChanges();
    const disclosure = fixture.nativeElement.querySelector('#journals summary') as HTMLElement;
    expect(disclosure.textContent).toContain('Create elimination journal');
    expect(disclosure.querySelector('[role="button"]')).toBeNull();
    const journalTables = [...fixture.nativeElement.querySelectorAll('#journals table')] as HTMLTableElement[];
    expect(journalTables[0].getAttribute('aria-label')).toBe('Draft elimination journal lines');
    expect(journalTables[journalTables.length - 1].getAttribute('aria-labelledby')).toBe('journals-heading');
    const button = [...fixture.nativeElement.querySelectorAll('button')]
      .find((item: HTMLButtonElement) => item.textContent?.includes('Approve perimeter')) as HTMLButtonElement;
    expect(button).toBeTruthy();
    expect(button.disabled).toBe(false);
    expect(button.hasAttribute('aria-describedby')).toBe(false);
    component.approveScope(id2);
    expect(api.command).not.toHaveBeenCalled();
    button.click();
    expect(api.command).toHaveBeenCalledWith(`${scopeUrl}/approve`, {}, 'POST');
  });

  it('gives every elimination journal line control a distinct accessible name', () => {
    const { fixture, component } = open();
    component.journalLines.set([
      { taxonomyCode: 'ASSETS.IC_AR', debit: '10.000000', credit: '0.000000', description: 'Receivable' },
      { taxonomyCode: 'LIABILITIES.IC_AP', debit: '0.000000', credit: '10.000000', description: 'Payable' },
      { taxonomyCode: 'EQUITY.RETAINED', debit: '0.000000', credit: '0.000000', description: 'Adjustment' },
    ]);
    fixture.detectChanges();

    const lineInputs = [...fixture.nativeElement.querySelectorAll(
      'table[aria-label="Draft elimination journal lines"] input',
    )] as HTMLInputElement[];
    expect(lineInputs.map(input => input.getAttribute('aria-label'))).toEqual([
      'Journal line 1 taxonomy code',
      'Journal line 1 debit amount',
      'Journal line 1 credit amount',
      'Journal line 1 description',
      'Journal line 2 taxonomy code',
      'Journal line 2 debit amount',
      'Journal line 2 credit amount',
      'Journal line 2 description',
      'Journal line 3 taxonomy code',
      'Journal line 3 debit amount',
      'Journal line 3 credit amount',
      'Journal line 3 description',
    ]);
    const removeButtons = [...fixture.nativeElement.querySelectorAll(
      'table[aria-label="Draft elimination journal lines"] button',
    )] as HTMLButtonElement[];
    expect(removeButtons.map(button => button.getAttribute('aria-label'))).toEqual([
      'Remove journal line 1',
      'Remove journal line 2',
      'Remove journal line 3',
    ]);
  });

  it('does not publish a delayed command result or reload after the scope owner changes or the host is destroyed', async () => {
    let finishFirst!: (result: { ok: true; value: unknown }) => void;
    api.command.mockImplementationOnce(() => new Promise(resolve => { finishFirst = resolve; }));
    const { fixture, component } = open(workspacePayload({
      scope: { ...workspacePayload().scope, status: 'DRAFT' },
      canApproveScope: true,
      scopeApprovalBlocker: null,
    }));
    component.approveScope(id1);
    await vi.waitFor(() => expect(finishFirst).toBeTypeOf('function'));
    params.next(convertToParamMap({ id: id2 }));
    fixture.detectChanges();
    finishFirst({ ok: true, value: true });
    await vi.waitFor(() => expect(component.cmd.busy()).toBe(false));
    expect(resource.reload).not.toHaveBeenCalled();
    expect(component.cmd.message()).toBe('');

    let finishDestroyed!: (result: { ok: true; value: unknown }) => void;
    api.command.mockImplementationOnce(() => new Promise(resolve => { finishDestroyed = resolve; }));
    workspaceData.set(decodeScopeWorkspace(workspacePayload({
      scope: { ...workspacePayload().scope, id: id2, status: 'DRAFT' },
      canApproveScope: true,
      scopeApprovalBlocker: null,
    }), 'root'));
    fixture.detectChanges();
    component.approveScope(id2);
    await vi.waitFor(() => expect(finishDestroyed).toBeTypeOf('function'));
    fixture.destroy();
    finishDestroyed({ ok: true, value: true });
    await vi.waitFor(() => expect(component.cmd.busy()).toBe(false));
    expect(resource.reload).not.toHaveBeenCalled();
    expect(component.cmd.message()).toBe('');
  });

  it('renders submitted-journal review, denies same-maker approval, and dispatches Returned resubmission', async () => {
    const payload = workspacePayload({ journals: [
      journal(id2, 'EJ-OTHER', 'SUBMITTED', id2),
      journal(id3, 'EJ-MAKER', 'SUBMITTED', actor),
      journal('44444444-4444-4444-8444-444444444444', 'EJ-RETURNED', 'Returned', id2),
    ] });
    const { fixture } = open(payload);
    const tables = fixture.nativeElement.querySelectorAll('#journals table');
    const rows = [...tables[tables.length - 1].querySelectorAll('tbody tr')] as HTMLTableRowElement[];
    const buttonTexts = (row: HTMLTableRowElement) => [...row.querySelectorAll('button')].map(x => x.textContent?.trim());
    expect(buttonTexts(rows[0])).toContain('Approve');
    expect(rows[1].textContent).toContain('Maker/checker');
    expect(buttonTexts(rows[1])).not.toContain('Approve');
    expect(buttonTexts(rows[2])).toContain('Resubmit');
    (rows[0].querySelector('button') as HTMLButtonElement).click();
    expect(api.command).toHaveBeenCalledWith(`/api/ui/consolidation/journals/${id2}/approve`, {}, 'POST');
    await vi.waitFor(() => expect(fixture.componentInstance.cmd.busy()).toBe(false));
    (rows[2].querySelector('button') as HTMLButtonElement).click();
    expect(api.command).toHaveBeenLastCalledWith(`/api/ui/consolidation/journals/44444444-4444-4444-8444-444444444444/resubmit`, {}, 'POST');
  });

  it('offers run approval only for another preparer’s SUBMITTED run and dispatches it', () => {
    const ownRunId = '44444444-4444-4444-8444-444444444444';
    const otherRunId = '55555555-5555-4555-8555-555555555555';
    const { fixture } = open(workspacePayload({ runs: [
      run(otherRunId, 'SUBMITTED', id2),
      run(ownRunId, 'SUBMITTED', actor),
    ] }));
    const rows = [...fixture.nativeElement.querySelectorAll('#runs tbody tr')] as HTMLTableRowElement[];
    expect([...rows[0].querySelectorAll('button')].map(x => x.textContent?.trim())).toContain('Approve run');
    expect(rows[1].textContent).toContain('Maker/checker');
    expect([...rows[1].querySelectorAll('button')].map(x => x.textContent?.trim())).not.toContain('Approve run');
    (rows[0].querySelector('button') as HTMLButtonElement).click();
    expect(api.command).toHaveBeenCalledWith(`/api/ui/consolidation/runs/${otherRunId}/approve`, {}, 'POST');
  });

  it('keeps component approval disabled with the server blocker and dispatches only ready independent submissions', async () => {
    const blockedId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
    const readyId = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
    const makerId = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';
    const blocker = 'The exact financial package composition is not approved.';
    const blocked = {
      ...consolidationComponent(blockedId, id2), sourceType: 'Package', packageId: id3, status: 'SUBMITTED',
      submittedByUserId: id2, approvedByUserId: null, approvedAt: null, canApprove: false, approvalBlocker: blocker,
    };
    const ready = {
      ...consolidationComponent(readyId, id3), sourceType: 'ExternalPack', packageId: null,
      externalComponentPackId: id2, status: 'SUBMITTED', submittedByUserId: id2,
      approvedByUserId: null, approvedAt: null, canApprove: true, approvalBlocker: null,
    };
    const ownSubmission = {
      ...consolidationComponent(makerId, id3), status: 'SUBMITTED', submittedByUserId: actor,
      approvedByUserId: null, approvedAt: null, canApprove: true, approvalBlocker: null,
    };
    const { fixture, component } = open(workspacePayload({ components: [blocked, ready, ownSubmission] }));
    const rows = [...fixture.nativeElement.querySelectorAll('#components tbody tr')] as HTMLTableRowElement[];
    expect(rows).toHaveLength(3);
    const [blockedRow, readyRow, makerRow] = rows;
    const blockedButton = [...blockedRow.querySelectorAll('button')]
      .find(button => button.textContent?.trim() === 'Approve') as HTMLButtonElement;
    expect(blockedRow.textContent).toContain(blocker);
    expect(blockedButton.disabled).toBe(true);
    expect(blockedButton.getAttribute('aria-describedby')).toBe(`component-approval-blocker-${blockedId}`);
    component.approveComponent(blockedId);
    component.approveComponent(makerId);
    component.approveComponent('dddddddd-dddd-4ddd-8ddd-dddddddddddd');
    expect(api.command).not.toHaveBeenCalled();

    const readyButton = [...readyRow.querySelectorAll('button')]
      .find(button => button.textContent?.trim() === 'Approve') as HTMLButtonElement;
    expect(readyButton.disabled).toBe(false);
    readyButton.click();
    expect(api.command).toHaveBeenCalledWith(`/api/ui/consolidation/components/${readyId}/approve`, {}, 'POST');
    await vi.waitFor(() => expect(component.cmd.busy()).toBe(false));
    expect(makerRow.textContent).toContain('Maker/checker');
    expect([...makerRow.querySelectorAll('button')].map(button => button.textContent?.trim())).not.toContain('Approve');
  });

  it('keeps ownership points and journal amounts as exact decimal strings on dispatch', async () => {
    expect(formatOwnershipPercent('100')).toBe('100%');
    expect(formatOwnershipPercent('50.250000')).toBe('50.25%');

    const packageId = '44444444-4444-4444-8444-444444444444';
    const draftScope = workspacePayload().scope;
    const payload = workspacePayload({
      scope: { ...draftScope, status: 'DRAFT' },
      canApproveScope: false,
      scopeApprovalBlocker: 'Scope approval is blocked in this fixture.',
      eligiblePackages: [{ packageId, clientId: id2, clientLegalName: 'Synthetic Component', engagementId: id3,
        periodBasis: '2026-01-01..2026-12-31', taxonomyVersion: 'IFRS-2026', mappingVersion: '1', currency: 'QAR', calculationHash: hash64 }],
    });
    const { component } = open(payload);
    component.selectedPackageId = packageId;
    component.ownershipPercent = '100';
    component.submitComponent(component.ws.data()!);
    expect(api.command).toHaveBeenCalledWith(`${scopeUrl}/components`, expect.objectContaining({ ownershipPercent: '100' }), 'POST');
    await vi.waitFor(() => expect(component.cmd.busy()).toBe(false));

    component.journalLines.set([
      { taxonomyCode: 'BS-100', debit: '9007199254.740993', credit: '0', description: 'Exact debit' },
      { taxonomyCode: 'BS-200', debit: '0', credit: '9007199254.740993', description: 'Exact credit' },
    ]);
    expect(component.totalDebits()).toBe('9007199254.740993');
    expect(component.totalCredits()).toBe('9007199254.740993');
    expect(component.isJournalBalanced()).toBe(true);
    component.journalNumber = 'EJ-EXACT';
    component.journalEvidence = 'SYN-EXACT';
    component.createJournal(id1);
    expect(api.command).toHaveBeenLastCalledWith(`${scopeUrl}/journals`, expect.objectContaining({
      lines: [
        { intercompanyMatchId: null, taxonomyCode: 'BS-100', debit: '9007199254.740993', credit: '0', description: 'Exact debit' },
        { intercompanyMatchId: null, taxonomyCode: 'BS-200', debit: '0', credit: '9007199254.740993', description: 'Exact credit' },
      ],
    }), 'POST');
  });

  it('rejects sub-cent imbalance and malformed or out-of-range amounts without dispatch', () => {
    const { component, fixture } = open();
    component.journalLines.set([
      { taxonomyCode: 'BS-100', debit: '0.000001', credit: '0', description: '' },
      { taxonomyCode: 'BS-200', debit: '0', credit: '0', description: '' },
    ]);
    expect(component.isJournalBalanced()).toBe(false);
    expect(component.journalDifference()).toBe('0.000001');
    component.createJournal(id1);
    expect(api.command).not.toHaveBeenCalled();

    component.journalLines.set([
      { taxonomyCode: 'BS-100', debit: '1e3', credit: '0', description: '' },
      { taxonomyCode: 'BS-200', debit: '0', credit: '1000', description: '' },
    ]);
    fixture.detectChanges();
    expect(component.journalAmountsValid()).toBe(false);
    expect(fixture.nativeElement.textContent).toContain('within numeric(19,6)');
    component.createJournal(id1);
    expect(api.command).not.toHaveBeenCalled();

    component.journalLines.set([
      { taxonomyCode: 'BS-100', debit: '10000000000000', credit: '0', description: '' },
      { taxonomyCode: 'BS-200', debit: '0', credit: '10000000000000', description: '' },
    ]);
    expect(component.journalAmountsValid()).toBe(false);
    component.createJournal(id1);
    expect(api.command).not.toHaveBeenCalled();
  });

  it('retains approved match history and requires an independent current-revision review before approval', async () => {
    intercompanyMatchesData.set(decodeIntercompanyExceptionsPage(intercompanyPagePayload([
      intercompanyMatch(matchId1, 'APPROVED', id2, id3),
      intercompanyMatch(matchId2, 'SUBMITTED', id2),
      intercompanyMatch(matchId3, 'SUBMITTED', actor),
    ]), 'root'));
    const { fixture, component } = open();
    expect(component.intercompanyUnresolvedOnly()).toBe(false);
    const pageUrl = api.resource.mock.calls[2][0] as () => string | null;
    expect(pageUrl()).toContain('unresolvedOnly=false');

    const rows = [...fixture.nativeElement.querySelectorAll('#intercompany tbody tr')] as HTMLTableRowElement[];
    expect(rows).toHaveLength(3);
    expect(rows[0].textContent).toContain('approved');
    expect(rows[0].textContent).toContain('1200.123456');
    expect(rows[0].textContent).toContain(id3);
    expect(rows[1].textContent).toContain('I reviewed the full retained match');
    expect([...rows[1].querySelectorAll('button')].map((button) => button.textContent?.trim())).toContain('Approve match');
    expect(rows[2].textContent).toContain('Maker/checker');
    expect([...rows[2].querySelectorAll('button')].map((button) => button.textContent?.trim())).not.toContain('Approve match');

    const review = rows[1].querySelector('input[type="checkbox"]') as HTMLInputElement;
    review.checked = true;
    review.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    (rows[1].querySelector('button') as HTMLButtonElement).click();
    expect(api.command).toHaveBeenCalledWith(`/api/ui/consolidation/intercompany-matches/${matchId2}/approve`,
      { expectedGroupRevision: 1, reviewed: true }, 'POST');
    await vi.waitFor(() => expect(component.cmd.busy()).toBe(false));

    component.showIntercompanyExceptions();
    expect(component.intercompanyUnresolvedOnly()).toBe(true);
    expect(pageUrl()).toContain('unresolvedOnly=true');
  });

  it('continues bounded histories and the 101st exception while surfacing refused subquery codes', async () => {
    const secondJournalId = '88888888-8888-4888-8888-888888888888';
    const thirdJournalId = '99999999-9999-4999-8999-999999999999';
    const exception101Id = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
    const pinned = consolidationComponent(matchId1, id2);
    const initial = workspacePayload({
      components: [pinned],
      componentPaging: { totalCount: 2, page: 1, pageSize: 1 },
      componentReadiness: [{ clientId: id2, memberName: 'Synthetic member', componentState: 'APPROVED', componentId: pinned.id, currency: 'QAR', periodBasis: 'ANNUAL', taxonomyVersion: 'v1', mappingVersion: 'v1', currencyCompatible: true, mismatchReason: null }],
      readinessPaging: { totalCount: 2, page: 1, pageSize: 1 },
      scope: { ...workspacePayload().scope, status: 'DRAFT' },
      canApproveScope: false,
      scopeApprovalBlocker: 'Scope approval is blocked in this fixture.',
      eligiblePackages: { items: [], totalCount: 501, page: 1, pageSize: 100 },
      journals: { items: [], totalCount: 601, page: 1, pageSize: 100 },
      runs: { items: [], totalCount: 701, page: 1, pageSize: 100 },
      latestReport: { available: false, value: null, errorCode: 'report.read-denied', message: 'Report read was refused.' },
      intercompanyExceptions: { available: false, value: null, errorCode: 'match.read-denied', message: 'Exception read was refused.' },
    });
    intercompanyMatchesData.set(decodeIntercompanyExceptionsPage(
      intercompanyPagePayload([], 1, 1, 101), 'root'));
    const { fixture, component } = open(initial);
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('report.read-denied');
    expect(fixture.nativeElement.textContent).toContain('Report read was refused.');
    expect(fixture.nativeElement.textContent).toContain('match.read-denied');
    const workspaceUrl = api.resource.mock.calls[0][0] as () => string | null;
    const pageUrl = api.resource.mock.calls[2][0] as () => string | null;
    expect(workspaceUrl()).toContain('eligiblePackagesPage=1&journalsPage=1&runsPage=1');
    expect(pageUrl()).toContain('page=1&pageSize=50');
    expect(fixture.nativeElement.querySelector('[aria-label="Component pages"]')?.textContent).toContain('2 components');
    (fixture.nativeElement.querySelector('[aria-label="Component pages"] button:last-child') as HTMLButtonElement).click();
    expect(component.componentsPageNumber()).toBe(2);
    expect(component.readinessPageNumber()).toBe(1);
    (fixture.nativeElement.querySelector('[aria-label="Readiness pages"] button:last-child') as HTMLButtonElement).click();
    expect(component.readinessPageNumber()).toBe(2);
    expect(workspaceUrl()).toContain('componentsPage=2&readinessPage=2');


    const nextException = fixture.nativeElement.querySelector('[aria-label="Intercompany match pages"] button:last-child') as HTMLButtonElement;
    nextException.click();
    await fixture.whenStable();
    expect(component.intercompanyPageNumber()).toBe(2);
    expect(pageUrl()).toContain('page=2&pageSize=50');
    intercompanyMatchesData.set(decodeIntercompanyExceptionsPage(
      intercompanyPagePayload([], 1, 2, 101), 'root'));
    await fixture.whenStable();
    (fixture.nativeElement.querySelector('[aria-label="Intercompany match pages"] button:last-child') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(component.intercompanyPageNumber()).toBe(3);
    expect(pageUrl()).toContain('page=3&pageSize=50');

    intercompanyMatchesData.set(decodeIntercompanyExceptionsPage(
      intercompanyPagePayload([intercompanyMatch(exception101Id, 'APPROVED', id2, id3)], 1, 3, 101), 'root'));
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('#intercompany')?.textContent).toContain(exception101Id);

    const workspace = component.ws.data()!;
    component.nextEligiblePackagesPage(workspace.eligiblePackages);
    component.nextJournalsPage(workspace.journals);
    component.nextRunsPage(workspace.runs);
    await fixture.whenStable();
    expect(workspaceUrl()).toContain('eligiblePackagesPage=2&journalsPage=2&runsPage=2');

    workspaceData.set(decodeScopeWorkspace(workspacePayload({
      scope: { ...workspace.scope, status: 'DRAFT' },
      canApproveScope: false,
      scopeApprovalBlocker: 'Scope approval is blocked in this fixture.',
      eligiblePackages: { items: [], totalCount: 501, page: 2, pageSize: 100 },
      journals: { items: [journal(secondJournalId, 'EJ-PAGE-2', 'SUBMITTED', id2)], totalCount: 601, page: 2, pageSize: 100 },
      runs: { items: [run(thirdJournalId, 'SUBMITTED', id2)], totalCount: 701, page: 2, pageSize: 100 },
    }), 'root'));
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('EJ-PAGE-2');
    expect(fixture.nativeElement.querySelectorAll('#runs tbody tr')).toHaveLength(1);
    expect(component.eligiblePackagesPageNumber()).toBe(2);
    expect(component.journalsPageNumber()).toBe(2);
    expect(component.runsPageNumber()).toBe(2);
    const decodeResponse = api.resource.mock.calls[0][1] as (value: unknown, path: string) => ScopeWorkspace;
    expect(() => decodeResponse(initial, 'root')).toThrow();

    params.next(convertToParamMap({ id: id3 }));
    await fixture.whenStable();
    expect(component.eligiblePackagesPageNumber()).toBe(1);
    expect(component.journalsPageNumber()).toBe(1);
    expect(component.runsPageNumber()).toBe(1);
    expect(component.intercompanyPageNumber()).toBe(1);
    expect(component.componentsPageNumber()).toBe(1);
    expect(component.readinessPageNumber()).toBe(1);
    expect(workspaceUrl()).toContain(`/api/ui/consolidation/scopes/${id3}?eligiblePackagesPage=1&journalsPage=1&runsPage=1`);
  });

  it('submits signed intercompany amounts as exact decimal strings against the reviewed revision', async () => {
    const { component } = open();
    component.intercompanyForm = {
      ...component.intercompanyForm,
      sellerClientId: id2,
      buyerClientId: id3,
      accountNature: 'RECEIVABLE_PAYABLE',
      periodCode: '2026-12',
      transactionReference: 'IC-EXACT-01',
      sellerAmount: '9007199254.740993',
      buyerAmount: '-9007199254.740992',
      matchedAmount: '9007199254.740992',
      sellerTaxonomyCode: 'ASSETS.IC_AR',
      buyerTaxonomyCode: 'LIABILITIES.IC_AP',
      evidenceReference: 'SIGNED-IC-EXACT-01',
      differenceReason: 'One micro-unit timing difference.',
      reviewed: true,
    };
    const workspace = component.ws.data()!;
    const options = component.intercompanyMatchOptions.data()!;
    expect(component.canSubmitIntercompanyMatch(workspace, options)).toBe(true);
    component.submitIntercompanyMatch(workspace, options);
    expect(api.command).toHaveBeenCalledWith(`${scopeUrl}/intercompany-matches`, {
      sellerClientId: id2,
      buyerClientId: id3,
      accountNature: 'RECEIVABLE_PAYABLE',
      periodCode: '2026-12',
      currency: 'QAR',
      transactionReference: 'IC-EXACT-01',
      sellerAmount: '9007199254.740993',
      buyerAmount: '-9007199254.740992',
      matchedAmount: '9007199254.740992',
      evidenceReference: 'SIGNED-IC-EXACT-01',
      sellerTaxonomyCode: 'ASSETS.IC_AR',
      buyerTaxonomyCode: 'LIABILITIES.IC_AP',
      differenceReason: 'One micro-unit timing difference.',
      matchMode: 'ONE_TO_ONE',
      matchGroupReference: '',
      outsidePerimeterReview: false,
      expectedGroupRevision: 1,
      reviewed: true,
    }, 'POST');
    await vi.waitFor(() => expect(component.cmd.busy()).toBe(false));
  });

  it('shows stored signed net beside gross residual and requires a reason for partial matches', () => {
    const partial = intercompanyMatch(matchId1, 'APPROVED', id2, id3);
    Object.assign(partial, {
      sellerAmount: '100.000000', buyerAmount: '-100.000000', matchedAmount: '50.000000', difference: '0.000000',
      unmatchedResidual: '100.000000', differenceReason: '',
    });
    const positive = intercompanyMatch(matchId2, 'SUBMITTED', id2);
    Object.assign(positive, {
      sellerAmount: '100.000000', buyerAmount: '-90.000000', matchedAmount: '90.000000', difference: '10.000000',
      unmatchedResidual: '10.000000',
    });
    const negative = intercompanyMatch(matchId3, 'SUBMITTED', id2);
    Object.assign(negative, {
      sellerAmount: '90.000000', buyerAmount: '-100.000000', matchedAmount: '90.000000', difference: '-10.000000',
      unmatchedResidual: '10.000000',
    });
    intercompanyMatchesData.set(decodeIntercompanyExceptionsPage(
      intercompanyPagePayload([partial, positive, negative], 1, 1, 3, '0.000000', '120.000000'), 'root'));
    const { fixture, component } = open();
    const page = fixture.nativeElement as HTMLElement;
    expect(page.textContent).toContain('signed net difference (stored; rows may offset) 0.000000');
    expect(page.textContent).toContain('gross unmatched exposure across 3 matches in this view');
    expect(page.textContent).toContain('Gross unmatched residual on both sides');
    expect(page.textContent).toContain('membership overlaps scope reporting period');
    expect(page.textContent).toContain('2026-01-01');
    expect(page.textContent).toContain('2026-12-31');
    expect(page.textContent).toContain('100.000000');

    component.intercompanyForm = {
      ...component.intercompanyForm,
      sellerClientId: id2, buyerClientId: id3, periodCode: 'FY2026', transactionReference: 'IC-PARTIAL',
      sellerAmount: '100.000000', buyerAmount: '-100.000000', matchedAmount: '50.000000',
      sellerTaxonomyCode: 'ASSETS.IC_AR', buyerTaxonomyCode: 'LIABILITIES.IC_AP',
      evidenceReference: 'SIGNED-IC-PARTIAL', differenceReason: '', reviewed: true,
    };
    const workspace = component.ws.data()!;
    const options = component.intercompanyMatchOptions.data()!;
    expect(component.intercompanyDifferenceReasonRequired()).toBe(true);
    expect(component.canSubmitIntercompanyMatch(workspace, options)).toBe(false);
    component.intercompanyForm.differenceReason = 'The remaining balances are supported by the signed confirmation.';
    expect(component.canSubmitIntercompanyMatch(workspace, options)).toBe(true);

    const missingPeriodOptions = publishCurrentIntercompanyOptions(intercompanyOptionsPayload({
      periodStartDate: null,
      periodEndDate: null,
      membershipBlocker: 'The scope reporting period date is missing or invalid; member eligibility cannot be confirmed.',
      members: [],
    }));
    fixture.detectChanges();
    expect(page.textContent).toContain('member eligibility cannot be confirmed');
    expect(component.canSubmitIntercompanyMatch(workspace, missingPeriodOptions)).toBe(false);
  });

  it('continues member options beyond 500, retains exact selections and rejects mismatched or stale responses', async () => {
    const { fixture, component } = open();
    const url = api.resource.mock.calls[1][0] as () => string;
    const decodeResponse = api.resource.mock.calls[1][1] as typeof decodeIntercompanyMatchOptions;
    component.selectIntercompanyMember('SELLER', id2);
    component.intercompanyMembersPageNumber.set(21);
    const lastPage = intercompanyOptionsPayload({
      members: [{ clientId: id3, legalName: 'Member 501', hasApprovedComponent: true }],
      memberPaging: { totalCount: 501, page: 21, pageSize: 25 },
      resolvedSeller: { clientId: id2, legalName: 'Selected seller', hasApprovedComponent: true },
    });
    beginIntercompanyOptionsRefresh();
    publishCurrentIntercompanyOptions(lastPage);
    await fixture.whenStable();
    expect(url()).toContain('membersPage=21&membersPageSize=25');
    expect(url()).toContain(`selectedSellerClientId=${id2}`);
    expect(fixture.nativeElement.querySelector('[aria-label="Intercompany member pages"]')?.textContent).toContain('501 members');
    expect(component.intercompanyMemberOptions(component.intercompanyMatchOptions.data()!).map(x => x.clientId)).toEqual([id3, id2]);
    component.selectIntercompanyMember('BUYER', id3);
    beginIntercompanyOptionsRefresh();
    const currentMemberOptions = publishCurrentIntercompanyOptions(lastPage);
    component.intercompanyForm = { ...component.intercompanyForm, periodCode: '2026-12', transactionReference: 'IC-501',
      sellerAmount: '1.000000', buyerAmount: '-1.000000', matchedAmount: '1.000000',
      sellerTaxonomyCode: 'ASSETS.IC_AR', buyerTaxonomyCode: 'LIABILITIES.IC_AP', evidenceReference: 'SIGNED-501', reviewed: true };
    expect(component.canSubmitIntercompanyMatch(component.ws.data()!, currentMemberOptions)).toBe(true);
    beginIntercompanyOptionsRefresh();
    expect(component.intercompany.displayOptions()).toBe(currentMemberOptions);
    expect(component.intercompanyForm.reviewed).toBe(true);
    expect(component.intercompany.canReviewAssent(component.ws.data()!, currentMemberOptions)).toBe(false);
    expect(component.canSubmitIntercompanyMatch(component.ws.data()!, currentMemberOptions)).toBe(false);
    intercompanyOptionsData.set(currentMemberOptions);
    intercompanyOptionsResource.loading.set(false);
    intercompanyOptionsResource.error.set('Options verification unavailable.');
    expect(component.canSubmitIntercompanyMatch(component.ws.data()!, currentMemberOptions)).toBe(false);
    const refreshedMemberOptions = publishCurrentIntercompanyOptions(lastPage);
    expect(component.canSubmitIntercompanyMatch(component.ws.data()!, currentMemberOptions)).toBe(false);
    expect(component.canSubmitIntercompanyMatch(component.ws.data()!, refreshedMemberOptions)).toBe(true);
    expect(() => decodeResponse({ ...lastPage, resolvedSeller: { clientId: id1, legalName: 'Wrong identity', hasApprovedComponent: true } }, 'root')).toThrow();
    component.changeIntercompanyOutside('enabled', true);
    component.changeIntercompanyOutside('side', 'BUYER');
    component.changeIntercompanyOutside('clientId', matchId3);
    beginIntercompanyOptionsRefresh();
    expect(component.intercompany.displayOptions()).toBe(refreshedMemberOptions);
    expect(component.intercompanyForm.reviewed).toBe(false);
    expect(component.canSubmitIntercompanyMatch(component.ws.data()!, refreshedMemberOptions)).toBe(false);
    expect(url()).toContain(`selectedBuyerClientId=${matchId3}`);
    const offPageMember = publishCurrentIntercompanyOptions({ ...lastPage,
      resolvedBuyer: { clientId: matchId3, legalName: 'Off-page approved member', hasApprovedComponent: true },
    });
    component.intercompanyForm.reviewed = true;
    expect(component.canSubmitIntercompanyMatch(component.ws.data()!, offPageMember)).toBe(false);
    const confirmedOutside = publishCurrentIntercompanyOptions({ ...lastPage, resolvedBuyer: null });
    component.intercompanyForm.reviewed = true;
    expect(component.canSubmitIntercompanyMatch(component.ws.data()!, confirmedOutside)).toBe(true);
    component.changeIntercompanyOutside('clientId', matchId2);
    beginIntercompanyOptionsRefresh();
    expect(component.intercompanyForm.reviewed).toBe(false);
    expect(component.canSubmitIntercompanyMatch(component.ws.data()!, confirmedOutside)).toBe(false);
    component.changeIntercompanyOutside('side', 'SELLER');
    component.selectIntercompanyMember('BUYER', id3);
    expect(url()).toContain(`selectedSellerClientId=${matchId2}`);
    const sellerOutside = publishCurrentIntercompanyOptions({ ...lastPage, resolvedSeller: null,
      resolvedBuyer: { clientId: id3, legalName: 'Internal buyer', hasApprovedComponent: true },
    });
    component.intercompanyForm.reviewed = true;
    expect(component.canSubmitIntercompanyMatch(component.ws.data()!, sellerOutside)).toBe(true);
    component.changeIntercompanyOutside('enabled', false);
    beginIntercompanyOptionsRefresh();
    expect(url()).toContain(`selectedSellerClientId=${id2}`);
    component.previousIntercompanyMembersPage();
    expect(component.intercompanyForm.reviewed).toBe(false);
    expect(() => decodeResponse(lastPage, 'root')).toThrow();
    component.nextIntercompanyMembersPage({ totalCount: 501, page: 20, pageSize: 25 });
    expect(component.intercompanyMembersPageNumber()).toBe(21);
    component.searchIntercompanyMembers('Member 501');
    expect(component.intercompanyMembersPageNumber()).toBe(1);
    expect(url()).toContain('memberSearch=Member%20501');
    expect(url()).toContain(`selectedBuyerClientId=${id3}`);
    params.next(convertToParamMap({ id: id3 }));
    await fixture.whenStable();
    expect(component.intercompanyMemberSearch()).toBe('');
    expect(component.intercompanyMembersPageNumber()).toBe(1);
    expect(url()).not.toContain('selectedSellerClientId');
    expect(() => decodeResponse(lastPage, 'root')).toThrow();
    expect(() => decodeIntercompanyMatchOptions({ ...lastPage, memberPaging: { totalCount: 501, page: 1, pageSize: 25 } }, 'root')).toThrow();
  });

  it('blocks malformed exact amounts and changed group revisions before dispatch', () => {
    const { component } = open();
    component.intercompanyForm = {
      ...component.intercompanyForm,
      sellerClientId: id2, buyerClientId: id3, periodCode: '2026-12', transactionReference: 'IC-BAD',
      sellerAmount: '1e3', buyerAmount: '-1000', matchedAmount: '1000', sellerTaxonomyCode: 'ASSETS.IC_AR',
      buyerTaxonomyCode: 'LIABILITIES.IC_AP', evidenceReference: 'SIGNED-IC-BAD', reviewed: true,
    };
    expect(component.canSubmitIntercompanyMatch(component.ws.data()!, component.intercompanyMatchOptions.data()!)).toBe(false);
    const staleOptions = publishCurrentIntercompanyOptions(intercompanyOptionsPayload({ currentGroupRevision: 2 }));
    component.intercompanyForm.sellerAmount = '1000.000000';
    component.intercompanyForm.buyerAmount = '-1000.000000';
    component.intercompanyForm.matchedAmount = '1000.000000';
    expect(component.canSubmitIntercompanyMatch(component.ws.data()!, staleOptions)).toBe(false);
    component.submitIntercompanyMatch(component.ws.data()!, staleOptions);
    expect(api.command).not.toHaveBeenCalled();
  });

  it('renders one selected component correction handoff from the current preparable scope and clears selection on scope change', async () => {
    const firstComponent = consolidationComponent(matchId1, id2);
    const secondComponent = consolidationComponent(matchId2, id3);
    const { fixture, component } = open(workspacePayload({ components: [firstComponent, secondComponent] }));
    const rows = [...fixture.nativeElement.querySelectorAll('#components tbody tr')] as HTMLTableRowElement[];
    const selectButtons = rows.map((row) => row.querySelector('button') as HTMLButtonElement);
    expect(selectButtons.map((button) => button.textContent?.trim())).toEqual(['Correct source', 'Correct source']);

    selectButtons[0].click();
    fixture.detectChanges();
    let handoffs = fixture.nativeElement.querySelectorAll('audit-consolidation-component-correction');
    expect(handoffs).toHaveLength(1);
    expect(handoffs[0].textContent).toContain(firstComponent.id);
    expect(handoffs[0].textContent).toContain('Exact original scope and component evidence');

    selectButtons[1].click();
    fixture.detectChanges();
    handoffs = fixture.nativeElement.querySelectorAll('audit-consolidation-component-correction');
    expect(handoffs).toHaveLength(1);
    expect(handoffs[0].textContent).toContain(secondComponent.id);
    expect(handoffs[0].textContent).not.toContain(firstComponent.id);

    params.next(convertToParamMap({ id: id3 }));
    fixture.detectChanges();
    await vi.waitFor(() => expect(component.selectedComponentCorrection()).toBeNull());
    expect(fixture.nativeElement.querySelectorAll('audit-consolidation-component-correction')).toHaveLength(0);
  });

  it('hides component correction selection for users without the current prepare grant', () => {
    const payload = workspacePayload({ canPrepare: false, components: [consolidationComponent(matchId1, id2)] });
    const { fixture, component } = open(payload);
    expect(fixture.nativeElement.querySelectorAll('#components tbody button')).toHaveLength(0);
    component.toggleComponentCorrection(component.ws.data()!, matchId1);
    expect(component.selectedComponentCorrection()).toBeNull();
    expect(fixture.nativeElement.querySelectorAll('audit-consolidation-component-correction')).toHaveLength(0);
  });
});
