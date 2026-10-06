# AuditSphereOps — HTTP Boundary and OpenAPI Contract Architecture

**Status: CURRENT.** Describes implemented, locally verified behavior of the hardened HTTP
boundary and the canonical OpenAPI contract. Production infrastructure as code is specified in
`infra/auditsphere-deployment-iac-runbook.md` and remains a compiled (not deployed) baseline.

---

## 1. Purpose and Authority

This document is the architecture authority for:

- the authenticated `/api/ui` and `/api/setup` HTTP boundary;
- the boundary rate-limit policy catalog and request-size policy;
- centralized browser-security headers and cache policy;
- the standardized boundary error shape;
- the canonical OpenAPI 3.1 contract and its drift protection.

It does not replace the business authorization model: `TrustedActorResolver`,
`AuthorizationDecision`, `RoleGrant` scope validation, firm/client/engagement isolation,
session-epoch validation and every professional gate remain the exclusive authority of the
Application layer. HTTP authorization answers only one question — *does this request belong to
an authenticated AuditSphere browser session?* The browser never becomes a business
authorization authority.

## 2. Authenticated Endpoint Groups

All `/api/ui` endpoints are registered on a single route group carrying
`RequireAuthorization(HttpPolicies.AuthenticatedSession())`
(`src/AuditSphereOps.Api/HttpBoundary/HttpPolicies.cs`); `/api/setup` reuses the same policy.
The policy requires an authenticated cookie session and nothing more: no role, scope or firm
decision is expressed at the HTTP layer. Unauthenticated requests are refused by the
authorization middleware before any business handler, workspace query or database command runs;
an established-but-revoked session intentionally passes the HTTP boundary and is refused by
`TrustedActorResolver` inside the handler — the two refusal sources are deliberately distinct
and covered by `HttpBoundarySecurityTests`.

## 3. Rate-Limit Policy Catalog

Native ASP.NET Core rate limiting (`AddRateLimiter`/`UseRateLimiter`) is configured as one
chained global limiter (`src/AuditSphereOps.Api/HttpBoundary/ApiRateLimiter.cs`) — .NET 10
removed the partition factories that return chained limiters, so the chain lives at the global
level and each stage no-ops outside its class. Only `/api` and `/auth` paths are limited;
static assets, health probes and the SPA shell are not.

| Class | Window (per identity) | Concurrency | Assignment |
| --- | --- | --- | --- |
| `NormalRead` | 240 / min | — | GET/HEAD under the API by default |
| `Command` | 60 / min | — | state-changing methods by default |
| `Search` | 30 / min | — | `/api/ui/search` via `ApiRateClassAttribute` |
| `Export` | 20 / min | 4 concurrent | CSV/financial-package/deliverable endpoints via endpoint metadata |
| `FileUpload` | 120 / min | 4 concurrent | GL CSV upload and PBC chunk streaming via endpoint metadata |
| `Authentication` | 60 / min | — | `/auth/sign-in` (IP-partitioned; shared origins tolerated) |
| `M365Administration` | 30 / min | — | `/api/ui/administration/microsoft365|directory`, `/auth/m365-consent` |

Partitioning uses the immutable authenticated identity (`tid`/`oid` claims) with a bounded
remote-IP fallback for anonymous authentication traffic. Browser-supplied firm, client or
engagement identifiers are never rate-limit identities. Behind a reverse proxy the anonymous
IP partition is the proxy address; deployments that need per-client IP partitioning must
configure forwarded headers with explicit known proxies. All budgets are overridable through
configuration (`HttpBoundary:RateLimit`).

Rejections return `429` with a `Retry-After` header and the standard error body
(`request.throttled`); rejected commands are never retried automatically by the client.

## 4. Request Size Policy

`HttpBoundary:BodyLimits` defines a host-level ceiling (`MaxRequestBodyBytes`, enforced by
Kestrel) and a JSON command ceiling (`MaxJsonBodyBytes`). JSON command bodies over the ceiling
are refused with a structured `413` (`request.too-large`) before business processing;
undeclared-length bodies are capped by bounded buffering. Multipart uploads and the PBC chunk
stream are exempt here because they carry explicit per-endpoint bounds and stream through
bounded staging writes.

