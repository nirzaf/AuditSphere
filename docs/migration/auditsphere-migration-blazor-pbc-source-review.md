# AuditSphere — Blazor PBC Source Review

**Status: `PARTIAL_REVIEWED`.** This review compares the two PBC source/action rows in the pinned Blazor discovery snapshot with the Angular/API implementation and current executable journeys. The evidence is focused and does not establish complete PBC parity. Exact run counts and source SHAs are recorded in `docs/execution/status.json`.

## Review boundary

The reviewed Blazor source hashes come from inventory snapshot `eb94ae5073558ec7192ddb5dfd4e24cecd7c4b39`. The Angular comparison is at `019126c`, including the accessible client file drop control and API-host journeys using synthetic PostgreSQL fixtures. The staff journey runs the Test simulation worker and verifies its result. It does not establish live Microsoft/SharePoint acceptance.

## Source/action crosswalk

| Blazor source/action | Current Angular/API evidence | Disposition and remaining work |
|---|---|---|
| `Pages/PbcRequests.razor`: scoped staff inbox; request creation and send; request-more conversation entry; staged upload completion; transfer/download state | `features/engagements/pbc.ts`; `AngularPbcStaffJourneyTests.StaffInbox_CreatesRequest_RequestsMoreFiles_AndQueuesVerifiedTransfer` | **Partial.** The API-host journey creates and sends a scoped request, checks persisted request revision, recipient/reviewer and queued email, records a follow-up message and queued email, then completes a pre-staged upload. It verifies the client/engagement/intent-bound operation first as `PENDING`, then runs the Test simulation worker, observes `COMPLETED`/`RECEIVED` in persisted state and the refreshed UI, and downloads the exact bytes through the authenticated API with safe cache/sniff headers. The live selected-site worker path, all staff authorization, empty/error/stale states, and the complete source-action assertions remain open. |
| `Pages/ClientPbcRequest.razor`: client request detail, first sign-in, delegation and revocation, reply, file selection/upload, refresh and access denial | `features/portal/request.ts`; `AngularClientPortalJourneyTests.ClientPortal_FirstSignIn_Delegation_Conversation_Upload_AndRevocation`; `AngularClientPortalUploadRecoveryJourneyTests.LostChunkAcknowledgement_ResumesExactFileFromPersistedChunkReceipt` | **Partial.** The journeys verify first sign-in, delegation and revocation, a client reply, file drop, upload staging and hash, same-document denial after the user's identity is disabled, and recovery after a lost chunk acknowledgement. Recovery requires the exact file name/size/hash, uses the persisted offset, rotates the upload capability, and verifies the reconstructed bytes. Expiry, all request/upload states, every failure and stale-session combination, and complete download authorization/byte parity remain open. |

## Verification evidence

- The combined PBC staff, client portal and upload-recovery regression passed 3/3 at code commit `019126c`; the staff case verifies queue-to-worker completion, UI state refresh and exact API download bytes.
- The client portal and recovery cases exercise first sign-in, delegation/revocation, reply, a `DataTransfer` drop event and staged file hash, then exact-byte resume after a lost acknowledgement with capability rotation.
- At `efc1831`, the Angular production build succeeded with the recorded initial-bundle budget warning, and Angular CI passed 476/476 tests across 91 files. Exact commands and outcomes are in `status.json`.
- A read-only built-in-browser visit to the staff PBC route rendered the request timeline. The Development fixture had no eligible client recipient/reviewer, so request submission was unavailable and no command was submitted. The drop event itself was verified in the API-host Playwright journey.

## Result

Both PBC source/action rows are `PARTIAL`; neither is promoted to `PARITY_VERIFIED`. The Test simulation worker and download-byte path now pass through the Angular staff journey. Live selected-site worker delivery through that route, the full failure, authorization and stale-state matrix, and the assertion-level crosswalk for removed `PbcUploadJourneyTests` remain open. The overall Blazor removal decision remains `NOT_READY`; the other 60 unanalyzed source/action rows, 28 supporting files, remaining removed-suite assertions, production rollback/canary, human accessibility/locale review, live Microsoft gates and separate owner acceptance remain open.
