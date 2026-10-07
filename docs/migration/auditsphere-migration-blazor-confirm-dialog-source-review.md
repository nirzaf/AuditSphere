# Blazor shared confirmation dialog source/action review

**Disposition:** `PARTIAL`
**Review baseline:** legacy sources at `d29f147c77fd7900afa0e25a99f344fb393ff546`; Angular implementation and browser assertions at `0d819cd9ed95742eb251c4612b07e6f22f9a3c94`.
**Legacy source SHA-256:** `src/AuditSphereOps.Web/Components/Shared/ConfirmDialog.razor` — `79b1e95244cb8e172eb3939ad10347d364018305d5b12b2518ae2e80e0ac428d`

| Reviewed caller or implementation | SHA-256 |
|---|---|
| `src/AuditSphereOps.Web/Components/Pages/Administration.razor` | `93d7b62102b620d15e6bd89860689dfc251fa98a7e72c45a595662577107241c` |
| `src/AuditSphereOps.Web/Components/Pages/Leads.razor` | `e467a74d007de30e672b9123489363e4e98a61054ee5ae0b2048d698efa8867c` |
| `src/AuditSphereOps.Ui/src/app/features/admin/access.ts` | `fce8d659b1010a5ce544b890fd4581bdcc2b8b788c9293c079f2a47727258683` |
| `src/AuditSphereOps.Ui/src/app/features/admin/roles.ts` | `a971b5cd295f4275b1d54143a8cc86d23e8e9d74971991527f3b5fc2bc3eb0df` |
| `src/AuditSphereOps.Ui/src/app/features/commercial/leads.ts` | `e0f50240756468885247790af4ed36c6b442c5a36b52f494fc9ea494235acb2d` |

## Scope and callers

The shared Razor dialog has two production callers: `Administration.razor`
confirms local role-grant revocation, and `Leads.razor` confirms commercial lead
qualification. Both require an explicit confirm or cancel. The revoke caller
requires a reason; qualification explains that it does not accept a client or
grant client access. The dialog closes before the caller runs its Application
command, so command results are rendered by the caller rather than by the
dialog's unused `ServerError` parameter.

Angular keeps those confirmations inside their owning workflows instead of
introducing a generic cross-feature dialog. This preserves the distinct
consequences and local recovery behavior:

| Legacy caller | Angular replacement | Evidence |
|---|---|---|
| Revoke role grant after identifying the target and grant; require a reason; invalidate protected sessions. | `UserAccess` opens `AccessRevocationDialog`, which shows the user's display name and Microsoft identity, the exact role and scope target, requires a reason and reviewed checkbox, prevents duplicate/unknown retries, and stays open on a known command refusal. Session invalidation closes the dialog. | `AngularAdministrationJourneyTests.ReviewedRoleScopeAndRevocation_PersistEvidence_AndInvalidateOpenSessions` verifies the target identity/scope, persisted revocation evidence, and invalidation of an open staff session. |
| Qualify one named lead; keep qualification separate from client acceptance and credit decisions. | `Leads` uses an inline confirmation that identifies the lead name and immutable record ID, states that acceptance and access are not granted, prevents duplicate submission, and keeps the confirmation visible through the command result. | `AngularCommercialJourneyTests.LeadQualificationConfirmationIdentifiesExactRecordAndKeepsAcceptanceSeparate` verifies the exact ID, separation message and persisted qualified state. |

The feature-specific controls are an intentional decomposition of a presentation
helper; they do not introduce a second command path. Revocation remains on the
current access API and application authorization. Lead qualification remains
on the existing leads API and firm-wide commercial authorization. No role or
scope is widened by the confirmation UI.

## Verification and remaining gaps

The complete Angular CI suite passed 491/491 tests across 95 files. The Angular
production build passed with the existing Commercial Settings stylesheet
warning (7.53 kB against the 4 kB warning budget). The focused PostgreSQL-backed
API-host Playwright cohort passed 2/2: the administration grant/revocation
journey and the new lead-qualification confirmation journey.

The source/action row remains partial. Complete role, scope, expiry,
cross-firm, unknown-result and revoked-session matrices across all affected
actions remain open, as do human screen-reader and wider-locale acceptance.
The full solution regression and EF drift check were not rerun for this slice.
This review does not change the Blazor retirement gate: keep the Web rollback
host until the migration readiness gate and separate owner acceptance pass.

### Follow-up browser evidence — 2026-10-07

`AngularAdministrationJourneyTests.ReviewedRoleScopeAndRevocation_PersistEvidence_AndInvalidateOpenSessions`
was extended to verify that closing the revocation dialog leaves the grant
active, and that the command stays disabled until a valid reason and the
explicit review checkbox are both present. The same journey then completes the
revocation, checks append-only evidence, and confirms that an already-open
protected staff session is invalidated.

The focused Release E2E command passed **1/1** in 47 seconds on an owned
isolated PostgreSQL/API host:

```bash
dotnet test tests/AuditSphereOps.E2E.Tests/AuditSphereOps.E2E.Tests.csproj --no-restore --configuration Release -m:1 --filter 'FullyQualifiedName~AngularAdministrationJourneyTests' --logger 'console;verbosity=minimal'
```

The separate built-in-browser check at `http://localhost:5099/ui/app/administration`
was unavailable because the local host refused the connection. The protected
journey was therefore verified in the owned API-host Playwright session, not in
the user’s interactive Development session.

This closes the cancel and required-confirmation assertions for the local
revocation flow. The shared source/action row remains `PARTIAL`: the full role,
scope, expiry, cross-firm and unknown-result matrices across both callers,
screen-reader and wider-locale review, and overall migration acceptance remain
open. The complete solution suite, Angular production build and EF drift check
were not rerun for this test-only change.
