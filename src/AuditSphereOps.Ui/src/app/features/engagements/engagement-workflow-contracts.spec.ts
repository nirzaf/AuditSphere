import { describe, expect, it } from 'vitest';
import {
  WORKFLOW_PHASES,
  decodeEngagementWorkflowReport,
  decodeEngagementWorkflowTasks,
  decodeOnboardingReadinessReport,
  phaseChecklists,
  workbenchLinksFor,
} from './engagement-workflow-contracts';

const id = '11111111-2222-3333-4444-555555555555';
const clientId = '66666666-7777-8888-9999-000000000000';

const phases = (current: number, status: string) =>
  WORKFLOW_PHASES.map((p) => ({
    index: p.index,
    code: p.code,
    label: p.label,
    status: p.index < current ? 'COMPLETED' : p.index === current ? status : 'NOT_STARTED',
  }));

function workflow(overrides: Record<string, unknown> = {}) {
  return {
    engagementId: id,
    clientId,
    serviceRoute: 'Audit',
    canonicalStage: 'DUAL_KEY_PENDING',
    canonicalIndex: 3,
    canonicalLabel: 'Dual-Key Acceptance Pending',
    currentPhase: { index: 1, code: 'CLIENT_ONBOARDING', label: 'Client Onboarding', status: 'BLOCKED' },
    phases: phases(1, 'BLOCKED'),
    responsibleRole: 'EngagementPartner',
    responsibleUsers: ['Aisha Al-Thani'],
    blockers: ['Unconditional Partner risk acceptance decision required'],
    nextActions: [{ key: 'OPEN_CANONICAL_ACTION', label: 'Open: Dual-Key Acceptance Pending', href: `/app/assessments/${clientId}` }],
    archive: null,
    evaluatedAtUtc: '2026-10-12T12:00:00Z',
    ...overrides,
  };
}

describe('Seven-phase workflow projection contract (WF-01/WF-02)', () => {
  it('defines seven ordered visible phases', () => {
    expect(WORKFLOW_PHASES.map((p) => p.index)).toEqual([1, 2, 3, 4, 5, 6, 7]);
    expect(WORKFLOW_PHASES[0].code).toBe('CLIENT_ONBOARDING');
    expect(WORKFLOW_PHASES[6].code).toBe('ARCHIVE_CLOSE');
  });

  it('decodes a truthful projection for an engagement still in client onboarding', () => {
    const decoded = decodeEngagementWorkflowReport(workflow());
    expect(decoded.canonicalStage).toBe('DUAL_KEY_PENDING');
    expect(decoded.currentPhase.status).toBe('BLOCKED');
    expect(decoded.phases).toHaveLength(7);
    expect(decoded.blockers).toHaveLength(1);
    expect(decoded.archive).toBeNull();
  });

  it('decodes archive and provider protection state without treating them as cleared', () => {
    const decoded = decodeEngagementWorkflowReport(
      workflow({
        canonicalStage: 'COMPLIANCE_COUNTDOWN',
        canonicalIndex: 10,
        currentPhase: { index: 7, code: 'ARCHIVE_CLOSE', label: 'Archive and Close', status: 'IN_PROGRESS' },
        phases: phases(7, 'IN_PROGRESS'),
        archive: {
          localArchiveState: 'SCHEDULED',
          providerProtectionState: 'BLOCKED_EXTERNAL',
          complianceWarning: 'Provider protection state is BLOCKED_EXTERNAL.',
          complianceCountdownDays: 45,
        },
      }),
    );
    expect(decoded.archive?.providerProtectionState).toBe('BLOCKED_EXTERNAL');
    expect(decoded.archive?.complianceCountdownDays).toBe(45);
  });

  it('fails closed on an unknown phase status', () => {
    const raw = workflow({ currentPhase: { index: 1, code: 'CLIENT_ONBOARDING', label: 'Client Onboarding', status: 'CLEARED' } });
    expect(() => decodeEngagementWorkflowReport(raw)).toThrow();
  });

  it('fails closed when the phase list is not exactly seven phases', () => {
    expect(() => decodeEngagementWorkflowReport(workflow({ phases: phases(1, 'BLOCKED').slice(0, 6) }))).toThrow(
      'Workflow phase count mismatch',
    );
  });

  it('fails closed on an out-of-range canonical stage index', () => {
    expect(() => decodeEngagementWorkflowReport(workflow({ canonicalIndex: 12 }))).toThrow(
      'Invalid canonical stage index bounds',
    );
  });

  it('fails closed when phases are not in canonical order', () => {
    const swapped = phases(1, 'BLOCKED');
    [swapped[1], swapped[2]] = [swapped[2], swapped[1]];
    expect(() => decodeEngagementWorkflowReport(workflow({ phases: swapped }))).toThrow('Workflow phase order mismatch');
  });

  it('fails closed on an out-of-range current phase index', () => {
    expect(() =>
      decodeEngagementWorkflowReport(
        workflow({ currentPhase: { index: 8, code: 'X', label: 'X', status: 'BLOCKED' } }),
      ),
    ).toThrow('Invalid workflow phase index bounds');
  });
});

