# ADR-0012: The 50% advance is an unconditional activation hard block

**Status: ACCEPTED** · Date: 2026-10-09 · Decider: repository owner

## Context

STE v2.1 §4.1.5 and mandatory control C-02 require the recorded 50% advance before transition into
`PORTAL_ACTIVE_PLANNING` (“50% paid and recorded before portal-active planning transition”, enforcement: **Hard block**).
The source fixes the split at 50/50 (`M1.02-R01` Brief Quotation payment terms, `M1.05-R01` 50% Advance Invoice).

The former linked-only behavior allowed activation when no fee agreement existed. That contradicted C-02 and the
activation workspace's stated blockers. A configurable exception could silently reintroduce the same bypass.

## Decision

Every activation requires:

1. A linked engagement fee agreement.
2. A 50% `ADVANCE` milestone in `PAID` state, which records full payment and allocation.

The command and its review workspace enforce the same rule. No configuration setting can weaken it. An engagement
without a fee agreement or with an unpaid advance remains blocked from activation and portal-active planning.

## Consequences

- `EngagementLifecycleService.ActivateAsync` fails closed when either prerequisite is missing.
- `EngagementActivationWorkspace` reports `advance.fee-agreement-missing` or `advance.unpaid` and includes the advance
  position in its review-basis digest.
- The application, API and legacy reference host have no linked-only bypass setting.
- Deployment configuration containing the removed `EngagementActivation:AdvanceGateMode` key has no effect and should
  be removed during the next configuration cleanup.
