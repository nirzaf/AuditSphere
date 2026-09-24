# Preserved original acceptance criteria and production interpretations

[Master index](../00_INDEX.md) · [Unchanged source blueprint](../source/ORIGINAL_R2R_Blueprint_Modules_20-26.md)

**Reference, not an execution task.** The excerpts below retain the source contract. Baseline facts and research are historical to the supplied document; this breakdown does not re-audit the repository or re-verify external standards.

<!-- SOURCE-LINES: 1519-1664 -->
## Appendix A — Preserved original acceptance criteria, with production interpretation

These are the **52 original criteria** for VP-034–VP-046 from the supplied file. They are reproduced for traceability, not marked passed. Prototype-only implementation details are explicitly translated below; this blueprint's corresponding production contracts and tests govern the implementation.

### VP-034 — Add accounting profiles, periods, books, charts and dimensions

**Production interpretation:** Retain the business criteria. Real PostgreSQL/identity/Blazor context replaces local demo state; configuration changes use reviewed immutable revisions.

| Criterion | Preserved original wording | Production acceptance |
|---|---|---|
| VP-034-AC01 | Given a client/engagement, when setting up a period/book, then subsequent imports inherit a visible explicit context and cannot attach to a sibling client by accident. | Planned; exact production test/run evidence required |
| VP-034-AC02 | Duplicate account codes, invalid date ranges, hierarchy cycles and posting accounts used as parents are rejected. | Planned; exact production test/run evidence required |
| VP-034-AC03 | Used/approved charts and period settings are revised rather than destructively overwritten; affected packages show staleness. | Planned; exact production test/run evidence required |
| VP-034-AC04 | New screens create neither client operational transactions nor tax/payroll configurations; empty setup provides a clear manual starting action. | Planned; exact production test/run evidence required |

### VP-035 — Complete bounded CSV and genuine XLSX trial-balance intake

**Production interpretation:** Retain real-format, precision, scope and replacement invariants. The original in-session-only source-byte rule is prototype-specific: production uses approved server-side bounded receipt/staging/storage with authorization and retention policy.

| Criterion | Preserved original wording | Production acceptance |
|---|---|---|
| VP-035-AC01 | Given valid balanced CSV/XLSX data, when committed, then source rows, normalized totals, file metadata/hash and reporting context are retained as one revision. | Planned; exact production test/run evidence required |
| VP-035-AC02 | Unbalanced totals, duplicate ambiguous accounts, missing headers, unknown dimensions, formula-dependent numeric cells and exceeded limits produce a non-committed error preview. | Planned; exact production test/run evidence required |
| VP-035-AC03 | A rejected import leaves the previous accepted source untouched; a successful replacement preserves it and stales dependent calculations/approvals. | Planned; exact production test/run evidence required |
| VP-035-AC04 | XLSX means an actual workbook format, not CSV renamed to .xlsx; source bytes remain in-session only and exported/imported formats are verified. | Planned; exact production test/run evidence required |

### VP-036 — Add GL intake, transaction browsing and TB completeness

**Production interpretation:** Retain file-based GL and non-posting boundary. Use real durable import, completeness records and PostgreSQL-backed filters/exports, not synthetic success.

| Criterion | Preserved original wording | Production acceptance |
|---|---|---|
| VP-036-AC01 | Given coherent opening balances, GL movements and closing TB, when completeness is calculated, then per-account residuals and source references reconcile. | Planned; exact production test/run evidence required |
| VP-036-AC02 | Missing opening data, partial journal batches, duplicate line keys, unmatched accounts, unbalanced journals and wrong periods/currencies are exposed rather than marked complete. | Planned; exact production test/run evidence required |
| VP-036-AC03 | Importing/replacing GL creates a new source revision and invalidates affected reconciliations/packages without changing the original source rows. | Planned; exact production test/run evidence required |
| VP-036-AC04 | Filters, source counts, drill-down and CSV export agree; the module never posts to client or firm books and handles the documented fixture size without freezing navigation. | Planned; exact production test/run evidence required |

### VP-037 — Extend account mappings and reporting validation

**Production interpretation:** Retain all mapping invariants. Module 21 owns mappings in this plan, consuming Module 20 chart/taxonomy and feeding Module 24.

| Criterion | Preserved original wording | Production acceptance |
|---|---|---|
| VP-037-AC01 | Given a source with unmapped material balances, when preparing a package, then validation flags the rows and blocks a misleading complete result. | Planned; exact production test/run evidence required |
| VP-037-AC02 | A mapping revision requires independent review; editing an approved mapping preserves prior version and stales its dependent output. | Planned; exact production test/run evidence required |
| VP-037-AC03 | Where splits are used, allocations reconcile exactly to each source balance and cannot double count; invalid/mismatched chart or note targets are rejected. | Planned; exact production test/run evidence required |
| VP-037-AC04 | Every generated line exposes its mapping/source references; no unrecognized account is silently assigned a zero balance or miscellaneous category. | Planned; exact production test/run evidence required |

