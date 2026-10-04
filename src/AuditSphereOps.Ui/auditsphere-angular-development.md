# AuditSphere Angular UI

`AuditSphereOps.Api` is the ASP.NET Core backend; `AuditSphereOps.Ui` is the Angular frontend. Domain/Application/Infrastructure/Worker remain unchanged in responsibility. Native Angular routes cover practice, accounting, audit, completion, administration and the client portal; broader source-action parity and production acceptance remain open. The owner-approved local cutover is complete. `AuditSphereOps.Web` is retained only for automated Test-environment regression journeys and refuses startup outside `Test`.

## Build and verify

The Microsoft 365 tenant page includes selected-site draft editing, exact boundary verification, reviewed activation and immutable folder-template save/approval. These commands compose existing Application services and add no Graph permission. The selected-site certificate and unrelated-site denial control remain server configuration; an unconfigured provider shows `BLOCKED_EXTERNAL`. Saving a draft invalidates its previous selected-resource pass. An active configuration cannot be edited in place, and a local template approval is not provider acceptance. The tenant screen also lists accepted client workspace intents and bounded engagement repositories, reviews the exact target/resource/template fingerprint and requires a reason before folder provisioning. Dedicated client-site and staff Full Control health is read-only; its privileged worker stays separate. Unconfigured providers, missing accepted decisions or active bindings remain blocked. Unknown folder outcomes require a persisted-state refresh and a new review. Full rollout policy/recovery and migration acceptance remain open.

From this directory, use the Node version in `.nvmrc`:

```sh
npm ci
npm run build
npm run test:ci
```

From the repository root, after building the Angular browser artifacts:

```sh
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj -c Release --filter FullyQualifiedName~PortfolioQueryTests
dotnet test tests/AuditSphereOps.Api.Tests/AuditSphereOps.Api.Tests.csproj -c Release --filter FullyQualifiedName~UiContractTests
dotnet test tests/AuditSphereOps.E2E.Tests/AuditSphereOps.E2E.Tests.csproj -c Release --filter FullyQualifiedName~AngularPortfolioJourneyTests
```

The browser journey requires PostgreSQL and the existing Playwright browser setup. It verifies real .NET endpoints against synthetic data, not Microsoft tenant acceptance. Build this workspace before running the whole solution suite, which includes the Angular browser journey.

## Run API with the Angular build

From the repository root, build Angular and then start API with privately supplied approved database/identity configuration:

```sh
npm --prefix src/AuditSphereOps.Ui ci
npm --prefix src/AuditSphereOps.Ui run build
DOTNET_ENVIRONMENT=Development dotnet run --project src/AuditSphereOps.Api -c Release --no-launch-profile -- --urls http://localhost:5100
```

Open `http://localhost:5100/app`; client identities use `/portal`. API includes no Razor components or legacy circuit endpoints. Its default enables the production Angular build and canonical routes; missing assets fail startup. Supply local credentials through the existing approved development user-secrets namespace. Do not copy secrets into JSON configuration or command history.

### First administrator on a fresh installation

Supply `Setup:FirmId`, `Setup:InstallationId`, `Setup:BootstrapProofHash`, `Setup:InitialAdministratorTenantId` and `Setup:InitialAdministratorObjectId` through the existing approved private configuration boundary. The deployment operator supplies the corresponding installation proof privately to the approved administrator. The API does not accept a browser-selected firm or bootstrap administrator identity.

1. Sign in with that exact Microsoft tenant/object identity. The API directs the approved, unmapped identity to `/ui/setup/microsoft365`; it has no protected application access yet.
2. Review the initial local Administrator assignment and submit the installation proof. The Angular form clears the proof on submission. The existing onboarding service stores only proof/capability hashes and append-only grant evidence.
3. Sign in again to obtain a cookie with the current session epoch. An old bootstrap cookie cannot open protected application APIs. Setup cannot restore a previously revoked administrator.
4. Open Administration → Microsoft 365 → Tenant connection. If the deployment tenant has not been prepared, review its configured identity and prepare the current setup revision. This creates a connection revision requiring Microsoft consent; it does not assert consent or selected-site verification.
5. Follow the configured Microsoft consent flow and verify each capability separately. Missing deployment credentials, consent or selected-site grants remain blocked.

A lost bootstrap/preparation response requires a fresh sign-in or refresh and review of persisted state before another write. These local tests do not establish live Microsoft authentication/consent acceptance. The Test-only Web regression host is not an operator setup route.

### Optional Microsoft administration

