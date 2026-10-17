# ReviewPoint source/action review

**Disposition: PARTIAL**
**Current implementation reviewed at:** `f9f06aca7b2cd74e9f48a52ec7bfb8bdfce58fa3`
**Earlier runtime evidence:** `55ba972d308a3d92fbd690629daa28b70f98ee32`
**Current implementation review:** 2026-10-16; earlier runtime observations below remain historical.

## Legacy behavior and Angular ownership

The legacy page at [src/AuditSphereOps.Web/Components/Pages/ReviewPoint.razor](https://github.com/nirzaf/AuditSphere/blob/c3905e078b44c84c5ec0ad4bbda6d11894acf42d/src/AuditSphereOps.Web/Components/Pages/ReviewPoint.razor)
owns `/app/reviews/{Id:guid}`. It shows the target kind and ID, target revision,
raiser, timestamp, significance and gate status, comment, and links back to the
engagement and completion checklist. Significant uncleared points are marked as
blocking. An authorized internal user can clear or reopen a point.

The Angular owner is `ReviewPointRecord` in
`src/AuditSphereOps.Ui/src/app/features/audit/records.ts`. The detail query retains
exact firm/client/engagement authorization and now returns target revisions as
exact strings, including values above JavaScript's safe integer range.

The native command workspace under `/api/ui/reviews/{id}/commands` provides state,
preview, execution and actor-owned receipt recovery. CLEAR and REOPEN require a
reviewed before/after preview, fresh explicit assent, the reviewed disposition
revision and request hash. A separate disposition revision fences clear/reopen/clear
ABA sequences without changing the immutable target revision. The Application
transaction publishes disposition, immutable event and receipt together. Recovery
returns the original committed evidence instead of applying another command.
Current scope and session checks also guard recovery.

The existing internal-role set and administrative disposition semantics are
preserved; this action does not introduce an independent professional approval.
Frozen originals refuse changed dispositions. The retained Razor caller now passes
its displayed disposition revision and reloads after success. This deliberate
compatibility correction changes its discovery hash; it is not retirement proof.

## Verification evidence

The earlier runtime evidence used the prior Razor source hash:
`af63b1a7cf1132238a1836c2c6c68d12a97e10c06e3b7ca5ec8612f293b09415`.
The earlier Angular replacement hash was
`e5f24ffa4fd86540fad14b7cb41f82e857a223da65c3b318d1a815858918a709`.

The focused PostgreSQL-backed API-host browser journey passed **1/1**:

```bash
dotnet test tests/AuditSphereOps.E2E.Tests/AuditSphereOps.E2E.Tests.csproj \
  --no-restore --configuration Release -m:1 \
  --filter 'FullyQualifiedName~AngularClientScopeAuditDetailJourneyTests'
```

It verifies the significant blocking message, successful clear and reopen with
persisted state, sibling and random-ID denial without protected markers,
same-document route restoration, six viewport widths, and visible keyboard
focus. After the exact client grant is revoked, the Angular page removes the
review context and actions, and another clear command is refused without
changing the row. The focused PostgreSQL-backed Domain test
`ReviewPointDisposition_IsScopedAndRetrySafe` also passed **1/1**; it verifies
same-action retry, a different-firm target denial, and a stale session epoch.
The built-in Development browser rendered the generic unavailable state for a
guessed review ID without disclosing record content.

## Current implementation and pending hosted verification

The current Razor source SHA-256 is
`cd8827295413fd0c16cbe5ce7e79fcff9145521ee95e25420c3ac59f788ce1a1`.

The Release solution build completed with no warnings or errors and the Angular
application build completed, with an existing commercial stylesheet budget warning.
An Angular spec compilation mismatch was corrected after that run; no passing
current spec result is claimed here.

Current automated coverage is implemented in `ReviewPointCommandReceiptTests`,
`ReviewPointCommandReceiptApiTests`, `AngularReviewPointCommandJourneyTests`, and
the scoped audit-detail journey. It covers exact revisions, stale intent, original
receipt recovery, role/scope enforcement and browser success/lost-response paths.
Test presence does not establish a pass. The pushed commit is being checked by
[CI](https://github.com/nirzaf/AuditSphere/actions/runs/38912167039) and
[full release verification](https://github.com/nirzaf/AuditSphere/actions/runs/38912167504).
Both results must be inspected before attributing current runtime acceptance.

## Gaps keeping this row partial

Independent inspection of the current code and assertions found no additional
concrete Angular source-action gap. The new Domain, API and browser cohorts still
need a recorded passing result at the pushed implementation commit. The retained
Razor revision-forwarding and post-success reload are inspected integration
changes; Domain tests prove stale explicit-revision refusal, rather than rendering
that retained component.

Screen-reader, broader locale, full authorization-program and operational
retirement acceptance remain separate global gates. They are not inferred from
this source-row review. The source row remains `PARTIAL` pending current runtime
evidence; the Web project is retired on branch `chore/retire-blazor-host` while the canonical
parity gate remains `NOT_READY`.

## Hosted verification attribution

The [d003ce0d CI run](https://github.com/nirzaf/AuditSphere/actions/runs/38919297108)
completed Angular and documentation successfully, but its .NET job was cancelled.
It therefore does not establish a completed browser cohort or full source-action
parity. The later Workpaper snapshot's full run stopped at an Angular regression
before .NET tests; it likewise cannot close this row. The current consolidated
[5309f856 full verification](https://github.com/nirzaf/AuditSphere/actions/runs/38926773581)
is pending. This source remains PARTIAL until the relevant current Domain, API
and browser results are inspected against the reviewed actions.
