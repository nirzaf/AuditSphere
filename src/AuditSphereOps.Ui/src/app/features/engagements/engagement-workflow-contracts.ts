import { arr, bool, date, dec, decode, guid, instant, int, nullable, obj, oneOf, str } from '../../core/decode';

/** Seven ordinary-language phases (WF-01). Presentation only: a phase never advances an engagement. */
export const WORKFLOW_PHASES = [
  { index: 1, code: 'CLIENT_ONBOARDING', label: 'Client Onboarding' },
  { index: 2, code: 'AUDIT_PLANNING', label: 'Audit Planning' },
  { index: 3, code: 'AUDIT_FIELDWORK', label: 'Audit Fieldwork' },
  { index: 4, code: 'MANAGER_REVIEW', label: 'Manager Review' },
  { index: 5, code: 'PARTNER_SIGNOFF', label: 'Partner Sign-off' },
  { index: 6, code: 'REPORT_DELIVERY', label: 'Report and Delivery' },
  { index: 7, code: 'ARCHIVE_CLOSE', label: 'Archive and Close' },
] as const;

/**
 * Workbench links that matter in each visible phase (STE v2.2 section 6.2). Each link opens an existing workbench, and that
 * workbench enforces its own permissions: the browser never decides who may open one.
 */
export interface WorkbenchLink {
  readonly key: string;
  readonly label: string;
  readonly href: string;
}

const WORKBENCH_KEYS_BY_PHASE: Readonly<Record<string, readonly string[]>> = {
  CLIENT_ONBOARDING: ['client', 'finance'],
  AUDIT_PLANNING: ['planning', 'statements', 'pbc', 'client'],
  AUDIT_FIELDWORK: ['statements', 'fieldwork', 'pbc'],
  MANAGER_REVIEW: ['fieldwork', 'pbc', 'completion'],
  PARTNER_SIGNOFF: ['completion', 'fieldwork'],
  REPORT_DELIVERY: ['completion', 'pbc', 'finance'],
  ARCHIVE_CLOSE: ['records', 'completion', 'finance'],
};

export function workbenchLinksFor(phaseCode: string, engagementId: string, clientId: string): WorkbenchLink[] {
  const keys = WORKBENCH_KEYS_BY_PHASE[phaseCode] ?? [];
  const build: Record<string, { label: string; href: string }> = {
    client: { label: 'Client workbench', href: `/app/clients/${clientId}` },
    planning: { label: 'Engagement planning', href: `/app/engagements/${engagementId}/audit-plan` },
    statements: { label: 'Financial statement lines (FSLI)', href: `/app/engagements/${engagementId}/statements` },
    fieldwork: { label: 'Fieldwork and review', href: `/app/engagements/${engagementId}/audit-fieldwork` },
    pbc: { label: 'Client requests (PBC)', href: `/app/engagements/${engagementId}/pbc` },
    completion: { label: 'Completion and report', href: `/app/engagements/${engagementId}/completion` },
    finance: { label: 'Finance', href: '/app/finance' },
    records: { label: 'Records and archive', href: `/app/records/archives/${engagementId}` },
  };
  return keys.map((key) => ({ key, ...build[key] }));
}

const phaseStatus = oneOf('NOT_STARTED', 'IN_PROGRESS', 'BLOCKED', 'COMPLETED', 'ARCHIVED');

const phaseDecoder = obj({
  index: int,
  code: str(100),
  label: str(200),
  status: phaseStatus,
  operationalDueDate: nullable(date),
});

const actionDecoder = obj({
  key: str(100),
  label: str(300),
  href: str(500),
});

const archiveDecoder = obj({
  localArchiveState: nullable(oneOf('SCHEDULED', 'FROZEN')),
  providerProtectionState: nullable(oneOf('NOT_REQUESTED', 'REQUESTED', 'OBSERVED', 'BLOCKED_EXTERNAL')),
  complianceWarning: nullable(str(2000)),
  complianceCountdownDays: nullable(int),
});

const blockerDetailDecoder = obj({
  blockerIndex: int,
  code: str(100),
  cause: str(2000),
  responsibleRole: str(200),
  guidance: str(2000),
});

type BlockerDetail = ReturnType<typeof blockerDetailDecoder>;

