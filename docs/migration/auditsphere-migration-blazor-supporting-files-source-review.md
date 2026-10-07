# AuditSphere — Blazor Supporting Files Source Review

**Status:** `PARTIAL_REVIEWED`

**Reviewed source commit:** `e9b10a7554fb6efb984e1a3040f33e32db853a71`

**Discovery inventory:** [`auditsphere-migration-blazor-retirement-inventory.md`](auditsphere-migration-blazor-retirement-inventory.md)

This review covers the 17 supporting Web artifacts that were still marked
`NOT_ANALYZED`. Their SHA-256 hashes are pinned in
[`auditsphere-migration-blazor-supporting-file-reviews.json`](auditsphere-migration-blazor-supporting-file-reviews.json)
and checked against the reviewed Git commit by the inventory generator. The
other 11 supporting artifacts already have partial reviews in the shell and
layout source review. This document traces ownership and retirement impact; it
does not establish full behavior parity or authorize removing the Web rollback
host.

| Legacy artifact | Angular / API owner and observed boundary | Open evidence |
|---|---|---|
| `AuditSphereDbContextFactory.cs` | Design-time EF factory only. Runtime persistence is composed by `ApiHost`; the documented EF command uses `AuditSphereOps.Api` as startup project. | Prove migration scripts, CI and operator workflows no longer depend on the Web factory before retirement. |
| `AuditSphereOps.Web.csproj` | Rollback presentation project; references API, Domain, Application and Infrastructure and adds MudBlazor/Razor dependencies. API does not reference Web. | Keep the project while the rollback/cutover gate is open; verify publish, CI and dependency references at the final retirement review. |
| `Authentication/CurrentActorResolver.cs` | Blazor adapter delegates principal resolution to the shared API `TrustedActorResolver`. Angular calls same-origin APIs; the API resolves the authenticated principal on each request. | Full old/new session, revocation and role/scope parity is not established for every route. |
| `Components/Administration/AdministrationDashboard.razor` | Displays the Application `AdministrationOverview` projection; `AdministrationOverviewQuery` and `/api/ui/administration/overview` own state, while Angular `features/admin/overview.ts` renders it. Existing query and Angular administration journeys cover selected cards, progress and authorization. | Exact card/detail, stale/error and accessibility parity across every state is incomplete. |
| `Components/Theme/AuditSphereTheme.cs` | MudBlazor-only color, typography, radius and drawer tokens. Angular Material and `src/styles.scss` own native Angular tokens and component styling. | No full token-by-token, responsive visual comparison or human assistive-technology acceptance. |
| `Components/_Imports.razor` | Compile-time Razor namespace imports; it has no Angular runtime counterpart. | Retires with Razor source only after the host removal gate is accepted. |
| `Program.cs` | Composes the shared `ApiHost` with Razor Interactive Server, MudBlazor and `legacyPresentation: true`. The API serves Angular routes and assets through its explicit route catalogue. | The Web host is still the rollback path. Production-like cutover and rollback acceptance remain open. |
| `Properties/launchSettings.json` | Local developer launch profiles only; no deployed route or business behavior. | Confirm local rollback instructions and supported developer profiles before removing the project. |
| `appsettings.Development.json` | Development logging and the legacy-presentation toggle. Configuration values were not copied into this review. | Reconcile every retained development default with the API host and remove only after rollback use ends. |
| `appsettings.json` | Web host configuration surface, including shared API settings and optional external capability keys. Values are intentionally omitted; secrets belong in approved secret stores. | Check all deployment configuration bindings and secret-store mounts at cutover; no production config review is claimed here. |
| `packages.lock.json` | Web dependency lock. At the reviewed commit it includes the API project's transitive OpenAPI package graph; it remains part of the rollback build. | Revalidate locked restore and remove only with the Web project after separate retirement acceptance. |
| `wwwroot/app.css` | Legacy base, form, focus, responsive and upload styles. Angular owns its own global and feature styles. | The styles are not a complete one-to-one token or layout migration; cross-route visual parity is open. |
| `wwwroot/draft-state.js` | Legacy generic native-form autosave/restoration, local/session storage fallback, field hints and cleanup after access revocation. Angular uses explicit typed draft services and feature-level recovery, including identity/revision-bound tab drafts. | Storage lifetime, identity partitioning and behavior differ; every remaining draft-bearing form needs a deliberate replacement or retirement decision. |
| `wwwroot/enterprise-ui.css` | Legacy MudBlazor shell and page styles. Angular Material tokens and feature styles own the native UI. | No whole-application screenshot/viewport comparison or human screen-reader acceptance. |
| `wwwroot/pbc-upload.js` | Blazor file selection/drop-zone helpers and chunk transport to the protected PBC API. Angular `features/portal/request.ts` owns the client file selection, drop zone, upload/resume states and progress; PBC journeys cover selected API/browser behavior. | Full old/new cancellation, network-failure, resume and accessibility matrices remain open; live selected-site acceptance remains external. |
| `wwwroot/reconnect-state.js` | Blazor Server SignalR circuit reconnect/reload handling. Angular uses HTTP session refresh, safe API errors and explicit recovery; it has no Blazor circuit. | Confirm the user-facing recovery/error contract in the production-like cutover; the SignalR code remains necessary to the rollback host meanwhile. |
| `wwwroot/search-shortcut.js` | Legacy `/` focus shortcut and search clear helper. Angular `features/search/search.ts` owns the current shortcut and search state; `AngularSearchShortcutAcceptanceTests` covers the native journey. | Complete locale, focus, overlay and route-state acceptance remains open. |

## Verification boundary

- The manifest records hashes for all 17 artifacts from the reviewed commit;
  the Web project files in the working tree matched that commit at review time.
- The inventory generator now renders evidence-linked `PARTIAL` statuses for
  reviewed supporting files and rejects missing evidence, duplicate paths,
  incomplete gaps, malformed hashes, or a hash mismatch at the reviewed commit.
- Existing API, Application, Angular and focused browser tests were mapped
  where relevant; no new browser journey or PostgreSQL-backed test was run for
  this source-only review. The configured PostgreSQL endpoint was unavailable
  in the prior review window.
- The optimized Angular build still aborts with an esbuild deadlock on both the
  current clean source snapshot and the earlier snapshot that had a recorded
  passing build. This is recorded in `docs/execution/status.json`; it does not
  prove a code regression or a successful current production build.
- The current migration decision remains `NOT_READY`. Seventy-five of 76
  source/action rows remain partial, no supporting artifact is promoted to
  parity, and production rollback/canary, human accessibility/locale, live
  Microsoft and separate owner acceptance remain open.
