# SPK-01 — Making the ISA 230 archive actually immutable

**Status: DESIGN DECIDED; IMPLEMENTATION AND PROVIDER ACCEPTANCE OPEN.** Written finding for spike SPK-01 (time box 3 days). ADR-0009, amended 2026-10-11 under the owner's delegated project authority, selects option 2: a separate read-only SharePoint library as a secondary copy, with the release/archive store remaining authoritative. No live tenant was used for this report, so provider behaviour is not observed. STE-NXT-011 implementation, permission reconciliation, hash readback and staff-denial evidence remain open; STE-GAP-007 remains `BLOCKED_EXTERNAL`.

## 1. The problem, from the repository

- ADR-0006: assigned staff hold Full Control on their client's SharePoint site. After AuditSphere's local freeze they can still edit or delete working files directly.
- The local freeze, the refused-write log and the `05_Final Signed Archive` folder name do not prove immutability. `ExternalReadOnly` is `BLOCKED_EXTERNAL` in `FileFreezeService`.
- ADR-0005: Purview and eSignature are out of scope, so no provider retention label can be relied on.
- The product's compliance claim must stay within what is observed. The UI already says AuditSphere does not claim provider immutability.

## 2. Comparison of the four options in the spike

| Option | Permission needed (capability model) | If a staff member is still assigned | How the state is proven | Cost | What a regulator export reads |
| --- | --- | --- | --- | --- | --- |
| 1. Break inheritance on `05_Final Signed Archive`; staff get read only | The client-sites worker already holds `Sites.FullControl.All` under the owner-approved exception. Document worker keeps exact-site write only. No new capability. | Staff keep their site access elsewhere. The archive folder has explicit read-only grants, so they cannot write there. Re-assignment must skip the folder. | Read back the folder's permission set after the change and record it. `OBSERVED` only after the read-back matches. | Low to moderate: one ACL change per freeze. | The archive folder and its manifest. |
| 2. Copy the five-part bundle and manifest to a separate archive library where staff have read only | Same worker exception to create and grant the library. The copy must be hash-verified. | Staff are not granted the archive library, so they cannot reach it. | Compare the SHA-256 of each copied file with the released bytes, read back the library's permissions, then record `OBSERVED`. | Moderate: duplicate storage and a second library per firm. | The archive library, which holds the same bytes as the release store. |
| 3. Keep issued evidence only in the independently controlled release and archive store; treat SharePoint as a working copy | None. The release store already has append-only triggers and SHA-256 identities. | Staff can still edit the working copy. The release store's bytes are the authority, so this does not change the record. | Internal proof: append-only triggers, SHA-256 identities and manifests. Provider state stays `NOT_REQUESTED` or `BLOCKED_EXTERNAL`; nothing is claimed about SharePoint. | Lowest. | The release store. |
| 4. Write-once object storage (immutability policy) for the bundle and manifest | A new provider and a new capability outside the Microsoft 365 model. Needs a new ADR and owner approval. | Not affected: staff have no access to the store. | Read back the immutability policy and the object hashes. | Moderate, plus an operating cost for a second cloud provider. | The write-once store. |

Limits that apply to every option:

- SharePoint site collection administrators and tenant administrators can change permissions or delete content. None of the options removes that path. The only protection that does not depend on the provider is an independently controlled copy (options 3 or 4).
- Permission changes are asynchronous Microsoft reconciliation (ADR-0006). A read-back shortly after a change proves little; the read-back must be repeated after reconciliation.

## 3. Recommendation

1. Option 2 is selected by the 2026-10-11 ADR-0009 amendment. Copy the released bundle and manifest to a separate read-only SharePoint library, compare every file hash, and read back grants after provider reconciliation. The release/archive store remains authoritative; no administrator-proof immutability claim is made.
2. Keep option 3's release-store-only boundary as the fallback whenever the provider copy or its permissions cannot be verified. In that state, report `BLOCKED_EXTERNAL` and do not imply the SharePoint archive is protected.
3. Do not adopt option 4 without a separate ADR and owner approval for a new provider. It is the strongest technical control and the most expensive.

## 4. Follow-up stories

- STE-NXT-011 (implementation open): copy the released bundle and manifest to the selected read-only archive library, verify hashes, reconcile and read back grants, and record observed provider state. The design choice is complete; only live provider evidence can close its external gates.
- STE-NXT-012 (proposed): show the local archive state, the provider protection state, the canonical lifecycle state and the compliance warning as four separate items (STE-GAP-008 section F). Today the lifecycle summary carries only the warning string.

## 5. What this finding does not decide

The design decision does not change `BLOCKED_EXTERNAL` for STE-GAP-007. Only a real-tenant test can close it after the selected library, hashes and read-only staff grants are observed.
