# ADR-0013: Pin approved FSLI mapping lineage to engagement work and time

**Status: APPROVED** · Date: 2026-10-10 · Decider: repository owner (delegated project decision)

## Context

Practice time can be recorded against client engagements and tasks. The accounting mapping is revisioned and scope-bound, so a time entry must not silently follow a later mapping revision or invent a destination that was never approved. The practice-time record is for attribution and traceability; it must not post to client books or change group-consolidation balances.

## Decision

- Engagement tasks may optionally select an FSLI destination from the latest approved mapping for that exact firm, client, and engagement.
- Persist the exact `MappingVersionId` and destination code on the task. Copy both values to each time entry and correction so later mapping revisions do not rewrite historical attribution.
- Enforce the scope relationship with restrictive composite foreign keys. A missing or stale mapping, or a destination absent from the selected approved mapping, fails closed.
- Firm-wide, client-only, and reporting-period-only tasks may remain unattributed. The system does not infer an FSLI, repartition time, calculate an accounting amount, or post a journal from this attribution.

## Alternatives considered

- Link only to the current mapping at query time: rejected because a later approval would rewrite the displayed attribution of existing time.
- Copy only the destination code: rejected because the code alone cannot identify the approved mapping revision that supplied it.
- Require an FSLI on all firm time: rejected because firm administration and time not tied to an engagement do not have client statement-line attribution.

## Consequences

- Mapping lineage is visible on the task and time workspace; source mapping IDs remain available for audit traceability.
- Revisions to mapping do not change existing task or time snapshots. New tasks use the then-current approved mapping.
- This is a technical attribution decision only. It does not approve financial-statement classification, accounting treatment, or professional conclusions.