/** Optional details are advisory: invalid or ambiguous entries never hide the original blocker. */
function blockerDetailsByIndex(value: unknown, blockers: readonly string[]): (BlockerDetail | null)[] {
  const result: (BlockerDetail | null)[] = blockers.map(() => null);
  if (!Array.isArray(value) || value.length > 100) return result;

  // Count even malformed entries with a valid index so a duplicate cannot select a preferred detail.
  const counts = new Map<number, number>();
  for (const entry of value) {
    if (entry && typeof entry === 'object' && !Array.isArray(entry)) {
      const index = (entry as Record<string, unknown>)['blockerIndex'];
      if (typeof index === 'number' && Number.isSafeInteger(index) && index >= 0 && index < blockers.length) {
        counts.set(index, (counts.get(index) ?? 0) + 1);
      }
    }
  }
  for (const entry of value) {
    try {
      const detail = decode(blockerDetailDecoder, entry);
      const index = detail.blockerIndex;
      if (index >= 0 && index < blockers.length && counts.get(index) === 1 && detail.cause === blockers[index] &&
          detail.code.trim() && detail.responsibleRole.trim() && detail.guidance.trim()) {
        result[index] = detail;
      }
    } catch {
      // Ignore unsupported optional detail while retaining the original blocker and fallback.
    }
  }
  return result;
}

const workflowDecoder = obj({
  engagementId: guid,
  clientId: guid,
  serviceRoute: str(200),
  canonicalStage: str(100),
  canonicalIndex: int,
  canonicalLabel: str(200),
  currentPhase: phaseDecoder,
  phases: arr(phaseDecoder, 7),
  responsibleRole: str(100),
  responsibleUsers: arr(str(300), 20),
  blockers: arr(str(2000), 100),
  nextActions: arr(actionDecoder, 1),
  archive: nullable(archiveDecoder),
  evaluatedAtUtc: instant,
});

export function decodeEngagementWorkflowReport(value: unknown) {
  const report = decode(workflowDecoder, value);
  if (report.phases.length !== WORKFLOW_PHASES.length) throw new Error('Workflow phase count mismatch');
  if (report.canonicalIndex < 1 || report.canonicalIndex > 11) throw new Error('Invalid canonical stage index bounds');
  if (report.currentPhase.index < 1 || report.currentPhase.index > WORKFLOW_PHASES.length) {
    throw new Error('Invalid workflow phase index bounds');
  }
  // Phases must arrive exactly in canonical order, so the current-phase highlight can never match more than one item.
  report.phases.forEach((phase, i) => {
    const expected = WORKFLOW_PHASES[i];
    if (phase.index !== expected.index || phase.code !== expected.code) throw new Error('Workflow phase order mismatch');
  });
  const rawDetails = (value as Record<string, unknown>)['blockerDetails'];
  return { ...report, blockerDetails: blockerDetailsByIndex(rawDetails, report.blockers) };
}

export type EngagementWorkflowReport = ReturnType<typeof decodeEngagementWorkflowReport>;

const milestonePlanDecoder = obj({
  engagementId: guid,
  fieldworkStartDate: date,
  draftReportDate: date,
  finalReportDate: date,
  statutoryFilingCutoff: date,
  archiveDeadlineDate: date,
});

/** A saved schedule read from the independently authorized Audit Plan endpoint. */
export function decodeEngagementMilestonePlan(value: unknown) {
  return decode(milestonePlanDecoder, value);
}
export type EngagementMilestonePlan = ReturnType<typeof decodeEngagementMilestonePlan>;

export interface WorkflowMilestoneRow {
  readonly phaseIndex: number;
  readonly phaseLabel: string;
  readonly label: string;
  readonly date: string;
}

/** Maps only persisted plan dates to their visible workflow phases; task deadlines remain a separate projection. */
export function projectWorkflowMilestones(plan: EngagementMilestonePlan): WorkflowMilestoneRow[] {
  return [
    { phaseIndex: 3, label: 'Fieldwork starts', date: plan.fieldworkStartDate },
    { phaseIndex: 6, label: 'Draft report', date: plan.draftReportDate },
    { phaseIndex: 6, label: 'Final signed report', date: plan.finalReportDate },
    { phaseIndex: 6, label: 'Statutory filing cutoff', date: plan.statutoryFilingCutoff },
    { phaseIndex: 7, label: 'Archive deadline', date: plan.archiveDeadlineDate },
  ].map((row) => ({ ...row, phaseLabel: WORKFLOW_PHASES[row.phaseIndex - 1].label }));
}

const workflowTaskDecoder = obj({
  id: guid,
  title: str(300),
  phase: oneOf(...WORKFLOW_PHASES.map((p) => p.code)),
  ordinal: int,
  status: oneOf('OPEN', 'IN_PROGRESS', 'COMPLETED', 'CANCELLED'),
  assigneeUserId: nullable(guid),
  assigneeDisplayName: nullable(str(200)),
  revision: int,
  dueDate: nullable(str(10)),
  fsliCode: nullable(str(100)),
});
const workflowTasksDecoder = obj({ engagementId: guid, tasks: arr(workflowTaskDecoder, 500), truncated: bool });