In Administration → Users & Access, creation, new B2B invitations and approved Microsoft groups are separate controls. Each remains disabled unless its deployment capability is enabled and freshly verified. Existing members/guests can be selected through bounded directory discovery. Local AuditSphere roles never assign Entra administrator roles.

Workforce creation requires review of identity, local role, exact scope and the approved one-time password handling process. The original creation response may display a masked password briefly; clearing, hiding the page, session changes or closing removes it. A replay or recovery cannot redisplay it. A lost password requires the approved Microsoft reset process.

For an unknown Microsoft result, inspect Microsoft operation history, review the persisted original intent and reconcile it. This observes the immutable Microsoft result and may finish local binding; it does not retry the create or invitation. A lost form for an operation still recorded as pre-dispatch `AUTHORIZED` requires the original request/key and remains a parity gap. Microsoft groups require an allowlist entry and an observed membership review; a changed membership refuses the save and requires refresh/review. Retiring an allowlist entry preserves Microsoft memberships.

For Angular live reload, run API as above and then, in another terminal:

```sh
npm --prefix src/AuditSphereOps.Ui start
```

Open `http://localhost:4200/ui/app`. The checked-in development proxy sends API, auth and health requests to `localhost:5100`. Keep browser-visible Host/Origin for same-origin upload checks. A live Microsoft callback must be registered and privately configured for the browser-visible origin; this proxy does not relax callback validation. HTTPS production uses the approved host configuration, not this loopback development profile.

## Publish the API and frontend artifacts

After building Angular:

```sh
dotnet publish src/AuditSphereOps.Api -c Release -o artifacts/api
```

The API publish target bundles the Angular browser artifacts into `ui/`. Missing browser output fails publication. `AngularUi:BuildPath` can select another approved build directory at runtime. A deliberate backend-only publication uses `-p:PublishAngularUi=false` and requires `AngularUi:Enabled=false` or separately deployed artifacts. Do not expose a blanket SPA fallback, permissive CORS, or browser-held Microsoft bearer tokens.

### Controlled canonical routes

`AngularUi:CanonicalRoutes` defaults to true in the API host. The native catalogue
serves `/app`, `/portal` and `/setup/microsoft365`. The `/ui` preview URLs remain
usable, and both modes load the same fingerprinted assets from `/ui/`. Sign-in
and consent destinations follow the configured mode. The legacy Web host
has been retired.

For an approved API release rollback, restore the previous compatible API build
and its matching retained Angular assets using the deployment's release process.
Setting `AngularUi:CanonicalRoutes=false` is only a route-prefix compatibility
mode: it serves the same Angular application under `/ui` and does not restore
any legacy host. Do not start a second database writer or loosen authorization for rollback.

Before replacing a build, retain its complete browser artifact directory separately.
`AngularUi:PreviousBuildPath` may point to that distinct approved directory for the
operator-approved compatibility/rollback window. Current assets take precedence;
only missing fingerprinted scripts, styles and media fall back to retained assets.
Old HTML, source maps and unhashed files are excluded. Verify old-tab lazy navigation
and current API compatibility before deployment. Remove the retention setting and
directory only after the approved window. Local tests do not establish production
canary or owner acceptance.

## Retired Legacy Presentation Host

The legacy `AuditSphereOps.Web` host and all Blazor components have been completely retired.
The shared API host rejects `legacyPresentation=true` across all environments.
Use `AuditSphereOps.Api` for local and deployed Angular UI.

Authentication, consent, API, health and document routes remain owned by the API host.

## Conventions

Standalone components; lazy capability routes; small session service; local page signals; canceled superseded requests; runtime-decoded HTTP responses; no browser access tokens, Graph calls or financial calculators. All mutations require reviewed DTOs, server CSRF validation, current scope/revision authorization and deliberate unknown-outcome reconciliation. Never add automatic write retries.

Source discovery is generated with `python3 scripts/ui/inventory.py` from the repository root. Discovery does not establish accepted parity. See the architecture migration document and execution ledger for remaining stories.

## Confirmation tab drafts

The native confirmation workspace offers explicit case, batch and action draft saving in the current browser tab. These drafts expire and are convenience state; they are not shared server records or retained audit evidence. Reload the workspace, choose Recover, inspect the current evidence and review again. A changed authorized revision prevents recovery of an old draft. In-memory edits survive background refresh but need explicit refreshed-revision selection before submission.

Navigation offers keep editing, save in this tab and continue, or discard. A storage failure keeps the page open and reports memory-only edits. Closing the tab can lose tab storage. Drafts never include review authorization, passwords, credentials or files. A recovered pending submission requires persisted-state refresh and is never automatically retried. These controls currently apply to confirmations; other modules still require draft parity checks.
