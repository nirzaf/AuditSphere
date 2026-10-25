# ADR-0019: SharePoint-primary working-document storage with functional parity

**Status: PROPOSED** · Date: 2026-10-25 · Decider: repository owner · Scope: narrow storage supplement; supersedes nothing

## Context

The owner assigned the [functional-parity storage specification](../auditsphere-sharepoint-primary-storage-functional-parity-spec-proposed.md), which replaces the [earlier specification](../auditsphere-sharepoint-primary-storage-implementation-spec-proposed.md). Selected-site PBC transfers already verify exact remote versions, but PBC downloads and accepted schedule-source reads still depended on retained local staging chunks, so those chunks could never be reclaimed. The [implementation handoff](../../execution/auditsphere-execution-sharepoint-primary-storage-current.md) records the inventory, the implemented slices and the remaining gates.

ADR-0006 (automated dedicated client sites and assigned-staff Full Control) and ADR-0009 (authoritative release/archive store with a secondary SharePoint copy) remain in force unchanged.

## Proposed decision

- Change only where already-verified PBC working bytes are read from and when temporary local staging may be reclaimed. Site provisioning, rollout cutoff, staff membership, folder taxonomy, Microsoft identities and permissions, PBC flows, limits, routes and contracts, and the archive/release controls are unchanged.
- Register each provider-verified live selected-site transfer as a scoped `DocumentReference`, exact-version `DocumentSnapshot` and append-only `PbcDocumentRegistration` in the existing `RECEIVED` publication transaction. Earlier verified transfers are backfilled only from the completed transfer's recorded identity, after an exact-version read-back. Upload, request and acceptance revisions, receipts and source digests are never rewritten.
- Read a registered upload from SharePoint only after its per-upload storage disposition selects it. The existing staff download endpoint and accepted schedule-source consumers verify the complete SHA-256 and size into a private spool before releasing bytes, recheck authority afterwards, never substitute another version and never fall back silently to a local copy. Provider I/O stays outside protected business write transactions. The API reuses the existing Sites.Selected credential it already holds for workspace provisioning.
- Reclaim a staged copy only through a separately enabled, administrator-run release for an explicitly enumerated cohort, after a fresh verified remote read, with a final PBC decision, no active operation or work hold, allowlisted non-link paths, and an append-only release record written before deletion. Cleanup is disabled by default and never touches SharePoint, PBC history or release/archive/checkpoint stores.

## Alternatives considered

- Manual site-management mode, administrator-managed onboarding or client-sites worker retirement: rejected; unnecessary for storage and conflicts with functional parity.
- Delta synchronization worker or new Graph permissions: rejected; exact known versions are read on access.
- Silent remote/local fallback: rejected because it obscures which exact retained source served the bytes.
- New storage provider, queue or framework: unnecessary.

## Consequences and adoption gates

Deleting a local copy makes that document's retrieval depend on SharePoint availability; literal outage parity cannot be promised once a copy is reclaimed. Remote-read activation and cleanup therefore stay off until the owner records the SharePoint version-retention/recovery, outage-parity and pilot-cohort decisions, approves a bounded synthetic pilot, and accepts a rehearsed rollback (a remote-read-capable build or verified rehydration). Exact-source CI, real selected-site observation and storage/custody acceptance remain separate evidence.
