# AuditSphere Angular UI

`AuditSphereOps.Api` is the ASP.NET Core backend; `AuditSphereOps.Ui` is the Angular frontend. Domain/Application/Infrastructure/Worker remain unchanged in responsibility. Native Angular routes now cover practice, accounting, audit, completion, administration and the client portal, but complete source-action parity and final retirement acceptance remain open. The legacy Web host is the rollback presentation host and reuses API composition.

## Build and verify

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
dotnet run --project src/AuditSphereOps.Api -c Release --no-launch-profile -- --urls http://localhost:5100
```

Open `http://localhost:5100/ui/app`; client identities use `/ui/portal`. API includes no Razor components or Blazor circuit endpoints. Its default enables the production Angular build; missing assets fail startup. Both API and rollback Web use the existing approved development user-secrets namespace. Do not copy secrets into JSON configuration or command history.

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

## Legacy rollback preview

From the repository root, keep the usual approved private database/identity configuration and run:

```sh
AngularUi__Enabled=true dotnet run --project src/AuditSphereOps.Web -c Release --no-launch-profile
```

Open `/ui/app` on that host and sign in through the existing `/auth/sign-in` flow. `AngularUi:BuildPath` can override the browser build directory when packaging deployment artifacts. These are preview instructions, not production rollout acceptance. The Web rollback host still requires a separately supplied Angular build if its preview is enabled. API publication bundles an already built frontend; neither host runs npm during .NET publish.

Disable `AngularUi:Enabled` and use `/app` to roll back. Auth, consent, API, health and document routes retain their existing owners.

## Conventions

Standalone components; lazy capability routes; small session service; local page signals; canceled superseded requests; runtime-decoded HTTP responses; no browser access tokens, Graph calls or financial calculators. All mutations require reviewed DTOs, server CSRF validation, current scope/revision authorization and deliberate unknown-outcome reconciliation. Never add automatic write retries.

Source discovery is generated with `python3 scripts/ui/inventory.py` from the repository root. Discovery does not establish accepted parity. See the architecture migration document and execution ledger for remaining stories.
