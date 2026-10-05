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
- Archive empty/incomplete/large-manifest variants and all failure/recovery
  states need direct assertions.
- Remaining Release validation/error states, human screen-reader, broader
  locale, and production acceptance remain open.

Neither row is promoted to parity verified. These page reviews do not establish
Purview provider acceptance or close overall migration acceptance.