function readiness(overrides: Record<string, unknown> = {}) {
  return {
    engagementId: id,
    clientId,
    serviceRoute: 'Audit',
    clientApproval: {
      status: 'CURRENT',
      displayStatus: 'APPROVED',
      label: 'The client accepted the exact current offer.',
      fee: '50000.000000',
      currency: 'QAR',
      quotationRevision: 2,
      respondedAtUtc: '2026-10-10T09:00:00Z',
      offerSha256Prefix: 'abcdef123456',
      owner: 'A. Owner',
      evidenceHref: `/app/practice/proposals/${clientId}`,
    },
    partnerRisk: {
      status: 'MISSING',
      displayStatus: 'PENDING',
      label: 'No unconditional Partner risk clearance is recorded for this service.',
      serviceRoute: 'Audit',
      decidedAtUtc: null,
      decidedByName: null,
      generation: null,
      evidenceHref: `/app/clients/${clientId}/assessment`,
    },
    engagementLetter: { status: 'NOT_ELIGIBLE', label: 'Both approval keys must be current before a letter can be issued.', documentId: null },
    advancePayment: {
      status: 'NO_AGREEMENT',
      label: 'No fee agreement yet.',
      amount: null,
      allocated: null,
      outstanding: null,
      invoiceStatus: null,
      currency: null,
      advancePercent: null,
      preparationState: 'NOT_APPLICABLE',
    },
    activation: { status: 'BLOCKED', label: 'Unconditional Partner risk clearance is recorded for this service.' },
    blockers: [{ code: 'PARTNER_ACCEPTANCE_REQUIRED', label: 'No unconditional Partner risk clearance is recorded for this service.' }],
    nextActions: [{ key: 'OPEN_ACCEPTANCE', label: 'Review the acceptance checklist and risk clearance', href: `/app/clients/${clientId}/assessment` }],
    canGenerateLetter: false,
    canActivate: false,
    evaluatedAtUtc: '2026-10-12T12:00:00Z',
    ...overrides,
  };
}

describe('Dual-Key onboarding readiness contract (WF-03)', () => {
  it('decodes independent cards with exact decimal money and never merges the keys', () => {
    const decoded = decodeOnboardingReadinessReport(readiness());
    expect(decoded.clientApproval.status).toBe('CURRENT');
    expect(decoded.clientApproval.fee).toBe('50000.000000');
    expect(decoded.partnerRisk.status).toBe('MISSING');
    expect(decoded.engagementLetter.status).toBe('NOT_ELIGIBLE');
    expect(decoded.activation.status).toBe('BLOCKED');
  });

  it('decodes only the STE approval vocabulary for each key', () => {
    const decoded = decodeOnboardingReadinessReport(readiness());
    expect(decoded.clientApproval.displayStatus).toBe('APPROVED');
    expect(decoded.partnerRisk.displayStatus).toBe('PENDING');
    expect(decoded.clientApproval.owner).toBe('A. Owner');
    expect(() =>
      decodeOnboardingReadinessReport(readiness({
        partnerRisk: { ...readiness().partnerRisk, displayStatus: 'CLEARED' },
      })),
    ).toThrow();
  });

  it('keeps a stale Partner clearance visible as stale', () => {
    const decoded = decodeOnboardingReadinessReport(
      readiness({
        partnerRisk: {
          status: 'STALE', label: 'Partner clearance predates the current client inputs.', serviceRoute: 'Audit',
          decidedAtUtc: '2026-10-01T09:00:00Z', decidedByName: 'Partner A', generation: 1,
          displayStatus: 'STALE', evidenceHref: `/app/clients/${clientId}/assessment`,
        },
      }),
    );
    expect(decoded.partnerRisk.status).toBe('STALE');
    expect(decoded.partnerRisk.decidedByName).toBe('Partner A');
  });

  it('fails closed on an unknown commercial status', () => {
    expect(() => decodeOnboardingReadinessReport(readiness({
      clientApproval: { ...readiness().clientApproval, status: 'ACCEPTED_BY_EMAIL' },
    }))).toThrow();
  });

  it('decodes a fee agreement that is not linked to this engagement as its own state', () => {
    const decoded = decodeOnboardingReadinessReport(readiness({
      advancePayment: {
        status: 'NOT_LINKED', label: 'The fee agreement is not linked to this engagement.', amount: '25000.000000',
        allocated: '0.000000', outstanding: '25000.000000', invoiceStatus: null, currency: 'QAR', advancePercent: '50.000000',
        preparationState: 'AWAITING_ENGAGEMENT_LETTER',
      },
    }));
    expect(decoded.advancePayment.status).toBe('NOT_LINKED');
    expect(decoded.advancePayment.advancePercent).toBe('50.000000');
    expect(decoded.canActivate).toBe(false);
  });

  it('carries the server-computed action flags without treating them as status', () => {
    const decoded = decodeOnboardingReadinessReport(readiness({ canGenerateLetter: true, canActivate: false }));
    expect(decoded.canGenerateLetter).toBe(true);
    expect(decoded.engagementLetter.status).toBe('NOT_ELIGIBLE');
  });

  it('fails closed on a non-exact money amount', () => {
    expect(() => decodeOnboardingReadinessReport(readiness({
      advancePayment: { ...readiness().advancePayment, amount: '25000.5e3' },
    }))).toThrow();
  });
});

