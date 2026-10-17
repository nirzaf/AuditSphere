import { signal, type WritableSignal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Api } from '../../core/api';
import { ConsolidationExternalPacks, decodeExternalPackDetail, decodeExternalPackPage } from './external-packs';

const id1 = '11111111-1111-4111-8111-111111111111';
const id2 = '22222222-2222-4222-8222-222222222222';
const id3 = '33333333-3333-4333-8333-333333333333';
const id4 = '44444444-4444-4444-8444-444444444444';
const id5 = '55555555-5555-4555-8555-555555555555';
const hash64 = 'a'.repeat(64);

function pack(overrides: Record<string, unknown> = {}) {
  return {
    id: id4, clientId: id2, clientLegalName: 'Synthetic Entity Ltd', engagementId: id3, priorPackId: null, version: 1,
    periodStart: '2026-01-01', periodEnd: '2026-12-31', framework: 'IFRS', reportingCurrency: 'QAR',
    periodBasis: 'FY2026', taxonomyVersion: 'IFRS-2026', mappingVersion: 'map-3', sourceReference: 'source-ref-7',
    rawSourceHash: hash64, normalizedSourceDigest: 'b'.repeat(64), packDigest: 'c'.repeat(64),
    declaredSignedTotal: '1.000000', reconciledSignedTotal: '0.000000',
    reconciliationStatus: 'PENDING', reconciliationReference: '', reconciledByUserId: null, reconciledAt: null,
    compatibilityBridgeStatus: 'NONE', compatibilityBridgeReference: '',
    compatibilityBridgeApprovedByUserId: null, compatibilityBridgeApprovedAt: null,
    status: 'RETURNED', returnReason: 'Correct source line', submittedByUserId: id5,
    submittedAt: '2026-10-01T10:00:00Z', returnedByUserId: id3, returnedAt: '2026-10-01T11:00:00Z',
    approvedByUserId: null, approvedAt: null, canReconcile: false, canReturn: false, canApprove: false, canPin: false,
    currentBlockers: [],
    ...overrides,
  };
}

function pagePayload(overrides: Record<string, unknown> = {}) {
  return {
    scopeVersionId: id1, groupId: id2, pinnedGroupRevision: 9, currentGroupRevision: 9,
    scopeStatus: 'DRAFT', reportingCurrency: 'QAR', method: 'PROPORTIONATE', currentUserId: id5,
    canSubmitPack: true, canReviewPacks: true, canPinApprovedPacks: true,
    items: [pack()], totalCount: 1, page: 1, pageSize: 25,
    eligibleClients: [{ clientId: id2, legalName: 'Synthetic Entity Ltd', membershipEffectiveFrom: '2026-01-01' }],
    eligibleClientTotalCount: 1, eligibleClientPage: 1, eligibleClientPageSize: 25, clientSearch: '',
    resolvedClient: null,
    eligibleEngagements: [{ engagementId: id3, clientId: id2, serviceRoute: 'AUDIT_ONLY', periodStart: '2026-01-01', periodEnd: '2026-12-31', status: 'ACTIVE' }],
    eligibleEngagementTotalCount: 1, eligibleEngagementPage: 1, eligibleEngagementPageSize: 25, engagementSearch: '',
    resolvedEngagement: null,
    ...overrides,
  };
}

function detailPayload(overrides: Record<string, unknown> = {}) {
  return {
    scopeVersionId: id1, groupId: id2, pinnedGroupRevision: 9, currentGroupRevision: 9,
    scopeStatus: 'DRAFT', currentUserId: id5, pack: pack({ status: 'SUBMITTED' }),
    lines: [{ id: id3, taxonomyCode: 'BS-100', amount: '1.000000', currency: 'QAR', sourceLineReference: 'line-1' }],
    totalLineCount: 1, linePage: 1, linePageSize: 100, hasPreviousLines: false, hasNextLines: false,
    ...overrides,
  };
}

async function sha256Hex(bytes: Uint8Array): Promise<string> {
  const copy = new ArrayBuffer(bytes.byteLength);
  new Uint8Array(copy).set(bytes);
  const digest = await globalThis.crypto.subtle.digest('SHA-256', copy);
  return Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, '0')).join('');
}

