# AuditSphere Angular conventions and safe AI-assisted implementation

**Status: CURRENT.** This document codifies the conventions the native Angular
workspace actually follows, so new pages and AI-assisted slices match the
established native code instead of inventing a second style. It describes
implemented patterns; acceptance claims always belong to `docs/execution/status.json`.

## Layer and transport

- `AuditSphereOps.Ui` is presentation only. It talks to the same-origin
  `/api/ui/*` contracts served by `AuditSphereOps.Api`; it never imports
  Application services, never touches EF Core, and never receives a bearer
  token. Antiforgery rides on the HttpClient XSRF cookie flow.
- Every read is a named Application query; every write is a named Application
  command. API endpoints (`UiEndpoints.*.cs`) compose them and add session,
  CSRF and shape handling only. New business rules go into the matching
  Application capability partial file, not the endpoint or the component.
- The server remains the authorization and business authority. Angular renders
  server-computed `can*` flags and never re-decides eligibility.

## Angular code conventions

- Files live under `src/app/features/<area>/` with business-semantic names
  (`proposal.ts`, `mapping-draft.ts`). Shared behavior lives under
  `src/AuditSphereOps.Ui/src/app/core/` (`api.ts`, `decode.ts`, `drafts.ts`, `tab-drafts.ts`,
  `session.ts`, `ui.ts`).
- Components use signals and `api.resource` for reads; commands use
  `CommandState` or explicit `api.command` with the established
  unknown-outcome handling: never retry, reconcile persisted receipts first.
- Every server payload is decoded at the edge by a bounded decoder from
  `core/decode.ts` (`obj`, `arr`, `dec`, `guid`, `instant`, `nullable`, …).
  Decimals arrive as exact strings and stay strings until display (`| money`);
  unsigned counters arrive as digit strings; array sizes are bounded.
- Forms mirror server validation exactly (`maxlength`, required, formats) and
  use typed or template-driven forms per the page's existing style; the server
  re-validates everything.
- For new forms, prefer Angular Signal Forms with one typed model. Keep an
  existing reactive or template-driven form coherent when extending or
  mechanically extracting it; do not mix form APIs within one form without a
  concrete need.
- Use `Api.resource`/`toSignal` for signal-owned reads. For long-lived
  imperative subscriptions, use `takeUntilDestroyed` with the owning
  `DestroyRef` and place lifetime termination after flattening operators so
  inner work is also bounded. An explicit subscription is acceptable when it
  is unsubscribed by its actual owner, as in the existing planning workspace.
- Promise-based commands, dialogs and async callbacks capture the current
  route/session/component owner and recheck it after each `await` and before
  changing signals, drafts, acknowledgments or triggering reloads. Close only
  dialogs owned by the invalidated component. Teardown cancels UI effects; it
  does not imply that a dispatched server command was canceled or rolled back.
- UI kit is Angular Material/CDK plus the shared `SHARED` components
  (`audit-page-header`, `audit-state`, `audit-command-message`, `audit-status`).
  MudBlazor is forbidden; new npm dependencies require owner approval.
- Shared visual tokens (`--as-ink`, `--as-line`, `--as-link`, `--as-focus`, radii and shadows) live
  in `src/AuditSphereOps.Ui/src/styles.scss`. Reuse them, with a literal fallback inside
  body-mounted overlays, instead of adding new hex colours. Panels, tables, native controls and
  page headers are styled globally; keep text and control contrast at WCAG 2.2 AA, and keep any
  new motion behind the existing `prefers-reduced-motion` rule.
- Routes are registered in `app.routes.ts` with `staffGuard`/`clientGuard`,
  kept in step with the `SpaRoutes` table in
  `UiEndpoints.Infrastructure.cs`, and deep links must survive reload.

## Static lint

- `npm run lint:ci` runs the Angular ESLint target over production TypeScript
  and external templates. It is part of both existing CI workflows; it is a
  static check, not a new test suite.
- For local feedback, pass only changed production files to
  `npm run lint:files -- <file...>`. Include the owning `.ts` file for an
  inline template and both the component and external `.html` file when both
  changed. Do not invoke `lint:files` without an explicit file list.
- The flat config intentionally uses syntax-level rules. It excludes generated
  output, caches, dependencies, specs and `testing/` fixtures; production
  directories remain in scope. No type-aware whole-project lint, formatter
  preset or style rewrite is enabled.
- Errors must be fixed at their production source. Use a narrow, documented
  exception only when the actual rendered semantics prove a selected rule is
  a false positive; do not ignore a feature directory or relax the rule
  globally.

## Drafts, assent and session fencing

- Convenience form persistence uses `core/drafts.ts` (identity-scoped keys,
  allowlist validators, debounced autosave, cleared on confirmed submission).
  Revision-fenced financial editors use `core/tab-drafts.ts` (explicit recovery,
  base-revision fences, pending-request references).
- Drafts never store assent, reviewed checkboxes, secrets or files. Tab fields
  never restore assent after an uncertain outcome.
- Pending submissions are recovery checkpoints, not editable drafts. Their original
  identity survives draft expiry and unrelated pruning within the same session.
  Reconcile the retained command or receipt before another write; unreadable
  checkpoints stay blocked. Identity invalidation still clears tab storage.
- Session-bound components fence on `SessionService.invalidation()`: dialogs
  close when the generation changes, responses from a stale identity are
  dropped, and identity switches clear in-memory form state.
- Finding and review-point pages keep an already verified receipt panel mounted during the independent same-scope detail refresh. Completed scope denial hides protected receipt content; route/session changes retain their existing invalidation rules. Test the parent loading condition as well as the command-state class: a retained receipt signal alone does not prove that its confirmation is visible. Historical receipt display never supplies current authorization or approval.

## Tests that hold the line

- Each feature carries `*.spec.ts` contract tests at the strength of its
  siblings; `app-navigation.spec.ts` and `control-compatibility.spec.ts` pin
  shell and control behavior. Native controls retired hosts relied on stay
  permanent; modernize tests only at equal strength, never weaker.
- E2E journeys wait for data markers (not static headers) and match each
  page's actual denial heading.
- Exact measurements, counts and fingerprints go only into
  `docs/execution/status.json`.

## Safe AI-assisted implementation rules

AI agents (and reviewers of AI work) operating in this repository must:

1. Read `AGENTS.md`, the code map and the newest `docs/execution/status.json`
   "remaining" lists before choosing work; older remaining lists are
   superseded by newer slices.
2. Deliver the smallest coherent vertical slice: Application command/query,
   endpoint, component, decoder, spec, route — in the file the capability map
   names. No bulk refactors of unaffected pages.
3. Never weaken an existing test, contract, authorization scope or E2E
   assertion to make a change pass; parity means equal or stronger.
4. Keep append-only lineage: revisions create new versions; historical
   evidence is never overwritten; financial inputs stay exact strings.
5. Record only observed facts in the execution ledger, with source commit and
   boundary statements; never claim acceptance, production readiness, live
   Microsoft capability or migration/retirement evidence that has not been observed.
6. Respect the physical architecture: no new frameworks, no second ERP, no
   microservices, no tenant-wide Graph scopes, and no autonomous professional
   conclusions.
