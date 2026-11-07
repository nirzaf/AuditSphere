# ADR-0004: The engagement lifecycle is a derived projection

**Status: APPROVED** (owner-delegated approval recorded 2026-10-10) · Date recorded: 2026-10-08 · Decider: repository owner (authority delegated 2026-10-10)

## Context
Specification §5 defines eleven states from `LEAD_INGESTION` to `ARCHIVED_READ_ONLY`, each with a gate. Commit `9e946d1` (STE-GAP-008, "lifecycle truthfulness") made the projection follow actual records: countdown anchored to report signing, archive only after a committed freeze, planning only with approved non-stale materiality, Partner approval only when the completion gate is clear.

## Decision
- The stage is computed by `EngagementLifecycleQuery` (`CanonicalEngagementStages`) from the records each gate produces; it is exposed at `GET /api/ui/engagements/{id}/lifecycle`.
- There is no writable `stage` column. Advancing a stage means completing the gate's own command (acceptance decision, letter issue, payment, sign-off, freeze).

## Alternatives considered
- A stored status column with transition commands: can drift from the evidence it claims to summarise, and invites "set status" shortcuts that bypass gates.

## Consequences
- Agents must never add a command that sets a lifecycle stage directly; they change the gate records.
- Reporting on stage history needs the underlying event timestamps, not a stage log.
- Performance of the projection matters for portfolio views; keep its reads bounded.
