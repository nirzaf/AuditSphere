# ADR-0009: Issued evidence is authoritative in the release and archive store; the SharePoint archive is a working copy

**Status: APPROVED** (owner-delegated approval recorded 2026-10-10; external implementation gates remain separate) · Date: 2026-10-08 · Decider: repository owner (authority delegated 2026-10-10)

## Context

- ADR-0006 gives assigned staff Full Control on their client's SharePoint site. After AuditSphere's local freeze they can still edit or delete working files in SharePoint.
- `FileFreezeService` records `ExternalReadOnly = BLOCKED_EXTERNAL`. Local freeze does not prove provider immutability.
- ADR-0005 excludes Purview retention labels and eSignature providers, so no provider retention control is available.
- The ISA 230 archive needs issued evidence that cannot be changed without detection. The release store already keeps issued bytes with SHA-256 identities, manifests and append-only triggers. Spike SPK-01 compared four ways to protect the archive; see its report.

## Decision

- The authoritative bytes of an issued deliverable and its manifest are the release and archive store, not the SharePoint folder. The store is independently controlled and append-only.
- The SharePoint `05_Final Signed Archive` folder is a working copy of the released bytes. The UI and the regulator export must say so and must not describe the folder as immutable.
- Provider protection state is reported separately: `NOT_REQUESTED`, `REQUESTED`, `OBSERVED` or `BLOCKED_EXTERNAL`. It becomes `OBSERVED` only after a read-back of the provider's permissions matches the expected state. Until then it is `BLOCKED_EXTERNAL`.
- No new Microsoft permission and no new provider are introduced by this decision.

## Alternatives considered

- Break inheritance on the archive folder (SPK-01 option 1): a sound control, but it depends on the worker exception and on permission reconciliation that has not been observed. Kept as a candidate for STE-NXT-011.
- Copy the archive to a read-only SharePoint library (option 2): a good follow-up that uses the existing worker exception. It needs owner approval of the design. Kept as STE-NXT-011.
- Write-once object storage (option 4): the strongest technical control, but it is a new provider and a new capability. Rejected for now; it needs its own ADR.

## Consequences

- STE-GAP-007 stays `BLOCKED_EXTERNAL` until a real-tenant test observes the provider state. This ADR does not close it.
- The lifecycle and the regulator export must show the authoritative store and the provider state, and must not imply the SharePoint copy is protected (STE-NXT-012).
- Administrators of the client site collection can still change the working copy. The release store is the only control in this design that does not depend on the provider.
- Once accepted, ADR-0006's ISA 230 risk note should cite this ADR.
