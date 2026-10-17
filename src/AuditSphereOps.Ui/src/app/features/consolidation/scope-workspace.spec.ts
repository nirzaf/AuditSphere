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
  decodeScopeSummary,
  ScopeWorkspace,
} from './scope-contracts';
import { ConsolidationScopeWorkspace, formatOwnershipPercent } from './scope-workspace';

const id1 = '11111111-1111-4111-8111-111111111111';
const id2 = '22222222-2222-4222-8222-222222222222';
const id3 = '33333333-3333-4333-8333-333333333333';
const hash64 = 'a'.repeat(64);
const scopeUrl = `/api/ui/consolidation/scopes/${id1}`;
const actor = id1;

function workspacePayload(overrides: Record<string, unknown> = {}) {
  return {
    scope: {
      id: id1, groupId: id2, groupName: 'Synthetic Group', groupCode: 'SYN-GRP', version: 1,
      groupRevision: 1, periodId: id3, reportingCurrency: 'QAR', method: 'PROPORTIONATE',
      status: 'APPROVED', openingBasis: 'OPENING_BALANCE', isAdvanced: false,
    },
    canPrepare: true,
    canReview: true,
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

  beforeEach(() => {
    params = new BehaviorSubject(convertToParamMap({ id: id1 }));
    workspaceData = signal<ReturnType<typeof decodeScopeWorkspace> | null>(null);
    resource = { data: workspaceData, loading: signal(false), error: signal(''), reload: vi.fn() };
    api = {
      resource: vi.fn((_url, decoder) => decoder === decodeScopeWorkspace ? resource : {
        data: signal(null), loading: signal(false), error: signal(''), reload: vi.fn(),
      }),
      command: vi.fn().mockResolvedValue({ ok: true, value: true }),
    };
    TestBed.configureTestingModule({
      imports: [ConsolidationScopeWorkspace],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap: params } },
        { provide: Api, useValue: api },
        { provide: SessionService, useValue: { invalidation: signal(0) } },
      ],
    });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
    vi.restoreAllMocks();
  });

  function open(raw: unknown = workspacePayload()) {
    workspaceData.set(decodeScopeWorkspace(raw, 'root'));
    const fixture = TestBed.createComponent(ConsolidationScopeWorkspace);
    fixture.detectChanges();
    return { fixture, component: fixture.componentInstance };
  }

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
      currentUserId: id1,
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
        },
      ],
      eligiblePackages: [
        {
          packageId: id1,
          clientId: id3,
          clientLegalName: 'Acme Subsidiary QSTP-LLC',
          engagementId: id2,
          periodBasis: '2026-01-01..2026-12-31',
          taxonomyVersion: 'IFRS-2026',
          mappingVersion: '1',
          currency: 'QAR',
          calculationHash: hash64,
        },
      ],
      journals: [
        {
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
        },
      ],
      runs: [
        {
          id: id1,
          engineVersion: '1.0',
          runHash: hash64,
          signedTotal: '0.00',
          status: 'APPROVED',
          createdByUserId: id2,
          createdAt: '2026-10-01T12:00:00Z',
          approvedByUserId: id1,
          approvedAt: '2026-10-01T12:30:00Z',
        },
      ],
      latestReport: {
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
      },
      componentReadiness: [
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
      ],
      intercompanyExceptions: [],
    };

    const decoded = decodeScopeWorkspace(raw, 'root');
    expect(decoded.scope.groupCode).toBe('ACME-GRP');
    expect(decoded.components.length).toBe(1);
    expect(decoded.components[0].clientLegalName).toBe('Acme Subsidiary QSTP-LLC');
    expect(decoded.eligiblePackages.length).toBe(1);
    expect(decoded.journals.length).toBe(1);
    expect(decoded.journals[0].lines.length).toBe(2);
    expect(decoded.journals[0].totalDebits).toBe('1500.00');
    expect(decoded.runs.length).toBe(1);
    expect(decoded.latestReport?.lines[0].consolidatedAmount).toBe('0.00');
    expect(() => decodeScopeWorkspace({ ...raw, scope: { ...raw.scope, status: 'Draft' } }, 'root')).toThrow();
    expect(() => decodeScopeWorkspace({ ...raw, journals: [{ ...raw.journals[0], status: 'Submitted' }] }, 'root')).toThrow();
    expect(() => decodeScopeWorkspace({ ...raw, runs: [{ ...raw.runs[0], status: 'VERIFIED' }] }, 'root')).toThrow();
    expect(() => decodeScopeWorkspace({ ...raw, componentReadiness: [{ ...raw.componentReadiness[0], componentState: 'READY' }] }, 'root')).toThrow();
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
    const payload = workspacePayload({ scope: { ...draftScope, status: 'DRAFT' } });
    const { fixture } = open(payload);
    const button = [...fixture.nativeElement.querySelectorAll('button')]
      .find((item: HTMLButtonElement) => item.textContent?.includes('Approve perimeter')) as HTMLButtonElement;
    expect(button).toBeTruthy();
    button.click();
    expect(api.command).toHaveBeenCalledWith(`${scopeUrl}/approve`, {}, 'POST');
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

  it('keeps ownership points and journal amounts as exact decimal strings on dispatch', async () => {
    expect(formatOwnershipPercent('100')).toBe('100%');
    expect(formatOwnershipPercent('50.250000')).toBe('50.25%');

    const packageId = '44444444-4444-4444-8444-444444444444';
    const draftScope = workspacePayload().scope;
    const payload = workspacePayload({
      scope: { ...draftScope, status: 'DRAFT' },
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
});
