# AuditSphere Audit Workflow Gap Closure — User Stories

**Status:** implementation backlog and traceability baseline  
**Source:** AuditSphereOps_NET_Codex_Implementation_Specification.md plus the controlled audit-methodology source  
**Audience:** product, audit methodology, engineering, QA, operations and independent review

## Purpose and boundary

This document converts the remaining audit-workflow gaps into issue-ready vertical slices. It is a requirements artifact, not professional advice, a signing approval, a Purview production result, or live-tenant acceptance evidence.

Contract:

- 28 implementation stories.
- 258 story-level acceptance criteria.
- 20 original audit sections and 165 stable procedure keys, P001–P165.
- Five dependency-ordered milestones.
- Focused verification for every story, one end-to-end scenario and 20 negative/boundary scenarios.
- Definition of Ready, Definition of Done and a coding-agent handoff prompt.

The existing tenancy, authorization, snapshot, evidence, review-generation, release, records and recovery primitives are reused. Planning and financial statements are residual-gap checks, not replacement modules.

### Traceability qualification

The 165-procedure/20-section count is the supplied backlog contract. The controlled source procedure titles must be reconciled into the P001–P165 register before professional approval. No procedure title, methodology conclusion, external grant, Purview behavior, signing result or production result is invented here. Until that reconciliation and the required human approvals occur, this is a planning baseline.

## Shared implementation rules

Every story must:

1. enforce firm, client and engagement scope on commands, reads, evidence and exports;
2. reuse versioned snapshots, evidence links, workpapers, review objects and safety-generation invalidation;
3. preserve prior revisions and invalidate dependent approvals after material input changes;
4. record performer, reviewer, timestamps, methodology version and audit events;
5. fail closed on missing population, unreliable evidence, unresolved exceptions, stale generation or unauthorized access;
6. keep local simulation separate from live provider behavior;
7. record facts, calculations, proposed statuses and human decisions without autonomous professional conclusions;
8. leave Entra, Graph, SharePoint, Purview, signing, recovery and production gates blocked until live prerequisites are evidenced.

## Implementation backlog

### AS-G01 — Engagement acceptance and independence residual checks

- **Objective:** controlled acceptance before audit work starts.
- **Gap:** independence, conflicts, continuance, terms, fees and resources need one guarded decision.
- **Reuse/data:** client and engagement state, questionnaires, review points; client, period, service, responses, blockers, staffing and decision evidence.
- **Scope/dependencies:** versioned checklist, blocker ownership and history; existing identity, authorization, review and audit events.
- **Tests/deliverable:** G01 service/PostgreSQL/authorization/stale-generation tests; acceptance command/UI, persistence and operator notes.
- **Acceptance criteria:**
  - [ ] AC-G01.01 — Client, period, service and responsible partner are required before acceptance.
  - [ ] AC-G01.02 — Independence responses are versioned; incomplete, conflicting or expired responses block acceptance.
  - [ ] AC-G01.03 — Conflict, continuance, terms, fee and resource blockers have owner, reason, status and due date.
  - [ ] AC-G01.04 — Decision basis, methodology version and input generation are recorded.
  - [ ] AC-G01.05 — Accept/reject are guarded commands, not ordinary field updates.
  - [ ] AC-G01.06 — Retrying the same command is idempotent and creates no duplicate decision.
  - [ ] AC-G01.07 — The reviewer is distinct from the preparer and properly scoped.
  - [ ] AC-G01.08 — Supporting declarations link to immutable evidence snapshots.
  - [ ] AC-G01.09 — Cross-client, missing-period and stale-generation requests fail closed.
  - [ ] AC-G01.10 — Every state change is queryable with actor and timestamp.

### AS-G02 — Confirmations and external response control

- **Objective:** manage bank, receivable, payable, legal and other confirmations.
- **Gap:** requests, responses, alternative procedures and exceptions need a common evidence workflow.
- **Reuse/data:** populations, samples, communications and snapshots; population, recipient, request, dispatch, response and exception data.
- **Scope/dependencies:** batch, request lifecycle, response and non-response handling; AS-G23 population/sampling and evidence links.
- **Tests/deliverable:** G02 workflow/integrity/non-response/scope tests; confirmation workpaper and exception queue.
- **Acceptance criteria:**
  - [ ] AC-G02.01 — A batch records assertion, population version, selection method, period and procedure.
  - [ ] AC-G02.02 — Each request has stable ID, recipient, authorization and immutable request snapshot.
  - [ ] AC-G02.03 — Sent status requires dispatch evidence or an explicit manual event.
  - [ ] AC-G02.04 — Responses preserve source bytes, received time, sender metadata and reliability assessment.
  - [ ] AC-G02.05 — Non-responses create follow-up and cannot become clear results silently.
  - [ ] AC-G02.06 — Exceptions require investigation, alternative procedure or unresolved disposition.
  - [ ] AC-G02.07 — A response cannot attach to another client, period, recipient or request.
  - [ ] AC-G02.08 — Resend/replay is idempotent and preserves history.
  - [ ] AC-G02.09 — Reviewer approval is generation-bound and invalidated by evidence changes.
  - [ ] AC-G02.10 — Reporting separates confirmed, alternative, non-response and unresolved items.

### AS-G03 — Receivables ageing, ECL and recoverability

- **Objective:** evaluate receivables completeness, ageing, recoverability and expected credit loss.
- **Gap:** listing, subsequent receipts, assumptions and exceptions need reproducible calculation.
- **Reuse/data:** TB/GL, calculator, populations and estimates; listing, due dates, receipts, credit notes, buckets, rates and model.
- **Scope/dependencies:** ledger tie-out, ageing/ECL, outliers and conclusion; AS-G23, AS-G17 and AS-G25.
- **Tests/deliverable:** G03 calculation/reconciliation/precision/stale-assumption tests; workpaper, calculation and evidence manifest.
- **Acceptance criteria:**
  - [ ] AC-G03.01 — Listing ties to a pinned ledger and statement generation.
  - [ ] AC-G03.02 — Ageing uses recorded due-date rules and exposes invalid dates.
  - [ ] AC-G03.03 — Receipts and credit notes link to exact evidence snapshots.
  - [ ] AC-G03.04 — ECL assumptions record source, owner, date, version and sensitivity.
  - [ ] AC-G03.05 — The calculation is deterministic and stores precision/rounding rules.
  - [ ] AC-G03.06 — Reconciliation differences create exceptions and cannot be hidden by display totals.
  - [ ] AC-G03.07 — Overdue, disputed, related-party and individually impaired items are identified.
  - [ ] AC-G03.08 — Changed listing, receipt or assumption invalidates review.
  - [ ] AC-G03.09 — Cross-engagement reuse is blocked and calculations are audited.
  - [ ] AC-G03.10 — The ageing and ECL output states the applicable assertion, limitation and human conclusion.

