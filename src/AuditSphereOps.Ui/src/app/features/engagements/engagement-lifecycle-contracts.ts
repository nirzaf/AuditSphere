import { arr, bool, decode, guid, instant, int, nullable, obj, oneOf, str } from '../../core/decode';

export const CANONICAL_STAGES = [
  { index: 1, key: 'LEAD_INGESTION', label: 'Lead Ingestion' },
  { index: 2, key: 'PROPOSAL_GENERATION', label: 'Proposal Generation' },
  { index: 3, key: 'DUAL_KEY_PENDING', label: 'Dual-Key Acceptance' },
  { index: 4, key: 'ADVANCE_BILLING', label: 'Advance Billing & Payment' },
  { index: 5, key: 'PORTAL_ACTIVE_PLANNING', label: 'Portal Active & Planning' },
  { index: 6, key: 'FIELDWORK_EXECUTION', label: 'Fieldwork Execution' },
  { index: 7, key: 'MANAGERIAL_REVIEW', label: 'Managerial Review' },
  { index: 8, key: 'PARTNER_APPROVAL', label: 'Partner Approval & Signing' },
  { index: 9, key: 'DELIVERABLE_RELEASE', label: 'Deliverable Release' },
  { index: 10, key: 'COMPLIANCE_COUNTDOWN', label: 'Compliance Countdown' },
  { index: 11, key: 'ARCHIVED_READ_ONLY', label: 'Archived (Read-Only)' },
] as const;

export type CanonicalStageKey = (typeof CANONICAL_STAGES)[number]['key'];

const summaryDecoder = obj({
  canonicalStage: str(100),
  stageLabel: str(200),
  stageIndex: int,
  blockedReasons: arr(str(2000), 100),
  responsibleRole: str(100),
  deepLink: str(500),
  complianceCountdownDays: nullable(int),
  isArchived: bool,
  isLegacyUnverified: bool,
  complianceWarning: nullable(str(2000)),
  localArchiveState: nullable(oneOf('SCHEDULED', 'FROZEN')),
  providerProtectionState: nullable(oneOf('NOT_REQUESTED', 'REQUESTED', 'OBSERVED', 'BLOCKED_EXTERNAL')),
});

const reportDecoder = obj({
  engagementId: guid,
  clientId: guid,
  clientName: str(500),
  serviceRoute: str(200),
  summary: summaryDecoder,
  evaluatedAtUtc: instant,
});

export function decodeEngagementLifecycleReport(value: unknown) {
  const report = decode(reportDecoder, value);
  if (report.summary.stageIndex < 1 || report.summary.stageIndex > 11) {
    throw new Error('Invalid lifecycle stage index bounds');
  }
  return report;
}

export type EngagementLifecycleReport = ReturnType<typeof decodeEngagementLifecycleReport>;
export type EngagementLifecycleSummary = EngagementLifecycleReport['summary'];
