# ADR-0019: Verified manual client sites and SharePoint-primary working documents

**Status: PROPOSED** · Date: 2026-10-25 · Decider: repository owner

## Context

The owner assigned the [SharePoint-primary implementation specification](../auditsphere-sharepoint-primary-storage-implementation-spec-proposed.md). Existing selected-site transfers verify remote versions, but PBC downloads and schedule-source currentness still require retained local chunks. Transfer success alone cannot authorize their deletion. The [implementation inventory](../../execution/auditsphere-execution-sharepoint-primary-storage-current.md) records the source boundaries and unresolved decisions.

ADR-0006 currently owns automated site creation and assigned-staff Full Control. ADR-0009 keeps issued evidence authoritative in the release/archive store. Neither is superseded by this proposal.

## Proposed decision

- Add an explicitly selected `MANUAL_VERIFIED` site-management mode alongside backward-compatible `AUTOMATED` behavior. Preserve the persisted dedicated-site rollout boundary; disabling automation does not permit shared-site fallback.
- A Microsoft 365 administrator creates/adopts the exact client site, grants the existing selected-site document identity and owns direct staff access. Activation requires reviewed, current tenant/client/site/drive/root, connection and template evidence. A manual receipt never asserts automated creation or observed membership.
- Reuse `DocumentReference`, `DocumentSnapshot`, repository bindings and durable operations. Register exact scoped PBC source/provider identities atomically; accepted source receipts and revisions are not rewritten during backfill or cleanup.
- Resolve enrolled working bytes through exact-version verified reads. Authorize before provider I/O, verify the complete hash and size in bounded private scratch, then recheck local authority and source/binding identity before releasing content. Keep remote I/O outside committing business transactions.
- Add shared temporary reservations and reader/transfer leases before enabling bounded cleanup. Cleanup requires migrated consumers, exact registration, custody/hold decisions, explicit cohort authorization and auditable recovery. No cleanup is enabled by this ADR proposal.
- Preserve issued reports, signed release evidence, authoritative archives/checkpoints, secrets and normalized business records in their existing custody. Retain the privileged archive capability until an independently accepted handover covers every remaining responsibility.

## Alternatives considered

- Permanent VPS mirror: retains the current operational dependency and does not meet the requested storage outcome.
- New storage provider, Graph SDK, queue or application framework: unnecessary; existing provider and durable-operation boundaries are reused.
- Silent global remote/local fallback: rejected because it obscures which exact retained source is authoritative.
- Disable the privileged worker immediately: rejected because archive-copy and permission-verification duties still exist.

## Consequences and adoption gates

This proposal permits review of a bounded implementation plan; it does not itself alter approved runtime policy. Independent additive engineering preserves current defaults and leaves production activation/deletion off. Owner decisions D-SP-01 through D-SP-06, actual grants, capacity measurements, custody/backup responsibilities and an accepted rollback/recovery rehearsal remain distinct evidence. Manual administration is an operator responsibility, not a claim of automated revocation or immutable SharePoint retention.