### AS-G04 — Revenue occurrence, completeness and cut-off

- **Objective:** test revenue assertions using contracts, delivery/service, receipts and cut-off.
- **Gap:** revenue needs reusable direction-of-testing and exception projection.
- **Reuse/data:** populations, samples, workpapers and misstatements; ledger, invoices, contracts, dispatch, credit notes and receipts.
- **Scope/dependencies:** population validation, vouch/trace, cut-off window and conclusion; AS-G23, AS-G02 and AS-G25.
- **Tests/deliverable:** G04 procedure/cut-off/duplicate/authorization tests; revenue program and cut-off worksheet.
- **Acceptance criteria:**
  - [ ] AC-G04.01 — Assertion, testing direction, population and period boundary are recorded.
  - [ ] AC-G04.02 — Population reconciles and identifies duplicates, gaps and reversals.
  - [ ] AC-G04.03 — Selected items link contract/order, delivery/service, invoice and receipt evidence where applicable.
  - [ ] AC-G04.04 — Cut-off windows are explicit and methodology-versioned.
  - [ ] AC-G04.05 — Exceptions record condition, amount, assertion, cause, follow-up and projection where applicable.
  - [ ] AC-G04.06 — Manual evidence preserves bytes, source, acquisition time and reliability.
  - [ ] AC-G04.07 — Occurrence, completeness, accuracy, cut-off and presentation results are distinct.
  - [ ] AC-G04.08 — Changed population, period or materiality invalidates affected approval.
  - [ ] AC-G04.09 — Unauthorized cross-engagement evidence access fails closed.
  - [ ] AC-G04.10 — The final revenue conclusion identifies unresolved exceptions and their reporting impact.

### AS-G05 — Inventory counts, existence and valuation

- **Objective:** plan, observe and conclude on counts, roll-forward/roll-back, condition and valuation.
- **Gap:** count-specific locations, test counts, movement controls, obsolescence and costing are missing.
- **Reuse/data:** site evidence, samples, exceptions and estimates; locations, sheets, listing, movements, test counts, costing and NRV.
- **Scope/dependencies:** count plan, attendance, independent counts, reconciliation and valuation; AS-G17 and AS-G23.
- **Tests/deliverable:** G05 count/reconciliation/movement/valuation tests; attendance pack and valuation workpaper.
- **Acceptance criteria:**
  - [ ] AC-G05.01 — Locations, date, instructions, attendance and responsible personnel are recorded.
  - [ ] AC-G05.02 — Count sheets and listing versions are pinned and not silently replaced.
  - [ ] AC-G05.03 — Test counts record identifiers, direction, quantity, condition and evidence.
  - [ ] AC-G05.04 — Pre/post-count movements link to cut-off procedures.
  - [ ] AC-G05.05 — Differences reconcile or become owned exceptions.
  - [ ] AC-G05.06 — Cost, NRV, ageing, obsolescence and damage assumptions are separately evidenced.
  - [ ] AC-G05.07 — Remote/unattended counts are not labelled attended observations.
  - [ ] AC-G05.08 — Changed count/listing generation invalidates valuation review.
  - [ ] AC-G05.09 — Location and row-level scope is enforced.
  - [ ] AC-G05.10 — Conclusion distinguishes existence, completeness, rights, valuation and presentation.

### AS-G06 — Purchases, payables, accruals and cut-off

- **Objective:** test completeness and cut-off of purchases, payables, accruals and unmatched goods/services.
- **Gap:** subsequent-invoice searches and receiving evidence need an exception-driven workflow.
- **Reuse/data:** reconciliation, sampling, cut-off and misstatements; AP, suppliers, POs, receipts, invoices, payments and accruals.
- **Scope/dependencies:** AP tie-out, selections, unrecorded-liability search and cut-off; AS-G23, AS-G04 and AS-G25.
- **Tests/deliverable:** G06 cutoff/payment/duplicate-supplier/scope tests; AP workpaper and exception register.
- **Acceptance criteria:**
  - [ ] AC-G06.01 — AP/accrual populations tie to the pinned ledger and identify unmatched items.
  - [ ] AC-G06.02 — Search window, selection basis and payment source are recorded.
  - [ ] AC-G06.03 — Items link invoice, receipt/service, approval and payment evidence where available.
  - [ ] AC-G06.04 — Cut-off distinguishes before, after and unrecorded liabilities.
  - [ ] AC-G06.05 — Accruals record basis, reversal, settlement and uncertainty.
  - [ ] AC-G06.06 — Supplier duplicates, related parties and unusual terms are flagged.
  - [ ] AC-G06.07 — Exceptions need evidence, rationale or explicit unresolved status.
  - [ ] AC-G06.08 — Changed ledger, population or window invalidates review.
  - [ ] AC-G06.09 — Selected and excluded items are reproducibly reportable.
  - [ ] AC-G06.10 — All commands and evidence are tenant/engagement scoped.

### AS-G07 — Property, plant, equipment and depreciation

- **Objective:** verify additions, disposals, rights, useful lives, depreciation and impairment indicators.
- **Gap:** PPE roll-forward and depreciation lack an asset-level controlled workpaper.
- **Reuse/data:** ledger, snapshots, estimates and calculations; register, additions, invoices, disposals, lives, residuals and physical evidence.
- **Scope/dependencies:** roll-forward, samples, recalculation and disclosure; AS-G17 and AS-G23.
- **Tests/deliverable:** G07 roll-forward/precision/disposal/authorization tests; PPE workpaper and recalculation.
- **Acceptance criteria:**
  - [ ] AC-G07.01 — Register reconciles opening, additions, disposals, transfers, depreciation and closing.
  - [ ] AC-G07.02 — Additions link approval, invoice, receipt and rights evidence.
  - [ ] AC-G07.03 — Disposals record authorization, date, proceeds, depreciation and gain/loss.
  - [ ] AC-G07.04 — Lives, residuals, methods and changes are versioned assumptions.
  - [ ] AC-G07.05 — Recalculation uses explicit start/stop and convention rules.
  - [ ] AC-G07.06 — Impairment indicators and outliers create exceptions.
  - [ ] AC-G07.07 — Changed register or assumption invalidates calculation and approval.
  - [ ] AC-G07.08 — Tested population and selection rationale are preserved.
  - [ ] AC-G07.09 — Cross-engagement asset evidence and unauthorized edits are blocked.

