# F25 / AUTO-020 / M365-022 mail acceptance composition (2026-11-27)

## Disposition

**IMPLEMENTED — deterministic harness and offline composition only. BLOCKED_EXTERNAL — live acceptance.** The live test was not run. No tenant, directory, recipient, credential, production or deployment effect was attempted.

## Source and behavior

- Audit baseline: `62059aa2e622bcb6245a211380021f5cf315cd97` (2026-11-26).
- Source commit: `bc1028084a8c8da88c6603287739787f1c87acac` (pushed to `master`).
- Before the change, the live fixture composed obsolete `GraphMailOptions` / `GraphPbcMailSender` constructors, used a random PBC firm/database unrelated to the tenant fixture, and treated generic role verification as `OUTBOUND_MAIL VERIFIED`.
- The live harness now reads the existing Worker-owned configuration and build identity through `MailAuthorizationDeploymentConfiguration.ReadBuildRepositoryCommit` and `.Read`, validates the existing `GraphMail:*` binding and FILE/ENROLLED evidence configuration, and reports a capability-specific blocker before provider setup. It does not invent environment keys or use synthetic test signatures as live evidence.
- The random PBC mail fixture and obsolete sender construction were removed. Generic tenant capability verification no longer includes outbound mail; the live directory fixture sets outbound mail disabled and asserts that role verification cannot mark it verified.
- Offline coverage composes the enrolled evidence source, current-binding runtime gate, standard Graph options and sender with synthetic signer/HTTP transport. It asserts one token and one Graph request for the synthetic success, and zero token/Graph requests for wrong-firm or wrong-sender binding. Existing stale connection/consent and denial coverage remains.

## Changed files

- `tests/AuditSphereOps.Domain.Tests/LiveTenantAdministrationMutationTests.cs`
- `tests/AuditSphereOps.Domain.Tests/GraphPbcMailSenderTests.cs`
- `tests/AuditSphereOps.Domain.Tests/MailAuthorizationAttestationTests.cs`

## Verification

- `./scripts/agent-check --list` — no matching packet; available packets are engagement-ui, review-permissions and accounting-calculation.
- PostgreSQL 19 on the approved isolated local port 5433 was reachable.
- `AUDITSPHERE_TEST_CONNECTION='Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=qts' dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj --no-restore -c Release --maxcpucount:1 --filter 'FullyQualifiedName~GraphPbcMailSenderTests|FullyQualifiedName~MailAuthorizationAttestationTests.EnrolledEvidenceRetainsExactBytesAndCannotOutliveItsCurrentBinding|FullyQualifiedName~MailAuthorizationAttestationTests.RuntimeGate_BindsPinnedReceiptToCurrentConnectionAndConsent'` — **10 passed, 0 failed**.
- `git diff --check` on the three source files — passed.
- F25 exact-source fast CI `45905263044` is running on `bc1028084a8c8da88c6603287739787f1c87acac`; its Angular job failed with three five-second timeouts (199/201 files, 2,155/2,158 tests), while .NET Release was still running at this observation. Docs, change classification, M365 offline safety, and MCP jobs passed. This run is not terminal evidence yet.
- F28 exact-source fast CI `45901158827` is still running on `8f1ba239d8a189c54d58a4c54a4429a73abcec36`; its Angular job failed with three five-second timeouts and its .NET Release job was still running at this observation. This is not F25 CI evidence.
- Live `LiveTenantAdministrationMutationTests` — **NOT_RUN**, as required without explicit owner authority for the exact retained Acceptance target, recipient and directory mutations.

## Remaining live gate

No approved retained Acceptance installation/database identity or matching firm/current connection/current consent was located for this harness. The required independently signed current Legacy evidence, exact deployment-bound tenant/client/sender/certificate/build-SHA identity and owner-approved recipient are also absent. Resume only when the owner supplies those exact target and evidence identities through the existing private configuration sources and separately authorizes the live mutations. Graph acceptance would not prove mailbox delivery.
