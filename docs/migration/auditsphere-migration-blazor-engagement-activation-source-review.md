# Angular migration source review — engagement activation

**Status:** PARTIAL_REVIEWED  
**Source reviewed against code:** `147ac2a02675b9c65ec43be520c76bc27837a167`  
**Verification run at repository head:** `836ab6a63b6025761ae36efbdbd387b0ff4a3af1`  
**Pinned discovery snapshot:** `eb94ae5073558ec7192ddb5dfd4e24cecd7c4b39`

The legacy activation panel hash matches the pinned discovery snapshot:

`src/AuditSphereOps.Web/Components/Acceptance/EngagementActivationPanel.razor`  
SHA-256: `bb9e2db0ac4ab10da2a12bd45dddd2f722ae16e17aa685023b15c6902179a40d`

## Action mapping

| Legacy behavior | Angular/API/Application owner | Evidence and remaining boundary |
|---|---|---|
| Read activation evidence for the current firm and engagement; show an action only for a draft engagement whose professional work is blocked. | `WorkspaceQuery.EngagementAsync` projects current status and Partner activation authority. The dedicated Angular `/app/engagements/{id}/activation` route loads `EngagementActivationWorkspace.StateAsync`. | The Application projection rechecks the exact engagement grant and the activation workspace separately requires a current Partner grant. Unassigned Development browser access returned the generic unavailable state. Full cross-firm and guessed-ID HTTP/browser assertions remain open. |
| Explain that the client must have a current unconditional acceptance for the same service route and no active hold; link back to the checklist. | `EngagementActivationWorkspace.StateAsync` returns the latest non-pending same-route decision, both generations, active holds, eligibility and typed blockers. | `AcceptanceChecklistTests.Activation_RequiresACurrentUnconditionalPartnerAcceptanceForTheSameRoute` covers no decision, declined, wrong-route, stale and conditional decisions, active holds, non-Partner refusal, successful activation, idempotent direct-service repeat and immutable activation evidence. |
| Activate and report the recorded decision, generation, path and timestamp. | Angular requires a server preview bound to the exact review basis, explicit assent, then posts to the reviewed activation command. `EngagementLifecycleService` remains the transactional domain authority and increments the engagement generation. | The activation journey verifies both canonical-route settings, explicit assent, one persisted activation after the API accepts a command whose browser response is lost, exact receipt reconciliation after reload, acknowledgement, and denial/clearing after Partner revocation. The legacy `/activate` compatibility endpoint remains for the rollback host and applies the same Partner/acceptance/hold domain checks; Angular uses the reviewed activation workspace. |
| Render safe failure and recovery states. | The Angular route retains only the request ID and hash in tab-scoped recovery storage, blocks blind retry after an unknown result, performs actor-owned receipt lookup, requires fresh review when no receipt exists, and clears protected content on session or route changes. | Eight activation unit cases cover exact revisions and decoders, explicit assent, receipt recovery, same-request retry review, malformed/other-actor receipts, A→B→A route fencing, session loss/destruction, and unavailable recovery storage or scope. Full presentation and assistive-technology parity remains open. |

## Authorization and side-effect boundary

The Angular activation state, preview, execution and receipt lookup use the
Application Partner authorization for the exact engagement and firm. The
transaction locks the client safety generation, rechecks the actor, validates
the current unconditional decision for the engagement's service route and
rejects active holds before activation. The persisted receipt captures the
acceptance decision/path and client and engagement generations. Activation does
not grant an AuditSphere role or assert Microsoft or SharePoint provisioning.

The legacy panel reads its status through its rollback host and calls the same
Application lifecycle service. The Angular UI uses the stronger reviewed
preview/assent/receipt workflow. That compatibility boundary is recorded here;
this review does not claim the rollback UI has been retired.

## Local verification

- Angular CI: 483/483 passed across 94 files.
- Angular production build passed. Initial bundle: 506.60 kB, 6.60 kB above
the 500 kB warning budget.
- PostgreSQL-backed domain activation gate: 1/1 passed, covering the acceptance,
route, generation, hold, Partner, idempotent repeat and immutable-evidence
conditions listed above.
- PostgreSQL-backed API-host Playwright: 2/2 passed across canonical and
`/ui` route ownership. It exercises explicit review, a lost browser response
after server acceptance, receipt recovery after reload, responsive width, and
revocation clearing.
- The built-in browser showed the generic unavailable state for the current
unassigned Development identity; no engagement details or mutation controls
were disclosed. Positive authorized behavior used synthetic identities in
Playwright.
- No EF entity/model changed. The EF pending-model command and complete solution
suite were not rerun. The latest complete solution result remains 1001/1001 at
`ead85032de2ccc4d4c8043398fa8471d395376a9`.

The source row remains `PARTIAL`: focused evidence covers the core authorization,
activation and recovery path, but complete cross-firm/guessed-ID HTTP controls,
all role and blocker combinations, every display/error state, and
assistive-technology parity remain open. Blazor retirement stays `NOT_READY`.
