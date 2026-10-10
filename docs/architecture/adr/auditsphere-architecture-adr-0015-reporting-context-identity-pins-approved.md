# ADR-0015: Reporting contexts pin existing identities and revisions

**Status: APPROVED** (owner-delegated technical decision) · Date: 2026-10-10 · Decider: repository owner (authority delegated 2026-10-10)

## Context

R2R task T014 asks for an exact reporting context, while the source blueprint introduces a `ReportingContextRevision` and several shared reference types. The current application already owns client, engagement, reporting-period, book, chart, mapping, and policy records. Duplicating those identities would create a second source of truth and could silently change accounting meaning.

## Decision

- A reporting context is an immutable, firm-scoped manifest of exact existing record IDs and approved revision IDs. It is not a second client, engagement, period, book, chart, dimension, mapping, or policy aggregate.
- Resolve every reference from server-side records and enforce firm, client, engagement, and effective-period scope. Never resolve by display name or accept a caller-supplied label as authority.
- Preserve the exact basis, functional/reporting currency, profile, period, book decision, chart, dimension schema, mapping, and policy versions that the relevant workflow actually uses. Omit a pin only when that workflow does not consume that input; do not manufacture defaults to fill the manifest.
- A change to a required input creates a successor context revision and makes dependent currentness stale. Historical imports, workpapers, and packages retain their original context identity and are never rebound in place.
- This is an identity and lineage decision only. It does not select an accounting framework, accounting treatment, FX source or method, period-end classification, or professional conclusion.
- Implement the context only after T013 supplies the dimension-schema revision contract and T007 establishes the shared scoped-identity checks. Until then, existing workflows continue to enforce their current scoped records and no new context activation is claimed.

## Alternatives considered

- Create a parallel aggregate containing copied profile, chart, period, book, and policy data: rejected because it duplicates canonical identities and invites drift.
- Accept display names or client-provided revisions: rejected because labels are mutable and do not prove authorization or current approval.
- Bind dependent records to whichever approved revision is newest: rejected because historical evidence must remain reproducible against the revision originally consumed.

## Consequences

- T014's technical identity decision is resolved; its implementation remains unstarted and blocked by its recorded T013 dependency and the T007 shared-identity work.
- Each pin needs an explicit server-side resolver and scope check. A new context does not itself approve any accounting choice.
- R2R-ADR-05's precise identity, dependency-lock, and staleness behavior is clarified for the existing modular-monolith architecture.