describe('Phase workbench links (STE v2.2 section 6.2)', () => {
  it('offers the workbenches that matter in each phase, and only those', () => {
    const keys = (phase: string) => workbenchLinksFor(phase, id, clientId).map((link) => link.key);
    expect(keys('AUDIT_FIELDWORK')).toEqual(['statements', 'fieldwork', 'pbc']);
    expect(keys('PARTNER_SIGNOFF')).toEqual(['completion', 'fieldwork']);
    expect(keys('ARCHIVE_CLOSE')).toEqual(['records', 'completion', 'finance']);
  });

  it('routes each link to its existing workbench for this engagement and client', () => {
    const links = workbenchLinksFor('AUDIT_PLANNING', id, clientId);
    expect(links.find((link) => link.key === 'planning')?.href).toBe(`/app/engagements/${id}/audit-plan`);
    expect(links.find((link) => link.key === 'client')?.href).toBe(`/app/clients/${clientId}`);
  });

  it('keeps every link on an internal application route', () => {
    for (const phase of WORKFLOW_PHASES) {
      for (const link of workbenchLinksFor(phase.code, id, clientId)) {
        expect(link.href).toMatch(/^\/app\/[A-Za-z0-9/_-]+$/);
      }
    }
  });

  it('offers no workbench for a phase it does not recognise', () => {
    expect(workbenchLinksFor('UNKNOWN_PHASE', id, clientId)).toEqual([]);
  });
});

describe('Phase checklists (STE v2.2 section 6.2)', () => {
  const engagementId = '0d3b6a53-7d0a-4b47-9d53-3a2c5c1f4b11';
  const task = (n: number, phase: string, status: string, ordinal = n) => ({
    id: `5a1c1f6e-2f0b-4a0e-8a53-0000000000${String(n).padStart(2, '0')}`,
    title: `Task ${n}`, phase, ordinal, status, assigneeUserId: null, assigneeDisplayName: null,
    revision: 1, dueDate: null, fsliCode: null,
  });

  it('builds one checklist per phase in phase order, with tasks in their ordinal order', () => {
    const workspace = decodeEngagementWorkflowTasks({
      engagementId, truncated: false,
      tasks: [task(2, 'AUDIT_FIELDWORK', 'OPEN', 20), task(1, 'AUDIT_FIELDWORK', 'COMPLETED', 10), task(3, 'AUDIT_PLANNING', 'COMPLETED')],
    });
    const lists = phaseChecklists(workspace.tasks);
    expect(lists.map((l) => l.code)).toEqual(WORKFLOW_PHASES.map((p) => p.code));
    const fieldwork = lists[2];
    expect(fieldwork.tasks.map((t) => t.title)).toEqual(['Task 1', 'Task 2']);
    expect([fieldwork.completed, fieldwork.eligible, fieldwork.percent]).toEqual([1, 2, 50]);
    expect(lists[1].percent).toBe(100);
    expect(lists[0].percent).toBeNull();
  });

  it('leaves cancelled tasks out of progress and never rounds up to complete', () => {
    const tasks = decodeEngagementWorkflowTasks({
      engagementId, truncated: false,
      tasks: [task(1, 'MANAGER_REVIEW', 'COMPLETED'), task(2, 'MANAGER_REVIEW', 'COMPLETED'), task(3, 'MANAGER_REVIEW', 'OPEN'),
        task(4, 'MANAGER_REVIEW', 'CANCELLED')],
    }).tasks;
    const review = phaseChecklists(tasks)[3];
    expect([review.completed, review.eligible, review.percent]).toEqual([2, 3, 66]);
    expect(review.tasks).toHaveLength(4);
  });

  it('refuses a task in an unknown phase or status', () => {
    expect(() => decodeEngagementWorkflowTasks({ engagementId, truncated: false, tasks: [task(1, 'SOME_PHASE', 'OPEN')] })).toThrow();
    expect(() => decodeEngagementWorkflowTasks({ engagementId, truncated: false, tasks: [task(1, 'AUDIT_PLANNING', 'APPROVED')] })).toThrow();
  });
});
