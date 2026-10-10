# SPK-01 — Making the ISA 230 archive actually immutable

**Status: DECISION RECORDED; PROVIDER ACCEPTANCE BLOCKED_EXTERNAL.** Written finding for spike SPK-01 (time box 3 days). ADR-0009 now accepts the release/archive store as authoritative and SharePoint as a working copy under the owner's delegated project decision dated 2026-10-10. No live tenant was used for this report, so provider behaviour is not observed; STE-NXT-011's selected read-only-copy implementation and provider readback remain separate work.

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

1. Adopt option 3 now, as draft ADR-0009 describes. It needs no new permission, uses the store that already holds the authoritative bytes, and claims no provider immutability. The UI must label the SharePoint copy as a working copy.
2. Plan option 2 as the follow-up if the owner wants provider-side protection inside Microsoft 365. It uses the existing worker exception, so it needs the owner's approval of the design, not a new capability. Story STE-NXT-011 carries it.
3. Do not adopt option 4 without a separate ADR and owner approval for a new provider. It is the strongest technical control and the most expensive.

## 4. Follow-up stories

- STE-NXT-011 (proposed, owner approval needed): copy the released bundle and manifest to a read-only archive library, verify hashes, record the observed provider state. Depends on option 2 being chosen.
- STE-NXT-012 (proposed): show the local archive state, the provider protection state, the canonical lifecycle state and the compliance warning as four separate items (STE-GAP-008 section F). Today the lifecycle summary carries only the warning string.

## 5. What this finding does not decide

It does not change `BLOCKED_EXTERNAL` for STE-GAP-007. Only a real-tenant test can close it, and only after the owner accepts a design.
