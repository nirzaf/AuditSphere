import { arr, bool, dec, decode, guid, instant, int, nullable, obj, oneOf, str } from '../../core/decode';

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
  return report;
}

export type EngagementWorkflowReport = ReturnType<typeof decodeEngagementWorkflowReport>;

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
