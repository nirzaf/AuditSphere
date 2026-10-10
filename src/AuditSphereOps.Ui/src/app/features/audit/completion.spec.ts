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
  function load(http: HttpTestingController, payload: object) {
    const f = TestBed.createComponent(EngagementCompletion);
    f.detectChanges();
    http.expectOne(`/api/ui/engagements/${id1}/completion`).flush(payload);
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
    f.detectChanges();
    return { f, http };
  }
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
  });

  it('labels a certificate-signed report and shows the certificate in force', () => {
    const { f } = open(validPayload);
    const host = f.nativeElement as HTMLElement;
    expect(host.textContent).toContain('certificate-signed');
    expect(host.querySelector('[data-report-signing]')?.textContent).toContain('firm certificate (required)');
    expect(host.querySelector('[data-report-signing]')?.textContent).toContain('valid until 2027-10-01');
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

