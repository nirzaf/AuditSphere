import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SessionService } from '../../core/session';
import { EngagementCompletion, decodeCompletion, decodeSignatureCheck } from './completion';

const id1 = '11111111-1111-4111-8111-111111111111';
const id2 = '22222222-2222-4222-8222-222222222222';
const id3 = '33333333-3333-4333-8333-333333333333';
const hash64 = 'f'.repeat(64);
const watchVarianceStateReload = (page: EngagementCompletion) => {
  const reload = page.loadVarianceState.bind(page);
  let started!: () => void;
  const requested = new Promise<void>(resolve => { started = resolve; });
  const spy = vi.spyOn(page, 'loadVarianceState').mockImplementation(() => {
    const result = reload();
    started();
    return result;
  });
  return { requested, restore: () => spy.mockRestore() };
};

const validPayload = {
      engagementId: id1,
      gates: [
        {
          name: 'Partner Review',
          status: 'CLEARED',
          tone: 'POSITIVE',
          authority: 'Lead Engagement Partner',
          date: '2026-10-02T10:00:00Z',
        },
      ],
      representations: [
        {
          id: id1,
          code: 'REP-01',
          title: 'Management Responsibilities',
          narrative: 'Management acknowledges its responsibility for the financial statements.',
          obtained: true,
          obtainedAt: '2026-10-02T10:30:00Z',
          signatoryName: 'Chief Financial Officer',
        },
      ],
      packageId: id2,
      packageStatus: 'VALIDATED',
      packageEvidence: { current: true, message: 'Exact package evidence is current.' },
      packageReviews: [{ id: id1, financialPackageId: id2, packageRevision: 4, packageGeneration: 2, packageHash: hash64,
        financialPackageArtifactId: id3, artifactVersion: 'v2', artifactSha256Hex: hash64, presentationRevisionId: id2,
        presentationCompositionId: id3, compositionManifestDigest: hash64, stage: 'PARTNER_APPROVAL', decision: 'APPROVED',
        evidenceMode: 'SIGNED_IN', evidenceReference: 'partner-review-4', decidedByUserId: id2, decidedAt: '2026-10-02T12:10:00Z', sequence: 3 }],
      partnerApproved: true,
      eqrStatus: 'NOT_ASSIGNED',
      releaseCandidateId: id3,
      release: {
        candidateId: id3,
        candidateStatus: 'ISSUED',
        targetKind: 'FINANCIAL_PACKAGE',
        targetRevision: 4,
        candidateRevision: 1,
        manifestDigest: hash64,
        candidateCreatedAt: '2026-10-02T12:20:00Z',
        checkpointId: id2,
        checkpointState: 'VERIFIED',
        checkpointVerifiedAt: '2026-10-02T12:25:00Z',
        checkpointMatchesCandidate: true,
        checkpointReadBackMatchesManifest: true,
        releaseId: id1,
        releasedAt: '2026-10-02T12:30:00Z',
        releaseCheckpointId: id2,
        releaseManifestDigest: hash64,
        releaseMatchesCandidate: true,
        issueState: 'ISSUED',
        deliveryDisposition: 'AWAITING_AUTHORIZATION_OR_CONFIGURATION',
      },
      canPrepareRelease: true,
      confirmations: [
        {
          caseId: id1,
          type: 'BANK',
          respondent: 'Qatar National Bank',
          bookedAmount: '5000000.00',
          currency: 'QAR',
          status: 'CONFIRMED',
          monitoring: 'ON_TRACK',
          daysSinceDispatch: 5,
          critical: true,
          criticalityRationale: 'Primary operational bank account',
        },
      ],
      deliverables: [
        {
          id: id2,
          kind: 'INDEPENDENT_AUDITORS_REPORT',
          title: "Independent Auditor's Report",
          version: 1,
          signed: true,
          current: true,
          contentSha256: hash64,
          createdAt: '2026-10-02T11:00:00Z',
          signatureKind: 'CERTIFICATE',
          certificateSubject: 'CN=Test Audit Firm',
        },
      ],
      clearance: {
        id: id3,
        summaryReviewMemorandumId: id1,
        partnerUserId: id2,
        clearedAt: '2026-10-02T12:00:00Z',
      },
      opinion: {
        id: id1,
        partnerClearanceId: id3,
        decidedByUserId: id2,
        decidedAt: '2026-10-02T12:10:30Z',
        opinionType: 'UNMODIFIED',
        label: 'Clean (unmodified)',
        focusArea: null,
      },
      opinionAreas: [
        {
          id: id3,
          code: 'GEN',
          name: 'General Audit',
        },
      ],
      shared: [
        {
          title: 'Closing Meeting Memo',
          openComments: [],
        },
      ],
      signedLetters: [
        {
          id: id1,
          deliverableId: id2,
          version: 1,
          managementSignatory: 'Chief Financial Officer',
          contentSha256: hash64,
          uploadedAt: '2026-10-02T11:30:00Z',
          verified: true,
          current: true,
        },
      ],
      bundles: {
        blockers: [],
        bundles: [
          {
            id: id3,
            sha256: hash64,
            assembledAt: '2026-10-02T12:30:00Z',
          },
        ],
      },
      opinionTypes: ['UNMODIFIED', 'QUALIFIED', 'ADVERSE', 'DISCLAIMER'],
      freeze: {
        state: 'FROZEN',
        reportSignedAt: '2026-10-02T12:00:00Z',
        dueAt: '2026-12-01T12:00:00Z',
        externalReadOnly: 'LOCKED',
        daysRemaining: 60,
        amendments: [],
      },
      freezeDays: 60,
      workprogramApproval: { hasApproval: false, isCurrent: false, approvedAt: null, canApprove: true },
      trail: [
        {
          at: '2026-10-02T12:00:00Z',
          kind: 'OPINION_SIGNED',
          actor: 'Lead Partner',
          description: 'Signed audit opinion',
          entityId: id1,
        },
      ],
      trailCoverageNote: 'Complete audit event trail',
      locks: [
        {
          id: id2,
          documentKey: 'AUDIT_PACKAGE_FINAL',
          lockedBy: 'Lead Partner',
          lockedAt: '2026-10-02T12:05:00Z',
        },
      ],
      reportSigning: {
        mode: 'CERTIFICATE',
        required: true,
        ready: true,
        subject: 'CN=Test Audit Firm',
        notAfter: '2027-10-01T00:00:00Z',
        message: 'The signed report is certificate-signed.',
      },
    };

describe('Audit Completion Contracts', () => {
  it('decodes a valid completion checklist payload', () => {
    const raw = validPayload;

    const decoded = decodeCompletion(raw, 'completion');
    expect(decoded.engagementId).toBe(id1);
    expect(decoded.gates.length).toBe(1);
    expect(decoded.gates[0].name).toBe('Partner Review');
    expect(decoded.partnerApproved).toBe(true);
    expect(decoded.opinion?.opinionType).toBe('UNMODIFIED');
    expect(decoded.freeze?.state).toBe('FROZEN');
    expect(decoded.release?.issueState).toBe('ISSUED');
    expect(decoded.release?.deliveryDisposition).toBe('AWAITING_AUTHORIZATION_OR_CONFIGURATION');
    expect(decoded.release?.checkpointReadBackMatchesManifest).toBe(true);
    expect(decoded.clearance).toEqual({ id: id3, summaryReviewMemorandumId: id1, partnerUserId: id2, clearedAt: '2026-10-02T12:00:00Z' });
    expect(decoded.opinion?.partnerClearanceId).toBe(id3);
    expect(decoded.representations[0].id).toBe(id1);
    expect(decoded.packageReviews[0].id).toBe(id1);
    expect(decoded.locks.length).toBe(1);
  });

  it('rejects invalid completion payloads', () => {
    expect(() => decodeCompletion(null, 'completion')).toThrow();
    expect(() => decodeCompletion({}, 'completion')).toThrow();
  });
});

const scheduled = { ...validPayload, freeze: { ...validPayload.freeze, state: 'SCHEDULED', frozenAt: null, daysRemaining: 12 } };
const readinessUrl = `/api/ui/engagements/${id1}/early-lock/readiness`;
const lockUrl = `/api/ui/engagements/${id1}/early-lock`;
const readyRecord = { freezeId: id2, revision: 3, state: 'SCHEDULED', dueAt: '2026-12-07T00:00:00Z', archiveReadinessDigest: hash64, blockers: [] };
const programBasis = { engagementId: id1, program: { programCode: 'ISA', version: '2026.1', sourceHash: hash64 }, canManageFieldwork: true,
  procedures: [
    { sourceProcedureId: 'ISA-001', title: 'Obtain sufficient evidence', applicabilityStatus: 'APPLICABLE', status: 'REVIEWED' },
    { sourceProcedureId: 'ISA-002', title: 'Evaluate subsequent events', applicabilityStatus: 'APPLICABLE', status: 'SUBMITTED' },
    { sourceProcedureId: 'ISA-003', title: 'Exclude irrelevant section', applicabilityStatus: 'NOT_APPLICABLE_APPROVED', status: 'NOT_STARTED' },
  ] };

