# GitHub Actions CI Optimization Report

## Scope

Reviewed the four workflows on `master` at `0ef1987ba55009359c00e3dce9212ece4c7b9fad`. The objective was to reduce GitHub-hosted runner waste without changing build products, deployment behavior, required checks, test scope, or secret handling. No workflow changes were justified by the requested checklist; the existing workflows already implement the applicable optimizations. This report is the only intended repository change for this review.

## Discovery

| Workflow | Triggers | Jobs and purpose | Runner and concurrency |
|---|---|---|---|
| `.github/workflows/ci.yml` | Push to `master`, pull requests, manual dispatch | Change-scope classification; Angular lint/build/specs; .NET Release compile, exact-run artifact reuse and fallback; focused guards, API publication and contract checks; MCP; documentation and Python safety checks | All jobs use `ubuntu-24.04`. Ref/event-scoped concurrency cancels superseded fast validation. No workflow-level path filters. |
| `.github/workflows/hostinger-acceptance-images.yml` | Successful same-repository `master` push CI completion; manual dispatch with source SHA | Resolve source and fast-CI evidence; build API, worker and migration images; publish immutable digest records and manifest to GHCR | All jobs use `ubuntu-24.04`. One master image group; active image publications are not cancelled. |
| `.github/workflows/release-verification.yml` | Manual dispatch; nightly schedule at `02:17 UTC` | Full Angular suite, complete Domain/API/E2E test projects, isolated PostgreSQL 19 services, four Chrome E2E shards, exact-source artifacts, publication/contracts and aggregate gate | All jobs use `ubuntu-24.04`. Requested-ref concurrency does not cancel an active verification. |
| `.github/workflows/release.yml` | Manual dispatch only | Separate protected Azure-oriented release workflow; builds source-bound images and performs protected release steps | All jobs use `ubuntu-24.04`. Environment-scoped release concurrency does not cancel an active release. Left unchanged. |

No workflow uses a Windows runner. The projects are one `AuditSphereOps.slnx`, five application projects, three test projects and two tool projects. There is no Azure Functions project. The pinned SDK is .NET `11.0.100` with roll-forward disabled; projects target `net11.0`. Angular uses Node `26.13.0`. `Directory.Build.props` enables lock-file restore, ten `packages.lock.json` files are present, and the workflows use `--locked-mode`. The only NuGet source is nuget.org; no private-feed credential configuration or floating package versions were found.

Domain and API full-test jobs use isolated PostgreSQL 19; E2E uses Playwright with Google Chrome. Test traits exist, but no `Category=Integration` trait exists. The full release workflow is unfiltered.

The API suite is partitioned into sequential, fresh-process shards by `scripts/release/api_test_shards.py`. The helper documents a 3 GiB process-group RSS ceiling observed when four fixture collections were colocated, so internal API sharding is intentionally serial to contain memory and preserve fixture collections.

Existing caching and artifact reuse:

- `setup-dotnet` caches NuGet packages using the lock files; `setup-node` caches npm packages using each package lock file.
- Full verification caches Playwright support files.
- Fast CI passes exact-run Release and Angular artifacts to downstream checks. Full verification shares one Release build across independently isolated test jobs and uses `--no-build` where those outputs are verified.
- Hostinger image publication uses the GHCR registry layer cache. The separate Azure-oriented release uses BuildKit GHA and registry caches.
- The Release build artifact already uses upload compression level `0`; release image archives use level `1`.
- No `bin/` or `obj/` cache exists. Checkout does not request full Git history. Actions are pinned by immutable commit SHA; checkout, setup-dotnet and artifact actions are on v7/v4 majors.

## Optimization decisions

| # | Optimization | Decision | Reason | Workflow files changed |
|---:|---|---|---|---|
| 1 | Concurrency cancellation | ADAPT — already implemented | Fast validation cancels superseded runs. Image publication, full verification and protected release retain active work. | None |
| 2 | Ignore documentation paths | SKIP | Workflow-level filtering risks skipping required PR checks. Existing change-scope logic narrows only verified fast paths while documentation and safety checks remain. | None |
| 3 | Use `ubuntu-latest` | SKIP | No Windows dependency was found, but `ubuntu-24.04` pins the runner family used for build provenance and reproducibility. | None |
| 4 | NuGet caching | SKIP — already implemented | `setup-dotnet` cache is enabled and keyed by lock files. | None |
| 5 | Generate NuGet locks | SKIP — already implemented | Lock generation and locked restore are enabled; lock files exist and no floating versions were found. | None |
| 6 | Restore/build/test/publish reuse | SKIP — already implemented | Same-job steps reuse restored assets and verified build outputs. Independent runners still require restore assets. | None |
| 7 | .NET environment variables | ADAPT — partly implemented | Telemetry opt-out and no-logo are set. Do not relocate `NUGET_PACKAGES` away from setup-dotnet's existing cache path; additional first-run settings have no demonstrated speed benefit. | None |
| 8 | Shallow checkout | SKIP — already default | No checkout requests full history, and no history-dependent versioning tool was found. | None |
| 9 | Upload artifacts only on main | SKIP | The exact-source Angular bundle is consumed by same-run PR publication checks and later release/image jobs. | None |
| 10 | Artifact compression level `0` | SKIP — already tuned | Large Release build artifact already uses level `0`; image archive compression remains unchanged. | None |
| 11 | Parallel test matrix | ADAPT — project/E2E parallelism already implemented; API subshards remain serial | Domain, API and E2E run as independent jobs; E2E has four coverage-aggregated shards. The API helper enforces fresh sequential shards and a 3 GiB RSS limit; fanning these out needs a new verified aggregation and resource model. | None |
| 12 | Exclude integration tests on PRs | SKIP | No Integration category exists. The full release suite is intentionally unfiltered; adding a filter could reduce coverage. | None |
| 13 | Docker GHA layer cache | SKIP — equivalent cache exists | Hostinger builds already use the persistent GHCR registry cache; the separate release workflow also uses GHA cache. | None |
| 14 | Pin/update action majors | SKIP — already pinned | Actions use immutable commit SHAs and current major versions. Updating them is unnecessary for this speed review. Node 20 deprecation notices are a maintenance follow-up, not a reason to silently update actions. | None |
| 15 | Remove `bin/` and `obj/` cache | SKIP — not present | No such cache exists. | None |

