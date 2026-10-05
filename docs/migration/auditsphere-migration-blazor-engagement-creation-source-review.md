# Angular migration source review — blocked engagement creation

**Status:** PARTIAL_REVIEWED  
**Source reviewed against code:** `147ac2a02675b9c65ec43be520c76bc27837a167`  
**Verification run at repository head:** `1b95c2de849fb0b75d208136647bb2cae119a331`  
**Pinned discovery snapshot:** `eb94ae5073558ec7192ddb5dfd4e24cecd7c4b39`

The legacy creation panel hash matches the pinned discovery snapshot:

`src/AuditSphereOps.Web/Components/Acceptance/EngagementCreatePanel.razor`  
SHA-256: `f36a652b19a33a7fa013020d78708b562e584271e07ddbb18b7ce880ca72e7e6`

## Action mapping

| Legacy behavior | Angular/API/Application owner | Evidence and remaining boundary |
|---|---|---|
| Enter service route, service profile, period start and period end for a client. | Angular `/app/clients/{id}/engagements/new` uses bounded Signal Forms and `EngagementCreationWorkspace.StateAsync` for the exact client, identity, session and current client revision. | `EngagementCreationWorkspace.Canonical` trims and validates route/profile and exact ISO dates; reversed or invalid periods fail before publication. Unit and journey coverage exercises fields and revision changes. Full validation/display and assistive-technology parity remains open. |
| Create a new engagement shell and tell the user it starts blocked until Partner activation. | The native route previews the exact fields, requires explicit assent and calls `EngagementCreationWorkspace.ExecuteAsync`. `EngagementLifecycleService.CreateDraftAsync` is the shared Application/domain lifecycle authority. | PostgreSQL assertions verify one `Draft` with `ProfessionalWorkBlocked=true`, one immutable creation receipt, and zero activation records. Activation stays a separate Partner decision; creation does not grant portal or Microsoft access. |
| Avoid duplicate service-period shells and let the user inspect an existing match. | The preview detects an existing exact client/service/period and shows its engagement link with no create control. The lifecycle service serializes creation per client and accepts only an identical existing service profile. | The API-host journey verifies the existing-shell state and refuses duplicate creation. Mismatched profiles fail closed in the service. Cross-firm and guessed-client-ID browser/HTTP assertions remain open. |
| Preserve work across navigation and recover a lost command response. | Angular saves editable fields and request identity in bounded identity/client-revision tab drafts; review assent is never persisted. Unknown results require immutable actor-owned receipt lookup, and a retry requires identical fields and fresh review. | Five PostgreSQL-backed domain tests pass. The two-mode browser journey verifies draft save/restore, explicit review, an accepted server command with a dropped response, reload reconciliation, duplicate detection, responsive layout and clearing after Manager-role revocation. |

## Authorization and mutation boundary

The creation workspace requires a current Partner or Manager grant scoped to
the exact client, derives the firm and actor from trusted session context, and
rechecks authority around projection and mutation. Publication locks the
client safety generation and actor row, validates the client and current grant,
and commits the blocked shell with its immutable request receipt. Request hashes
bind firm, actor, session epoch, client, request identity and normalized fields;
reusing an identity for changed fields is refused.

The rollback Blazor panel calls the same `CreateDraftAsync` Application service.
The legacy-compatible direct API route remains available for that rollback
host; Angular uses the reviewed preview/assent/receipt endpoints. Both paths
create blocked drafts only, and neither implicitly accepts or activates the
engagement.

## Local verification

- Angular CI: 483/483 passed across 94 files; the engagement-creation unit file
  contains ten focused contract/state/recovery cases.
- Angular production build passed. Initial bundle: 506.60 kB, 6.60 kB above
  the 500 kB warning budget.
- PostgreSQL-backed domain review/recovery tests: 5/5 passed, including
  concurrent idempotency, current scope/session/revision, revocation rollback,
  serialized receipt lookup and database consistency guards.
- PostgreSQL-backed API-host Playwright: 2/2 passed across canonical and `/ui`
  route modes.
- Built-in browser access for the current unassigned Development identity
  showed the generic Engagement creation unavailable state, with no client
  details or mutation controls. Authorized creation used synthetic identities
  in Playwright.
- No EF entity/model changed. The EF pending-model command and full solution
  suite were not rerun. The latest complete solution result remains 1001/1001
  at `ead85032de2ccc4d4c8043398fa8471d395376a9`.

The row remains `PARTIAL`: the core blocked-draft creation, duplicate prevention
and uncertain-result recovery are exercised, while the full cross-firm/guessed-
ID HTTP matrix, every validation/error state, and accessibility parity remain
open. Blazor retirement remains `NOT_READY`.