describe('External consolidation component packs', () => {
  let component: ConsolidationExternalPacks;
  let workspaceData: WritableSignal<ReturnType<typeof decodeExternalPackPage> | null>;
  let detailData: WritableSignal<ReturnType<typeof decodeExternalPackDetail> | null>;
  let workspaceResource: { data: typeof workspaceData; loading: WritableSignal<boolean>; error: WritableSignal<string>; reload: ReturnType<typeof vi.fn> };
  let detailResource: { data: typeof detailData; loading: WritableSignal<boolean>; error: WritableSignal<string>; reload: ReturnType<typeof vi.fn> };
  let api: { resource: ReturnType<typeof vi.fn>; command: ReturnType<typeof vi.fn> };
  let fixture: ComponentFixture<ConsolidationExternalPacks>;

  beforeEach(() => {
    workspaceData = signal(decodeExternalPackPage(pagePayload(), 'root'));
    detailData = signal<ReturnType<typeof decodeExternalPackDetail> | null>(null);
    workspaceResource = { data: workspaceData, loading: signal(false), error: signal(''), reload: vi.fn() };
    detailResource = { data: detailData, loading: signal(false), error: signal(''), reload: vi.fn() };
    let resourceIndex = 0;
    api = {
      resource: vi.fn(() => resourceIndex++ === 0 ? workspaceResource : detailResource),
      command: vi.fn().mockResolvedValue({ ok: true, value: id5 }),
    };
    TestBed.configureTestingModule({
      imports: [ConsolidationExternalPacks],
      providers: [{ provide: Api, useValue: api }],
    });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
    vi.restoreAllMocks();
  });

  async function mount() {
    fixture = TestBed.createComponent(ConsolidationExternalPacks);
    fixture.componentRef.setInput('scopeId', id1);
    fixture.autoDetectChanges();
    await fixture.whenStable();
    component = fixture.componentInstance;
    return component;
  }

  it('decodes the bounded paged contract and rejects the old unpaged response shape', () => {
    const decoded = decodeExternalPackPage(pagePayload(), 'root');
    expect(decoded.eligibleClientPageSize).toBe(25);
    expect(decoded.resolvedClient).toBeNull();
    const reconciled = pack({ status: 'SUBMITTED', reconciliationStatus: 'RECONCILED', canReconcile: false, canApprove: true });
    expect(decodeExternalPackPage(pagePayload({ items: [reconciled], totalCount: 1 }), 'root').items[0].canApprove).toBe(true);
    expect(() => decodeExternalPackPage(pagePayload({ items: [{ ...reconciled, reconciliationStatus: 'PENDING' }], totalCount: 1 }), 'root')).toThrow();
    expect(() => decodeExternalPackPage({ ...pagePayload(), eligibleClientPage: undefined, clientsTruncated: false }, 'root')).toThrow();
    expect(() => decodeExternalPackDetail({ ...detailPayload(), linesTruncated: false, linePage: undefined }, 'root')).toThrow();
  });

  it('hashes actual source bytes and canonical typed lines while keeping decimals exact and preview bounded', async () => {
    await mount();
    const rows = Array.from({ length: 26 }, (_, index) => ({
      taxonomyCode: `BS-${String(index).padStart(3, '0')}`,
      amount: index === 0 ? '9007199254.740993' : '0.000001',
      currency: 'QAR', sourceLineReference: `L${String(index).padStart(2, '0')}`,
    }));
    const tiedRows = [
      { taxonomyCode: 'BS-Z', amount: '2.000000', currency: 'QAR', sourceLineReference: 'L99' },
      { taxonomyCode: 'BS-Z', amount: '-2.000000', currency: 'QAR', sourceLineReference: 'L99' },
    ];
    const bytes = new TextEncoder().encode(JSON.stringify({ lines: [...rows].reverse().concat(tiedRows) }));
    const file = {
      name: 'trial-balance.json', size: bytes.byteLength,
      arrayBuffer: vi.fn().mockResolvedValue(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength)),
    } as unknown as File;

    await component.sourceFileSelected({ currentTarget: { files: [file] } } as unknown as Event);
    await vi.waitFor(() => expect(component.sourceBusy()).toBe(false));

    const canonical = [...rows.map(row => `${row.taxonomyCode}|${row.amount}|${row.currency}|${row.sourceLineReference}`),
      'BS-Z|-2.000000|QAR|L99', 'BS-Z|2.000000|QAR|L99'].join('\n');
    expect(component.rawSourceHash()).toBe(await sha256Hex(bytes));
    expect(component.normalizedSourceDigest()).toBe(await sha256Hex(new TextEncoder().encode(canonical)));
    expect(component.sourceLines().find(line => line.sourceLineReference === 'L00')?.amount).toBe('9007199254.740993');
    expect(component.declaredSignedTotal()).toBe('9007199254.741018');
    expect(component.sourcePreview()).toHaveLength(25);
    expect(component.sourceLines()).toHaveLength(28);
  });

  it('submits exact hashes, amounts, current revision and returned-pack lineage', async () => {
    await mount();
    component.prepareReviewed.set(true);
    component.clientChanged(id2);
    expect(component.prepareReviewed()).toBe(false);
    component.prepareReviewed.set(true);
    component.engagementChanged(id3);
    expect(component.prepareReviewed()).toBe(false);
    const returned = pack();
    workspaceData.set(decodeExternalPackPage(pagePayload({
      items: [returned], resolvedClient: pagePayload().eligibleClients[0], resolvedEngagement: pagePayload().eligibleEngagements[0],
    }), 'root'));
    component.clientChanged(id2);
    component.engagementChanged(id3);
    component.form = {
      clientId: id2, engagementId: id3, periodStart: '2026-01-01', periodEnd: '2026-12-31', framework: 'IFRS',
      reportingCurrency: 'QAR', periodBasis: 'FY2026', taxonomyVersion: 'IFRS-2026', mappingVersion: 'map-3',
      sourceReference: 'source-ref-7', priorPackId: '', compatibilityBridgeReference: '',
    };
    component.priorPackChanged(id4, workspaceData()!);
    component.prepareReviewed.set(true);
    const bytes = new TextEncoder().encode(JSON.stringify({ lines: [
      { taxonomyCode: 'BS-100', amount: '9007199254.740993', currency: 'QAR', sourceLineReference: 'source-1' },
      { taxonomyCode: 'BS-200', amount: '0.000001', currency: 'QAR', sourceLineReference: 'source-2' },
    ] }));
    const file = {
      name: 'typed-lines.json', size: bytes.byteLength,
      arrayBuffer: vi.fn().mockResolvedValue(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength)),
    } as unknown as File;
    await component.sourceFileSelected({ currentTarget: { files: [file] } } as unknown as Event);
    await vi.waitFor(() => expect(component.sourceBusy()).toBe(false));
    component.prepareReviewed.set(true);
    expect(component.validPackForm(workspaceData()!)).toBe(true);

    await component.submitPack(workspaceData()!);

    expect(api.command).toHaveBeenCalledWith(`/api/ui/consolidation/scopes/${id1}/external-packs`, expect.objectContaining({
      clientId: id2, engagementId: id3, expectedGroupRevision: 9, reviewed: true, priorPackId: id4,
      rawSourceHash: await sha256Hex(bytes), normalizedSourceDigest: await sha256Hex(new TextEncoder().encode(
        'BS-100|9007199254.740993|QAR|source-1\nBS-200|0.000001|QAR|source-2')),
      declaredSignedTotal: '9007199254.740994',
      lines: [
        { taxonomyCode: 'BS-100', amount: '9007199254.740993', currency: 'QAR', sourceLineReference: 'source-1' },
        { taxonomyCode: 'BS-200', amount: '0.000001', currency: 'QAR', sourceLineReference: 'source-2' },
      ],
    }), 'POST');
  });

  it('covers reconcile, return, approve and pin routes with current server revisions', async () => {
    await mount();
    const currentPage = decodeExternalPackPage(pagePayload({ items: [] }), 'root');
    workspaceData.set(currentPage);
    const setDetail = (rawPack: Record<string, unknown>) => detailData.set(decodeExternalPackDetail(detailPayload({ pack: rawPack }), 'root'));

    const reviewable = pack({ status: 'SUBMITTED', reconciliationStatus: 'PENDING', canReconcile: true, canReturn: true });
    setDetail(reviewable);
    component.reconciliationEvidence.set('bank-reconciliation-26');
    component.reconcileReviewed.set(true);
    await component.reconcilePack(detailData()!.pack);
    component.returnReason.set('Line references require correction');
    component.returnReviewed.set(true);
    await component.returnPack(detailData()!.pack);

    const approvable = pack({ status: 'RESUBMITTED', reconciliationStatus: 'RECONCILED', canReconcile: false, canApprove: true });
    setDetail(approvable);
    component.approveReviewed.set(true);
    await component.approvePack(detailData()!.pack);

    const pinnable = pack({ status: 'APPROVED', reconciliationStatus: 'RECONCILED', canPin: true });
    setDetail(pinnable);
    component.pinOwnershipPercent.set('100');
    component.pinControlMethod.set('CONTROLLED');
    component.pinReviewed.set(true);
    await component.pinPack(detailData()!.pack);

    expect(api.command.mock.calls).toEqual([
      [`/api/ui/consolidation/scopes/${id1}/external-packs/${id4}/reconcile`,
        { expectedGroupRevision: 9, reviewed: true, evidenceReference: 'bank-reconciliation-26' }, 'POST'],
      [`/api/ui/consolidation/scopes/${id1}/external-packs/${id4}/return`,
        { expectedGroupRevision: 9, reviewed: true, reason: 'Line references require correction' }, 'POST'],
      [`/api/ui/consolidation/scopes/${id1}/external-packs/${id4}/approve`,
        { expectedGroupRevision: 9, reviewed: true }, 'POST'],
      [`/api/ui/consolidation/scopes/${id1}/external-components`,
        { externalComponentPackId: id4, ownershipPercent: '100', controlMethod: 'CONTROLLED', expectedGroupRevision: 9, reviewed: true }, 'POST'],
    ]);
  });

  it('blocks stale scopes and invalid typed amounts before sending a command', async () => {
    await mount();
    for (const name of ['clientSearch', 'engagementSearch']) {
      const input = fixture.nativeElement.querySelector(`input[name="${name}"]`) as HTMLInputElement;
      const enter = new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true });
      input.dispatchEvent(enter);
      expect(enter.defaultPrevented).toBe(true);
      expect(api.command).not.toHaveBeenCalled();
    }
    const stale = decodeExternalPackPage(pagePayload({
      pinnedGroupRevision: 8, currentGroupRevision: 9, canSubmitPack: false, canReviewPacks: false, canPinApprovedPacks: false,
    }), 'root');
    workspaceData.set(stale);
    await component.submitPack(stale);
    expect(component.scopeCurrent(stale)).toBe(false);
    expect(api.command).not.toHaveBeenCalled();

    const invalidBytes = new TextEncoder().encode(JSON.stringify({ lines: [
      { taxonomyCode: 'BS-100', amount: 9007199254.740993, currency: 'QAR', sourceLineReference: 'numeric-not-exact' },
    ] }));
    const file = {
      name: 'invalid-lines.json', size: invalidBytes.byteLength,
      arrayBuffer: vi.fn().mockResolvedValue(invalidBytes.buffer.slice(invalidBytes.byteOffset, invalidBytes.byteOffset + invalidBytes.byteLength)),
    } as unknown as File;
    await component.sourceFileSelected({ currentTarget: { files: [file] } } as unknown as Event);
    await vi.waitFor(() => expect(component.sourceBusy()).toBe(false));
    expect(component.sourceError()).toContain('exact decimal string');
    expect(component.rawSourceHash()).toBe('');
    expect(component.normalizedSourceDigest()).toBe('');
  });
});