### AS-G08 — Intangibles, impairment and valuation evidence

- **Objective:** test intangible existence, rights, amortization, impairment indicators and valuation.
- **Gap:** intangible assets need a specialized evidence and estimate path.
- **Reuse/data:** estimates, specialists, snapshots and review; register, licenses, capitalization support, forecasts and model.
- **Scope/dependencies:** additions, rights, amortization and impairment; AS-G17 and AS-G22.
- **Tests/deliverable:** G08 impairment/sensitivity/license/access tests; intangible workpaper and manifest.
- **Acceptance criteria:**
  - [ ] AC-G08.01 — Each asset has stable identifier, owner, class, cost and period.
  - [ ] AC-G08.02 — Rights and useful-life support link to exact snapshots.
  - [ ] AC-G08.03 — Amortization is recalculated from versioned methods and dates.
  - [ ] AC-G08.04 — Indicators, cash-generating unit and model generation are recorded.
  - [ ] AC-G08.05 — Forecast, discount-rate and sensitivity inputs are separate.
  - [ ] AC-G08.06 — Specialist evidence records scope, competence and limitations.
  - [ ] AC-G08.07 — Missing rights, expired licenses or unsupported models remain open.
  - [ ] AC-G08.08 — Material input changes invalidate review and completion.
  - [ ] AC-G08.09 — Conclusion separates existence, rights, valuation, amortization and disclosure.

### AS-G09 — Payroll and employee benefits

- **Objective:** test payroll completeness, occurrence, accuracy, cut-off and liabilities.
- **Gap:** payroll needs a privacy-aware population and reconciliation workflow.
- **Reuse/data:** restricted workpapers, testing and calculations; payroll, employees, contracts, joiners/leavers, timesheets, bank and statutory reports.
- **Scope/dependencies:** ledger tie-out, employee/payment tests and liabilities; AS-G23 and AS-G25.
- **Tests/deliverable:** G09 restricted-access/joiner-leaver/totals tests; payroll workpaper and restricted folder.
- **Acceptance criteria:**
  - [ ] AC-G09.01 — Payroll is restricted to permitted roles and engagement scope.
  - [ ] AC-G09.02 — Totals reconcile to ledger, bank and statutory reports or create exceptions.
  - [ ] AC-G09.03 — Joiners, leavers, duplicates and unusual payments are identified.
  - [ ] AC-G09.04 — Samples link contract, time/approval, calculation and payment evidence.
  - [ ] AC-G09.05 — Bonus, leave, pension, tax and benefit accruals record basis and cut-off.
  - [ ] AC-G09.06 — Sensitive values are redacted from ordinary logs and exports.
  - [ ] AC-G09.07 — Changed payroll generation invalidates calculation and review.
  - [ ] AC-G09.08 — Conclusion separates occurrence, completeness, accuracy, cut-off and classification.
  - [ ] AC-G09.09 — Unauthorized cross-client searches and downloads fail closed.

### AS-G10 — Loans, borrowings, interest and covenants

- **Objective:** test debt completeness, terms, balances, interest, classification and covenants.
- **Gap:** borrowings need contract evidence, recalculation and covenant follow-up.
- **Reuse/data:** confirmations, evidence, calculations and exceptions; agreements, statements, schedules, rates, payments and waivers.
- **Scope/dependencies:** debt tie-out, interest, terms and current/non-current classification; AS-G02, AS-G17 and AS-G25.
- **Tests/deliverable:** G10 interest/covenant/response tests; borrowing workpaper and covenant queue.
- **Acceptance criteria:**
  - [ ] AC-G10.01 — Debt population reconciles to ledger and lender evidence.
  - [ ] AC-G10.02 — Facility stores agreement version, lender, currency, maturity and security.
  - [ ] AC-G10.03 — Interest records rate source, reset dates, day-count and precision.
  - [ ] AC-G10.04 — Covenant calculations preserve numerator, denominator, threshold and period.
  - [ ] AC-G10.05 — Breaches, waivers and post-period events create owned exceptions.
  - [ ] AC-G10.06 — Classification changes link to contract and reporting-date evidence.
  - [ ] AC-G10.07 — Changed agreement, rate, balance or confirmation invalidates review.
  - [ ] AC-G10.08 — Unresolved terms cannot be cleared by management assertion alone.
  - [ ] AC-G10.09 — Facility data is scoped and recalculations are auditable.

### AS-G11 — Leases and commitments

- **Objective:** test lease completeness, right-of-use assets, liabilities, modifications and commitments.
- **Gap:** contracts and schedules need a controlled population and estimate workflow.
- **Reuse/data:** contract evidence, calculations and PPE roll-forwards; lease register, contracts, amendments, payments and rates.
- **Scope/dependencies:** completeness search, schedule replay, modifications and disclosure; AS-G07, AS-G17 and AS-G23.
- **Tests/deliverable:** G11 schedule/modification/missing-contract tests; lease workpaper and disclosure support.
- **Acceptance criteria:**
  - [ ] AC-G11.01 — Population records source, search method, locations and completeness result.
  - [ ] AC-G11.02 — Material leases link signed contracts and amendments to snapshots.
  - [ ] AC-G11.03 — Liability and right-of-use schedules replay from pinned inputs.
  - [ ] AC-G11.04 — Discount rate, term, renewal and termination assumptions are versioned.
  - [ ] AC-G11.05 — Modifications, reassessments and variable payments create events.
  - [ ] AC-G11.06 — Missing contracts or schedule differences remain exceptions.
  - [ ] AC-G11.07 — Material changes invalidate review and disclosure.
  - [ ] AC-G11.08 — Conclusion separates completeness, measurement, classification and disclosure.
  - [ ] AC-G11.09 — Lease evidence cannot cross engagement or client boundaries.

### AS-G12 — Equity, capital and reserves