### VP-038 — Generalize adjustment journals and source-reflection decisions

**Production interpretation:** Use real current-person decisions, persisted exact source-reflection records and immutable adjusted snapshots. Reporting inclusion never asserts external ledger posting.

| Criterion | Preserved original wording | Production acceptance |
|---|---|---|
| VP-038-AC01 | Given a balanced journal, when independently reviewed and management-accepted, then its effect is included once in the selected reporting layer. | Planned; exact production test/run evidence required |
| VP-038-AC02 | Given a replacement TB already containing that journal, when marked reflected with evidence, then additional effect is zero and no double counting occurs. | Planned; exact production test/run evidence required |
| VP-038-AC03 | Unknown/partial reflection blocks final reporting inclusion until resolved; changed source or journal revision stales the relevant decision. | Planned; exact production test/run evidence required |
| VP-038-AC04 | Unbalanced/mixed-context lines, same-person approval and duplicate inclusion are rejected; amendments preserve prior versions and do not alter source or firm ledgers. | Planned; exact production test/run evidence required |

### VP-039 — Implement editable manual reconciliation schedules

**Production interpretation:** Retain manual proof and independent review; use real scoped evidence/sources and reject ambiguous sign/currency/as-of treatment.

| Criterion | Preserved original wording | Production acceptance |
|---|---|---|
| VP-039-AC01 | Given valid schedule items, when recalculated, then opening/source/supporting totals and unexplained residual are reproducible from displayed inputs. | Planned; exact production test/run evidence required |
| VP-039-AC02 | An unexplained nonzero residual or missing required evidence blocks approval; proposed corrections cannot masquerade as already cleared timing items. | Planned; exact production test/run evidence required |
| VP-039-AC03 | An accepted source/evidence replacement makes current reconciliation review stale; the previous approved snapshot remains viewable. | Planned; exact production test/run evidence required |
| VP-039-AC04 | Item currency/date/scope validation and independent reviewer checks work; no bank feed, automated matching, payment initiation or tax integration is introduced. | Planned; exact production test/run evidence required |

### VP-040 — Build configurable financial statements and comparatives

**Production interpretation:** Replace bounded demo-only output with explicitly approved production policy profiles and real versioned statement sets. Do not claim universal accounting-method coverage.

| Criterion | Preserved original wording | Production acceptance |
|---|---|---|
| VP-040-AC01 | Given valid mapped current/prior periods, when statements are built, then each column and subtotal reconciles to its selected source; a missing prior period shows unavailable, not zero. | Planned; exact production test/run evidence required |
| VP-040-AC02 | Assets, liabilities/equity and period-result movements reconcile in the supported fixture; invalid totals display blocking validation. | Planned; exact production test/run evidence required |
| VP-040-AC03 | Changing source, mapping, layout or comparative selection creates a new output revision and stales the previous current review. | Planned; exact production test/run evidence required |
| VP-040-AC04 | The preview provides complete visible structure, editing and drill-down for the supported demonstration; unsupported calculations never render invented balanced figures. | Planned; exact production test/run evidence required |

### VP-041 — Complete notes, cash-flow support and disclosure review

**Production interpretation:** Provide real source-backed cash/equity schedules and notes. Human professional decisions replace any simulation marker; no fabricated cash-flow balances.

| Criterion | Preserved original wording | Production acceptance |
|---|---|---|
| VP-041-AC01 | Given closing TB data alone, when cash flows lack required movement support, then the screen reports incomplete support instead of inventing movements. | Planned; exact production test/run evidence required |
| VP-041-AC02 | Not-applicable notes need a reason and reviewer decision; a blank note is not an approved exemption. | Planned; exact production test/run evidence required |
| VP-041-AC03 | Approved note/support edits preserve prior revision and invalidate current package review; totals tie to current statement context. | Planned; exact production test/run evidence required |
| VP-041-AC04 | Client previews expose only deliberately shared note content; internal reviewer comments remain internal and no professional conclusion is autogenerated. | Planned; exact production test/run evidence required |

### VP-042 — Complete financial-package assembly and genuine exports

**Production interpretation:** Render genuine server-side artifacts with exact persistent identities. Browser-local generation wording is prototype-specific; content, authorization and history requirements remain.

