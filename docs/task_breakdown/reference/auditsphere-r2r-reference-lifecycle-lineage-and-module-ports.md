# End-to-end lifecycle, invalidation and module ports







[Master index](../auditsphere-r2r-index-task-breakdown.md) · [Unchanged source blueprint](../source/auditsphere-r2r-source-blueprint-modules-20-26-historical.md)







**Status:** HISTORICAL_REFERENCE



**Authority:** Preserved contract excerpts and design research. Does not override `AGENTS.md` or `docs/architecture/auditsphere-architecture-current-architecture.md`. Current implementation uses static capability services and modular monolith architecture (see [`auditsphere-r2r-reference-baseline-architecture-and-adrs.md` §2.3.1](auditsphere-r2r-reference-baseline-architecture-and-adrs.md#section-2-3-1)).







**Reference, not an execution task.** The excerpts below retain the source contract. Baseline facts and research are historical to the supplied document; this breakdown does not re-audit the repository or re-verify external standards.







<!-- SOURCE-LINES: 286-347 -->



## 5. End-to-end lifecycle and dependency contract







<a id="section-5-1"></a>



### 5.1 Normal entity cycle







```text



Approved client/engagement eligibility + current user grants



  → M20 approved reporting context / chart / period / book / policy



  → M21 receive and validate TB/GL → seal source → accept source



  → M21 independently reviewed reporting mapping + completeness proof



  → M22 proposed journals → technical review → management disposition



       → source-reflection classification → sealed adjustment plan



  ↔ M23 manual reconciliations → evidence → independent review



  → M24 statements + notes + cash/equity schedules + comparatives



       → validation → reviewed exact statement-set revision



  → M25 package definition → render artifacts → verify bytes → seal



       → applicable accounting/management/partner decisions



  → handoff to M37 controlled release, then M38 archive



  → M20 authorized period close; explicit amendment/next-period action



```







Reconciliation may discover an AJE: M23 **requests a user-initiated M22 command**, then binds the reviewed resulting journal/adjusted basis and recalculates. It does not mutate ledger balances. A source or adjustment change can require another reconciliation pass. This is a controlled dependency loop in the human workflow, not a cyclic object graph or an automated posting loop.







<a id="section-5-2"></a>



### 5.2 Group cycle







```text



Reviewed eligible entity packages from M25



  → M26 approved effective perimeter and component pins



  → policy alignment + approved rate set/translation



  → manual intercompany review + balanced approved eliminations



  → deterministic consolidated result + independent group review



  → M25 shared artifact renderer for GroupResultRef (not entity recalculation)



  → group package approval → M37 release → M38 archive



```







M25 supplies reusable rendering/sealing infrastructure; it does not invoke M26 recursively while computing an entity. The entity and group artifact inputs are a discriminated contract. Nested consolidation is only enabled by an approved acyclic method profile, not by allowing a group to include itself.







<a id="section-5-3"></a>



### 5.3 Exact input manifest







For an entity calculation, capture context/profile/chart/dimension/taxonomy/policy revisions; selected raw TB/GL IDs and normalized hashes; opening source and completeness proof; effective adjustment-set membership plus journal decisions/reflection plan; required reconciliation-set membership; current/prior statement context; layout/note/cash/equity schedule versions; accounting input and policy generations; calculator version.







For a package, additionally capture selected ordered sections, reviewed statement set, template/renderer versions, language/rounding policy and expected output formats. For a group add perimeter/ownership/method, component package **and accounting-input** hashes, rate/policy versions, alignment/elimination-set membership, prior-group result and group-review basis.







A list of existing journal IDs is insufficient: a newly accepted journal must change the adjustment-set revision. The same applies to a newly required disclosure/reconciliation. Record both **item revisions and membership revisions**.







<a id="section-5-4"></a>



### 5.4 Invalidation rules







| Upstream change | Must lose current applicability | Must remain unchanged |



|---|---|---|



| Accepted TB pointer or source revision | Mapping applicability, reflection decisions for that basis, completeness, affected reconciliations, statements, unissued package/reviews and consumer group runs | Old sealed TB/GL, old decisions, already issued bytes/manifests |



| GL replacement/opening-source change | Completeness and GL-dependent reconciliations/evidence; dependent statements/packages if required inputs changed | Unrelated clients and snapshots not using that source |



| Chart/dimension/policy revision | Any context/mapping/calculation that depended on changed definitions | Published earlier chart/policy and historic reports |



| AJE acceptance/rejection/reflection change | Effective adjustment plan, affected reconciliations, statements/packages and downstream group consumers | Raw TB, prior journal revisions/decisions |



| Mapping allocation revision | Dependent statement/equity/note amounts, packages and group component compatibility | Source rows and unrelated reconciliations that do not depend on mapping |



| Note/cash-flow/equity input edit | Relevant statement validation, package composition and exact reviews | Raw balances and prior published output |



| Render template/version changes | Replacement rendered artifact set and its approvals | Existing approved artifact bytes; never regenerate behind an old hash |



| Group perimeter/component/rate/elimination change | Current group run, group reviews, group artifacts | Component books/packages and prior group output |



| Scope revocation | Current reads/actions/downloads and cached projections for that actor | Accounting history; never delete an audit record because access ended |







Apply the immediate generation fence and an append-only invalidation record in the originating transaction. A worker can update derived projections afterward, but `Approve`, `Seal`, `Close` and `PrepareRelease` **recompute the manifest before committing**, so a delayed event cannot permit stale output. Keep conservative existing client-wide invalidation until narrower propagation has direct safety tests.







Applicability is stored in a separate current-state projection/sidecar or computed from manifests; it must not require updating an immutable approved snapshot or bypassing its database trigger. Historical approval stays an immutable decision. “Stale” means it no longer authorizes the current working result. An issued package stays a historical issue, with amendment lineage where needed; never rewrite it as though it were never approved.











<!-- SOURCE-LINES: 1249-1269 -->



<a id="section-6-2"></a>



### 6.2 Cross-module API/port register







These are **proposed application interfaces or query/command contracts**, implemented by existing owning modules where possible. They must not be duplicate databases or direct foreign-aggregate repositories.







| Consumer → owner | Contract and minimum returned content | Boundary rule |



|---|---|---|



| R2R → Identity/Access (19) | Current actor/session epoch, explicit capabilities and firm/client/engagement/group scopes | Server authority only; no role/caller-supplied approver shortcuts. |



| M20/R2R → Engagements/Acceptance (4/27) | Client/entity, service profile, period/team scope, acceptance/hold/lifecycle and revision | Commercial acceptance alone does not permit professional work. |



| M21/M23/M24 → Documents/PBC (9/10/33) | Authorized exact document snapshot, version/hash/classification, availability and evidence status | A current working URL is not a reviewed immutable source. |



| M21–25 → M20 | Approved immutable reporting context/chart/dimension/taxonomy/policy and period state | Resolve IDs, never infer from selected screen labels. |



| M22–24 → M21 | Accepted source snapshots, approved mapping, completeness and source-set revision | Read-only; preserve raw rows. |



| M23/24 → M22 | Effective adjustment plan, contributions, eligibility/reflection and membership hash | Never independently count all posted journals in UI. |



| M24/25 → M23 | Required reconciliation readiness, exact proof/review refs and blockers | Reconciled is distinct from audit conclusion. |



| M25 → M24 | Canonical reviewed statement-set DTO with all source/supplementary/lineage refs | Renderer cannot recalculate or fetch newer balances. |



| M26 → M25 | Approved component accounting/package manifest and scoped canonical values | No component mutation or ungranted raw-file read. |



| M25 → M26 | Approved GroupResultRef and canonical group statement DTO | Discriminated group scope; no fake client ID. |



| M22/24/25 → Audit/Review (28/34/35/36) | Materiality/context, current human conclusion and permitted decision refs | Numeric threshold cannot auto-create professional opinion. |



| M25 → Completion/Release (37) | Exact content/artifact manifest plus applicable stage decisions/currentness | Freeze/issue remain separate rechecked commands. |



| Release → Records (38) | Immutable release/artifact IDs and predecessor/amendment lineage | Archive exact bytes; no Purview or deletion guarantee. |



| Reporting (16) → R2R | Scoped immutable result/operation summaries and exact-revision drill-down contracts | Currency/date totals reconcile; no unscoped cross-client cache. |
