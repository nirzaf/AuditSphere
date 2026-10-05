# AuditSphere — Blazor PBC Source Review

**Status: `PARTIAL_REVIEWED`.** This review compares the two PBC source/action rows in the pinned Blazor discovery snapshot with the Angular/API implementation and current executable journeys. The evidence is focused and does not establish complete PBC parity. Exact run counts and source SHAs are recorded in `docs/execution/status.json`.

## Review boundary

The reviewed Blazor source hashes come from inventory snapshot `eb94ae5073558ec7192ddb5dfd4e24cecd7c4b39`. The current Angular comparison is at `efc183146c5b94cdda4e99de37b65312f1f3cf1f`, which includes the accessible client file drop control and a Playwright journey that dispatches a real browser `DataTransfer` drop event. Browser journeys use the API host and synthetic PostgreSQL fixtures. They do not establish live Microsoft/SharePoint acceptance.

## Source/action crosswalk

| Blazor source/action | Current Angular/API evidence | Disposition and remaining work |
|---|---|---|
| `Pages/PbcRequests.razor`: scoped staff inbox; request creation and send; request-more conversation entry; staged upload completion; transfer/download state | `features/engagements/pbc.ts`; `AngularPbcStaffJourneyTests.StaffInbox_CreatesRequest_RequestsMoreFiles_AndQueuesVerifiedTransfer` | **Partial.** The API-host journey creates and sends a scoped request, checks persisted request revision, recipient/reviewer and queued email, records a follow-up message and queued email, then completes a pre-staged upload. It checks the client/engagement/intent-bound durable operation in `PENDING`, timeline content, and the exact download link. It does not run the operation through the background worker or verify downloaded bytes. All staff authorization, empty/error/stale states, upload outcomes, and the source actions still need full assertion mapping. |
| `Pages/ClientPbcRequest.razor`: client request detail, first sign-in, delegation and revocation, reply, file selection/upload, refresh and access denial | `features/portal/request.ts`; `AngularClientPortalJourneyTests.ClientPortal_FirstSignIn_Delegation_Conversation_Upload_AndRevocation`; `AngularClientPortalUploadRecoveryJourneyTests.LostChunkAcknowledgement_ResumesExactFileFromPersistedChunkReceipt` | **Partial.** The journeys verify first sign-in, delegation and revocation, a client reply, file drop, upload staging and hash, same-document denial after the user's identity is disabled, and recovery after a lost chunk acknowledgement. Recovery requires the exact file name/size/hash, uses the persisted offset, rotates the upload capability, and verifies the reconstructed bytes. Expiry, all request/upload states, every failure and stale-session combination, and complete download authorization/byte parity remain open. |

## Verification evidence

- `AngularPbcStaffJourneyTests` passed 1/1 at code commit `c92a44aa37bafa02aea231090ae430e922b0388b`.
- The client portal upload and recovery journeys passed 2/2 at `efc183146c5b94cdda4e99de37b65312f1f3cf1f`. The first journey dispatches a `DataTransfer` drop event and verifies the staged file hash; the second verifies exact-byte recovery after a lost acknowledgement.
- At `efc1831`, the Angular production build succeeded with the recorded initial-bundle budget warning, and Angular CI passed 476/476 tests across 91 files. Exact commands and outcomes are in `status.json`.
- A read-only built-in-browser visit to the staff PBC route rendered the request timeline. The Development fixture had no eligible client recipient/reviewer, so request submission was unavailable and no command was submitted. The drop event itself was verified in the API-host Playwright journey.

## Result

Both PBC source/action rows are `PARTIAL`; neither is promoted to `PARITY_VERIFIED`. The Angular staff journey queues a durable transfer but does not prove worker delivery through the Angular path, and the client journeys do not close the full failure, authorization, download-byte, and stale-state matrix. The assertion-level crosswalk for the removed `PbcUploadJourneyTests` remains part of the nine-suite audit. The overall Blazor removal decision remains `NOT_READY`; the other 60 unanalyzed source/action rows, 28 supporting files, remaining removed-suite assertions, production rollback/canary, human accessibility/locale review, live Microsoft gates and separate owner acceptance remain open.
