# Angular migration source review — completion and file records

**Status:** PARTIAL_REVIEWED
**Source reviewed against code:** `147ac2a02675b9c65ec43be520c76bc27837a167`
**Verification run at repository head:** `2d34fca3966b9be9956dc8a645b71e8234899848`
**Pinned discovery snapshot:** `eb94ae5073558ec7192ddb5dfd4e24cecd7c4b39`

All three legacy artifacts match the pinned discovery hashes:

| Source artifact | SHA-256 |
|---|---|
| `src/AuditSphereOps.Web/Components/Pages/Completion.razor` | `b8236df8a8c196db794a970d6bbf88b1dd9335b2bd9cbd7fab55afe8c72b4bfb` |
| `src/AuditSphereOps.Web/Components/Completion/CompletionDeliverablesPanel.razor` | `484ef2b7744ffb93bb0491dd930a8336c94647d6901dd2e3cf0e78b56a10a1ec` |
| `src/AuditSphereOps.Web/Components/Completion/FileRecordsPanel.razor` | `6800cc22ec58491bc86172ac377fbcecb52925daca302d9affdf678b5b01b7c0` |

## Action mapping

| Legacy behavior | Angular/API/Application owner | Evidence and remaining boundary |
|---|---|---|
| Open completion from either the engagement route or the legacy completion alias; require an internal role scoped to the exact engagement; show review-point, financial-package approval, written-representation, EQR and release-candidate state. | Angular `EngagementCompletion` is mounted at both route forms. `GET /api/ui/engagements/{id}/completion` composes `EngagementCompletionWorkspaceQuery.GetAsync`, which authorizes the current firm and exact engagement before projecting persisted state. | The PostgreSQL browser journey verifies the private representation is visible to an assigned identity, denied to an unrelated client-scoped Manager, and removed when navigation changes to an unassigned sibling engagement. Full cross-firm, guessed-ID, and every role/state assertion remain open. |
| Prepare the release candidate only for a Partner/Administrator after package validation and all three review stages are approved. | The Angular action uses `CanPrepareRelease` from the Application projection and posts to `EngagementCompletionWorkspaceQuery.PrepareReleaseCandidateAsync`; `ApprovalService` and `ReleaseService` retain the approval, generation and scope checks. | The projection requires the validated package and approved management, accounting and Partner review stages before exposing the action. Exact stale-generation, duplicate/retry and all denial-message states still need direct HTTP and browser assertions. |
| Review third-party confirmation monitoring and change confirmation criticality with a rationale. | The completion workspace links to Angular's scoped confirmation dashboard; `POST /api/ui/confirmations/{caseId}/criticality` calls `AuditDeliverableService.SetConfirmationCriticalityAsync`. | The operation is present in the native dashboard/API. This completion-focused browser cohort does not exercise its complete role, rationale, stale-state and failure matrix. |
| Generate a Summary Review Memorandum, record separate Partner clearance, choose one of four opinion types, and require a taxonomy area and basis for a modified opinion. | Angular completion posts to the SRM, clearance and opinion endpoints; `AuditDeliverableService` owns the domain rules. | `CompletionDeliverablesJourneyTests` covers clearance refusal before a current SRM, successful clearance and a qualified opinion with focus and basis. All opinion types, taxonomy staleness and full error/role variants remain open. |
| Generate reports; share eligible versions with the client; resolve client comments; verify the exact signed representation version/hash; sign the current independent report. | Angular completion uses the report, share, comment-resolution, scan-verification and sign endpoints, all dispatched to `AuditDeliverableService`. | The API-host journey covers report generation/download, client review/acknowledgement, signed PDF upload, Partner scan verification, registered seal/signature and report signing. Full lost-response, stale-version and cross-client download matrices remain open. |
| Assemble the five-part final bundle only after its required report, client-signed representation, reviewed/released statements and posted balance-fee evidence exist. | Angular presents persisted bundle blockers and sends the exact statements-reviewed assent to `AuditDeliverableService.AssembleBundleAsync`; the protected download is served by the API. | The browser journey confirms assembly remains unavailable while evidence is missing, then assembles and downloads the bundle after isolated synthetic evidence is seeded. Every blocker permutation and unknown-result recovery remain open. |
| Show the post-signature freeze schedule and external SharePoint read-only observation; request a documented amendment while frozen; require another Partner to approve and close it to re-freeze. | The Angular completion page renders `CompletionFreeze`; API commands call `FileFreezeService` for request, approval and closure. External SharePoint state remains an explicit observed/blocked value. | Domain coverage verifies the freeze lifecycle, refusal of self-approval, independent Partner approval and re-freezing. A browser journey for amendment request/approval/close, revocation during the workflow and API guessed-ID responses remains open. Live SharePoint observation is `BLOCKED_EXTERNAL`. |
| Acquire/release document locks and show a role-scoped, filterable activity trail with its SharePoint observation boundary. | Angular completion posts lock commands and renders locks/trail from `EngagementCompletionWorkspaceQuery`; API delegates to `EngagementActivityQuery`. | The built-in browser rendered the completion page, lock controls, activity filter and local activity records without submitting a command. Direct browser assertions for lock contention, unauthorized release, trail redaction/filtering and error recovery remain open. Direct SharePoint edits are not observed by this local trail. |

## Authorization and mutation boundary

The Angular read is a bounded Application projection. The API obtains the
trusted actor from the authenticated session; `AuthorizationDecision` checks
the exact firm, engagement, internal-user status and supported AuditSphere
roles. Commands dispatch to the existing Application services, which own
role/scope decisions and persisted evidence. The Angular page does not call
Microsoft Graph or write business state directly.

The legacy Razor page performs its own scoped projections and exposes the same
Application command families. The reviewed Angular workspace composes the
completion and file-record sections on one native page. No Entra directory
role is inferred from an AuditSphere role. SharePoint read-only is displayed
only as an observed state; the page does not claim an external change-tracking
or protection provider has accepted it.

## Local verification

- Focused PostgreSQL-backed Release domain and API-host Angular Playwright
  commands passed. Exact commands, results and durations are recorded in
  [`status.json`](../execution/status.json).
- A read-only built-in-browser visit to the local Development completion route
  rendered completion gates, deliverables, file freeze, document locks and
  activity trail. No business command was submitted. This is not production
  acceptance.
- No application, domain, infrastructure or EF model source was changed in
  this documentation/review slice. The full solution regression and EF model
  drift check were not rerun; the latest complete solution result remains the
  earlier checkpoint recorded in `status.json`.

All three source rows remain `PARTIAL`; route ownership and these focused
journeys do not establish complete source-action parity. The open cross-firm,
guessed-ID, role/state, validation, stale-content, command retry/unknown-result,
accessibility and assistive-technology matrices are still required. The
file-record amendment/lock UI actions need direct positive and negative browser
coverage. The overall migration and Blazor retirement remain `NOT_READY`.