describe('Early compliance lock panel (STE 4.4.3)', () => {
  function setup(): HttpTestingController {
    const params = new BehaviorSubject(convertToParamMap({ id: id1 }));
    TestBed.configureTestingModule({
      imports: [EngagementCompletion],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap: params } },
      ],
    });
    TestBed.inject(SessionService).current.set({ userId: id3, firmId: id2, generation: '1', staff: true });
    return TestBed.inject(HttpTestingController);
  }
  function load(http: HttpTestingController, payload: object, basis = programBasis) {
    const f = TestBed.createComponent(EngagementCompletion);
    f.detectChanges();
    http.expectOne(`/api/ui/engagements/${id1}/completion`).flush(payload);
    http.expectOne(`/api/ui/engagements/${id1}/fieldwork`).flush(basis);
    f.detectChanges();
    return f;
  }
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
  });

  it('does not create an EQR case or request reviewer-only evidence when no case is assigned', () => {
    const http = setup();
    const f = load(http, validPayload);
    expect((f.nativeElement as HTMLElement).textContent).toContain('No EQR case is assigned. Applicability is not established here');
    expect((f.nativeElement as HTMLElement).querySelector('audit-eqr-decision')).toBeNull();
    http.expectNone(`/api/ui/engagements/${id1}/completion/eqr-review-basis`);
  });

  it('states that an issued release is awaiting an authorized delivery method and has no recipient receipt', () => {
    const http = setup();
    const f = load(http, validPayload);
    const host = f.nativeElement as HTMLElement;
    const delivery = host.querySelector('[data-release-delivery-disposition]');
    expect(delivery?.getAttribute('role')).toBe('status');
    expect(delivery?.textContent).toContain('Release is issued; delivery is awaiting authorization or configuration.');
    expect(delivery?.textContent).toContain('No provider delivery or recipient receipt is recorded.');
  });

  it('alerts when the persisted delivery outcome cannot be bound to the issued release', () => {
    const http = setup();
    const f = load(http, { ...validPayload, release: { ...validPayload.release, deliveryDisposition: 'OUTCOME_UNCONFIRMED' } });
    const delivery = (f.nativeElement as HTMLElement).querySelector('[data-release-delivery-disposition]');
    expect(delivery?.getAttribute('role')).toBe('alert');
    expect(delivery?.textContent).toContain('Delivery outcome is unconfirmed.');
    expect(delivery?.textContent).toContain('Do not treat a delivery intent as provider delivery or recipient receipt.');
  });

  it('renders a retained but non-current EQR concurrence as pending and loads only its existing basis', async () => {
    const http = setup();
    const f = TestBed.createComponent(EngagementCompletion);
    f.detectChanges();
    http.expectOne(`/api/ui/engagements/${id1}/completion`).flush({ ...validPayload,
      eqrStatus: 'RECORDED_CONCURRENCE_NOT_CURRENT',
      gates: [...validPayload.gates, { name: 'EQR completed', status: 'RECORDED_CONCURRENCE_NOT_CURRENT', tone: 'pending', authority: null, date: null }] });
    http.expectOne(`/api/ui/engagements/${id1}/fieldwork`).flush(programBasis);
    await f.whenStable();
    f.detectChanges();
    http.expectOne(`/api/ui/engagements/${id1}/completion/eqr-review-basis`).flush({ caseRevision: 4, factsDigest: hash64,
      reportDeliverableId: id2, reportSha256: hash64 });
    await f.whenStable();
    f.detectChanges();
    const host = f.nativeElement as HTMLElement;
    expect(host.querySelector('audit-eqr-decision')).not.toBeNull();
    expect(host.textContent).toContain('Recorded EQR case status');
    expect(host.textContent).toContain('RECORDED_CONCURRENCE_NOT_CURRENT');
    const gateRow = Array.from(host.querySelectorAll('tbody tr')).find(row => row.textContent?.includes('EQR completed'));
    expect(gateRow?.querySelector('audit-status .status-chip')?.getAttribute('data-status')).toBe('RECORDED_CONCURRENCE_NOT_CURRENT');
    expect(gateRow?.textContent).toContain('recorded concurrence not current');
    expect(host.textContent).toContain('Download exact report');
  });

  it('retains the confirmed EQR decision through parent reload and clears it on session replacement', async () => {
    sessionStorage.clear();
    const http = setup();
    const payload = { ...validPayload, eqrStatus: 'IN_PROGRESS' };
    const f = load(http, payload);
    const basisUrl = `/api/ui/engagements/${id1}/completion/eqr-review-basis`;
    const decisionUrl = `/api/ui/engagements/${id1}/completion/eqr-decision`;
    http.expectOne(basisUrl).flush({ caseRevision: 4, factsDigest: hash64, reportDeliverableId: id2, reportSha256: hash64 });
    await f.whenStable();
    f.detectChanges();
    const host = f.nativeElement as HTMLElement;
    const notes = host.querySelector<HTMLTextAreaElement>('#eqr-decision-notes')!;
    notes.value = 'Reviewed the report'; notes.dispatchEvent(new Event('input', { bubbles: true }));
    f.detectChanges();
    const assent = host.querySelector<HTMLInputElement>('input[name="eqr-exact-basis-reviewed"]')!;
    assent.checked = true; assent.dispatchEvent(new Event('change', { bubbles: true }));
    f.detectChanges();
    host.querySelector<HTMLButtonElement>('[data-eqr-submit]')!.click();
    let posts = http.match(decisionUrl);
    if (!posts.length) await vi.waitFor(() => { posts = http.match(decisionUrl); expect(posts.length).toBeGreaterThan(0); });
    expect(posts).toHaveLength(1);
    posts[0].flush({ value: id3 });
    http.expectNone(decisionUrl);
    await vi.waitFor(() => expect(f.componentInstance.confirmedEqrDecision()).toBe(id3));
    http.expectOne(`/api/ui/engagements/${id1}/completion`).flush(payload);
    f.detectChanges();
    const basisRequests = http.match(basisUrl);
    for (const request of basisRequests.filter(request => !request.cancelled))
      request.flush({ caseRevision: 5, factsDigest: hash64, reportDeliverableId: id2, reportSha256: hash64 });
    f.detectChanges();
    for (const request of http.match(basisUrl).filter(request => !request.cancelled))
      request.flush({ caseRevision: 5, factsDigest: hash64, reportDeliverableId: id2, reportSha256: hash64 });
    await f.whenStable(); f.detectChanges();
    expect(host.querySelector('[data-eqr-confirmation]')?.textContent).toContain(id3);
    expect(host.querySelector('[data-eqr-confirmation]')?.textContent).toContain('Current concurrence is not assumed');
    TestBed.inject(SessionService).current.set({ userId: id3, firmId: id2, generation: '2', staff: true });
    f.detectChanges();
    expect(host.querySelector('[data-eqr-confirmation]')).toBeNull();
    http.expectOne(`/api/ui/engagements/${id1}/completion`).flush(payload);
    http.expectOne(`/api/ui/engagements/${id1}/fieldwork`).flush(programBasis);
    f.detectChanges();
    http.expectOne(basisUrl).flush({ caseRevision: 5, factsDigest: hash64, reportDeliverableId: id2, reportSha256: hash64 });
    await f.whenStable(); f.detectChanges();
    expect(host.querySelector('[data-eqr-confirmation]')).toBeNull();
    f.destroy();
  });

  it('shows the digest-bound confirmation to a Partner when readiness has no blockers', () => {
    const http = setup();
    const f = load(http, scheduled);
    http.expectOne(readinessUrl).flush(readyRecord);
    f.detectChanges();
    const host = f.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Early compliance lock (STE 4.4.3)');
    expect(host.querySelector('input[name="lockConfirm"]')).not.toBeNull();
    expect(host.textContent).toContain(hash64);
  });

  it('lists readiness blockers and withholds the confirmation until they are cleared', () => {
    const http = setup();
    const f = load(http, scheduled);
    http.expectOne(readinessUrl).flush({ ...readyRecord, blockers: ['Final release has not been recorded.'] });
    f.detectChanges();
    const host = f.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Final release has not been recorded.');
    expect(host.querySelector('input[name="lockConfirm"]')).toBeNull();
  });

  it('hides the early-lock control when readiness is refused as Partner-only', () => {
    const http = setup();
    const f = load(http, scheduled);
    http.expectOne(readinessUrl).flush({ code: 'scope.denied', message: 'Access denied.' }, { status: 403, statusText: 'Forbidden' });
    f.detectChanges();
    expect((f.nativeElement as HTMLElement).textContent).not.toContain('Early compliance lock (STE 4.4.3)');
  });

  it('offers no lock action for a file that is already frozen', () => {
    const http = setup();
    const f = load(http, validPayload);
    http.expectNone(readinessUrl);
    expect((f.nativeElement as HTMLElement).textContent).not.toContain('Early compliance lock (STE 4.4.3)');
  });

  it('shows the applicable review set and blocks Manager approval while a procedure is unreviewed', () => {
    const http = setup();
    const f = load(http, validPayload);
    const host = f.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Current adopted programme: ISA v2026.1');
    expect(host.textContent).toContain('ISA-001Obtain sufficient evidence');
    expect(host.textContent).toContain('ISA-002 — Evaluate subsequent events: independent review is SUBMITTED');
    expect(host.textContent).not.toContain('ISA-003');
    const action = Array.from(host.querySelectorAll('button')).find((button) => button.textContent?.includes('Record Manager workprogramme approval')) as HTMLButtonElement;
    expect(action.disabled).toBe(true);
  });

  it('shows exact Partner clearance identities only for the current SRM', () => {
    const http = setup();
    const summary = { ...validPayload.deliverables[0], id: id3, kind: 'SUMMARY_REVIEW_MEMORANDUM',
      title: 'Summary Review Memorandum', version: 2, current: true };
    const f = load(http, { ...validPayload, deliverables: [...validPayload.deliverables, summary], clearance: {
      id: id3, summaryReviewMemorandumId: id3, partnerUserId: id2, clearedAt: '2026-10-02T12:00:00Z' } });
    const host = f.nativeElement as HTMLElement;
    const clearance = host.querySelector('[data-partner-clearance]');
    expect(clearance?.textContent).toContain(id3);
    expect(clearance?.textContent).toContain(id3);
    expect(clearance?.textContent).toContain(id2);
    expect(clearance?.textContent).toContain('2026-10-02 12:00 UTC');
    const opinion = host.querySelector('[data-current-opinion]');
    expect(opinion?.textContent).toContain(id1);
    expect(opinion?.textContent).toContain(id3);
    expect(opinion?.textContent).toContain(id2);
    expect(host.querySelector(`[data-package-review="${id1}"]`)?.textContent).toContain('partner-review-4');
    expect(host.querySelector(`[data-management-representation="${id1}"]`)?.textContent).toContain('Chief Financial Officer');
    expect(host.querySelector(`[data-management-representation="${id1}"]`)?.textContent).toContain('2026-10-02T10:30:00Z');
  });

  it('does not associate a clearance with a different or stale latest SRM', () => {
    const http = setup();
    const summary = { ...validPayload.deliverables[0], id: id3, kind: 'SUMMARY_REVIEW_MEMORANDUM',
      title: 'Summary Review Memorandum', version: 2, current: false };
    const f = load(http, { ...validPayload, deliverables: [...validPayload.deliverables, summary], clearance: {
      id: id1, summaryReviewMemorandumId: id1, partnerUserId: id2, clearedAt: '2026-10-02T12:00:00Z' } });
    const host = f.nativeElement as HTMLElement;
    expect(host.querySelector('[data-partner-clearance]')).toBeNull();
    expect(host.textContent).toContain('No current Partner clearance is established for this SRM.');
    expect(host.querySelector('[data-current-opinion]')).toBeNull();
  });

  it('submits Manager rationale once and refreshes current approval and SRM state', async () => {
    const http = setup();
    const f = load(http, validPayload, { ...programBasis, procedures: [programBasis.procedures[0], programBasis.procedures[2]] });
    const page = f.componentInstance;
    const host = f.nativeElement as HTMLElement;
    const rationale = host.querySelector('textarea[name="workprogramRationale"]') as HTMLTextAreaElement;
    rationale.value = 'Reviewed the current applicable procedures and their independent review records.';
    rationale.dispatchEvent(new Event('input', { bubbles: true }));
    const assent = host.querySelector('input[name="workprogramReviewed"]') as HTMLInputElement;
    assent.checked = true;
    assent.dispatchEvent(new Event('change', { bubbles: true }));
    f.detectChanges();
    expect(page.programBasis.data()?.engagementId).toBe(id1);
    expect(page.canApproveCurrentWorkprograms()).toBe(true);
    expect(page.f.workprogramReviewed).toBe(true);
    expect(page.f.workprogramRationale.trim().length).toBeGreaterThan(0);
    expect(page.ws.data()?.workprogramApproval).toEqual({ hasApproval: false, isCurrent: false, approvedAt: null, canApprove: true });
    expect(page.cmd.busy()).toBe(false);
    const action = Array.from((f.nativeElement as HTMLElement).querySelectorAll('button')).find((button) => button.textContent?.includes('Record Manager workprogramme approval')) as HTMLButtonElement;
    expect(action.disabled).toBe(false);
    const attempt = page.approveWorkprograms();
    const post = http.expectOne(`/api/ui/engagements/${id1}/completion/workprogram-approval`);
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ rationale: page.f.workprogramRationale });
    post.flush({ value: id3 });
    await attempt;
    http.expectOne(`/api/ui/engagements/${id1}/completion`).flush({ ...validPayload,
      workprogramApproval: { hasApproval: true, isCurrent: true, approvedAt: '2026-10-17T10:00:00Z', canApprove: true },
      deliverables: [...validPayload.deliverables, { ...validPayload.deliverables[0], id: id3, kind: 'SUMMARY_REVIEW_MEMORANDUM', title: 'Summary Review Memorandum', version: 2 }] });
    http.expectOne(`/api/ui/engagements/${id1}/fieldwork`).flush({ ...programBasis, procedures: [programBasis.procedures[0], programBasis.procedures[2]] });
    f.detectChanges();
    expect((f.nativeElement as HTMLElement).textContent).toContain('Manager approval is current');
    expect((f.nativeElement as HTMLElement).textContent).toContain('Latest SRM v2 is current');
  });

  it('keeps approval unresolved and refreshes both sources after a stale refusal', async () => {
    const http = setup();
    const f = load(http, validPayload, { ...programBasis, procedures: [programBasis.procedures[0], programBasis.procedures[2]] });
    const page = f.componentInstance;
    page.f.workprogramReviewed = true;
    page.f.workprogramRationale = 'Reviewed current workprogramme set after procedure review.';
    const attempt = page.approveWorkprograms();
    http.expectOne(`/api/ui/engagements/${id1}/completion/workprogram-approval`).flush(
      { code: 'generation.stale', message: 'The procedure review set changed; refresh before approval.' }, { status: 409, statusText: 'Conflict' });
    await attempt;
    http.expectOne(`/api/ui/engagements/${id1}/completion`).flush({ ...validPayload,
      workprogramApproval: { hasApproval: true, isCurrent: false, approvedAt: '2026-10-16T10:00:00Z', canApprove: true } });
    http.expectOne(`/api/ui/engagements/${id1}/fieldwork`).flush(programBasis);
    f.detectChanges();
    expect((f.nativeElement as HTMLElement).textContent).toContain('Manager approval is stale and must be renewed');
    expect(page.cmd.failed()).toBe(true);
  });

  it('reloads readiness after a stale refusal and keeps the Partner reason', async () => {
    const http = setup();
    const f = load(http, scheduled);
    http.expectOne(readinessUrl).flush(readyRecord);
    f.detectChanges();
    const page = f.componentInstance;
    page.early = { confirmed: true, rationale: 'Locking after the final release review.' };
    const attempt = page.lockEarly(readyRecord);
    http.expectOne(lockUrl).flush({ code: 'generation.stale', message: 'The underlying inputs changed.' }, { status: 409, statusText: 'Conflict' });
    await attempt;
    f.detectChanges();
    expect(page.early.rationale).toBe('Locking after the final release review.');
    expect(page.early.confirmed).toBe(false);
    http.expectOne(readinessUrl).flush({ ...readyRecord, revision: 4, archiveReadinessDigest: 'e'.repeat(64) });
    f.detectChanges();
    expect((f.nativeElement as HTMLElement).textContent).toContain('e'.repeat(64));
  });

  it('locks the file on success, sends the digest-bound payload and shows FROZEN with the frozen time', async () => {
    const http = setup();
    const f = load(http, scheduled);
    http.expectOne(readinessUrl).flush(readyRecord);
    f.detectChanges();
    const page = f.componentInstance;
    page.early = { confirmed: true, rationale: 'Locking after the final release review.' };
    const attempt = page.lockEarly(readyRecord);
    const post = http.expectOne(lockUrl);
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({
      expectedRevision: '3',
      partnerConfirmed: true,
      rationale: 'Locking after the final release review.',
      archiveReadinessDigest: hash64,
    });
    post.flush({});
    await attempt;
    http.expectOne(`/api/ui/engagements/${id1}/completion`).flush({
      ...validPayload,
      freeze: { ...validPayload.freeze, state: 'FROZEN', frozenAt: '2026-10-08T10:00:00Z', daysRemaining: 0 },
    });
    f.detectChanges();
    const host = f.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Frozen at 2026-10-08 10:00 UTC.');
    expect(host.textContent).not.toContain('Early compliance lock (STE 4.4.3)');
    expect(page.early.confirmed).toBe(false);
  });
});