- **Objective:** test equity movements, share capital, distributions, reserves and presentation.
- **Gap:** equity needs legal-document and ledger reconciliation.
- **Reuse/data:** mapping, snapshots, approvals and statement calculator; opening equity, minutes, register, resolutions and dividends.
- **Scope/dependencies:** roll-forward and movement tests; AS-G23 and AS-G27.
- **Tests/deliverable:** G12 roll-forward/movement/access/disclosure tests; equity workpaper.
- **Acceptance criteria:**
  - [ ] AC-G12.01 — Opening balances reconcile to prior approved package and current ledger.
  - [ ] AC-G12.02 — Material movements link resolution, register or authoritative evidence.
  - [ ] AC-G12.03 — Issuances, transfers, buybacks and dividends preserve date, amount and authorization.
  - [ ] AC-G12.04 — Reserve restrictions and reclassifications are separate records.
  - [ ] AC-G12.05 — Unresolved differences block completion.
  - [ ] AC-G12.06 — Presentation checks identify classification and disclosure mismatches.
  - [ ] AC-G12.07 — Changed ledger or legal evidence invalidates review.
  - [ ] AC-G12.08 — Restricted equity documents require authorized roles.
  - [ ] AC-G12.09 — Workpaper exposes reproducible movement roll-forward.

### AS-G13 — Current and deferred tax

- **Objective:** test tax balances, current/deferred tax, uncertain positions and disclosures.
- **Gap:** tax needs controlled reconciliation and specialist support.
- **Reuse/data:** ledger, estimates, specialists and evidence; computations, returns, payments, differences, rates and correspondence.
- **Scope/dependencies:** tax roll-forward, rate reconciliation and exceptions; AS-G17, AS-G22 and AS-G23.
- **Tests/deliverable:** G13 deferred-tax/rate/access tests; tax workpaper and disclosure evidence.
- **Acceptance criteria:**
  - [ ] AC-G13.01 — Current tax reconciles to ledger, computation, return and payment evidence.
  - [ ] AC-G13.02 — Deferred tax identifies difference source, rate and reversal period.
  - [ ] AC-G13.03 — Losses, uncertain positions and allowances record basis and review.
  - [ ] AC-G13.04 — Rate reconciliation identifies material reconciling items.
  - [ ] AC-G13.05 — Correspondence and specialist input retain source and reliability metadata.
  - [ ] AC-G13.06 — Changed rate, return, computation or ledger invalidates approvals.
  - [ ] AC-G13.07 — Missing support or exposure remains a completion exception.
  - [ ] AC-G13.08 — Tax evidence access and export are restricted.
  - [ ] AC-G13.09 — Conclusion separates current, deferred, uncertainty and disclosure.

### AS-G14 — Provisions, contingencies and legal matters

- **Objective:** capture legal inquiries, claims, provisions, contingencies and disclosures.
- **Gap:** legal matters need response and evaluation without treating management assertion as independent evidence.
- **Reuse/data:** confirmations, communications, snapshots and estimates; matter register, correspondence, counsel, status and amount.
- **Scope/dependencies:** inquiry, response, evaluation, provision and disclosure; AS-G02, AS-G17 and AS-G21.
- **Tests/deliverable:** G14 response/reliability/unresolved/access tests; legal workpaper and open-matter report.
- **Acceptance criteria:**
  - [ ] AC-G14.01 — Matter register records claim, parties, status, reporting date and owner.
  - [ ] AC-G14.02 — Counsel requests/responses preserve dispatch, receipt, source and reliability.
  - [ ] AC-G14.03 — Management assessment records probability, estimate, range and uncertainty.
  - [ ] AC-G14.04 — Minutes and subsequent developments link where relevant.
  - [ ] AC-G14.05 — Provision, contingency and no-action outcomes require reviewer disposition.
  - [ ] AC-G14.06 — Unanswered or unreliable legal evidence remains unresolved.
  - [ ] AC-G14.07 — Material changes invalidate completion and reporting dependencies.
  - [ ] AC-G14.08 — Legal evidence is restricted and not an ordinary client export.
  - [ ] AC-G14.09 — Report distinguishes recognition, disclosure and follow-up.

### AS-G15 — Journal-entry analysis and management override

- **Objective:** analyze journals and unusual adjustments for fraud/override indicators.
- **Gap:** criteria, population completeness and investigation outcomes need a controlled workflow.
- **Reuse/data:** GL, populations, samples, findings and review; full journals, users, dates, accounts, approvals and flags.
- **Scope/dependencies:** criteria, validation, selection, investigation and conclusion; AS-G23 and AS-G25.
- **Tests/deliverable:** G15 population/duplicate/privilege tests; journal analytics workpaper and exception report.
- **Acceptance criteria:**
  - [ ] AC-G15.01 — Full journal population is pinned and completeness checks recorded.
  - [ ] AC-G15.02 — Selection criteria are versioned, explainable and approved.
  - [ ] AC-G15.03 — Selected entries retain preparer, approver, time, source and accounts.
  - [ ] AC-G15.04 — Unusual timing, round amounts, senior-user and unusual combinations are surfaced.
  - [ ] AC-G15.05 — Exceptions record investigation, evidence, response and disposition.
  - [ ] AC-G15.06 — Changed population or criteria invalidates selection and conclusion.
  - [ ] AC-G15.07 — Source journals cannot be edited through analytics.
  - [ ] AC-G15.08 — Unresolved override indicators block relevant completion.
  - [ ] AC-G15.09 — Journal analytics are tenant/engagement scoped.

### AS-G16 — Related parties and unusual transactions

- **Objective:** identify related parties and test completeness, authorization, terms and disclosure.
- **Gap:** declarations, registers, minutes, master data and ledger need one reconciled workflow.
- **Reuse/data:** questionnaires, restricted evidence, populations and confirmations; registers, transactions and disclosures.
- **Scope/dependencies:** source reconciliation, identification, investigation and conclusion; AS-G02, AS-G15 and AS-G23.
- **Tests/deliverable:** G16 source/omission/access tests; related-party workpaper.
- **Acceptance criteria:**
  - [ ] AC-G16.01 — Sources, period and completeness procedures are recorded.
  - [ ] AC-G16.02 — Conflicts among sources create exceptions.
  - [ ] AC-G16.03 — Transactions preserve amount, terms, approval and counterparty evidence.
  - [ ] AC-G16.04 — Unusual transactions link to journal analysis and response where relevant.
  - [ ] AC-G16.05 — Disclosure decisions record recognition, measurement and presentation impact.
  - [ ] AC-G16.06 — Source-register changes invalidate affected review.
  - [ ] AC-G16.07 — Related-party evidence is restricted.
  - [ ] AC-G16.08 — Unresolved omissions block completion.
  - [ ] AC-G16.09 — Report distinguishes identified, tested, unresolved and disclosed matters.

