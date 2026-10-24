# AuditSphere — Microsoft 365 Onboarding User Stories

| Attribute | Value |
| --- | --- |
| Revision | 2.3 — proposed for adoption |
| Prepared | 24 October 2026 |
| Scope | Five existing modules; seven display phases; eleven derived audit lifecycle states |
| Implementation status | Unverified in this document review |
| Purpose | Design-level identity, SharePoint, access and recovery stories within existing modules |

> **Authority:** This is a review-derived revision candidate. A filename ending in `current` is the requested naming convention; it does not establish approval or implementation. The six repository counterparts were not inspected. Source-reported approved ADRs and executed contracts remain controlling until an authorized change is adopted. Proposed changes and D01–D10 decisions do not silently override them.

The [complete proposed specification](auditsphere-requirements-specification-complete-practice-management-proposed.md) governs the proposed design across this package. The [preserved v2.2 source](auditsphere-requirements-source-ste-audit-management-specification-v22.md) supplies historical requirements and approved source overlays. Companion documents use the same control identifiers and do not add product modules.

## Story status and integration boundary

These are proposed stories derived from the reviewed requirements. Story identifiers are newly assigned for this package; they are not claimed to be existing repository issues, endpoints, classes or completed features. The supplied source and the complete proposed specification remain authoritative for scope and owner decisions. Technical implementation should follow existing repository conventions after inspection.

## Scope and authority

These **proposed implementation stories** complete the existing Microsoft identity, tenant administration, client-site, PBC, correspondence and archive capabilities. They do not introduce another product module or assert that any capability is implemented. The master specification and retained ADRs control interpretation; unresolved decisions remain visible.

Preserve Microsoft sign-in without local portal passwords, one SharePoint site per client, assigned-staff Full Control under ADR-0006, the isolated privileged provisioner exception, selected-site document access, the unconditional paid-advance activation gate and ADR-0009 archive authority. Identity provisioning, professional acceptance, payment, portal activation, remote access and archive observation are separate facts.