## Workflow diff summary

- `.github/workflows/ci.yml`: no diff.
- `.github/workflows/hostinger-acceptance-images.yml`: no diff.
- `.github/workflows/release-verification.yml`: no diff.
- `.github/workflows/release.yml`: no diff.

Triggers, branch/path behavior, runners, concurrency, environments, secrets, deployment targets, build commands, artifacts and test coverage remain as inspected.

## Verification

Exact-source GitHub Actions evidence available during this review:

| Check | Result | Evidence |
|---|---|---|
| Fast CI on `0ef1987ba55009359c00e3dce9212ece4c7b9fad` | PASS | Run [45595466922](https://github.com/nirzaf/AuditSphere/actions/runs/45595466922); all fast-gate jobs passed. |
| Hostinger image publication on the same SHA | PASS | Run [45600276342](https://github.com/nirzaf/AuditSphere/actions/runs/45600276342); API, worker, migrations and immutable manifest completed successfully. |
| Locked restore, Release build, full tests and publish | PASS | Full verification run [45600443152](https://github.com/nirzaf/AuditSphere/actions/runs/45600443152), attempt 1, completed successfully on the exact SHA. Locked restore, Release build, all unfiltered Domain/API tests, all Angular specs (2,141 passed across 200 files), all four E2E shards (351 discovered and exact coverage aggregate passed), and contract/publication checks passed. The API suite took 74m16s; the workflow completed in about 78m. |
| Duplicate same-SHA scheduled verification | PASS, redundant run observed | Scheduled run [45602402206](https://github.com/nirzaf/AuditSphere/actions/runs/45602402206) overlapped the manual run for the same SHA and also completed successfully. Neither run was cancelled or suppressed. |
| Local full .NET suites | NOT RUN | Repository policy keeps full regression suites in CI. The pinned .NET 11 SDK is also unavailable in the local environment. |
| Previous-vs-current publish tree comparison | NOT RUN | No publish workflow or build output was changed. Existing exact-source publication checks verify the API assembly and Angular entry point. |
| Deployment trigger, environment and secret handling | UNCHANGED | Workflow diff is empty; protected release and image workflows were not edited. |
| Workflow syntax | PASS | `actionlint .github/workflows/*.yml` completed successfully. |
| Documentation integrity | PASS | `git diff --check`; `validate-markdown-documentation.py` (193 Markdown files, 22 canonical documents, 3,014 links and 286 backticked repository paths); `validate-markdown-filenames.py` (all 193 files); and `validate-narrative-metrics.py` all passed. |

No timing improvement is claimed: the workflows already contain the applicable safe optimizations. Recent Actions history shows the .NET Release compile is the largest fast-CI job; the latest full run took about 78 minutes wall-clock, with the API job taking 74m16s. These are current-run observations, not before/after measurements.

## Risks and follow-up

- Two full verification runs for the same SHA overlapped. Skipping a scheduled run based on another run would change verification scheduling, so it is not applied under the no-coverage-change requirement. Consider deduplication only with an explicit policy that preserves the required nightly and release gates.
- The API suite's sequential process shards are the long-running component: the API job took 74m16s in run `45600443152`, while the four E2E jobs took about 24–37 minutes each. Parallelizing API shards could reduce wall-clock time, but needs exact partition/coverage aggregation, one isolated PostgreSQL service per runner, memory validation and a measured concurrency plan. No such change is made without that verification.
- GitHub emitted non-blocking Node.js 20 deprecation annotations for pinned actions in the full run. Updating action revisions is outside this optimization because it is not required to preserve current behavior or improve measured speed.
- Any future workflow change needs fresh exact-source fast/image/full verification. The active runs above qualify only their recorded SHA.

## Expected impact

The checklist changes are already present or unsafe to apply without affecting required checks, runner reproducibility, artifacts, or test scope. This review therefore makes no workflow speed claim and predicts no runtime reduction. Measure Actions wall-clock and aggregate runner time on comparable runs before and after any future optimization.