### AS-G17 — Accounting estimates and fair values

- **Objective:** evaluate methods, data, assumptions, bias, uncertainty and specialist evidence.
- **Gap:** estimates need a common method-to-outcome workpaper.
- **Reuse/data:** calculations, snapshots, specialists and sensitivity; estimate register, inputs, forecasts, models and reports.
- **Scope/dependencies:** inventory, risk, recalculation/back-testing and conclusion; AS-G22, AS-G23 and AS-G25.
- **Tests/deliverable:** G17 sensitivity/input/specialist tests; estimate register and uncertainty report.
- **Acceptance criteria:**
  - [ ] AC-G17.01 — Each material estimate records account, assertion, method, owner and period.
  - [ ] AC-G17.02 — Data sources and generations are pinned and reconciled.
  - [ ] AC-G17.03 — Assumptions record basis, date, source, sensitivity and approval.
  - [ ] AC-G17.04 — Prior outcomes and bias indicators are captured.
  - [ ] AC-G17.05 — Recalculation, alternative range or specialist work is linked when used.
  - [ ] AC-G17.06 — Uncertainty, limitations and contradictory evidence create exceptions.
  - [ ] AC-G17.07 — Material input changes invalidate review and conclusion.
  - [ ] AC-G17.08 — Unsupported management conclusions cannot close exceptions.
  - [ ] AC-G17.09 — Report separates method, data, assumptions, outcome and disclosure.

### AS-G18 — Cash and bank reconciliations

- **Objective:** verify cash existence, rights, reconciliations, transfers, restrictions and cut-off.
- **Gap:** bank reconciliation and confirmation need an account-level workflow.
- **Reuse/data:** confirmations, reconciliation, snapshots and review; account register, statements, reconciliations, transfers and restrictions.
- **Scope/dependencies:** balance tie-out, reconciling items, transfers and conclusion; AS-G02, AS-G23 and AS-G25.
- **Tests/deliverable:** G18 replay/transfer/restricted-cash tests; cash lead schedule and exception report.
- **Acceptance criteria:**
  - [ ] AC-G18.01 — Bank accounts reconcile to ledger and account register.
  - [ ] AC-G18.02 — Statements and confirmations preserve source snapshots and response metadata.
  - [ ] AC-G18.03 — Reconciling items record age, owner, explanation and clearance.
  - [ ] AC-G18.04 — Period-end transfers are selected and tested for cut-off/kiting.
  - [ ] AC-G18.05 — Restricted cash, liens and overdraft terms are separate.
  - [ ] AC-G18.06 — Missing evidence or unexplained differences remain unresolved.
  - [ ] AC-G18.07 — Changed bank/ledger evidence invalidates review.
  - [ ] AC-G18.08 — Cash count evidence is distinguished from bank evidence.
  - [ ] AC-G18.09 — Account-by-account conclusion is reproducible.

### AS-G19 — Analytical review and substantive analytics

- **Objective:** perform planning, substantive and final analytics with explicit expectations and thresholds.
- **Gap:** analytics need separation between dashboard information and evidence-bearing procedures.
- **Reuse/data:** calculator, populations, reliability assessment and workpapers; balances, budgets, metrics, expectations and thresholds.
- **Scope/dependencies:** expectation, precision, variance investigation and conclusion; AS-G23, AS-G27 and AS-G25.
- **Tests/deliverable:** G19 precision/source/baseline tests; analytical workpaper and variance queue.
- **Acceptance criteria:**
  - [ ] AC-G19.01 — Procedure is labelled planning, substantive or final.
  - [ ] AC-G19.02 — Expectation, source, period, granularity and precision are recorded.
  - [ ] AC-G19.03 — Thresholds and investigation criteria are methodology-versioned.
  - [ ] AC-G19.04 — Reliability covers completeness, accuracy and limitations.
  - [ ] AC-G19.05 — Variances link explanations, corroborating evidence and open exceptions.
  - [ ] AC-G19.06 — A dashboard cannot be signed off as an analytical procedure without a workpaper.
  - [ ] AC-G19.07 — Changed data, expectation or threshold invalidates conclusion.
  - [ ] AC-G19.08 — Conclusion states whether intended risk/assertion was addressed.
  - [ ] AC-G19.09 — Cross-engagement joins and unauthorized exports are blocked.

### AS-G20 — Going concern

- **Objective:** evaluate indicators, management assessment, forecast period, plans and reporting impact.
- **Gap:** going concern needs time-bound evidence and escalation.
- **Reuse/data:** estimates, analytics, events and completion; forecasts, budgets, covenants, financing and sensitivities.
- **Scope/dependencies:** indicators, assessment, plans, testing and conclusion; AS-G10, AS-G17, AS-G19 and AS-G21.
- **Tests/deliverable:** G20 forecast/covenant/indicator tests; workpaper, sensitivity pack and escalation.
- **Acceptance criteria:**
  - [ ] AC-G20.01 — Indicators, assessment period, owner and reporting date are recorded.
  - [ ] AC-G20.02 — Forecast inputs and generation are pinned.
  - [ ] AC-G20.03 — Forecast arithmetic, liquidity, covenant and financing assumptions are tested.
  - [ ] AC-G20.04 — Mitigating plans have owner, evidence, feasibility and timing.
  - [ ] AC-G20.05 — Sensitivity and downside cases are reproducible.
  - [ ] AC-G20.06 — Contradictory post-period evidence creates a review point.
  - [ ] AC-G20.07 — Material uncertainty cannot be closed by an unreviewed assertion.
  - [ ] AC-G20.08 — Changed forecast, financing or event invalidates completion approvals.
  - [ ] AC-G20.09 — Conclusion and reporting implication are separate human decisions.

### AS-G21 — Subsequent events