| Criterion | Preserved original wording | Production acceptance |
|---|---|---|
| VP-042-AC01 | Given a valid supported package, when exported, then XLSX/DOCX/PDF files open as their actual formats and contain the displayed totals, entity, period and demo watermark. | Planned; exact production test/run evidence required |
| VP-042-AC02 | Export generation failure or unsupported content blocks that output and reports the reason; no renamed CSV, empty PDF or fake success is accepted. | Planned; exact production test/run evidence required |
| VP-042-AC03 | When package content changes, prior artifacts and decisions remain historical and a new artifact revision must be reviewed. | Planned; exact production test/run evidence required |
| VP-042-AC04 | External sharing remains explicit and scope-bound; internal workpapers/comments are excluded from management/client outputs by default. | Planned; exact production test/run evidence required |

### VP-043 — Create consolidation groups and effective perimeters

**Production interpretation:** Keep the bounded whole-owned baseline and explicit unsupported methods. Preserve existing separately approved advanced method implementations without treating them as globally activated.

| Criterion | Preserved original wording | Production acceptance |
|---|---|---|
| VP-043-AC01 | Given a valid group/perimeter, when saved, then the group has its own scope, revision and component links and no source client balances are changed. | Planned; exact production test/run evidence required |
| VP-043-AC02 | Duplicate components, cycles, invalid ownership percentages and incompatible period/entity assignments are rejected. | Planned; exact production test/run evidence required |
| VP-043-AC03 | Adding a component does not expand the operator’s access to its unrelated engagements; narrow group access exposes only approved component projections. | Planned; exact production test/run evidence required |
| VP-043-AC04 | The selected calculation profile and limitations are visible; unsupported ownership/accounting methods cannot silently fall back to full consolidation. | Planned; exact production test/run evidence required |

### VP-044 — Select component packages and demonstrate currency translation

**Production interpretation:** Use approved manually maintained rates and exact accepted component identities. Synthetic fixture language is replaced by approved production policy plus separate QA fixtures; no online rate feed.

| Criterion | Preserved original wording | Production acceptance |
|---|---|---|
| VP-044-AC01 | Given eligible component packages, when selected, then exact revisions are pinned and a subsequent replacement produces a stale-component warning rather than silent refresh. | Planned; exact production test/run evidence required |
| VP-044-AC02 | Missing rates, incompatible basis/period or unreviewed component packages block group output; missing amounts never default to zero. | Planned; exact production test/run evidence required |
| VP-044-AC03 | The fixture’s translated values and rounding reconcile to published test expectations; every rate and translation difference is traceable. | Planned; exact production test/run evidence required |
| VP-044-AC04 | An unapproved/unsupported translation rule shows a limitation and no fabricated consolidation result; component client packages remain unchanged. | Planned; exact production test/run evidence required |

### VP-045 — Implement manual eliminations and group adjustment review

**Production interpretation:** Retain every balancing, independence, nonmutation and unmatched-difference criterion. No automated matching or component-ledger posting is introduced.

| Criterion | Preserved original wording | Production acceptance |
|---|---|---|
| VP-045-AC01 | Given a balanced supported elimination, when independently approved, then it affects group output once and neither component book/package is modified. | Planned; exact production test/run evidence required |
| VP-045-AC02 | Unbalanced lines, unsupported counterparties, mixed contexts and duplicate source inclusion are rejected. | Planned; exact production test/run evidence required |
| VP-045-AC03 | Unmatched intercompany amounts remain visible for human resolution; approval does not hide the difference by netting an unexplained plug. | Planned; exact production test/run evidence required |
| VP-045-AC04 | A component/rate/perimeter change stales dependent elimination approval and preserves the previous decision and journal revision. | Planned; exact production test/run evidence required |

### VP-046 — Produce, review and export consolidated output

**Production interpretation:** Use actual group-run/package DTOs, database lineage, independent review and exports. No group operation writes into component or firm ledgers.

| Criterion | Preserved original wording | Production acceptance |
|---|---|---|
| VP-046-AC01 | Given compatible reviewed components and approved adjustments, when the supported fixture is consolidated, then consolidated = translated components + approved group adjustments/eliminations. | Planned; exact production test/run evidence required |
| VP-046-AC02 | Statement equations and reconciliation columns agree with fixed expected fixture values; unresolved required inputs prevent a ready-for-review state. | Planned; exact production test/run evidence required |
| VP-046-AC03 | Group output review binds to the exact perimeter/component/rate/elimination revisions; edits require fresh review. | Planned; exact production test/run evidence required |
| VP-046-AC04 | Exported group demo artifacts preserve these references and exclude unrelated client information; no group action posts into component or firm ledgers. | Planned; exact production test/run evidence required |
