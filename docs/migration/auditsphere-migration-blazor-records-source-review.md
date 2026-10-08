# Records Archive and Release source/action review

**Disposition: PARTIAL for both source rows**
**Reviewed against repository commit:** `b3b4e1e3`
**Review observed:** 2026-10-05

## Records Archive

The legacy `RecordsArchive.razor` page owns
`/app/records/archives/{Id:guid}`. It displays the archive state, latest
manifest version and digest, immutable manifest entries and hashes, local legal
hold count, and requested/observed records-protection evidence. Incomplete
manifests are visibly identified. Both the legacy and Angular pages explicitly
state that a local request or hold is not proof of Microsoft Purview behavior.

Angular `ArchiveRecord` in `features/audit/records.ts` reads
`GET /api/ui/records/archives/{id}` through `AuditRecordQueries.ArchiveAsync`.
The Application query checks firm and exact client/engagement authorization
before projecting manifest entries, hold count, and records-action metadata.
The API returns a generic unavailable response for inaccessible IDs. There is
no client-side write action on this detail route.

## Release candidate

The legacy `Release.razor` page owns `/app/releases/{CandidateId:guid}`. It
shows the exact candidate revision and digest, external checkpoint,
protection-attestation expiry and signature-lineage states, and allows a
human-authorized issue only when configured preflight gates are satisfied.
The server rechecks authorization, approval, revisions, generations, holds,
manifest and recovery mode. The workflow does not claim provider acceptance.

Angular `ReleaseCandidate` in the same `records.ts` file reads
`GET /api/ui/releases/{id}` through `AuditRecordQueries.ReleaseCandidateAsync`
and posts issue requests to `POST /api/ui/releases/{id}/issue`. It clears the
one-time release-key model before dispatch; the form does not persist the key
as a browser draft. The Application `ReleaseService` remains the authorization
and release-evidence owner.

## Verification evidence

Pinned Razor hashes match the source-discovery inventory:

- Archive: `d3601645bc97021062bc444a9f71579f61d27450750a26913463fde99c98ed5b`
- Release: `0b47f8747fac34b68eb3583a20e1e0836793c81b52600e5a738bad4bdab76d88`
- Angular records feature: `10fce26b2ad2adc48c792c395edd867b8327c2951912b03a9c2cf56ddc7d12a3`

The focused PostgreSQL-backed API-host archive journey passed **1/1** and the
Angular Release cohort passed **2/2**:

```bash
dotnet test tests/AuditSphereOps.E2E.Tests/AuditSphereOps.E2E.Tests.csproj \
  --no-restore --configuration Release -m:1 \
  --filter 'FullyQualifiedName~AngularClientScopeAuditDetailJourneyTests'

dotnet test tests/AuditSphereOps.E2E.Tests/AuditSphereOps.E2E.Tests.csproj \
  --no-restore --configuration Release -m:1 \
  --filter 'FullyQualifiedName~AngularReleaseParityJourneyTests'
```

The archive detail journey verifies its authorized manifest and entry data,
client/sibling and guessed-ID denial, responsive layout, keyboard focus, and
absence of private archive markers on denied routes. The original Release
journey verifies expired protection is shown as expired rather than verified,
issuance stays disabled, a direct issue attempt writes no release, sibling
scope is denied, a guessed-ID route clears digest/profile/checkpoint content,
and a revoked grant removes candidate details after reload. It also checks six
viewport widths and visible keyboard focus. The new verified-candidate journey
submits once with a current protection attestation and read-back checkpoint,
drops the accepted issue response, and verifies that refreshed persisted
`ISSUED` state resolves the unknown outcome. It confirms the key is cleared
before dispatch and absent from browser storage, only one POST is sent, and
one immutable release event and one durable operation exist. The built-in
Development browser checked a guessed Release ID and showed the generic
unavailable state without protected record content.

## Gaps keeping both rows partial

- Complete role/scope, expired-grant, cross-firm, and route-error matrices for
  both pages remain open.
- Large-manifest behavior and remaining archive failure/recovery states need
  direct assertions. Empty and incomplete manifest display is covered by the
  follow-up journey below. A bounded paging implementation was added below,
  but its current Angular browser journey is awaiting a successful bundle build.
- Remaining Release validation/error states, human screen-reader, broader
  locale, and production acceptance remain open.

Neither row is promoted to parity verified. These page reviews do not establish
Purview provider acceptance or close overall migration acceptance.

### Incomplete empty-manifest browser evidence — 2026-10-07

At code commit `21e85a17`,
`AngularArchiveCompletenessJourneyTests.IncompleteEmptyManifestShowsBlockerAndNoEntries`
passed **1/1** in 33 seconds on an owned isolated PostgreSQL/API host. It
verifies the exact incomplete reason, zero-entry empty state, manifest
profile/version and digest, and distinct `Not observed` / `Not requested`
records-protection states. The route does not claim the archive is complete.

This closes the empty and incomplete-manifest presentation gap only. The
Records Archive row remains `PARTIAL` for large-manifest behavior, failure and
recovery paths, complete authorization matrices, human accessibility/locale
review and production acceptance. The full solution regression and EF drift
check were not rerun.

### Large-manifest paging implementation — focused verification passed — 2026-10-07

Archive detail reads now return at most 100 ordered manifest entries per
request, with total count and an ordinal cursor; the Angular page exposes a
progressive “Load next 100 entries” action. The production Angular build passed
outside the restricted sandbox (349.89 kB initial raw, 94.09 kB estimated
transfer; existing commercial-settings stylesheet budget warning remains). The
focused PostgreSQL Playwright journey passed 2/2, covering ordered,
duplicate-free 100/100/5 rendering and the incomplete/empty manifest state. Its
first run found an ambiguous status locator in the test, which was narrowed to
the archive summary before the passing rerun. The API paging journey passed
1/1 for exact totals/cursors, negative-cursor 400 and nondisclosing
foreign/guessed-ID denial. The built-in browser could not reach localhost:5105;
manual visual verification is still open. This focused result does not close
the broader Records Archive parity row or the overall `NOT_READY` migration
gate.

The records Angular contract suite now passes 6/6, including a component-level
failure/retry check: a transient next-page read error leaves the first page and
cursor intact, and an explicit retry appends the next ordered row and clears
the error. This does not replace the unavailable authenticated Playwright
failure-injection run.
