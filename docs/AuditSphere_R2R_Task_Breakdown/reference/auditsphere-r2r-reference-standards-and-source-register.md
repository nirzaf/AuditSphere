# Source research and provenance

[Master index](../auditsphere-r2r-index-task-breakdown.md) · [Unchanged source blueprint](../source/auditsphere-r2r-source-blueprint-modules-20-26-historical.md)

**Reference, not an execution task.** The excerpts below retain the source contract. Baseline facts and research are historical to the supplied document; this breakdown does not re-audit the repository or re-verify external standards.

<!-- SOURCE-LINES: 151-175 -->
## 3. Standards and comparable-product research

<a id="section-3-1"></a>
### 3.1 Standards-informed design, with edition control

| Primary source | Relevant principle | Proposed implementation consequence |
|---|---|---|
| IFRS 18 official overview [S1](auditsphere-r2r-reference-standards-and-source-register.md#source-s1) | Effective for annual periods beginning on/after 1 January 2027, early adoption permitted; replaces IAS 1 and changes presentation. | `ReportingPolicyVersion` pins framework/edition/effective period/early-adoption decision. Old reports never silently use a new layout or subtotal policy. |
| IAS 7 official overview [S2](auditsphere-r2r-reference-standards-and-source-register.md#source-s2) | Cash-flow classification, cash reconciliation and separately disclosed noncash activity; IFRS 18 consequential changes matter. | Explicit cash/noncash movement inputs and edition-specific indirect starting point/classification; no cash-flow plug from two TB totals. |
| IAS 8 official overview [S3](auditsphere-r2r-reference-standards-and-source-register.md#source-s3) | Distinguish prior-period errors, accounting-policy changes and estimate changes. | Typed change reason, affected periods and approved treatment; preserve as-issued and restated comparatives separately. |
| IAS 21 [S4](auditsphere-r2r-reference-standards-and-source-register.md#source-s4) | Distinguish functional/presentation currency, monetary remeasurement and foreign-operation translation. | Separate rate-rule profiles, historical equity/movement lineage and explained translation reserve. Missing rates cannot become 1. |
| IFRS 10 [S5](auditsphere-r2r-reference-standards-and-source-register.md#source-s5) | Consolidation follows control; combine like items and eliminate intragroup effects, including investment/equity. | Evidence-based perimeter and compatible component policies; simple balance addition is not complete consolidation. |
| IAS 1 and IAS 10 overviews [S6](auditsphere-r2r-reference-standards-and-source-register.md#source-s6)[S7](auditsphere-r2r-reference-standards-and-source-register.md#source-s7) | Complete statement set/comparatives and classification of subsequent events. | Include OCI where applicable, opening comparative position where required, authorization date and human subsequent-event disposition. |

The detailed IAS 21/IFRS 10 HTML references are **2024 issued editions**, used for the cited stable principles, not represented as an exhaustive 2026 consolidated standards database. Before enabling a production policy pack, the methodology owner checks all effective amendments and local requirements using its licensed current material. Do not embed copyrighted standard text or a vendor's template library without appropriate permission.

<a id="section-3-2"></a>
### 3.2 Lessons from comparable systems—not copied product scope

| Comparable system / official documentation | Adopt | Deliberately do not copy |
|---|---|---|
| Dynamics 365 Finance period-end close [P1](auditsphere-r2r-reference-standards-and-source-register.md#source-p1) | A sequenced, accountable close process, reporting checks and controlled period restrictions. | Its operational ERP, automatic allocations, batch close or recurring task engine. |
| Oracle Account Reconciliation [P2](auditsphere-r2r-reference-standards-and-source-register.md#source-p2) | Preparer submission, independent review, required evidence and reasoned return/reopen. | Optional auto-close, automatic notifications or weaker no-review paths. |
| Caseware trial-balance mapping [P3](auditsphere-r2r-reference-standards-and-source-register.md#source-p3) | Separate client account codes from the firm's standardized reporting presentation; trace outputs to mappings. | Tax codes/filing workflows, proprietary template contents, automatic mapping acceptance. |

These products inform workflow usability. They are not authorities for accounting recognition policy, and their feature lists do not expand this scope.


<!-- SOURCE-LINES: 1424-1474 -->
## 9. Source register and provenance

<a id="section-9-1"></a>
### 9.1 Supplied requirements

<a id="source-f1"></a>
**[F1]** `Progress_Tracker.md`, supplied by the user. It contains the original 39-module/64-story scope, including VP-034–VP-046 and the prior prototype progress narrative. This blueprint uses the requirements, **not its prototype completion statuses**, as the functional basis. The latest user instruction explicitly requests the production .NET/CQRS/Blazor implementation design.

<a id="section-9-2"></a>
### 9.2 Inspected repository sources

All repository URLs below pin `ba1a3ec23335b40667b679e4f0cd5f66b9e723b9`. The architecture review read selected source sections, not every method in the solution. No application execution or exhaustive repository audit is asserted.

<a id="source-r1"></a>
- **[R1]** [Pinned production commit](https://github.com/nirzaf/AuditSphere/commit/ba1a3ec23335b40667b679e4f0cd5f66b9e723b9).
<a id="source-r2"></a>
- **[R2]** [AGENTS.md](https://github.com/nirzaf/AuditSphere/blob/ba1a3ec23335b40667b679e4f0cd5f66b9e723b9/AGENTS.md): three financial boundaries, architecture, immutable records, scope and durable operations; historical Purview/signing references require the scope ADR.
<a id="source-r3"></a>
- **[R3]** [Directory.Packages.props](https://github.com/nirzaf/AuditSphere/blob/ba1a3ec23335b40667b679e4f0cd5f66b9e723b9/Directory.Packages.props): actual central dependencies; MediatR/bUnit additions are proposed.
<a id="source-r4"></a>
- **[R4]** [Domain Accounting.cs](https://github.com/nirzaf/AuditSphere/blob/ba1a3ec23335b40667b679e4f0cd5f66b9e723b9/src/AuditSphereOps.Domain/Accounting/Accounting.cs): sources, mappings, journals, plans and financial-package identities.
<a id="source-r5"></a>
- **[R5]** [AuditSphereDbContext.cs](https://github.com/nirzaf/AuditSphere/blob/ba1a3ec23335b40667b679e4f0cd5f66b9e723b9/src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.cs): current authoritative context, application interfaces, DbSets and persistence conventions.
<a id="source-r6"></a>
- **[R6]** [Domain ClientAccounting.cs](https://github.com/nirzaf/AuditSphere/blob/ba1a3ec23335b40667b679e4f0cd5f66b9e723b9/src/AuditSphereOps.Domain/Accounting/ClientAccounting.cs): profiles, periods/books, charts, groups, capability profiles and restatements.
<a id="source-r7"></a>
- **[R7]** [ClientAccountingService.cs](https://github.com/nirzaf/AuditSphere/blob/ba1a3ec23335b40667b679e4f0cd5f66b9e723b9/src/AuditSphereOps.Application/Accounting/ClientAccountingService.cs): existing request shapes, GL limits and transaction-owning period operations.
<a id="source-r8"></a>
- **[R8]** [Domain Shared Kernel.cs](https://github.com/nirzaf/AuditSphere/blob/ba1a3ec23335b40667b679e4f0cd5f66b9e723b9/src/AuditSphereOps.Domain/Shared/Kernel.cs): existing CommandResult/error contracts, six-decimal midpoint-to-even MoneyPolicy and SHA-256 helpers.
<a id="source-r9"></a>
- **[R9]** [ConsolidationService.cs](https://github.com/nirzaf/AuditSphere/blob/ba1a3ec23335b40667b679e4f0cd5f66b9e723b9/src/AuditSphereOps.Application/Accounting/ConsolidationService.cs): component, rate/scope/match/journal/report contracts and current group-creation grant behavior.
<a id="source-r10"></a>
- **[R10]** [FinancialStatementService.cs](https://github.com/nirzaf/AuditSphere/blob/ba1a3ec23335b40667b679e4f0cd5f66b9e723b9/src/AuditSphereOps.Application/Accounting/FinancialStatementService.cs): confirmed existing owner/service seam; inspect its complete relevant methods before implementation.

<a id="section-9-3"></a>
### 9.3 Primary standards research

Consulted 24 September 2026. Official overviews are design research, not a substitute for licensed complete standards, local law or professional sign-off.

<a id="source-s1"></a>
- **[S1]** [IFRS Foundation — IFRS 18](https://www.ifrs.org/issued-standards/list-of-standards/ifrs-18-presentation-and-disclosure-in-financial-statements/).
<a id="source-s2"></a>
- **[S2]** [IFRS Foundation — IAS 7](https://www.ifrs.org/issued-standards/list-of-standards/ias-7-statement-of-cash-flows/).
<a id="source-s3"></a>
- **[S3]** [IFRS Foundation — IAS 8](https://www.ifrs.org/issued-standards/list-of-standards/ias-8-basis-of-preparation-of-financial-statements/). Use the edition effective for the selected period.
<a id="source-s4"></a>
- **[S4]** [IAS 21 official overview](https://www.ifrs.org/issued-standards/list-of-standards/ias-21-the-effects-of-changes-in-foreign-exchange-rates/) and [2024 issued HTML, particularly paragraphs 21–23, 35–40](https://www.ifrs.org/content/dam/ifrs/publications/html-standards/english/2024/issued/ias21.html). Historical edition explicitly identified; current applicability reviewed before policy activation.
<a id="source-s5"></a>
- **[S5]** [IFRS 10 official overview](https://www.ifrs.org/issued-standards/list-of-standards/ifrs-10-consolidated-financial-statements/) and [2024 issued HTML, particularly control requirements and B86–B87](https://www.ifrs.org/content/dam/ifrs/publications/html-standards/english/2024/issued/ifrs10.html).
<a id="source-s6"></a>
- **[S6]** [IFRS Foundation — IAS 1](https://www.ifrs.org/issued-standards/list-of-standards/ias-1-presentation-of-financial-statements/).
<a id="source-s7"></a>
- **[S7]** [IFRS Foundation — IAS 10](https://www.ifrs.org/issued-standards/list-of-standards/ias-10-events-after-the-reporting-period/).

<a id="section-9-4"></a>
### 9.4 Comparable-system research

<a id="source-p1"></a>
- **[P1]** [Microsoft Dynamics 365 Finance — Close the general ledger at period end](https://learn.microsoft.com/en-us/dynamics365/finance/general-ledger/close-general-ledger-at-period-end).
<a id="source-p2"></a>
- **[P2]** [Oracle Account Reconciliation — Reviewing Reconciliations](https://docs.oracle.com/en/cloud/saas/account-reconcile-cloud/raarc/reconcile_user_review_114xd902a65f.html).
<a id="source-p3"></a>
- **[P3]** [Caseware — Mapping the Trial Balance](https://www.caseware.com/docs/en/desktop/jazzit/jazzit-fundamentals/implementing-the-jazzit-templates/mapping-the-trial-balance).

<a id="section-9-5"></a>
### 9.5 Primary engineering documentation

<a id="source-t1"></a>
- **[T1]** [MediatR official repository and registration/licensing documentation](https://github.com/LuckyPennySoftware/MediatR).
<a id="source-t2"></a>
- **[T2]** [FluentValidation — asynchronous validation](https://docs.fluentvalidation.net/en/latest/async.html).
<a id="source-t3"></a>
- **[T3]** [EF Core — handling concurrency conflicts](https://learn.microsoft.com/en-us/ef/core/saving/concurrency).
<a id="source-t4"></a>
- **[T4]** [Npgsql EF Core — concurrency tokens](https://www.npgsql.org/efcore/modeling/concurrency.html).
<a id="source-t5"></a>
- **[T5]** [Blazor form validation](https://learn.microsoft.com/en-us/aspnet/core/blazor/forms/validation?view=aspnetcore-10.0) and [input components](https://learn.microsoft.com/en-us/aspnet/core/blazor/forms/input-components?view=aspnetcore-10.0).
<a id="source-t6"></a>
- **[T6]** [Blazor with EF Core and per-operation context lifetimes](https://learn.microsoft.com/en-us/aspnet/core/blazor/blazor-ef-core?view=aspnetcore-10.0).
<a id="source-t7"></a>
- **[T7]** [Blazor routing/navigation and NavigationLock](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/routing?view=aspnetcore-10.0).
<a id="source-t8"></a>
- **[T8]** [bUnit — writing component tests](https://bunit.dev/docs/getting-started/writing-tests.html).
<a id="source-t9"></a>
- **[T9]** [Playwright .NET — assertions](https://playwright.dev/dotnet/docs/test-assertions).