- **Objective:** identify, evaluate and document events between reporting date, completion and release.
- **Gap:** discovery, cut-off, adjustment/disclosure and release impact need a durable workflow.
- **Reuse/data:** communications, snapshots, completion and release; event log, ledger searches, minutes, legal responses and release date.
- **Scope/dependencies:** inquiries, searches, assessment, follow-up and disposition; AS-G14, AS-G20, AS-G26 and AS-G28.
- **Tests/deliverable:** G21 date-window/release-lock/duplicate tests; events register and checklist integration.
- **Acceptance criteria:**
  - [ ] AC-G21.01 — Reporting, search, completion and planned release dates are recorded.
  - [ ] AC-G21.02 — Sources searched, performer and result are preserved.
  - [ ] AC-G21.03 — Events record discovery, condition, amount, evidence and owner.
  - [ ] AC-G21.04 — Adjusting, non-adjusting, disclosure and follow-up dispositions are distinct.
  - [ ] AC-G21.05 — Open events block release when effect is unresolved.
  - [ ] AC-G21.06 — A post-completion event reopens/amends rather than editing history.
  - [ ] AC-G21.07 — Duplicate events are detected without losing evidence.
  - [ ] AC-G21.08 — Event register is in completion package and release manifest.
  - [ ] AC-G21.09 — Event access and notifications obey scope.

### AS-G22 — Group, component and specialist work

- **Objective:** coordinate delegated work with bounded scope, instructions, evidence, evaluation and review.
- **Gap:** delegated work needs controlled acceptance without claiming the software establishes competence.
- **Reuse/data:** roster, workpapers, evidence, review and restricted access; assignment, scope, instructions, deliverables and findings.
- **Scope/dependencies:** assign, request, receive, evaluate and close; AS-G01, AS-G17 and AS-G24.
- **Tests/deliverable:** G22 access/late-delivery/reliance tests; delegated-work register and instruction pack.
- **Acceptance criteria:**
  - [ ] AC-G22.01 — Assignment records area, scope, period, instructions and reviewer.
  - [ ] AC-G22.02 — Independence, competence and authorization evidence are recorded, not auto-approved.
  - [ ] AC-G22.03 — Deliverables retain bytes, version, receipt date and covered procedures.
  - [ ] AC-G22.04 — Limitations, exceptions and unresolved matters create review points.
  - [ ] AC-G22.05 — Reviewer records whether reliance is permitted and why.
  - [ ] AC-G22.06 — Late, missing or changed deliverables invalidate dependent work.
  - [ ] AC-G22.07 — Component users see only assigned scope and evidence.
  - [ ] AC-G22.08 — Specialist reports retain limitations and do not become automatic conclusions.
  - [ ] AC-G22.09 — Final package lists delegated work and outstanding responses.

### AS-G23 — Population validation, sampling and data reliability

- **Objective:** provide shared populations, sampling and data-quality controls.
- **Gap:** every account-area story needs pinned populations and replayable selections.
- **Reuse/data:** intake, mapping, sampling, query results and snapshots; dataset, filters, totals, method, seed, selections and exclusions.
- **Scope/dependencies:** validate data, create populations, select samples and preserve manifests; existing intake and evidence primitives.
- **Tests/deliverable:** G23 replay/empty/duplicate/scope tests; population and sample services plus manifest.
- **Acceptance criteria:**
  - [ ] AC-G23.01 — Population records source, generation, period, owner, scope and assertion.
  - [ ] AC-G23.02 — Completeness checks identify missing, duplicate, malformed and excluded rows.
  - [ ] AC-G23.03 — Method, parameters, seed, date and methodology version are stored.
  - [ ] AC-G23.04 — Pinned input and parameters replay the same selection.
  - [ ] AC-G23.05 — Manual additions/exclusions require reason, actor and evidence.
  - [ ] AC-G23.06 — Empty, stale, cross-client or unreconciled populations cannot be submitted complete.
  - [ ] AC-G23.07 — Source, filters or materiality changes invalidate dependent work.
  - [ ] AC-G23.08 — Exports contain enough metadata to reproduce the result and are scoped.
  - [ ] AC-G23.09 — Population commands are idempotent and auditable.

### AS-G24 — Evidence, exceptions and review-point closure

- **Objective:** connect procedures to evidence, exceptions, follow-up and independent review.
- **Gap:** account-area stories need common closure that cannot hide contradictory evidence.
- **Reuse/data:** workpaper versions, links, review objects and generation guards; procedure, evidence, result, exception, follow-up and conclusion.
- **Scope/dependencies:** evidence linking, exception lifecycle and re-review; AS-G23 and all account-area stories.
- **Tests/deliverable:** G24 generation/contradiction/authorization tests; reusable closure component and dashboard.
- **Acceptance criteria:**
  - [ ] AC-G24.01 — Submitted workpaper identifies objective, procedure, population, evidence and conclusion.
  - [ ] AC-G24.02 — Evidence links point to exact snapshots with purpose, scope and reliability.
  - [ ] AC-G24.03 — Exceptions record condition, amount, assertion, owner, due date and status.
  - [ ] AC-G24.04 — Review points distinguish request, response, resolution and acceptance.
  - [ ] AC-G24.05 — Material source change increments safety generation and invalidates approval.
  - [ ] AC-G24.06 — Contradictory evidence cannot be hidden by replacing a link.
  - [ ] AC-G24.07 — Reviewer cannot approve own preparation or bypass object authorization.
  - [ ] AC-G24.08 — Closed points retain full history and resolution evidence.
  - [ ] AC-G24.09 — Commands emit audit events and deterministic results.

### AS-G25 — Misstatement evaluation, corrections and representations

- **Objective:** aggregate corrected/uncorrected misstatements, evaluate qualitative effects and capture representations.
- **Gap:** findings need one final evaluation connecting exceptions, entries, responses and reporting.
- **Reuse/data:** findings, adjustments, statements, reviews and completion; exception, amount, period, status, factors and representation.
- **Scope/dependencies:** record, aggregate, evaluate, propose correction and review; AS-G24, AS-G27 and AS-G26.
- **Tests/deliverable:** G25 aggregation/precision/qualitative/reopen tests; misstatement register and checklist.
- **Acceptance criteria:**
  - [ ] AC-G25.01 — Each item links procedure, evidence, account, assertion and period.
  - [ ] AC-G25.02 — Corrected, uncorrected, projected and control-only statuses are distinct.
  - [ ] AC-G25.03 — Aggregation uses pinned materiality and preserves inputs.
  - [ ] AC-G25.04 — Qualitative factors record rationale, users, trend and bias/fraud impact.
  - [ ] AC-G25.05 — Proposed corrections link journal/statement impact and management decision.
  - [ ] AC-G25.06 — Unresolved/disputed items remain in completion package.
  - [ ] AC-G25.07 — Changed item, materiality or statements invalidates approval.
  - [ ] AC-G25.08 — Representations record scope, date, signatory and evidence without replacing audit work.
  - [ ] AC-G25.09 — Cross-engagement amounts and unscoped exports are blocked.