const draft = { ...validPayload.deliverables[0], id: id3, signed: false, signatureKind: 'NONE', certificateSubject: null };

describe('Report signing contract (ADR-0016)', () => {
  it('requires the server to state how each report was signed', () => {
    expect(decodeCompletion(validPayload, 'completion').deliverables[0].signatureKind).toBe('CERTIFICATE');
    expect(() => decodeCompletion({ ...validPayload, reportSigning: undefined }, 'completion')).toThrow();
    expect(() => decodeCompletion({ ...validPayload, deliverables: [{ ...draft, signatureKind: 'SIGNED' }] }, 'completion')).toThrow();
    expect(() => decodeSignatureCheck({ signatureKind: 'CERTIFICATE', state: 'TRUSTED', subject: null, thumbprintSha256: null, message: '' }, 'check')).toThrow();
  });
});

describe('Report certificate signing (ADR-0016)', () => {
  function open(payload: object) {
    const params = new BehaviorSubject(convertToParamMap({ id: id1 }));
    TestBed.configureTestingModule({
      imports: [EngagementCompletion],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), { provide: ActivatedRoute, useValue: { paramMap: params } }],
    });
    TestBed.inject(SessionService).current.set({ userId: id3, firmId: id2, generation: '1', staff: true });
    const http = TestBed.inject(HttpTestingController);
    const f = TestBed.createComponent(EngagementCompletion);
    f.detectChanges();
    http.expectOne(`/api/ui/engagements/${id1}/completion`).flush(payload);
    http.expectOne(`/api/ui/engagements/${id1}/fieldwork`).flush(programBasis);
    f.detectChanges();
    return { f, http, params };
  }
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
  });

  it('labels a certificate-signed report and shows its exact evidence identities', () => {
    const reportHash = 'a'.repeat(63) + '1', scanHash = 'a'.repeat(63) + '2', bundleHash = 'a'.repeat(63) + '3';
    const { f } = open({ ...validPayload,
      deliverables: [{ ...validPayload.deliverables[0], contentSha256: reportHash }],
      signedLetters: [{ ...validPayload.signedLetters[0], contentSha256: scanHash }],
      bundles: { ...validPayload.bundles, bundles: [{ ...validPayload.bundles.bundles[0], sha256: bundleHash }] },
    });
    const host = f.nativeElement as HTMLElement;
    expect(host.textContent).toContain('certificate-signed');
    expect(host.querySelector('[data-report-signing]')?.textContent).toContain('firm certificate (required)');
    expect(host.querySelector('[data-report-signing]')?.textContent).toContain('valid until 2027-10-01');
    // Exact retained identities must be copyable; a shared 12-character hash prefix is not sufficient.
    const report = host.querySelector(`[data-completion-deliverable="${id2}"]`);
    expect(report?.textContent).toContain(`Document ID: ${id2}`);
    expect(report?.querySelector('td:nth-child(4) code')?.textContent).toBe(reportHash);
    const scan = host.querySelector(`[data-signed-letter="${id1}"]`);
    expect(scan?.textContent).toContain(`Scan ID: ${id1}`);
    expect(scan?.textContent).toContain(`source document: ${id2}`);
    expect(Array.from(scan?.querySelectorAll('p code') ?? []).map(node => node.textContent)).toEqual([id1, id2, scanHash]);
    const bundle = host.querySelector(`[data-completion-bundle="${id3}"]`);
    expect(bundle?.textContent).toContain(`Bundle ID: ${id3}`);
    expect(Array.from(bundle?.querySelectorAll('code') ?? []).map(node => node.textContent)).toEqual([id3, bundleHash]);
    expect(host.querySelector('#release-state-heading')?.parentElement?.textContent).toContain(`Financial package: ${id2}`);
    f.destroy();
  });

  it('withholds signing while a required certificate is unavailable and says why', () => {
    const blocked = { ...validPayload, deliverables: [draft],
      packageEvidence: { current: false, message: 'The approved source has been returned.' }, partnerApproved: false, canPrepareRelease: false,
      reportSigning: { mode: 'CERTIFICATE', required: true, ready: false, subject: null, notAfter: null, message: 'No report-signing certificate is configured.' } };
    const { f } = open(blocked);
    const host = f.nativeElement as HTMLElement;
    const sign = Array.from(host.querySelectorAll('button')).find((b) => b.textContent?.includes('Sign with')) as HTMLButtonElement;
    expect(sign.disabled).toBe(true);
    expect(host.querySelector('[data-report-signing]')?.getAttribute('role')).toBe('alert');
    expect(host.textContent).toContain('No report-signing certificate is configured.');
    expect(host.querySelector('[data-package-evidence]')?.getAttribute('role')).toBe('alert');
    expect(host.textContent).toContain('The approved source has been returned.');
    expect(host.textContent).toContain('Current approval not established');
    expect(host.textContent).toContain('Recorded approvals remain historical evidence');
    f.destroy();
  });

  it('does not check guessed or unsigned deliverables outside the current report selection', async () => {
    const { f, http } = open({ ...validPayload, deliverables: [
      { ...validPayload.deliverables[0], id: id2, signed: false },
      { ...validPayload.deliverables[0], id: id3, kind: 'MANAGEMENT_LETTER' },
    ] });
    await f.componentInstance.verifySignature(id1);
    await f.componentInstance.verifySignature(id2);
    await f.componentInstance.verifySignature(id3);
    http.expectNone(request => request.url.endsWith('/signature'));
    expect(f.componentInstance.signatureCheck()).toBeNull(); f.destroy();
  });

  it('rejects an old signature result across A-to-B-to-A navigation without ending the newer check', async () => {
    const { f, http, params } = open(validPayload), page = f.componentInstance;
    const oldCheck = page.verifySignature(id2), oldRequest = http.expectOne(`/api/ui/deliverables/${id2}/signature`);
    params.next(convertToParamMap({ id: id3 })); f.detectChanges();
    http.expectOne(`/api/ui/engagements/${id3}/completion`).flush({ ...validPayload, engagementId: id3 });
    http.expectOne(`/api/ui/engagements/${id3}/fieldwork`).flush({ ...programBasis, engagementId: id3 }); f.detectChanges();
    params.next(convertToParamMap({ id: id1 })); f.detectChanges();
    http.expectOne(`/api/ui/engagements/${id1}/completion`).flush(validPayload);
    http.expectOne(`/api/ui/engagements/${id1}/fieldwork`).flush(programBasis); f.detectChanges();
    const newCheck = page.verifySignature(id2), newRequest = http.expectOne(`/api/ui/deliverables/${id2}/signature`);
    oldRequest.flush({ signatureKind: 'CERTIFICATE', state: 'VALID', subject: null, thumbprintSha256: null, message: 'Old result.' });
    await oldCheck; expect(page.signatureCheck()).toBeNull(); expect(page.verifying()).toBe(true);
    newRequest.flush({ signatureKind: 'CERTIFICATE', state: 'ALTERED', subject: null, thumbprintSha256: null, message: 'Current result.' });
    await newCheck; expect(page.signatureCheck()?.state).toBe('ALTERED'); expect(page.verifying()).toBe(false); f.destroy();
  });

  it('discards a signature response after the completion component is destroyed', async () => {
    const { f, http } = open(validPayload), page = f.componentInstance;
    const checking = page.verifySignature(id2), request = http.expectOne(`/api/ui/deliverables/${id2}/signature`);
    f.destroy();
    request.flush({ signatureKind: 'CERTIFICATE', state: 'VALID', subject: null, thumbprintSha256: null, message: 'Synthetic verification.' });
    await checking; expect(page.signatureCheck()).toBeNull();
  });

  it('shows the server result of a signature check and flags a changed document', async () => {
    const { f, http } = open(validPayload);
    const checking = f.componentInstance.verifySignature(id2);
    http.expectOne(`/api/ui/deliverables/${id2}/signature`).flush({ signatureKind: 'CERTIFICATE', state: 'ALTERED', subject: null, thumbprintSha256: null,
      message: 'The signature does not match the document: it was changed after signing.' });
    await checking;
    f.detectChanges();
    const result = (f.nativeElement as HTMLElement).querySelector('[data-signature-check]');
    expect(result?.getAttribute('role')).toBe('alert');
    expect(result?.textContent).toContain('Document changed after signing');
    f.destroy();
  });
});