All stories inherit the [protected-command contract](auditsphere-requirements-specification-complete-practice-management-proposed.md#ARCH-05), [recovery contract](auditsphere-requirements-specification-complete-practice-management-proposed.md#ARCH-06) and [authorization coverage](auditsphere-requirements-specification-complete-practice-management-proposed.md#SEC-01). Fit changes into the actual repository owners after inspection; these stories prescribe no new API paths or class names.

## M365-S01 Tenant binding and capability verification

**Goal and owner.** Establish a verified Microsoft tenant and capability boundary for an authorized firm. Owner: existing tenant-administration capability, with the tenant operator and security owner.

**Prerequisites.** Approved firm/tenant association, authorized operator, intended app identities and recorded capability policy. Optional directory, invitation, group and mail actions remain disabled until specifically enabled and verified.

**Main behavior.** Bind consent initiation and callback to the intended firm, tenant, operator, capability and expiring single-use request. Retain approved verification hashes for stored consent state/nonce; do not log raw values or credentials. Validate the actual tenant, application identity, granted permissions and allowed operation. Consent acceptance does not establish a selected-site grant or successful resource access. Keep requested configuration, consent outcome and observed verification distinguishable. Credentials remain server-side and capability-separated.

**Failures and recovery.** Reject replayed, expired, mismatched or ambiguous callbacks. Missing consent, excessive/unexpected permissions or unverifiable configuration blocks the affected capability. A retry reconciles the original request; it does not enable unrelated capabilities.

**Acceptance.** Wrong-tenant callback, replay and unexpected permissions cannot activate a capability; valid consent without the required site grant still cannot access that site. Evidence identifies the policy revision and observed identity without secrets. Trace to [SEC-02](auditsphere-requirements-specification-complete-practice-management-proposed.md#SEC-02) and [AC-46](auditsphere-requirements-specification-complete-practice-management-proposed.md#AC-46).

## M365-S02 Server-owned staff and client sign-in

**Goal and owner.** Authenticate staff and client contacts through Microsoft while deriving application authority independently. Owner: existing API identity/session capability.

**Prerequisites.** S01 identity configuration, allowed tenant policy, verified local user/contact association and scoped grants. The approved client first-sign-in requirement must be defined and evidenced.

**Main behavior.** Use server-owned OIDC, immutable tenant/object identity binding, secure HttpOnly sessions, CSRF protection and same-origin requests. Resolve current local identity, session validity and grants for protected reads and commands. First successful sign-in establishes identity; it does not create Partner authority or bypass engagement activation. Email/display-name changes must not silently bind a different identity. No temporary portal password is generated, stored or emailed; browser code receives no Graph application credentials or access tokens.

**Failures and recovery.** Invalid callback, wrong tenant, disabled local identity or revoked session denies access. Failed sign-in does not create an active client association. Preserve a safe return destination and actionable failure message without leaking account details.

**Acceptance.** Unactivated clients cannot reach protected engagement data; authenticated technical administrators cannot perform Partner decisions; CSRF failures cause no mutation. Check scoped lists, previews and downloads as well as command routes. Trace to [SEC-01](auditsphere-requirements-specification-complete-practice-management-proposed.md#SEC-01), [AC-06](auditsphere-requirements-specification-complete-practice-management-proposed.md#AC-06) and [AC-43](auditsphere-requirements-specification-complete-practice-management-proposed.md#AC-43).

## M365-S03 Idempotent client-site provisioning

**Goal and owner.** Provision or reconcile exactly one approved SharePoint site for a client. Owner: existing M2 provisioning capability and isolated client-sites worker.

**Prerequisites.** Authorized client identity, required current risk clearance, approved target tenant and standing operator/rollout policy. Preserve the approved privileged provisioner exception; verify its actual permissions before use.

**Main behavior.** Commit a scoped provisioning intent before provider calls. The isolated provisioner may use the approved broad capability, including the source-authorized Sites.FullControl.All exception; its credentials must never enter the web API, portal or ordinary PBC worker. Verify the site's exact ownership marker, tenant and remote IDs before binding it locally. Create the five-folder engagement taxonomy within the client's existing site; annual engagements must not create another client site. Retain requested, provisioned and observed outcomes.

**Failures and recovery.** A name/URL collision without matching ownership is blocked for investigation. After timeout or a crash, reconcile the original remote effect before another create. Preserve partial remote results and the original operation; no blind destructive cleanup.

**Acceptance.** Double submission and crash-after-create produce one verified local site binding; an unrelated matching-name site is not adopted; another annual engagement reuses the client site. Trace to [M2-05](auditsphere-requirements-specification-complete-practice-management-proposed.md#M2-05), [ARCH-06](auditsphere-requirements-specification-complete-practice-management-proposed.md#ARCH-06), [AC-06](auditsphere-requirements-specification-complete-practice-management-proposed.md#AC-06) and [AC-45](auditsphere-requirements-specification-complete-practice-management-proposed.md#AC-45).

## M365-S04 Scoped document grants and staff access

**Goal and owner.** Verify remote grants without confusing them with application permissions. Owner: existing tenant/site administration and document capabilities.

**Prerequisites.** S01 and S03, approved staff assignments and exact-site document-worker identity. The ordinary document worker uses Sites.Selected plus the approved grant to the specific site.

**Main behavior.** Request and read back intended application/site grants and managed staff-group membership. Verify actual access to the target and denial for an ungranted site. Preserve assigned staff Full Control under ADR-0006. Clearly document that direct SharePoint rights can expose sibling engagement folders on that client site; narrower AuditSphere engagement grants cannot reduce those external rights. Corporate group membership does not grant access to another client. Never infer provider success from a locally saved assignment.

**Failures and recovery.** Wrong tenant, wrong site, unexpected broad access or incomplete grant readback leaves the capability blocked/unverified. Reconcile requested versus observed membership after partial failure or drift; avoid duplicate grants and unrelated membership removal.

**Acceptance.** The ordinary worker cannot access an ungranted site; grants resolve to the intended identities; application access remains engagement-scoped despite broader staff site rights. Trace to [SEC-02](auditsphere-requirements-specification-complete-practice-management-proposed.md#SEC-02), [AC-43](auditsphere-requirements-specification-complete-practice-management-proposed.md#AC-43) and [AC-46](auditsphere-requirements-specification-complete-practice-management-proposed.md#AC-46). Any narrowing of Full Control requires D08 adoption.

## M365-S05 Gated client activation

**Goal and owner.** Activate the client portal only after current business and technical conditions are satisfied. Owner: M1 onboarding, consuming M2 decisions, M5 payment evidence and verified identity/site outcomes.

**Prerequisites.** Exact accepted offer, current staffed-Partner risk clearance, agreed Engagement Letter/fee basis, verified contact identity and applicable provisioning evidence.

**Main behavior.** Evaluate both acceptance keys, linked 50% ADVANCE milestone fully paid and allocated, first-sign-in requirements, applicable holds and verified scope. Keep identity/site preparation separate from portal activation. Optional invitation or identity actions require their own approved capability; they are not activated merely because a lead exists. Produce one activation receipt and explicit blocker list. An invoice, partial receipt, unallocated payment or successful Microsoft login alone is insufficient.

**Failures and recovery.** Changed quotation, stale risk, reversed allocation or failed provisioning causes current eligibility to be reevaluated. Preserve earlier valid decisions and activation history; do not manufacture a paid state or erase already-issued evidence.

**Acceptance.** For QAR 20,000 fees, QAR 9,999.99 allocated advance blocks activation; QAR 10,000 permits activation only with all other gates. Concurrent retries produce one receipt. Trace to [M1-05](auditsphere-requirements-specification-complete-practice-management-proposed.md#M1-05), [M1-06](auditsphere-requirements-specification-complete-practice-management-proposed.md#M1-06), [AC-04](auditsphere-requirements-specification-complete-practice-management-proposed.md#AC-04) and [AC-05](auditsphere-requirements-specification-complete-practice-management-proposed.md#AC-05).

## M365-S06 Verified PBC document transfer

**Goal and owner.** Connect every accepted PBC document to verified, scoped provider bytes. Owner: existing M3 PBC capability and isolated selected-site document worker.

**Prerequisites.** Active authorized client, open request for the correct engagement/period, verified site grant and no applicable upload fence.

**Main behavior.** Validate type, content, size and safe naming; assign opaque identity. Persist request, original source hash and durable transfer intent. Upload to the verified client location, retaining provider item/version identity. Read back the exact version and verify its hash before treating it as accepted evidence. An authorized reviewer then accepts or rejects that version with reason. Re-upload creates a new version and preserves earlier decisions. Use safe preview/download behavior; do not introduce a scanning service.

**Failures and recovery.** Interrupted transfer, lost response, hash mismatch or changed destination cannot produce an approved evidence status. Resume/reconcile using the existing operation and exact target; stale worker leases cannot publish. Recheck scope and release fence before publication.

**Acceptance.** Retry does not create duplicate accepted evidence; mismatched bytes remain blocked; another client's request ID is denied; replacing evidence makes dependent approvals stale. Trace to [M3-06](auditsphere-requirements-specification-complete-practice-management-proposed.md#M3-06), [SEC-03](auditsphere-requirements-specification-complete-practice-management-proposed.md#SEC-03), [AC-24](auditsphere-requirements-specification-complete-practice-management-proposed.md#AC-24), [AC-43](auditsphere-requirements-specification-complete-practice-management-proposed.md#AC-43) and [AC-47](auditsphere-requirements-specification-complete-practice-management-proposed.md#AC-47).

## M365-S07 Controlled outbound correspondence

**Goal and owner.** Dispatch authorized proposals, requests, invoices and deliverables with truthful outcome evidence. Owner: the originating module and existing isolated outbound-mail capability.

**Prerequisites.** Approved current document/message, permitted recipient/contact role, verified mail capability and explicit dispatch authority. Generation, posting, release and sending remain distinct actions.

**Main behavior.** Pin content/attachment versions, recipient scope and initiating actor. Reauthorize current recipients before dispatch, excluding internal risk, staff-cost and reviewer information from client content. Commit outbound intent atomically with relevant local records, then perform the provider action outside the database transaction. Record queued, provider-accepted and evidenced delivery separately; never infer receipt/read from API acceptance. WhatsApp remains a source label only.

**Failures and recovery.** Known transient failures use bounded retries. Timeout after a possible send requires reconciliation using supported provider evidence. If the isolated capability cannot determine the outcome, keep it uncertain for an authorized decision; do not broaden mail permissions or blindly resend.

**Acceptance.** Revoked recipient authority blocks dispatch; changed attachments require renewed validation; same-key/same-payload recovery reuses the original operation, while changed payload is rejected. Trace to [ARCH-06](auditsphere-requirements-specification-complete-practice-management-proposed.md#ARCH-06), [M1-01](auditsphere-requirements-specification-complete-practice-management-proposed.md#M1-01), [AC-32](auditsphere-requirements-specification-complete-practice-management-proposed.md#AC-32) and [AC-50](auditsphere-requirements-specification-complete-practice-management-proposed.md#AC-50).

## M365-S08 Revocation reassignment and grant drift

**Goal and owner.** Stop new unauthorized access/effects when assignments or identity authority change. Owner: existing identity, authorization and tenant-operation capabilities.

**Prerequisites.** Current scoped grants, operation ownership, session invalidation mechanism and an adopted external-deprovisioning freshness policy.

**Main behavior.** Reevaluate local identity/session/grants on protected reads and commands, including cached results, exports, streaming and worker publication. Revoke/invalidate affected sessions and cache scopes when local authority changes. Reconcile corresponding remote staff access under the approved policy, recording requested versus observed removal independently. A local role removal must not be represented as proof that SharePoint access has ended.

**Failures and recovery.** Revocation during an in-flight operation prevents subsequent unauthorized publication. An already-completed provider effect remains recorded for authorized reconciliation; do not erase it or repeat it. Unobserved remote removal stays visibly pending/blocked. Do not promise instantaneous detection of external Entra changes.

**Acceptance.** A revoked local actor cannot use an old tab, export link or queued job to perform a new protected action. Expired-lease workers cannot publish. Remote removal failures are distinguishable from successful local denial. Trace to [SEC-01](auditsphere-requirements-specification-complete-practice-management-proposed.md#SEC-01), [ARCH-06](auditsphere-requirements-specification-complete-practice-management-proposed.md#ARCH-06) and [AC-44](auditsphere-requirements-specification-complete-practice-management-proposed.md#AC-44).

## M365-S09 Release access and engagement upload freeze

**Goal and owner.** Apply the final-release intake fence while preserving authorized access for other periods/services. Owner: existing M4 release capability with M3 PBC and authorization.

**Prerequisites.** Current authorized release, exact approved bundle manifest, allowed recipients and completed professional/finance gates. Microsoft integration cannot replace these decisions.

**Main behavior.** Commit authoritative issue evidence, the released engagement's upload fence and outbound intent in the required local boundary. Revalidate the fence on new uploads and in-flight transfer publication. Serve only authorized released artifact versions; downloads follow recipient/closure policy. Freeze scope is the released audit engagement, not the whole client or its separately authorized bookkeeping period. Keep customer archival, period close and engagement freeze distinct.

**Failures and recovery.** Failed transmission does not remove the fence or create a different issued bundle. Reconcile the original send. Transfers racing release cannot silently become post-release accepted evidence; preserve their outcome for authorized handling. Corrections use a linked supplement/reissue rather than overwriting issued bytes.

**Acceptance.** Released-engagement uploads are denied, approved downloads remain policy-controlled, and next-year authorized PBC/bookkeeping works. Trace to [M4-07](auditsphere-requirements-specification-complete-practice-management-proposed.md#M4-07), [SEC-03](auditsphere-requirements-specification-complete-practice-management-proposed.md#SEC-03), [AC-32](auditsphere-requirements-specification-complete-practice-management-proposed.md#AC-32) and [AC-33](auditsphere-requirements-specification-complete-practice-management-proposed.md#AC-33).

## M365-S10 Secondary archive-copy observation

**Goal and owner.** Produce verifiable secondary SharePoint copies without overstating their protection. Owner: existing M4 records/archive and document-operation capabilities.

**Prerequisites.** Authoritative issued manifest/bytes, approved secondary library and expected grant policy, exact client/tenant/site identity and permitted worker capability.

**Main behavior.** Queue copying from the authoritative issued version independently of the assembly countdown/local freeze. Preserve source item/version/hash identities. Read back exact copied-file hashes and expected access grants before reporting the secondary copy as OBSERVED. A successful upload alone is insufficient. Keep copy outcome, committed local freeze and lifecycle projection separate. Assigned staff rights and privileged administration mean read-only grants are not administrator-proof immutability.

**Failures and recovery.** Missing file, hash mismatch or unexpected grant leaves observation blocked/pending under the existing status vocabulary. Retry/reconcile the same copy operation; do not overwrite or invalidate the authoritative issued record. Restore/recovery must revalidate identities and jobs before further external effects.

**Acceptance.** Copying can be verified before the 60-day deadline; failed copy does not fabricate local-freeze failure or OBSERVED status. Exact hash/grant mismatch is visible; recovery preserves source hashes and issue history. Trace to [M4-08](auditsphere-requirements-specification-complete-practice-management-proposed.md#M4-08), [AC-34](auditsphere-requirements-specification-complete-practice-management-proposed.md#AC-34), [AC-35](auditsphere-requirements-specification-complete-practice-management-proposed.md#AC-35) and [AC-49](auditsphere-requirements-specification-complete-practice-management-proposed.md#AC-49).

## Delivery order and acceptance evidence

Implement S01–S04 before enabling their dependent live routes; implement S05–S06 before accepting client intake; complete S07–S09 before external release; complete S10 before claiming verified secondary archives. Shared recovery and revocation checks apply throughout, not as optional final hardening.

For each story retain the release commit, adopted policy, exact test scope, non-secret request/operation IDs, local result, provider readback and denial/recovery evidence. Mocked tests support development but cannot prove live tenant boundaries. Exercise both an authorized target and an unauthorized target. A blocked external prerequisite remains blocked; environment variables or local fixtures do not establish provider acceptance.

## Open owner decisions

- **D08 tenant/security/operations owners:** approve any change to staff Full Control, privileged-worker custody/rollout, expected grants, external-revocation freshness and operating/recovery targets. Preserve approved rights until an explicit revised decision is adopted.
- **D01 Partner/finance/technical owners:** resolve financial-package publication versus final-bundle release semantics before binding mail or invoice automation to an event.
- **D02 records/professional owner:** confirm assembly-clock alignment, retention/holds and permitted supplementary records. Preserve existing archive authority and timer while flagging discrepancies.
- **D03 professional/product owner:** confirm service-specific portal access and closure behavior. Non-audit services must not acquire fabricated audit approvals.

These decisions qualify affected behavior only; they do not authorize new modules or require unrelated defined work to stop. See the [master decision register](auditsphere-requirements-specification-complete-practice-management-proposed.md#rev-decisions).