### AS-G26 — Final completion, EQR and report package

- **Objective:** assemble final completion and enforce report prerequisites.
- **Gap:** completion must aggregate areas, reviews, events, misstatements, representations and EQR.
- **Reuse/data:** release state, review generation, feedback package, signing boundary and manifest; area conclusions and open points.
- **Scope/dependencies:** checklist, EQR applicability, sign-off preparation and readiness; AS-G20, AS-G21, AS-G24, AS-G25, AS-G27 and AS-G28.
- **Tests/deliverable:** G26 release/missing-prerequisite/reopen tests; completion dashboard and manifest.
- **Acceptance criteria:**
  - [ ] AC-G26.01 — Checklist enumerates applicable areas, procedures, review points and conclusions.
  - [ ] AC-G26.02 — Missing, stale, unresolved or unauthorized prerequisites block completion.
  - [ ] AC-G26.03 — EQR applicability, reviewer and evidence are explicit.
  - [ ] AC-G26.04 — Going concern, events, related parties, misstatements and representations are included.
  - [ ] AC-G26.05 — Package records exact statement, report and evidence generations.
  - [ ] AC-G26.06 — Completion is distinct from signing and external release.
  - [ ] AC-G26.07 — Material change reopens affected checklist and candidate.
  - [ ] AC-G26.08 — Independent review cannot be satisfied by preparer or unverified automation.
  - [ ] AC-G26.09 — Manifest is deterministic and complete.

### AS-G27 — Planning and financial-statement residual-gap checks

- **Objective:** prove that existing planning and statement features feed the new workflows.
- **Gap:** residual integration checks are needed; duplicate modules are out of scope.
- **Reuse/data:** current planning, materiality, mapping, calculator, sealed TB and approvals; plans, risks, mappings, adjustments and statements.
- **Scope/dependencies:** integration assertions, orphan detection and tie-outs; existing P0/P6 slices and AS-G23.
- **Tests/deliverable:** G27 generation/orphan/tie-out tests; residual-gap report.
- **Acceptance criteria:**
  - [ ] AC-G27.01 — Approved plan and materiality generation are available to applicable stories.
  - [ ] AC-G27.02 — Significant risks without procedures and procedures without objectives are reported.
  - [ ] AC-G27.03 — Area conclusions link assertions, risks, procedures and materiality.
  - [ ] AC-G27.04 — Statements reconcile to sealed TB, mappings and approved adjustments.
  - [ ] AC-G27.05 — Cross-check failures create completion blockers.
  - [ ] AC-G27.06 — Existing records are reused rather than duplicated.
  - [ ] AC-G27.07 — Plan, materiality, mapping or statement changes invalidate downstream work.
  - [ ] AC-G27.08 — Output separates local status from professional approval.
  - [ ] AC-G27.09 — Report is tenant-scoped and reproducible.

### AS-G28 — Records, release, archive and recovery acceptance

- **Objective:** connect final work to release, records, retention observation and recovery reconciliation.
- **Gap:** release, archive and recovery gates must remain separate.
- **Reuse/data:** release state, provider boundaries, Purview observations, manifests and restore drill; final package, signatures, checkpoint, archive and recovery results.
- **Scope/dependencies:** local manifest, capability checks, release block/report, archive and recovery; AS-G26 plus external P1–P8 prerequisites.
- **Tests/deliverable:** G28 manifest/recovery/fail-closed tests; readiness report and runbook, with no simulated production pass.
- **Acceptance criteria:**
  - [ ] AC-G28.01 — Release, signature, archive and recovery are separate states and decisions.
  - [ ] AC-G28.02 — Manifest includes workpapers, snapshots, approvals, events, misstatements and audit history.
  - [ ] AC-G28.03 — Provider capability and selected-resource grants are checked before live action.
  - [ ] AC-G28.04 — Missing Graph, SharePoint, Purview, signing or checkpoint prerequisites keep live gate blocked.
  - [ ] AC-G28.05 — Local archive assembly is deterministic and records hashes, versions and omissions.
  - [ ] AC-G28.06 — Recovery restores to isolation and reconciles counts, hashes and generations.
  - [ ] AC-G28.07 — RPO/RTO values are measured evidence, not configuration claims.
  - [ ] AC-G28.08 — Production release cannot be inferred from simulation or green tests.
  - [ ] AC-G28.09 — Report names outstanding human approval and operational ownership.

## Source traceability register

The register below supplies the requested 20-section mapping. Keys are contiguous and cover exactly P001–P165. The controlled source must provide the authoritative title and wording for each key before methodology sign-off.

| Section | Original audit area | Procedure keys | Count | Owning stories |
|---|---|---:|---:|---|
| S01 | Engagement acceptance and independence | P001–P006 | 6 | AS-G01 |
| S02 | Continuance, terms and resources | P007–P013 | 7 | AS-G01, AS-G27 |
| S03 | Planning, materiality and risk response | P014–P021 | 8 | AS-G23, AS-G27 |
| S04 | Cash and bank | P022–P029 | 8 | AS-G02, AS-G18 |
| S05 | Receivables and revenue | P030–P039 | 10 | AS-G02, AS-G03, AS-G04 |
| S06 | Inventory | P040–P048 | 9 | AS-G05 |
| S07 | PPE and intangibles | P049–P055 | 7 | AS-G07, AS-G08 |
| S08 | Purchases, payables and accruals | P056–P063 | 8 | AS-G06 |
| S09 | Payroll and employee benefits | P064–P070 | 7 | AS-G09 |
| S10 | Borrowings, leases and equity | P071–P078 | 8 | AS-G10, AS-G11, AS-G12 |
| S11 | Tax, provisions, contingencies and legal | P079–P087 | 9 | AS-G13, AS-G14 |
| S12 | Journal entries and management override | P088–P095 | 8 | AS-G15 |
| S13 | Estimates, ECL and fair values | P096–P103 | 8 | AS-G03, AS-G17 |
| S14 | Related parties and unusual transactions | P104–P109 | 6 | AS-G16 |
| S15 | Data reliability, analytics and sampling | P110–P117 | 8 | AS-G19, AS-G23 |
| S16 | Going concern and subsequent events | P118–P125 | 8 | AS-G20, AS-G21 |
| S17 | Group, component and specialist work | P126–P132 | 7 | AS-G22 |
| S18 | Misstatements, findings and completion | P133–P140 | 8 | AS-G24, AS-G25 |
| S19 | Reporting, signing and controlled release | P141–P153 | 13 | AS-G26, AS-G28 |
| S20 | Records, archive, retention and recovery | P154–P165 | 12 | AS-G28 |
| **Total** | **20 sections** | **P001–P165** | **165** | **AS-G01–AS-G28** |

