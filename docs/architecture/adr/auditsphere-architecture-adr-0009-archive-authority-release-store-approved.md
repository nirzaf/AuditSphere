# ADR-0009: Issued evidence is authoritative in the release and archive store; the SharePoint archive is a working copy

**Status: APPROVED** (owner-delegated decision, amended 2026-10-11; provider verification remains separate) · Date: 2026-10-08 · Decider: repository owner (authority delegated 2026-10-10)

## Context

- ADR-0006 gives assigned staff Full Control on their client's SharePoint site. After AuditSphere's local freeze they can still edit or delete working files in SharePoint.
- `FileFreezeService` records `ExternalReadOnly = BLOCKED_EXTERNAL`. Local freeze does not prove provider immutability.
- ADR-0005 excludes Purview retention labels and eSignature providers, so no provider retention control is available.
- The ISA 230 archive needs issued evidence that cannot be changed without detection. The release store already keeps issued bytes with SHA-256 identities, manifests and append-only triggers. Spike SPK-01 compared four ways to protect the archive; see its report.

## Decision

- The authoritative bytes of an issued deliverable and its manifest are the release and archive store, not the SharePoint folder. The store is independently controlled and append-only.
- Add a separate read-only SharePoint archive library as a provider copy of the released bundle and manifest. Authorized staff receive read access only; the isolated client-sites worker creates the copy under its existing approved capability.
- Verify each copied file against the released SHA-256 and read back the library grants after provider reconciliation. A mismatch or unknown provider outcome keeps the provider state `BLOCKED_EXTERNAL`; mark it `OBSERVED` only when both the file hashes and expected grants match.
- The release and archive store remains authoritative. The SharePoint library is a secondary access copy; the UI and regulator export must identify it as such and must not claim write-once or administrator-proof immutability. Site-collection and tenant administrators retain provider authority; a hash mismatch is detected when the copy is checked.
- Provider protection state is reported separately: `NOT_REQUESTED`, `REQUESTED`, `OBSERVED` or `BLOCKED_EXTERNAL`. It becomes `OBSERVED` only after a read-back of the provider's permissions matches the expected state. Until then it is `BLOCKED_EXTERNAL`.
- No new Microsoft permission and no new provider are introduced by this decision.

**Amendment (2026-10-11):** Under the owner's delegated project decision, select SPK-01 option 2, the separate read-only SharePoint library. This supersedes the earlier disposition that kept option 2 out of the current scope. It does not change the authoritative release-store boundary or close any live-provider gate.

## Alternatives considered

- Break inheritance on the archive folder (SPK-01 option 1): not selected; the separate library provides a clearer staff-access boundary.
- Copy the archive to a read-only SharePoint library (option 2): selected as STE-NXT-011; it uses the existing worker exception and keeps the release store authoritative.
- Write-once object storage (option 4): not selected; it adds a provider, capability and operating cost. Reconsider only through a separate ADR.

## Consequences

- STE-NXT-011's design decision is complete; implementation, exact-site permission readback, hash verification and staff-denial evidence remain provider work. STE-GAP-007 stays `BLOCKED_EXTERNAL` until a real-tenant test observes those states. This ADR does not close it.
- The lifecycle and the regulator export must show the authoritative store and the provider state, and must not imply the SharePoint copy is protected (STE-NXT-012).
- Administrators of the client site collection can still change the SharePoint copy or its grants. Read-only staff grants and hash checks detect only the states actually read back; the release store remains the control that does not depend on the provider.
- Once accepted, ADR-0006's ISA 230 risk note should cite this ADR.
