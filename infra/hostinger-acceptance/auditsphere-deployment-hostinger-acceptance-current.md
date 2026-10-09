# Hostinger acceptance deployment

This is a separate, synthetic-data acceptance environment for the Hostinger VPS. It uses the
existing host Caddy instance and does not publish the API or PostgreSQL ports publicly. Its
Compose project, network, database, data-protection keys and upload staging are isolated from
the VPS's existing applications. External provider effects and development identity are off.

## Required owner configuration

Before startup, create an Entra confidential-client application for the acceptance tenant and
register `https://audit.steaudit.com/signin-oidc` as its web redirect URI. Keep its client secret
in a protected server-side `.env` file; never put it in this repository or chat. The first
administrator must be an existing user in the same tenant. Generate the bootstrap proof hash
using the application's approved setup procedure and place it in `.env` alongside the tenant,
application, administrator object, firm and installation IDs. Use fresh synthetic identifiers
and data only.

## Deployment outline

Run commands from `/opt/auditsphere/infra/hostinger-acceptance` on the VPS. The deployment
operator must first install Git and Docker Compose if unavailable, create `/opt/auditsphere` with
owner-only access, and check out the approved source revision there. Do not run OS upgrades or
restart the VPS as part of this deployment.

1. Create `.env` with `umask 077` and mode `0600`. Set `RELEASE_TAG` to the full source commit,
   a generated `POSTGRES_PASSWORD`, `FIRM_ID`, `INSTALLATION_ID`, `BOOTSTRAP_PROOF_HASH`,
   `IDENTITY_TENANT_ID`, `IDENTITY_CLIENT_ID`, `IDENTITY_CLIENT_SECRET`,
   `INITIAL_ADMIN_OBJECT_ID`, and a positive `DEPLOYMENT_EPOCH`. Use synthetic values only.
2. Validate the Compose model with `docker compose config -q` and build the API, worker and
   migration images with `docker compose --profile migration build`.
3. Start only PostgreSQL, wait for its healthy status, then run the one-shot migration job:
   `docker compose up -d db`; `docker compose --profile migration run --rm migrate`.
   Application containers never apply schema changes at startup.
4. Start the API and three core workers with `docker compose up -d api worker-general
   worker-processing worker-records`. Verify their logs and `http://127.0.0.1:18080/health/ready`
   locally on the VPS before changing DNS or Caddy.
5. Add the Caddy site block from `Caddyfile.audit.steaudit.com` to the existing host Caddyfile.
   Back up that file first, validate the complete config with `caddy validate`, then reload Caddy.
   Do not replace the existing Caddyfile or modify any existing site block.
6. Add a Cloudflare proxied `A` record `audit` → `72.62.252.165`, then verify HTTPS and the Entra
   sign-in flow in a browser. Ensure Cloudflare's origin mode is Full (strict) so the Caddy
   certificate is validated.
7. Bootstrap the initial firm administrator through the application's proof-backed setup flow.
   Confirm only synthetic acceptance records are present.

## Operational boundaries

- PostgreSQL is not published on a host port. The API is published only on host loopback port
  `18080`; host Caddy is the only public route.
- The network uses `172.29.91.0/24`; the API trusts forwarded headers only from its Docker
  gateway `172.29.91.1`. Change both the Compose subnet and trusted proxy together if the range
  conflicts with the VPS.
- Keep `ExternalEffects__Enabled=false`, `DevelopmentIdentity__Enabled=false`, and
  `AllowSimulationAdapters=false`. Do not add production client data, approved release data or
  external-effect credentials to this environment.
- Persistent volumes are `auditsphere-acceptance_postgres_data`,
  `auditsphere-acceptance_data_protection`, `auditsphere-acceptance_pbc_staging`, and
  `auditsphere-acceptance_release_checkpoints`. Back them up before upgrades and retain the exact
  image tag for rollback.
- This VPS has existing services. Do not alter their containers, databases, ports, Caddy routes,
  firewall rules or host packages as part of this app deployment.