For issue creation, each procedure must eventually have procedure_key, source_section, source_title, source_text_hash, owning_story, acceptance_criteria, focused_test, evidence_type and professional_owner. Multiple-story mappings need one primary owner and explicit hand-off criteria.

## Delivery plan

| Milestone | Outcome | Stories | Exit evidence |
|---|---|---|---|
| M1 — Workflow foundations | Acceptance, population/sampling and residual integration ready. | AS-G01, AS-G23, AS-G27 | Guarded commands, generation links, focused tests and residual report |
| M2 — Account-area execution | Core balance, transaction and estimate workflows usable. | AS-G02–AS-G14, AS-G17–AS-G18 | Reconciled populations, evidence chains, exceptions and reviewer paths |
| M3 — Risk and completion inputs | Override, related parties, analytics, going concern, events and delegated work covered. | AS-G15–AS-G16, AS-G19–AS-G22 | Investigation queues, delegated evidence and open-risk report |
| M4 — Completion and records | Review closure, misstatement evaluation, final package and archive/recovery readiness integrated. | AS-G24–AS-G26, AS-G28 | Completion gate, deterministic manifest, reopen behavior and recovery evidence |
| M5 — Approval and live acceptance | Professional and real-tenant/operational acceptance independently evidenced. | All stories | Human approvals, grants, Purview behavior, signing, RPO/RTO and protected merge evidence |

Dependency order is M1 → M2 → M3 → M4 → M5. A local simulation, mock provider or green build never substitutes for an external or human exit condition.

## Verification plan

Every story's focused test group must cover a happy path, missing-input block, stale-generation invalidation, scope/authorization denial, idempotent retry and audit-event assertion. Database stories use the repository PostgreSQL profile. Live provider tests remain fail-closed and BLOCKED_EXTERNAL until approved prerequisites exist.

### End-to-end audit scenario

1. Create scoped client and engagement; complete acceptance and independence.
2. Pin planning, materiality, trial balance, mapping and statement generations.
3. Import/reconcile populations and select deterministic samples.
4. Execute cash, receivables/ECL, inventory, revenue/cut-off, payables, PPE, payroll, loans, tax, legal and related-party procedures.
5. Run journal and analytical procedures; evaluate estimates, going concern and events.
6. Receive delegated work; resolve or retain review points.
7. Aggregate corrected/uncorrected misstatements and capture representations.
8. Assemble completion/EQR package and verify prerequisites.
9. Produce deterministic local release/archive manifest.
10. Run isolated recovery reconciliation; stop at live release unless external and human evidence exists.

Pass requires scope-checked transitions, generation-bound conclusions, dispositions for all exceptions and distinct statuses LOCAL_READY, PROFESSIONAL_APPROVAL_REQUIRED, BLOCKED_EXTERNAL and PRODUCTION_ACCEPTED.

### Negative and boundary scenarios

| ID | Scenario | Required result |
|---|---|---|
| N01 | Missing independence response | Acceptance blocked |
| N02 | Cross-client population query | Authorization denied |
| N03 | Stale trial-balance generation | Review invalidated |
| N04 | Empty/unreconciled population | Sampling blocked |
| N05 | Duplicate confirmation response | Duplicate rejected; original retained |
| N06 | Confirmation non-response | Follow-up/alternative procedure required |
| N07 | Inventory listing changed after attendance | Review reopened |
| N08 | Invoice outside search window | Limitation/exception recorded |
| N09 | Invalid ageing date | Rejected or exception created |
| N10 | Unsupported ECL assumption | Estimate not approvable |
| N11 | Depreciation after disposal date | Calculation blocked |
| N12 | Covenant breach without waiver | Escalation required |
| N13 | Unauthorized payroll export | Denied and logged |
| N14 | Journal criteria changed after selection | Selection invalidated |
| N15 | Related-party conflict with minutes | Review point opened |
| N16 | Specialist scope limitation | Reliance decision required |
| N17 | New event after completion | Reopen/amendment required |
| N18 | Uncorrected misstatement over threshold | Release blocked |
| N19 | Missing Purview/Graph/signing capability | Live gate remains blocked |
| N20 | Recovery hash/count mismatch | Recovery failed; quarantine retained |

## Definition of Ready

A story is ready when source procedure keys and methodology owner are identified, roles and scope are known, reused primitives are named, data/evidence are defined, migration/API/UI boundaries are clear, negative cases are listed and the professional owner approves the intended procedure.

## Definition of Done

A story is done when the guarded vertical slice, migration, authorization, evidence/generation behavior, focused/PostgreSQL/negative tests and documentation are complete; source traceability is reconciled; no external result is simulated; and the current commit has required review. Local completion is not live acceptance.

## Coding-agent handoff prompt

Implement one story in dependency order. Read repository instructions and the authoritative specification. Inspect Git state and preserve unrelated work. Reuse existing entities, commands, snapshot/evidence, review-generation and release primitives. Make the smallest coherent vertical slice; enforce firm/client/engagement scope; fail closed on missing, stale or unauthorized inputs; add the named focused tests; update only observed evidence; never invent Entra, Graph, SharePoint, Purview, signing, recovery or professional approval evidence; report exact commit and test output; stop at external/human gates instead of simulating them.

## Approval and live-acceptance boundary

This document does not close Entra identity/secret-store evidence, selected Graph/SharePoint grants, Purview profile and label behavior, approved signing methodology, professional sign-off, independent review/protected merge evidence, or cross-store production recovery with measured RPO/RTO. Those gates must be recorded in the execution ledger and verified against current repository and tenant state before any story or milestone is called production-complete.
