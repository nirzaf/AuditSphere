import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { afterEach, describe, expect, it } from 'vitest';
import { SessionService } from '../../core/session';
import { EngagementCompletion, decodeCompletion, decodeSignatureCheck } from './completion';

const id1 = '11111111-1111-4111-8111-111111111111';
const id2 = '22222222-2222-4222-8222-222222222222';
const id3 = '33333333-3333-4333-8333-333333333333';
const hash64 = 'f'.repeat(64);

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
          code: 'REP-01',
          title: 'Management Responsibilities',
          narrative: 'Management acknowledges its responsibility for the financial statements.',
          obtained: true,
        },
      ],
      packageId: id2,
      packageStatus: 'VALIDATED',
      partnerApproved: true,
      eqrStatus: 'CONCURRED',
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
        clearedAt: '2026-10-02T12:00:00Z',
      },
      opinion: {
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
    expect(decoded.release?.checkpointReadBackMatchesManifest).toBe(true);
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
      reportSigning: { mode: 'CERTIFICATE', required: true, ready: false, subject: null, notAfter: null, message: 'No report-signing certificate is configured.' } };
    const { f } = open(blocked);
    const host = f.nativeElement as HTMLElement;
    const sign = Array.from(host.querySelectorAll('button')).find((b) => b.textContent?.includes('Sign with')) as HTMLButtonElement;
    expect(sign.disabled).toBe(true);
    expect(host.querySelector('[data-report-signing]')?.getAttribute('role')).toBe('alert');
    expect(host.textContent).toContain('No report-signing certificate is configured.');
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