export function decodeEngagementWorkflowTasks(value: unknown) {
  return decode(workflowTasksDecoder, value);
}
export type EngagementWorkflowTask = ReturnType<typeof decodeEngagementWorkflowTasks>['tasks'][number];

export interface PhaseChecklist {
  index: number;
  code: string;
  label: string;
  tasks: EngagementWorkflowTask[];
  /** Tasks that count toward progress: everything except cancelled tasks. */
  eligible: number;
  completed: number;
  /** Whole percent of eligible tasks completed, or null when the phase has no eligible task. */
  percent: number | null;
}

/**
 * One operational checklist per visible phase (STE v2.2 section 6.2), built from the engagement's own tasks. Progress here is
 * task progress only. It is a different fact from gate readiness, which the server reports on the phase itself: a phase with
 * every task done can still be blocked.
 */
export function phaseChecklists(tasks: readonly EngagementWorkflowTask[]): PhaseChecklist[] {
  return WORKFLOW_PHASES.map((phase) => {
    const mine = tasks.filter((t) => t.phase === phase.code).sort((a, b) => a.ordinal - b.ordinal || a.id.localeCompare(b.id));
    const eligible = mine.filter((t) => t.status !== 'CANCELLED').length;
    const completed = mine.filter((t) => t.status === 'COMPLETED').length;
    return {
      index: phase.index, code: phase.code, label: phase.label, tasks: mine, eligible, completed,
      percent: eligible === 0 ? null : Math.floor((completed * 100) / eligible),
    };
  });
}

const clientStatus = oneOf(
  'CURRENT',
  'AWAITING_CLIENT',
  'NOT_DISPATCHED',
  'DECLINED',
  'SUPERSEDED',
  'STALE',
  'QUOTATION_NOT_APPROVED',
);
const partnerStatus = oneOf('CURRENT', 'MISSING', 'STALE', 'CONDITIONAL', 'DECLINED', 'DEFERRED', 'UNAVAILABLE');
const letterStatus = oneOf('NOT_ELIGIBLE', 'ELIGIBLE', 'ISSUED', 'STALE');
const advanceStatus = oneOf('NO_AGREEMENT', 'NOT_LINKED', 'NOT_INVOICED', 'INVOICED_UNPAID', 'PARTIALLY_ALLOCATED', 'PAID');
const activationStatus = oneOf('BLOCKED', 'READY_FOR_REVIEW', 'ACTIVE');

/** The STE v2.2 approval vocabulary (section 4.1.3). Only APPROVED clears a key. */
const displayStatus = oneOf('PENDING', 'AWAITING_EVIDENCE', 'APPROVED', 'REJECTED', 'STALE');

const readinessDecoder = obj({
  engagementId: guid,
  clientId: guid,
  serviceRoute: str(200),
  clientApproval: obj({
    status: clientStatus,
    displayStatus,
    label: str(400),
    fee: nullable(dec),
    currency: nullable(str(10)),
    quotationRevision: nullable(int),
    respondedAtUtc: nullable(instant),
    offerSha256Prefix: nullable(str(12)),
    owner: nullable(str(200)),
    evidenceHref: nullable(str(500)),
  }),
  partnerRisk: obj({
    status: partnerStatus,
    displayStatus,
    label: str(400),
    serviceRoute: str(200),
    decidedAtUtc: nullable(instant),
    decidedByName: nullable(str(200)),
    generation: nullable(int),
    evidenceHref: nullable(str(500)),
  }),
  engagementLetter: obj({
    status: letterStatus,
    label: str(400),
    documentId: nullable(guid),
  }),
  advancePayment: obj({
    status: advanceStatus,
    label: str(400),
    amount: nullable(dec),
    allocated: nullable(dec),
    outstanding: nullable(dec),
    invoiceStatus: nullable(str(100)),
    currency: nullable(str(10)),
    advancePercent: nullable(dec),
    preparationState: str(100),
  }),
  activation: obj({
    status: activationStatus,
    label: str(400),
  }),
  // Open holds are not capped on the server, so the bound here is generous rather than a display limit.
  blockers: arr(obj({ code: str(100), label: str(2000) }), 1000),
  nextActions: arr(actionDecoder, 10),
  canGenerateLetter: bool,
  canActivate: bool,
  evaluatedAtUtc: instant,
});

export function decodeOnboardingReadinessReport(value: unknown) {
  return decode(readinessDecoder, value);
}

export type OnboardingReadinessReport = ReturnType<typeof decodeOnboardingReadinessReport>;
