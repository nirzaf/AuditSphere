# Scope isolation and tenant administration test cases

These are proposed acceptance cases, not recorded passes. Use the [execution ledger](../execution/status.json) for observed results. Run local cases against an owned PostgreSQL database and browser host; run live Microsoft cases only in the approved Development tenant with dedicated test identities and reversible resources. Never put credentials, temporary passwords, tokens, or personal data in test output.

## AS-PAR-002: confirm the current leak repairs

Use firm F with client A and sibling client B. Give the viewer only a CLIENT grant for A, then repeat with only an ENGAGEMENT grant for engagement A1. Seed distinctive marker data in B after taking the initial list snapshot. For denied detail routes, compare B's response with a random nonexistent identifier after normalizing identifiers; compare status, body, redirects, and any downloadable bytes. A denial must not disclose B's existence or content.

| ID | Case | Expected result | Automation target |
|---|---|---|---|
| PAR-01 | Visit each scoped staff list, queue, search, and count route before and after adding B's marker data. Repeat for CLIENT and ENGAGEMENT grants. | Rendered text and counts are unchanged; no B marker appears. | Extend or rerun `SiblingClientIsolationJourneyTests`. |
| PAR-02 | Open every scoped B detail route and an equivalent random-ID route under each grant. | Both responses are indistinguishable after identifier normalization; neither contains the marker. | `SiblingClientIsolationJourneyTests`. |
| PAR-03 | Download B's PBC upload with only A access and compare to a random upload ID. | Same denial status and body, no redirect or bytes that reveal B. | `SiblingClientIsolationJourneyTests`. |
| PAR-04 | Revoke or disable the viewer after an authorized page has loaded, then refresh, navigate directly, and invoke any retained action. | No stale content or action remains available; the page uses the current uniform unavailable state. | Existing page-specific revocation journeys; add missing route coverage. |
| PAR-05 | Render the three repaired pages with missing grant, wrong client, and random ID. | The same nondisclosing unavailable state appears. Test semantic state and absence of protected data, not an obsolete heading string. | Focused page E2E tests. |

## AS-PAR-002: next isolation candidates

| ID | Case | Expected result | Automation target |
|---|---|---|---|
| PAR-06 | As client portal user X, open X's requests/uploads and try Y's direct URLs and download IDs; compare with random IDs. | X sees only X; Y and random IDs are indistinguishable, including byte endpoints. | New portal browser/API journey. |
| PAR-07 | Revoke X's portal grant while a request or upload page is open; refresh and retry any retained action. | Content clears and the command is denied without modifying data. | New portal browser journey. |
| PAR-08 | Give a group viewer only group G1. Add an otherwise similar group G2 with marker data and compare consolidation lists, counts, exports, and details. | G2 cannot change G1-visible output; G2 detail/export and random IDs are indistinguishable. | New group-scope browser/API journey. |
| PAR-09 | For each Application command touched by the repaired pages, submit A-scoped identity with B's real client, engagement, or record ID. | Command denies before mutation; no new row, revision, operation, external effect, or audit decision is created. | PostgreSQL-backed Application tests. |
| PAR-10 | Repeat PAR-09 with an ENGAGEMENT grant for A1 and a sibling engagement A2 in the same client. | The grant never widens to A2. | PostgreSQL-backed Application tests. |
| PAR-11 | Race grant revocation with a queued or retried command. | Execution rechecks current scope and leaves no unauthorized partial state. | Application/worker integration test. |

## Local verification after a repair

1. Run the focused sibling-client and changed-page suites. If a failure refers only to an old heading, update the assertion to the uniform unavailable behavior and verify protected data remains absent; do not weaken authorization assertions.
2. Run `dotnet build src/AuditSphereOps.Web/AuditSphereOps.Web.csproj --no-restore --configuration Release`.
3. Run `dotnet test AuditSphereOps.slnx --no-build --configuration Release` with PostgreSQL available. Record the actual outcome and any unrelated failures.
4. Run `dotnet ef migrations has-pending-model-changes --project src/AuditSphereOps.Infrastructure --startup-project src/AuditSphereOps.Web`.
5. Put observed commands, outcomes, source commit, and remaining coverage gaps in `docs/execution/status.json`; update the current-slice document only with those observed facts. Passing these cases leaves the broader authorization audit partial until remaining routes and commands are covered.

## Live Microsoft 365 Development tenant checks

These are separate external checks, not substitutes for local provider-contract tests. Use the authorized administrator session and only the approved test domain, user prefix, guest address, and managed group. Verify each capability is enabled and separately consented before a mutation. Capture redacted request/correlation IDs, resulting resource IDs in private evidence, capability state, and cleanup outcome. If consent, provider, or cleanup is unavailable, record `BLOCKED_EXTERNAL` rather than pass.

| ID | Case | Expected result | Cleanup |
|---|---|---|---|
| M365-01 | Attempt user creation with capability disabled or consent missing. | Fail closed; no Graph mutation or local role grant. | None. |
| M365-02 | Create one synthetic member with the approved domain and force password change; query it through exact-member read. | Exactly one enabled tenant user exists; local AuditSphere access is still absent until an explicit scoped grant. Never log the temporary password. | Delete the synthetic user and verify exact-member read no longer finds it. |
| M365-03 | Retry the same creation after an induced timeout or unknown response. | The UI reports unknown outcome and reconciles by exact identity; no duplicate user is created. | Delete any created synthetic user after reconciliation. |
| M365-04 | Attempt guest invitation with capability disabled or consent missing. | Fail closed; no invitation is sent. | None. |
| M365-05 | Invite one approved synthetic external address, then verify invitation status without treating it as app access. | One invitation is observed; no local role grant appears automatically. | Remove the test guest and verify removal. |
| M365-06 | Add a synthetic member to the one approved managed group; verify exact membership and retry the request. | Membership exists once; retry is idempotent and cannot change another group. | Remove membership and verify it is absent. |
| M365-07 | Submit a group outside the allowlist, a user from another tenant, or an unapproved domain. | Fail closed before Graph mutation and do not create local access. | None. |
| M365-08 | Revoke consent or disable a capability between loading the page and submitting a mutation. | Fresh authorization/capability check refuses the mutation; no local success claim. | Restore only the approved test configuration, if appropriate. |

Do not run M365-02, M365-05, or M365-06 against production identities or groups. A local stub pass does not establish live provider acceptance, and a successful Graph mutation does not establish production readiness.