## 5. Security Headers and Cache Policy

`SecurityHeadersMiddleware` (`src/AuditSphereOps.Api/HttpBoundary/`) applies to every response:
`X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, a restrictive
`Permissions-Policy`, `X-Frame-Options: DENY`, and — for HTML documents only — a
`Content-Security-Policy` with `frame-ancestors 'none'`, `object-src 'none'`,
`base-uri 'self'`, `form-action 'self'` and `default-src 'self'`.

The CSP keeps `script-src` strict without `'unsafe-inline'` or `'unsafe-eval'`: SHA-256 hashes
of inline scripts are computed at startup from the approved Angular build, so newly built
assets are re-hashed automatically. `style-src 'self' 'unsafe-inline'` is a reviewed,
documented need: the Angular production build inlines critical CSS and Angular Material
injects component styles at runtime, which style hashing cannot cover.

Cache policy: `/api` and `/auth` responses are `no-store` unless a handler set an explicit
policy; SPA shell responses remain `no-store`; fingerprinted Angular static assets are
`public, max-age=31536000, immutable` (previous-build fallback assets included), while
non-fingerprinted files are never cached.

## 6. Standardized Boundary Error Body

Boundary-produced refusals use the bounded `ApiError` record
(`src/AuditSphereOps.Api/HttpBoundary/ApiError.cs`):

```json
{ "code": "request.throttled", "message": "…", "correlationId": "…" }
```

`code` is stable; `message` is safe for display and never exposes SQL, stack traces, provider
exception bodies, tokens, secrets, internal paths or out-of-scope tenant details; and
`correlationId` matches the `X-Correlation-Id` response header and telemetry events. Existing
capability endpoints already use the same `{ code, message }` shape and are migrated
incrementally; the OpenAPI document registers `ApiError` as the shared schema.

## 7. Canonical OpenAPI 3.1 Contract

`src/AuditSphereOps.Api/Contracts/ApiContract.cs` configures native ASP.NET Core OpenAPI
(`Microsoft.AspNetCore.OpenApi`) to generate the `auditsphere` document from the actual
endpoints, exposed at `/api/contract/auditsphere.json` outside production (production exposure
is disabled by default). The document describes transport behavior only — URL, method,
parameters, request body, response status, content type and authentication requirement — and is
never the business authorization authority.

- Protected operations carry the `sessionCookie` security requirement and the boundary-produced
  `401`/`429` responses; JSON-bodied POST commands additionally declare `413`.
- `/auth/sign-in` and other anonymous endpoints declare no security.
- The `ApiError` schema is registered in components.
- Financial decimal schemas are exact strings (`type: string` with
  `^-?[0-9]{1,29}(?:\.[0-9]{1,28})?$`), matching the `DecimalStringConverter` wire behavior —
  financial values are never coerced to JavaScript numbers.

### Contract generation policy

The committed artifact `contracts/auditsphere-openapi.json` is the drift-detection reference.
`scripts/contracts/verify-openapi.sh` regenerates the document in memory (no database) and
fails on any diff; CI runs it after the Release build. Decision: **commit the artifact and
verify currency in CI** (option 1 of the contract policy). Generated transport types were not
introduced: the Angular `Api` service remains the request-execution authority with its
existing command/outcome handling — no automatic retries, `outcome.unknown` semantics, session
invalidation fences, timeouts, safe messages, download-context validation and antiforgery
integration — and runtime decoders remain in place for IDs, enums, revisions, durable-operation
outcomes and exact decimal strings.

## 8. Test Coverage

`tests/AuditSphereOps.Api.Tests/HttpBoundarySecurityTests.cs` and `OpenApiContractTests.cs`
cover: unauthenticated refusal before handlers, established-cookie reachability, revoked
session refusals at the Application layer, per-class rate rejection with safe bodies, search
budget independence, anonymous IP partitioning, export concurrency chaining, rate-class
resolution, global security headers, strict CSP hashing, asset cacheability, oversized-command
refusal, contract generation from real endpoints, protected-operation security declarations,
the error schema, decimal-as-string schemas, and serialization determinism.

Rate limiting and the new middleware do not change business authorization semantics; the
existing PostgreSQL-backed authorization and E2E suites remain the regression authority.