describe('Completion variance investigation panel (R03)', () => {
  afterEach(() => { try { TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true }); } finally { TestBed.resetTestingModule(); sessionStorage.clear(); } });

  it('loads the exact-scope panel independently when the completion projection is denied and preserves exact decimals and revisions', async () => {
    const params = new BehaviorSubject(convertToParamMap({ id: id1 }));
    TestBed.configureTestingModule({ imports: [EngagementCompletion], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: ActivatedRoute, useValue: { paramMap: params } }] });
    const session = TestBed.inject(SessionService);
    session.current.set({ userId: id3, firmId: id2, generation: '9223372036854775806', staff: true });
    const http = TestBed.inject(HttpTestingController), fixture = TestBed.createComponent(EngagementCompletion);
    fixture.detectChanges();
    http.expectOne(`/api/ui/engagements/${id1}/completion`).flush({ code: 'scope.denied' }, { status: 403, statusText: 'Forbidden' });
    http.expectOne(`/api/ui/engagements/${id1}/fieldwork`).flush({ code: 'scope.denied' }, { status: 403, statusText: 'Forbidden' });
    const page = fixture.componentInstance;
    page.varianceForm.accountArea = 'revenue'; page.varianceForm.periodReference = '2026-q1';
    const loading = page.loadVarianceState();
    http.expectOne(`/api/ui/engagements/${id1}/completion/variance-investigations/state?accountArea=REVENUE&periodReference=2026-Q1`).flush({
      engagementId: id1, clientId: id2, accountArea: 'REVENUE', periodReference: '2026-Q1', expectedRevision: '9223372036854775807',
      reviewBasis: hash64, record: { id: id3, clientId: id2, engagementId: id1, procedureId: null, accountArea: 'REVENUE', periodReference: '2026-Q1',
        expectedAmount: '100.000001', actualAmount: '112.000009', differenceAmount: '12.000008', differencePercent: '12.0000',
        investigationThreshold: '10.000000', exceedsThreshold: true, explanation: 'Reconciled with the retained source.', conclusion: 'CORROBORATED',
        currency: 'QAR', evidenceReferences: ['procedure-result:exact'], revision: '9223372036854775807', recordedByUserId: id3,
        reviewedByUserId: null, recordedAt: '2026-11-10T10:00:00Z', reviewedAt: null, reviewedRevision: null },
      canPrepare: false, canReview: false, blockers: ['A different current reviewer is required.'],
    });
    await loading; fixture.detectChanges();
    expect(page.varianceState()?.expectedRevision).toBe('9223372036854775807');
    expect(page.varianceState()?.record?.expectedAmount).toBe('100.000001');
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-variance-investigation]')?.textContent).toContain('100.000001 QAR');
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-variance-investigation]')?.textContent).toContain('Not independently reviewed.');
    expect(page.ws.error()).toContain('authorized internal staff identity');
    fixture.destroy();
  });

  it('preserves an unresolved reference across a session epoch change and clears the old record immediately', async () => {
    const params = new BehaviorSubject(convertToParamMap({ id: id1 }));
    TestBed.configureTestingModule({ imports: [EngagementCompletion], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: ActivatedRoute, useValue: { paramMap: params } }] });
    const session = TestBed.inject(SessionService);
    session.current.set({ userId: id3, firmId: id2, generation: '1', staff: true });
    const fixture = TestBed.createComponent(EngagementCompletion); fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(`/api/ui/engagements/${id1}/completion`).flush({ code: 'scope.denied' }, { status: 403, statusText: 'Forbidden' });
    http.expectOne(`/api/ui/engagements/${id1}/fieldwork`).flush({ code: 'scope.denied' }, { status: 403, statusText: 'Forbidden' });
    const page = fixture.componentInstance;
    const key = `auditsphere-variance-pending-v1:${id2}:${id3}:${id1}`;
    const pending = { version: 2, firmId: id2, actorId: id3, engagementId: id1, accountArea: 'REVENUE', periodReference: '2026-Q1',
      requestId: '44444444-4444-4444-8444-444444444444', requestHash: hash64, action: 'PREPARE', varianceId: null, actorEpoch: '1',
      expectedRevision: '0', reviewBasis: hash64, basisContentHash: null, resultContentHash: hash64 };
    sessionStorage.setItem(key, JSON.stringify(pending));
    page.varianceState.set({ engagementId: id1, clientId: id2, accountArea: 'REVENUE', periodReference: '2026-Q1', expectedRevision: '1',
      reviewBasis: hash64, record: null, canPrepare: true, canReview: false, blockers: [] });
    session.current.set({ userId: id3, firmId: id2, generation: '2', staff: true });
    session.invalidation.update(x => x + 1); fixture.detectChanges();
    expect(page.varianceState()).toBeNull();
    expect(page.variancePending()?.requestId).toBe(pending.requestId);
    expect(sessionStorage.getItem(key)).not.toBeNull();
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-variance-pending-reference]')?.textContent).toContain('REVENUE / 2026-Q1');
    const loadingPending = page.loadPendingVarianceScope();
    expect(page.varianceForm.accountArea).toBe('REVENUE'); expect(page.varianceForm.periodReference).toBe('2026-Q1');
    http.expectOne(`/api/ui/engagements/${id1}/completion/variance-investigations/state?accountArea=REVENUE&periodReference=2026-Q1`).flush({
      engagementId: id1, clientId: id2, accountArea: 'REVENUE', periodReference: '2026-Q1', expectedRevision: '0', reviewBasis: hash64,
      record: null, canPrepare: true, canReview: false, blockers: [],
    });
    await loadingPending;
    fixture.destroy();
  });

  it('keeps an unknown preparation fenced and validates recovery against the original preview after the basis advances', async () => {
    const params = new BehaviorSubject(convertToParamMap({ id: id1 }));
    TestBed.configureTestingModule({ imports: [EngagementCompletion], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: ActivatedRoute, useValue: { paramMap: params } }] });
    const session = TestBed.inject(SessionService);
    session.current.set({ userId: id3, firmId: id2, generation: '1', staff: true });
    const http = TestBed.inject(HttpTestingController), fixture = TestBed.createComponent(EngagementCompletion);
    fixture.detectChanges();
    http.expectOne(`/api/ui/engagements/${id1}/completion`).flush({ code: 'scope.denied' }, { status: 403, statusText: 'Forbidden' });
    http.expectOne(`/api/ui/engagements/${id1}/fieldwork`).flush({ code: 'scope.denied' }, { status: 403, statusText: 'Forbidden' });
    const page = fixture.componentInstance;
    Object.assign(page.varianceForm, { accountArea: 'REVENUE', periodReference: '\uFEFF2026-Q1', expectedAmount: '100.000001', actualAmount: '112.000009',
      investigationThreshold: '10.000000', currency: 'QAR', explanation: '\u0085', evidenceReferences: 'procedure-result:exact' });
    const periodReference = '\uFEFF2026-Q1';
    const encodedPeriodReference = '%EF%BB%BF2026-Q1';
    const firstState = { engagementId: id1, clientId: id2, accountArea: 'REVENUE', periodReference, expectedRevision: '0',
      reviewBasis: hash64, record: null, canPrepare: true, canReview: false, blockers: [] };
    const stateLoad = page.loadVarianceState();
    http.expectOne(`/api/ui/engagements/${id1}/completion/variance-investigations/state?accountArea=REVENUE&periodReference=${encodedPeriodReference}`).flush(firstState);
    await stateLoad;
    expect(page.varianceInputsValid()).toBe(true);
    page.varianceForm.expectedAmount = '100.0000001'; expect(page.varianceInputsValid()).toBe(false);
    page.varianceForm.expectedAmount = '100.000001'; expect(page.varianceInputsValid()).toBe(true);
    const previewing = page.previewVariancePreparation();
    const previewRequest = http.expectOne(`/api/ui/engagements/${id1}/completion/variance-investigations/prepare/preview`);
    const requestId = previewRequest.request.body.requestId as string;
    expect(previewRequest.request.body.fields.periodReference).toBe(periodReference);
    expect(previewRequest.request.body.fields.explanation).toBeNull();
    previewRequest.flush({ value: { requestId, action: 'PREPARE', requestHash: hash64, reviewBasis: hash64, expectedRevision: '0', canProceed: true,
      blockers: [], currentRecord: null, summary: 'Prepare the current variance investigation.' } });
    await previewing; page.varianceAssent = true;
    const submitting = page.submitVarianceCommand();
    const command = http.expectOne(`/api/ui/engagements/${id1}/completion/variance-investigations/prepare`);
    expect(command.request.body.reviewed).toBe(true);
    expect(command.request.body.expectedRequestHash).toBe(hash64);
    command.flush({ message: 'simulated lost acknowledgement' }, { status: 500, statusText: 'Server Error' });
    await submitting;
    const storageKey = `auditsphere-variance-pending-v1:${id2}:${id3}:${id1}`;
    const raw = sessionStorage.getItem(storageKey)!;
    const pending = JSON.parse(raw);
    expect(pending.expectedRevision).toBe('0'); expect(pending.reviewBasis).toBe(hash64);
    expect(page.varianceBasisCurrent()).toBe(false);
    expect(page.varianceState()).not.toBeNull();

    session.clear(); fixture.detectChanges();
    session.current.set({ userId: id3, firmId: id2, generation: '1', staff: true }); fixture.detectChanges();
    expect(page.varianceState()).toBeNull(); expect(page.variancePending()?.requestId).toBe(requestId);
    page.varianceForm.accountArea = 'REVENUE'; page.varianceForm.periodReference = periodReference;
    const advanced = { ...firstState, expectedRevision: '1', reviewBasis: 'e'.repeat(64), canPrepare: true,
      record: { id: id1, clientId: id2, engagementId: id1, procedureId: null, accountArea: 'REVENUE', periodReference,
        expectedAmount: '100.000001', actualAmount: '112.000009', differenceAmount: '12.000008', differencePercent: '12.0000',
        investigationThreshold: '10.000000', exceedsThreshold: true, explanation: null, conclusion: 'UNEXPLAINED', currency: 'QAR',
        evidenceReferences: ['procedure-result:exact'], revision: '1', recordedByUserId: id3, reviewedByUserId: null,
        recordedAt: '2026-11-10T10:00:00Z', reviewedAt: null, reviewedRevision: null } };
    const secondState = page.loadVarianceState();
    http.expectOne(`/api/ui/engagements/${id1}/completion/variance-investigations/state?accountArea=REVENUE&periodReference=${encodedPeriodReference}`).flush(advanced);
    await secondState;
    const lookup = page.lookupVarianceRequest();
    const receiptRequest = http.expectOne(`/api/ui/engagements/${id1}/completion/variance-investigations/receipts/${requestId}?requestHash=${hash64}`);
    const receipt = { id: id2, engagementId: id1, varianceInvestigationId: id1, actorId: id3, actorEpoch: '1', requestId,
      requestHash: hash64, action: 'PREPARE', createdAt: '2026-11-10T10:00:00Z',
      preview: { requestId, action: 'PREPARE', requestHash: hash64, reviewBasis: hash64, expectedRevision: '0', canProceed: true, blockers: [], currentRecord: null, summary: 'Prepare.' },
      result: advanced.record };
    const replacementReference = JSON.stringify({ ...pending, requestId: '55555555-5555-4555-8555-555555555555', requestHash: 'a'.repeat(64) });
    sessionStorage.setItem(storageKey, replacementReference);
    receiptRequest.flush({ found: true, receipt });
    await lookup;
    expect(sessionStorage.getItem(storageKey)).toBe(replacementReference);
    expect(page.variancePending()?.requestId).toBe(requestId);
    expect(page.varianceError()).toContain('could not be cleared safely');
    http.expectNone(`/api/ui/engagements/${id1}/completion/variance-investigations/state?accountArea=REVENUE&periodReference=2026-Q1`);

    sessionStorage.setItem(storageKey, raw);
    const confirmedLookup = page.lookupVarianceRequest();
    const confirmedReceiptRequest = http.expectOne(`/api/ui/engagements/${id1}/completion/variance-investigations/receipts/${requestId}?requestHash=${hash64}`);
    const refreshedRequest = watchVarianceStateReload(page);
    confirmedReceiptRequest.flush({ found: true, receipt });
    await refreshedRequest.requested;
    const refreshedState = http.expectOne(`/api/ui/engagements/${id1}/completion/variance-investigations/state?accountArea=REVENUE&periodReference=${encodedPeriodReference}`);
    refreshedState.flush(advanced);
    await confirmedLookup;
    refreshedRequest.restore();
    expect(sessionStorage.getItem(storageKey)).toBeNull();
    expect(page.varianceState()?.expectedRevision).toBe('1');
    expect(page.varianceMessage()).toContain('confirmed the retained prepare request');
    fixture.destroy();
  });

  it('rejects late A-B-A variance state responses', async () => {
    const params = new BehaviorSubject(convertToParamMap({ id: id1 }));
    TestBed.configureTestingModule({ imports: [EngagementCompletion], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: ActivatedRoute, useValue: { paramMap: params } }] });
    const session = TestBed.inject(SessionService);
    session.current.set({ userId: id3, firmId: id2, generation: '1', staff: true });
    const http = TestBed.inject(HttpTestingController), fixture = TestBed.createComponent(EngagementCompletion);
    fixture.detectChanges();
    http.expectOne(`/api/ui/engagements/${id1}/completion`).flush({ code: 'scope.denied' }, { status: 403, statusText: 'Forbidden' });
    http.expectOne(`/api/ui/engagements/${id1}/fieldwork`).flush({ code: 'scope.denied' }, { status: 403, statusText: 'Forbidden' });
    const page = fixture.componentInstance;
    page.varianceForm.periodReference = '2026-Q1'; page.varianceForm.accountArea = 'A';
    const a1 = page.loadVarianceState(), r1 = http.expectOne(req => req.url.includes('/variance-investigations/state?accountArea=A'));
    page.varianceForm.accountArea = 'B'; page.varianceSelectionChanged();
    const b = page.loadVarianceState(), r2 = http.expectOne(req => req.url.includes('/variance-investigations/state?accountArea=B'));
    page.varianceForm.accountArea = 'A'; page.varianceSelectionChanged();
    const a2 = page.loadVarianceState(), r3 = http.expectOne(req => req.url.includes('/variance-investigations/state?accountArea=A'));
    const payload = (hash: string) => ({ engagementId: id1, clientId: id2, accountArea: page.varianceForm.accountArea,
      periodReference: '2026-Q1', expectedRevision: '0', reviewBasis: hash, record: null, canPrepare: true, canReview: false, blockers: [] });
    r1.flush(payload('a'.repeat(64))); await a1; expect(page.varianceState()).toBeNull();
    r2.flush(payload('b'.repeat(64))); await b; expect(page.varianceState()).toBeNull();
    r3.flush(payload('c'.repeat(64))); await a2; expect(page.varianceState()?.reviewBasis).toBe('c'.repeat(64));

    fixture.destroy();
  });

  it('confirms a successful independent review only for the exact reviewer, prior revision and authoritative receipt', async () => {
    const params = new BehaviorSubject(convertToParamMap({ id: id1 }));
    TestBed.configureTestingModule({ imports: [EngagementCompletion], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: ActivatedRoute, useValue: { paramMap: params } }] });
    const session = TestBed.inject(SessionService);
    session.current.set({ userId: id3, firmId: id2, generation: '1', staff: true });
    const http = TestBed.inject(HttpTestingController), fixture = TestBed.createComponent(EngagementCompletion);
    fixture.detectChanges();
    http.expectOne(`/api/ui/engagements/${id1}/completion`).flush({ code: 'scope.denied' }, { status: 403, statusText: 'Forbidden' });
    http.expectOne(`/api/ui/engagements/${id1}/fieldwork`).flush({ code: 'scope.denied' }, { status: 403, statusText: 'Forbidden' });
    const page = fixture.componentInstance;
    const original = { id: id2, clientId: id2, engagementId: id1, procedureId: null, accountArea: 'REVENUE', periodReference: '2026-Q1',
      expectedAmount: '100.000001', actualAmount: '112.000009', differenceAmount: '12.000008', differencePercent: '12.0000',
      investigationThreshold: '10.000000', exceedsThreshold: true, explanation: 'Reconciled to exact evidence.', conclusion: 'CORROBORATED' as const, currency: 'QAR',
      evidenceReferences: ['procedure-result:exact'], revision: '4', recordedByUserId: id1, reviewedByUserId: null,
      recordedAt: '2026-11-10T10:00:00Z', reviewedAt: null, reviewedRevision: null };
    const current = { engagementId: id1, clientId: id2, accountArea: 'REVENUE', periodReference: '2026-Q1', expectedRevision: '4',
      reviewBasis: hash64, record: original, canPrepare: false, canReview: true, blockers: [] };
    page.varianceForm.accountArea = 'REVENUE'; page.varianceForm.periodReference = '2026-Q1';
    const stateLoad = page.loadVarianceState();
    http.expectOne(`/api/ui/engagements/${id1}/completion/variance-investigations/state?accountArea=REVENUE&periodReference=2026-Q1`).flush(current);
    await stateLoad;
    const previewing = page.previewVarianceReview();
    const reviewPreview = http.expectOne(`/api/ui/engagements/${id1}/completion/variance-investigations/${id2}/review/preview`);
    const changedPreviewRecord = { ...original, actualAmount: '999.000000', evidenceReferences: ['procedure-result:other'] };
    reviewPreview.flush({ value: { requestId: reviewPreview.request.body.requestId, action: 'REVIEW', requestHash: 'a'.repeat(64), reviewBasis: hash64,
      expectedRevision: '4', canProceed: true, blockers: [], currentRecord: changedPreviewRecord, summary: 'Review exact current evidence.' } });
    await previewing;
    expect(page.variancePreview()).toBeNull();
    expect(page.varianceError()).toContain('exact current variance intent');

    const exactPreviewing = page.previewVarianceReview();
    const exactReviewPreview = http.expectOne(`/api/ui/engagements/${id1}/completion/variance-investigations/${id2}/review/preview`);
    const requestId = exactReviewPreview.request.body.requestId as string;
    exactReviewPreview.flush({ value: { requestId, action: 'REVIEW', requestHash: 'a'.repeat(64), reviewBasis: hash64, expectedRevision: '4',
      canProceed: true, blockers: [], currentRecord: original, summary: 'Review exact current evidence.' } });
    await exactPreviewing; page.varianceAssent = true;
    const submitting = page.submitVarianceCommand();
    const command = http.expectOne(`/api/ui/engagements/${id1}/completion/variance-investigations/${id2}/review`);
    expect(command.request.body.expectedRevision).toBe('4'); expect(command.request.body.reviewed).toBe(true);
    const reviewed = { ...original, revision: '5', reviewedByUserId: id3, reviewedAt: '2026-11-10T11:00:00Z', reviewedRevision: '4' };
    const receipt = { id: id3, engagementId: id1, varianceInvestigationId: id2, actorId: id3, actorEpoch: '1', requestId,
      requestHash: 'a'.repeat(64), action: 'REVIEW', createdAt: '2026-11-10T11:00:00Z',
      preview: { requestId, action: 'REVIEW', requestHash: 'a'.repeat(64), reviewBasis: hash64, expectedRevision: '4', canProceed: true,
        blockers: [], currentRecord: original, summary: 'Review exact current evidence.' },
      result: { ...reviewed, actualAmount: '999.000000', evidenceReferences: ['procedure-result:other'] } };
    command.flush({ value: receipt });
    await submitting;
    expect(page.variancePending()?.requestId).toBe(requestId);
    expect(sessionStorage.length).toBe(1);
    expect(page.varianceMessage()).not.toContain('server confirmed');
    expect(page.varianceError()).toContain('cannot confirm this exact variance command');
    http.expectNone(req => req.url.includes('/variance-investigations/state?'));

    const recovering = page.lookupVarianceRequest();
    const receiptLookup = http.expectOne(`/api/ui/engagements/${id1}/completion/variance-investigations/receipts/${requestId}?requestHash=${'a'.repeat(64)}`);
    const refreshedRequest = watchVarianceStateReload(page);
    receiptLookup.flush({ found: true, receipt: { ...receipt, result: reviewed } });
    const currentReviewed = { ...current, expectedRevision: '5', reviewBasis: 'e'.repeat(64), record: reviewed, canReview: false };
    await refreshedRequest.requested;
    const refreshed = http.expectOne(`/api/ui/engagements/${id1}/completion/variance-investigations/state?accountArea=REVENUE&periodReference=2026-Q1`);
    expect(page.isVarianceReviewCurrent(reviewed)).toBe(true);
    page.varianceForm.accountArea = 'CASH'; page.varianceSelectionChanged();
    refreshed.flush(currentReviewed);
    await recovering; fixture.detectChanges();
    refreshedRequest.restore();
    expect(page.varianceState()).toBeNull();
    expect(page.varianceMessage()).toContain('Current state was not refreshed for the selected scope');
    expect(page.varianceMessage()).not.toContain('exact variance state was refreshed');
    expect(page.variancePending()).toBeNull(); expect(sessionStorage.length).toBe(0);
    fixture.destroy();
  });

  it('keeps an exact recovery reference when the session changes during command or lookup receipt hashing', async () => {
    const params = new BehaviorSubject(convertToParamMap({ id: id1 }));
    TestBed.configureTestingModule({ imports: [EngagementCompletion], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: ActivatedRoute, useValue: { paramMap: params } }] });
    const session = TestBed.inject(SessionService);
    session.current.set({ userId: id3, firmId: id2, generation: '1', staff: true });
    const http = TestBed.inject(HttpTestingController), fixture = TestBed.createComponent(EngagementCompletion);
    fixture.detectChanges();
    http.expectOne(`/api/ui/engagements/${id1}/completion`).flush({ code: 'scope.denied' }, { status: 403, statusText: 'Forbidden' });
    http.expectOne(`/api/ui/engagements/${id1}/fieldwork`).flush({ code: 'scope.denied' }, { status: 403, statusText: 'Forbidden' });
    const page = fixture.componentInstance;
    Object.assign(page.varianceForm, { accountArea: 'REVENUE', periodReference: '2026-Q1', expectedAmount: '100.000001', actualAmount: '112.000009',
      investigationThreshold: '10.000000', currency: 'QAR', explanation: 'Reconciled.', evidenceReferences: 'procedure-result:exact' });
    const state = page.loadVarianceState();
    http.expectOne(req => req.url.includes('/variance-investigations/state?')).flush({ engagementId: id1, clientId: id2,
      accountArea: 'REVENUE', periodReference: '2026-Q1', expectedRevision: '0', reviewBasis: hash64, record: null,
      canPrepare: true, canReview: false, blockers: [] });
    await state;
    const previewing = page.previewVariancePreparation(), previewRequest = http.expectOne(req => req.url.endsWith('/prepare/preview'));
    const requestId = previewRequest.request.body.requestId as string;
    previewRequest.flush({ value: { requestId, action: 'PREPARE', requestHash: 'a'.repeat(64), reviewBasis: hash64, expectedRevision: '0',
      canProceed: true, blockers: [], currentRecord: null, summary: 'Prepare exact variance.' } });
    await previewing; page.varianceAssent = true;

    const holdNextReceiptDigest = () => {
      const realDigest = crypto.subtle.digest.bind(crypto.subtle);
      let release!: () => void, started!: () => void;
      const gate = new Promise<void>(resolve => { release = resolve; });
      const digestStarted = new Promise<void>(resolve => { started = resolve; });
      let count = 0;
      const spy = vi.spyOn(crypto.subtle, 'digest').mockImplementation((algorithm, data) =>
        realDigest(algorithm, data).then(async result => {
          if (++count === 1) started();
          await gate;
          return result;
        }));
      return { digestStarted, release, restore: () => spy.mockRestore() };
    };
    const result = { id: id2, clientId: id2, engagementId: id1, procedureId: null, accountArea: 'REVENUE', periodReference: '2026-Q1',
      expectedAmount: '100.000001', actualAmount: '112.000009', differenceAmount: '12.000008', differencePercent: '12.0000',
      investigationThreshold: '10.000000', exceedsThreshold: true, explanation: 'Reconciled.', conclusion: 'CORROBORATED', currency: 'QAR',
      evidenceReferences: ['procedure-result:exact'], revision: '1', recordedByUserId: id3, reviewedByUserId: null,
      recordedAt: '2026-11-10T10:00:00Z', reviewedAt: null, reviewedRevision: null };
    const receipt = { id: id3, engagementId: id1, varianceInvestigationId: id2, actorId: id3, actorEpoch: '1', requestId,
      requestHash: 'a'.repeat(64), action: 'PREPARE', createdAt: '2026-11-10T10:00:00Z',
      preview: { requestId, action: 'PREPARE', requestHash: 'a'.repeat(64), reviewBasis: hash64, expectedRevision: '0',
        canProceed: true, blockers: [], currentRecord: null, summary: 'Prepare exact variance.' }, result };
    const key = `auditsphere-variance-pending-v1:${id2}:${id3}:${id1}`;

    const commandHash = holdNextReceiptDigest();
    const submitting = page.submitVarianceCommand(), command = http.expectOne(req => req.url.endsWith('/variance-investigations/prepare'));
    command.flush({ value: receipt });
    await commandHash.digestStarted;
    const commandReference = sessionStorage.getItem(key)!;
    session.current.set({ userId: id3, firmId: id2, generation: '2', staff: true });
    session.invalidation.update(value => value + 1); fixture.detectChanges();
    commandHash.release(); await submitting; commandHash.restore();
    expect(sessionStorage.getItem(key)).toBe(commandReference);
    expect(page.variancePending()?.requestId).toBe(requestId);
    expect(page.varianceMessage()).not.toContain('server confirmed');
    http.expectNone(req => req.url.includes('/variance-investigations/state?'));

    session.current.set({ userId: id3, firmId: id2, generation: '1', staff: true });
    session.invalidation.update(value => value + 1); fixture.detectChanges();
    const loadingPending = page.loadPendingVarianceScope();
    http.expectOne(req => req.url.includes('/variance-investigations/state?')).flush({ engagementId: id1, clientId: id2,
      accountArea: 'REVENUE', periodReference: '2026-Q1', expectedRevision: '1', reviewBasis: 'b'.repeat(64), record: result,
      canPrepare: true, canReview: false, blockers: [] });
    await loadingPending;

    const lookupHash = holdNextReceiptDigest();
    const lookup = page.lookupVarianceRequest();
    http.expectOne(req => req.url.includes(`/receipts/${requestId}?`)).flush({ found: true, receipt });
    await lookupHash.digestStarted;
    const lookupReference = sessionStorage.getItem(key)!;
    session.current.set({ userId: id3, firmId: id2, generation: '2', staff: true });
    session.invalidation.update(value => value + 1); fixture.detectChanges();
    lookupHash.release(); await lookup; lookupHash.restore();
    expect(sessionStorage.getItem(key)).toBe(lookupReference);
    expect(page.variancePending()?.requestId).toBe(requestId);
    expect(page.varianceMessage()).not.toContain('server confirmed');
    http.expectNone(req => req.url.includes('/variance-investigations/state?'));
    fixture.destroy();
  });

  it('retains the recovery reference when a review receipt contains an unsupported conclusion', async () => {
    const params = new BehaviorSubject(convertToParamMap({ id: id1 }));
    TestBed.configureTestingModule({ imports: [EngagementCompletion], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: ActivatedRoute, useValue: { paramMap: params } }] });
    const session = TestBed.inject(SessionService);
    session.current.set({ userId: id3, firmId: id2, generation: '1', staff: true });
    const http = TestBed.inject(HttpTestingController), fixture = TestBed.createComponent(EngagementCompletion);
    fixture.detectChanges();
    http.expectOne(`/api/ui/engagements/${id1}/completion`).flush({ code: 'scope.denied' }, { status: 403, statusText: 'Forbidden' });
    http.expectOne(`/api/ui/engagements/${id1}/fieldwork`).flush({ code: 'scope.denied' }, { status: 403, statusText: 'Forbidden' });
    const page = fixture.componentInstance;
    const original = { id: id2, clientId: id2, engagementId: id1, procedureId: null, accountArea: 'REVENUE', periodReference: '2026-Q1',
      expectedAmount: '100.000001', actualAmount: '112.000009', differenceAmount: '12.000008', differencePercent: '12.0000',
      investigationThreshold: '10.000000', exceedsThreshold: true, explanation: 'Reconciled to exact evidence.', conclusion: 'CORROBORATED' as const, currency: 'QAR',
      evidenceReferences: ['procedure-result:exact'], revision: '4', recordedByUserId: id1, reviewedByUserId: null,
      recordedAt: '2026-11-10T10:00:00Z', reviewedAt: null, reviewedRevision: null };
    page.varianceForm.accountArea = 'REVENUE'; page.varianceForm.periodReference = '2026-Q1';
    const stateLoad = page.loadVarianceState();
    http.expectOne(`/api/ui/engagements/${id1}/completion/variance-investigations/state?accountArea=REVENUE&periodReference=2026-Q1`).flush({
      engagementId: id1, clientId: id2, accountArea: 'REVENUE', periodReference: '2026-Q1', expectedRevision: '4',
      reviewBasis: hash64, record: original, canPrepare: false, canReview: true, blockers: [],
    });
    await stateLoad;
    const previewing = page.previewVarianceReview();
    const reviewPreview = http.expectOne(`/api/ui/engagements/${id1}/completion/variance-investigations/${id2}/review/preview`);
    const requestId = reviewPreview.request.body.requestId as string;
    reviewPreview.flush({ value: { requestId, action: 'REVIEW', requestHash: 'a'.repeat(64), reviewBasis: hash64, expectedRevision: '4',
      canProceed: true, blockers: [], currentRecord: original, summary: 'Review exact current evidence.' } });
    await previewing; page.varianceAssent = true;
    const submitting = page.submitVarianceCommand();
    http.expectOne(`/api/ui/engagements/${id1}/completion/variance-investigations/${id2}/review`).flush({ value: {
      id: id3, engagementId: id1, varianceInvestigationId: id2, actorId: id3, actorEpoch: '1', requestId,
      requestHash: 'a'.repeat(64), action: 'REVIEW', createdAt: '2026-11-10T11:00:00Z',
      preview: { requestId, action: 'REVIEW', requestHash: 'a'.repeat(64), reviewBasis: hash64, expectedRevision: '4', canProceed: true,
        blockers: [], currentRecord: original, summary: 'Review exact current evidence.' },
      result: { ...original, conclusion: 'NOT_ASSESSED', revision: '5', reviewedByUserId: id3,
        reviewedAt: '2026-11-10T11:00:00Z', reviewedRevision: '4' },
    } });
    await submitting;
    expect(page.variancePending()?.requestId).toBe(requestId);
    expect(sessionStorage.length).toBe(1);
    expect(page.varianceMessage()).not.toContain('server confirmed');
    expect(page.varianceError()).toBeTruthy();
    http.expectNone(req => req.url.includes('/variance-investigations/state?'));
    fixture.destroy();
  });

  it('retires only typed first-send refusals, not arbitrary conflicts or idempotency conflicts', () => {
    const params = new BehaviorSubject(convertToParamMap({ id: id1 }));
    TestBed.configureTestingModule({ imports: [EngagementCompletion], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: ActivatedRoute, useValue: { paramMap: params } }] });
    TestBed.inject(SessionService).current.set({ userId: id3, firmId: id2, generation: '1', staff: true });
    const fixture = TestBed.createComponent(EngagementCompletion); fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(`/api/ui/engagements/${id1}/completion`).flush({ code: 'scope.denied' }, { status: 403, statusText: 'Forbidden' });
    http.expectOne(`/api/ui/engagements/${id1}/fieldwork`).flush({ code: 'scope.denied' }, { status: 403, statusText: 'Forbidden' });
    const classify = (fixture.componentInstance as unknown as { definitiveVarianceRefusal: (value: { ok: false; unknown: false; code: string; status: number }) => boolean }).definitiveVarianceRefusal.bind(fixture.componentInstance);
    expect(classify({ ok: false, unknown: false, code: 'revision.stale', status: 409 })).toBe(true);
    expect(classify({ ok: false, unknown: false, code: 'generation.stale', status: 409 })).toBe(true);
    expect(classify({ ok: false, unknown: false, code: 'idempotency.conflict', status: 409 })).toBe(false);
    expect(classify({ ok: false, unknown: false, code: 'another.conflict', status: 409 })).toBe(false);
    fixture.destroy();
  });

  it('fails closed when recovery storage cannot persist the exact request before command dispatch', async () => {
    const params = new BehaviorSubject(convertToParamMap({ id: id1 }));
    TestBed.configureTestingModule({ imports: [EngagementCompletion], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: ActivatedRoute, useValue: { paramMap: params } }] });
    TestBed.inject(SessionService).current.set({ userId: id3, firmId: id2, generation: '1', staff: true });
    const http = TestBed.inject(HttpTestingController), fixture = TestBed.createComponent(EngagementCompletion);
    fixture.detectChanges();
    http.expectOne(`/api/ui/engagements/${id1}/completion`).flush({ code: 'scope.denied' }, { status: 403, statusText: 'Forbidden' });
    http.expectOne(`/api/ui/engagements/${id1}/fieldwork`).flush({ code: 'scope.denied' }, { status: 403, statusText: 'Forbidden' });
    const page = fixture.componentInstance;
    Object.assign(page.varianceForm, { accountArea: 'REVENUE', periodReference: '2026-Q1', expectedAmount: '100.000001', actualAmount: '112.000009',
      investigationThreshold: '10.000000', currency: 'QAR', explanation: 'Reconciled.', evidenceReferences: 'procedure-result:exact' });
    const state = page.loadVarianceState();
    http.expectOne(req => req.url.includes('/variance-investigations/state')).flush({ engagementId: id1, clientId: id2, accountArea: 'REVENUE',
      periodReference: '2026-Q1', expectedRevision: '0', reviewBasis: hash64, record: null, canPrepare: true, canReview: false, blockers: [] });
    await state;
    const previewing = page.previewVariancePreparation(), previewRequest = http.expectOne(req => req.url.endsWith('/prepare/preview'));
    const requestId = previewRequest.request.body.requestId as string;
    previewRequest.flush({ value: { requestId, action: 'PREPARE', requestHash: hash64, reviewBasis: hash64, expectedRevision: '0', canProceed: true,
      blockers: [], currentRecord: null, summary: 'Prepare.' } });
    await previewing; page.varianceAssent = true;
    const storage = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new DOMException('quota', 'QuotaExceededError'); });
    await page.submitVarianceCommand(); storage.mockRestore();
    http.expectNone(req => req.url.endsWith('/variance-investigations/prepare'));
    expect(page.variancePending()).toBeNull(); expect(page.varianceRecoveryUnavailable()).toBe(true);
    expect(page.varianceError()).toContain('No command was sent');
    fixture.destroy();
  });

  it('retires a typed 409 stale first-send refusal after refreshing, while preserving ambiguous conflicts', async () => {
    const params = new BehaviorSubject(convertToParamMap({ id: id1 }));
    TestBed.configureTestingModule({ imports: [EngagementCompletion], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: ActivatedRoute, useValue: { paramMap: params } }] });
    TestBed.inject(SessionService).current.set({ userId: id3, firmId: id2, generation: '1', staff: true });
    const http = TestBed.inject(HttpTestingController), fixture = TestBed.createComponent(EngagementCompletion);
    fixture.detectChanges();
    http.expectOne(`/api/ui/engagements/${id1}/completion`).flush({ code: 'scope.denied' }, { status: 403, statusText: 'Forbidden' });
    http.expectOne(`/api/ui/engagements/${id1}/fieldwork`).flush({ code: 'scope.denied' }, { status: 403, statusText: 'Forbidden' });
    const page = fixture.componentInstance;
    Object.assign(page.varianceForm, { accountArea: 'REVENUE', periodReference: '2026-Q1', expectedAmount: '100.000001', actualAmount: '112.000009',
      investigationThreshold: '10.000000', currency: 'QAR', explanation: 'Reconciled.', evidenceReferences: 'procedure-result:exact' });
    const state = page.loadVarianceState();
    http.expectOne(req => req.url.includes('/variance-investigations/state')).flush({ engagementId: id1, clientId: id2, accountArea: 'REVENUE',
      periodReference: '2026-Q1', expectedRevision: '0', reviewBasis: hash64, record: null, canPrepare: true, canReview: false, blockers: [] });
    await state;
    const previewing = page.previewVariancePreparation(), previewRequest = http.expectOne(req => req.url.endsWith('/prepare/preview'));
    const requestId = previewRequest.request.body.requestId as string;
    previewRequest.flush({ value: { requestId, action: 'PREPARE', requestHash: hash64, reviewBasis: hash64, expectedRevision: '0', canProceed: true,
      blockers: [], currentRecord: null, summary: 'Prepare.' } });
    await previewing; page.varianceAssent = true;
    const submitting = page.submitVarianceCommand(), command = http.expectOne(req => req.url.endsWith('/variance-investigations/prepare'));
    const refreshedRequest = watchVarianceStateReload(page);
    command.flush({ code: 'revision.stale', message: 'The exact source revision changed.' }, { status: 409, statusText: 'Conflict' });
    await refreshedRequest.requested;
    const refresh = http.expectOne(req => req.url.includes('/variance-investigations/state'));
    refresh.flush({ engagementId: id1, clientId: id2, accountArea: 'REVENUE', periodReference: '2026-Q1', expectedRevision: '0', reviewBasis: 'e'.repeat(64),
      record: null, canPrepare: true, canReview: false, blockers: [] });
    await submitting;
    refreshedRequest.restore();
    expect(page.variancePending()).toBeNull(); expect(page.varianceBasisCurrent()).toBe(true);
    expect(page.varianceMessage()).toContain('definitively refused the first request');
    expect(page.varianceError()).toBe('This record changed since you loaded it. Refresh and review the current revision.');
    expect(sessionStorage.length).toBe(0);
    fixture.destroy();
  });
});

