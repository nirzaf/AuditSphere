# Audit Workflow Gap-Closure Traceability Ledger

[Master index](../00_INDEX.md) · [Preserved Source Document](../source/ORIGINAL_Audit_Workflow_Gap_Closure_User_Stories.md)

**Traceability record only.** Task front matter is the authoritative status source. This ledger records the exact disposition and ownership mapping for every requirement in the audit-workflow gap-closure backlog (`ORIGINAL_Audit_Workflow_Gap_Closure_User_Stories.md`).

## 1. Summary statistics

| Measure | Count | Notes |
|---|---:|---|
| Source user stories (`AS-AUD-*`) | 28 | All 28 stories analyzed and dispositioned |
| Acceptance criteria (`AS-AUD-*-AC*`) | 258 | All 258 criteria mapped to canonical tasks |
| Source procedures (`AWP-*`) | 165 | All 165 procedures across 20 audit sections mapped |
| Orphan requirements | 0 | Every requirement, AC, and procedure has a canonical owner |
| Existing tasks enhanced | 14 | T001, T022, T023, T031, T032, T033, T037, T038, T040, T048, T049, T050, T051, T054 |
| Genuinely new tasks created | 21 | T055–T075 across 4 logical audit folders |
| Total implementation tasks | 75 | 54 existing R2R + 21 new Audit |

### Dispositions breakdown

- `MERGE_EXISTING`: 59 criteria merged into existing tasks (AS-AUD-001 baseline, AS-AUD-018 equity, AS-AUD-025 statements/disclosures, AS-AUD-026 audit differences, AS-AUD-027 release gates, AS-AUD-028 acceptance/journeys).
- `NEW_TASK_REQUIRED`: 196 criteria assigned to 21 new tasks (T055–T075) covering genuine residual gaps (audit program library, tailoring, lead schedules, sampling, confirmations, 14 field audit workpapers, analytics, going concern, subsequent events).
- `REFERENCE_ONLY`: 1 criterion (AS-AUD-001-AC04) recording architectural boundary preservation.
- `BLOCKED_EXTERNAL`: 2 criteria (AS-AUD-001-AC06 methodology inputs; AS-AUD-027-AC14 final partner professional sign-off and report issuance) recording external human/methodology prerequisites.

## 2. Requirement-by-requirement traceability matrix

| Source Story | Acceptance Criterion | AWP IDs | Canonical Task | Disposition | Notes |
|---|---|---|---|---|---|
| AS-AUD-001 | `AS-AUD-001-AC01` | — | **T001** | `MERGE_EXISTING` | Consolidated into baseline implementation inventory |
| AS-AUD-001 | `AS-AUD-001-AC02` | — | **T001** | `MERGE_EXISTING` | Consolidated into baseline implementation inventory |
| AS-AUD-001 | `AS-AUD-001-AC03` | — | **T001** | `MERGE_EXISTING` | Consolidated into baseline implementation inventory |
| AS-AUD-001 | `AS-AUD-001-AC04` | — | **T001** | `REFERENCE_ONLY` | Target repo and namespace preservation contract |
| AS-AUD-001 | `AS-AUD-001-AC05` | — | **T001** | `MERGE_EXISTING` | Consolidated into baseline implementation inventory |
| AS-AUD-001 | `AS-AUD-001-AC06` | — | **T001** | `BLOCKED_EXTERNAL` | External methodology approvals required before release |
| AS-AUD-001 | `AS-AUD-001-AC07` | — | **T001** | `MERGE_EXISTING` | Consolidated into baseline implementation inventory |
| AS-AUD-002 | `AS-AUD-002-AC01` | — | **T055** | `NEW_TASK_REQUIRED` | Versioned audit program template library |
| AS-AUD-002 | `AS-AUD-002-AC02` | — | **T055** | `NEW_TASK_REQUIRED` | Versioned audit program template library |
| AS-AUD-002 | `AS-AUD-002-AC03` | — | **T055** | `NEW_TASK_REQUIRED` | Versioned audit program template library |
| AS-AUD-002 | `AS-AUD-002-AC04` | — | **T055** | `NEW_TASK_REQUIRED` | Versioned audit program template library |
| AS-AUD-002 | `AS-AUD-002-AC05` | — | **T055** | `NEW_TASK_REQUIRED` | Versioned audit program template library |
| AS-AUD-002 | `AS-AUD-002-AC06` | — | **T055** | `NEW_TASK_REQUIRED` | Versioned audit program template library |
| AS-AUD-002 | `AS-AUD-002-AC07` | — | **T055** | `NEW_TASK_REQUIRED` | Versioned audit program template library |
| AS-AUD-002 | `AS-AUD-002-AC08` | — | **T055** | `NEW_TASK_REQUIRED` | Versioned audit program template library |
| AS-AUD-003 | `AS-AUD-003-AC01` | — | **T056** | `NEW_TASK_REQUIRED` | Engagement tailoring and procedure review lifecycle |
| AS-AUD-003 | `AS-AUD-003-AC02` | — | **T056** | `NEW_TASK_REQUIRED` | Engagement tailoring and procedure review lifecycle |
| AS-AUD-003 | `AS-AUD-003-AC03` | — | **T056** | `NEW_TASK_REQUIRED` | Engagement tailoring and procedure review lifecycle |
| AS-AUD-003 | `AS-AUD-003-AC04` | — | **T056** | `NEW_TASK_REQUIRED` | Engagement tailoring and procedure review lifecycle |
| AS-AUD-003 | `AS-AUD-003-AC05` | — | **T056** | `NEW_TASK_REQUIRED` | Engagement tailoring and procedure review lifecycle |
| AS-AUD-003 | `AS-AUD-003-AC06` | — | **T056** | `NEW_TASK_REQUIRED` | Engagement tailoring and procedure review lifecycle |
| AS-AUD-003 | `AS-AUD-003-AC07` | — | **T056** | `NEW_TASK_REQUIRED` | Engagement tailoring and procedure review lifecycle |
| AS-AUD-003 | `AS-AUD-003-AC08` | — | **T056** | `NEW_TASK_REQUIRED` | Engagement tailoring and procedure review lifecycle |
| AS-AUD-003 | `AS-AUD-003-AC09` | — | **T056** | `NEW_TASK_REQUIRED` | Engagement tailoring and procedure review lifecycle |
| AS-AUD-003 | `AS-AUD-003-AC10` | — | **T056** | `NEW_TASK_REQUIRED` | Engagement tailoring and procedure review lifecycle |
| AS-AUD-004 | `AS-AUD-004-AC01` | — | **T057** | `NEW_TASK_REQUIRED` | Audit lead schedules and source population reconciliation |
| AS-AUD-004 | `AS-AUD-004-AC02` | — | **T057** | `NEW_TASK_REQUIRED` | Audit lead schedules and source population reconciliation |
| AS-AUD-004 | `AS-AUD-004-AC03` | — | **T057** | `NEW_TASK_REQUIRED` | Audit lead schedules and source population reconciliation |
| AS-AUD-004 | `AS-AUD-004-AC04` | — | **T057** | `NEW_TASK_REQUIRED` | Audit lead schedules and source population reconciliation |
| AS-AUD-004 | `AS-AUD-004-AC05` | — | **T057** | `NEW_TASK_REQUIRED` | Audit lead schedules and source population reconciliation |
| AS-AUD-004 | `AS-AUD-004-AC06` | — | **T057** | `NEW_TASK_REQUIRED` | Audit lead schedules and source population reconciliation |
| AS-AUD-004 | `AS-AUD-004-AC07` | — | **T057** | `NEW_TASK_REQUIRED` | Audit lead schedules and source population reconciliation |
| AS-AUD-004 | `AS-AUD-004-AC08` | — | **T057** | `NEW_TASK_REQUIRED` | Audit lead schedules and source population reconciliation |
| AS-AUD-004 | `AS-AUD-004-AC09` | — | **T057** | `NEW_TASK_REQUIRED` | Audit lead schedules and source population reconciliation |
| AS-AUD-005 | `AS-AUD-005-AC01` | — | **T058** | `NEW_TASK_REQUIRED` | Audit sampling, item tests and subsequent matching |
| AS-AUD-005 | `AS-AUD-005-AC02` | — | **T058** | `NEW_TASK_REQUIRED` | Audit sampling, item tests and subsequent matching |
| AS-AUD-005 | `AS-AUD-005-AC03` | — | **T058** | `NEW_TASK_REQUIRED` | Audit sampling, item tests and subsequent matching |
| AS-AUD-005 | `AS-AUD-005-AC04` | — | **T058** | `NEW_TASK_REQUIRED` | Audit sampling, item tests and subsequent matching |
| AS-AUD-005 | `AS-AUD-005-AC05` | — | **T058** | `NEW_TASK_REQUIRED` | Audit sampling, item tests and subsequent matching |
| AS-AUD-005 | `AS-AUD-005-AC06` | — | **T058** | `NEW_TASK_REQUIRED` | Audit sampling, item tests and subsequent matching |
| AS-AUD-005 | `AS-AUD-005-AC07` | — | **T058** | `NEW_TASK_REQUIRED` | Audit sampling, item tests and subsequent matching |
| AS-AUD-005 | `AS-AUD-005-AC08` | — | **T058** | `NEW_TASK_REQUIRED` | Audit sampling, item tests and subsequent matching |
| AS-AUD-005 | `AS-AUD-005-AC09` | — | **T058** | `NEW_TASK_REQUIRED` | Audit sampling, item tests and subsequent matching |
| AS-AUD-006 | `AS-AUD-006-AC01` | — | **T059** | `NEW_TASK_REQUIRED` | External confirmations and alternative procedures |
| AS-AUD-006 | `AS-AUD-006-AC02` | — | **T059** | `NEW_TASK_REQUIRED` | External confirmations and alternative procedures |
| AS-AUD-006 | `AS-AUD-006-AC03` | — | **T059** | `NEW_TASK_REQUIRED` | External confirmations and alternative procedures |
| AS-AUD-006 | `AS-AUD-006-AC04` | — | **T059** | `NEW_TASK_REQUIRED` | External confirmations and alternative procedures |
| AS-AUD-006 | `AS-AUD-006-AC05` | — | **T059** | `NEW_TASK_REQUIRED` | External confirmations and alternative procedures |
| AS-AUD-006 | `AS-AUD-006-AC06` | — | **T059** | `NEW_TASK_REQUIRED` | External confirmations and alternative procedures |
| AS-AUD-006 | `AS-AUD-006-AC07` | — | **T059** | `NEW_TASK_REQUIRED` | External confirmations and alternative procedures |
| AS-AUD-006 | `AS-AUD-006-AC08` | — | **T059** | `NEW_TASK_REQUIRED` | External confirmations and alternative procedures |
| AS-AUD-006 | `AS-AUD-006-AC09` | — | **T059** | `NEW_TASK_REQUIRED` | External confirmations and alternative procedures |
| AS-AUD-007 | `AS-AUD-007-AC01` | `AWP-01-01` | **T060** | `NEW_TASK_REQUIRED` | Audit planning, materiality and risk assessment (Section 1) |
| AS-AUD-007 | `AS-AUD-007-AC02` | `AWP-01-02` | **T060** | `NEW_TASK_REQUIRED` | Audit planning, materiality and risk assessment (Section 1) |
| AS-AUD-007 | `AS-AUD-007-AC03` | `AWP-01-03` | **T060** | `NEW_TASK_REQUIRED` | Audit planning, materiality and risk assessment (Section 1) |
| AS-AUD-007 | `AS-AUD-007-AC04` | `AWP-01-04` | **T060** | `NEW_TASK_REQUIRED` | Audit planning, materiality and risk assessment (Section 1) |
| AS-AUD-007 | `AS-AUD-007-AC05` | `AWP-01-05` | **T060** | `NEW_TASK_REQUIRED` | Audit planning, materiality and risk assessment (Section 1) |
| AS-AUD-007 | `AS-AUD-007-AC06` | `AWP-01-06` | **T060** | `NEW_TASK_REQUIRED` | Audit planning, materiality and risk assessment (Section 1) |
| AS-AUD-007 | `AS-AUD-007-AC07` | `AWP-01-07` | **T060** | `NEW_TASK_REQUIRED` | Audit planning, materiality and risk assessment (Section 1) |
| AS-AUD-007 | `AS-AUD-007-AC08` | `AWP-01-08` | **T060** | `NEW_TASK_REQUIRED` | Audit planning, materiality and risk assessment (Section 1) |
| AS-AUD-007 | `AS-AUD-007-AC09` | `AWP-01-09` | **T060** | `NEW_TASK_REQUIRED` | Audit planning, materiality and risk assessment (Section 1) |
| AS-AUD-007 | `AS-AUD-007-AC10` | — | **T060** | `NEW_TASK_REQUIRED` | Audit planning, materiality and risk assessment (Section 1) |
| AS-AUD-008 | `AS-AUD-008-AC01` | `AWP-02-01` | **T061** | `NEW_TASK_REQUIRED` | Cash & Bank audit workpapers and reconciliations (Section 2) |
| AS-AUD-008 | `AS-AUD-008-AC02` | `AWP-02-02` | **T061** | `NEW_TASK_REQUIRED` | Cash & Bank audit workpapers and reconciliations (Section 2) |
| AS-AUD-008 | `AS-AUD-008-AC03` | `AWP-02-03` | **T061** | `NEW_TASK_REQUIRED` | Cash & Bank audit workpapers and reconciliations (Section 2) |
| AS-AUD-008 | `AS-AUD-008-AC04` | `AWP-02-04` | **T061** | `NEW_TASK_REQUIRED` | Cash & Bank audit workpapers and reconciliations (Section 2) |
| AS-AUD-008 | `AS-AUD-008-AC05` | `AWP-02-05` | **T061** | `NEW_TASK_REQUIRED` | Cash & Bank audit workpapers and reconciliations (Section 2) |
| AS-AUD-008 | `AS-AUD-008-AC06` | `AWP-02-06` | **T061** | `NEW_TASK_REQUIRED` | Cash & Bank audit workpapers and reconciliations (Section 2) |
| AS-AUD-008 | `AS-AUD-008-AC07` | `AWP-02-07` | **T061** | `NEW_TASK_REQUIRED` | Cash & Bank audit workpapers and reconciliations (Section 2) |
| AS-AUD-008 | `AS-AUD-008-AC08` | `AWP-02-08` | **T061** | `NEW_TASK_REQUIRED` | Cash & Bank audit workpapers and reconciliations (Section 2) |
| AS-AUD-008 | `AS-AUD-008-AC09` | — | **T061** | `NEW_TASK_REQUIRED` | Cash & Bank audit workpapers and reconciliations (Section 2) |
| AS-AUD-009 | `AS-AUD-009-AC01` | `AWP-03-01` | **T062** | `NEW_TASK_REQUIRED` | Trade receivables ageing, confirmations & cut-off (Section 3) |
| AS-AUD-009 | `AS-AUD-009-AC02` | `AWP-03-02` | **T062** | `NEW_TASK_REQUIRED` | Trade receivables ageing, confirmations & cut-off (Section 3) |
| AS-AUD-009 | `AS-AUD-009-AC03` | `AWP-03-03` | **T062** | `NEW_TASK_REQUIRED` | Trade receivables ageing, confirmations & cut-off (Section 3) |
| AS-AUD-009 | `AS-AUD-009-AC04` | `AWP-03-04` | **T062** | `NEW_TASK_REQUIRED` | Trade receivables ageing, confirmations & cut-off (Section 3) |
| AS-AUD-009 | `AS-AUD-009-AC05` | `AWP-03-05` | **T062** | `NEW_TASK_REQUIRED` | Trade receivables ageing, confirmations & cut-off (Section 3) |
| AS-AUD-009 | `AS-AUD-009-AC06` | `AWP-03-06` | **T062** | `NEW_TASK_REQUIRED` | Trade receivables ageing, confirmations & cut-off (Section 3) |
| AS-AUD-009 | `AS-AUD-009-AC07` | `AWP-03-07` | **T062** | `NEW_TASK_REQUIRED` | Trade receivables ageing, confirmations & cut-off (Section 3) |
| AS-AUD-009 | `AS-AUD-009-AC08` | `AWP-03-09` | **T062** | `NEW_TASK_REQUIRED` | Trade receivables ageing, confirmations & cut-off (Section 3) |
| AS-AUD-009 | `AS-AUD-009-AC09` | — | **T062** | `NEW_TASK_REQUIRED` | Trade receivables ageing, confirmations & cut-off (Section 3) |
| AS-AUD-010 | `AS-AUD-010-AC01` | `AWP-03-08` | **T062** | `NEW_TASK_REQUIRED` | Trade receivables ECL & allowance adequacy review (Section 3) |
| AS-AUD-010 | `AS-AUD-010-AC02` | — | **T062** | `NEW_TASK_REQUIRED` | Trade receivables ECL & allowance adequacy review (Section 3) |
| AS-AUD-010 | `AS-AUD-010-AC03` | — | **T062** | `NEW_TASK_REQUIRED` | Trade receivables ECL & allowance adequacy review (Section 3) |
| AS-AUD-010 | `AS-AUD-010-AC04` | — | **T062** | `NEW_TASK_REQUIRED` | Trade receivables ECL & allowance adequacy review (Section 3) |
| AS-AUD-010 | `AS-AUD-010-AC05` | — | **T062** | `NEW_TASK_REQUIRED` | Trade receivables ECL & allowance adequacy review (Section 3) |
| AS-AUD-010 | `AS-AUD-010-AC06` | — | **T062** | `NEW_TASK_REQUIRED` | Trade receivables ECL & allowance adequacy review (Section 3) |
| AS-AUD-010 | `AS-AUD-010-AC07` | — | **T062** | `NEW_TASK_REQUIRED` | Trade receivables ECL & allowance adequacy review (Section 3) |
| AS-AUD-011 | `AS-AUD-011-AC01` | `AWP-04-01` | **T063** | `NEW_TASK_REQUIRED` | Inventory count, costing, condition and NRV (Section 4) |
| AS-AUD-011 | `AS-AUD-011-AC02` | `AWP-04-02` | **T063** | `NEW_TASK_REQUIRED` | Inventory count, costing, condition and NRV (Section 4) |
| AS-AUD-011 | `AS-AUD-011-AC03` | `AWP-04-03` | **T063** | `NEW_TASK_REQUIRED` | Inventory count, costing, condition and NRV (Section 4) |
| AS-AUD-011 | `AS-AUD-011-AC04` | `AWP-04-04` | **T063** | `NEW_TASK_REQUIRED` | Inventory count, costing, condition and NRV (Section 4) |
| AS-AUD-011 | `AS-AUD-011-AC05` | `AWP-04-05` | **T063** | `NEW_TASK_REQUIRED` | Inventory count, costing, condition and NRV (Section 4) |
| AS-AUD-011 | `AS-AUD-011-AC06` | `AWP-04-06` | **T063** | `NEW_TASK_REQUIRED` | Inventory count, costing, condition and NRV (Section 4) |
| AS-AUD-011 | `AS-AUD-011-AC07` | `AWP-04-07` | **T063** | `NEW_TASK_REQUIRED` | Inventory count, costing, condition and NRV (Section 4) |
| AS-AUD-011 | `AS-AUD-011-AC08` | `AWP-04-08` | **T063** | `NEW_TASK_REQUIRED` | Inventory count, costing, condition and NRV (Section 4) |
| AS-AUD-011 | `AS-AUD-011-AC09` | — | **T063** | `NEW_TASK_REQUIRED` | Inventory count, costing, condition and NRV (Section 4) |
| AS-AUD-012 | `AS-AUD-012-AC01` | `AWP-05-01` | **T064** | `NEW_TASK_REQUIRED` | Revenue testing and sales cut-off review (Section 5) |
| AS-AUD-012 | `AS-AUD-012-AC02` | `AWP-05-02` | **T064** | `NEW_TASK_REQUIRED` | Revenue testing and sales cut-off review (Section 5) |
| AS-AUD-012 | `AS-AUD-012-AC03` | `AWP-05-03` | **T064** | `NEW_TASK_REQUIRED` | Revenue testing and sales cut-off review (Section 5) |
| AS-AUD-012 | `AS-AUD-012-AC04` | `AWP-05-04` | **T064** | `NEW_TASK_REQUIRED` | Revenue testing and sales cut-off review (Section 5) |
| AS-AUD-012 | `AS-AUD-012-AC05` | `AWP-05-05` | **T064** | `NEW_TASK_REQUIRED` | Revenue testing and sales cut-off review (Section 5) |
| AS-AUD-012 | `AS-AUD-012-AC06` | `AWP-05-06` | **T064** | `NEW_TASK_REQUIRED` | Revenue testing and sales cut-off review (Section 5) |
| AS-AUD-012 | `AS-AUD-012-AC07` | `AWP-05-07` | **T064** | `NEW_TASK_REQUIRED` | Revenue testing and sales cut-off review (Section 5) |
| AS-AUD-012 | `AS-AUD-012-AC08` | `AWP-05-08` | **T064** | `NEW_TASK_REQUIRED` | Revenue testing and sales cut-off review (Section 5) |
| AS-AUD-013 | `AS-AUD-013-AC01` | `AWP-06-01` | **T065** | `NEW_TASK_REQUIRED` | Purchases, payables & unrecorded liabilities (Section 6) |
| AS-AUD-013 | `AS-AUD-013-AC02` | `AWP-06-02` | **T065** | `NEW_TASK_REQUIRED` | Purchases, payables & unrecorded liabilities (Section 6) |
| AS-AUD-013 | `AS-AUD-013-AC03` | `AWP-06-03` | **T065** | `NEW_TASK_REQUIRED` | Purchases, payables & unrecorded liabilities (Section 6) |
| AS-AUD-013 | `AS-AUD-013-AC04` | `AWP-06-04` | **T065** | `NEW_TASK_REQUIRED` | Purchases, payables & unrecorded liabilities (Section 6) |
| AS-AUD-013 | `AS-AUD-013-AC05` | `AWP-06-05` | **T065** | `NEW_TASK_REQUIRED` | Purchases, payables & unrecorded liabilities (Section 6) |
| AS-AUD-013 | `AS-AUD-013-AC06` | `AWP-06-06` | **T065** | `NEW_TASK_REQUIRED` | Purchases, payables & unrecorded liabilities (Section 6) |
| AS-AUD-013 | `AS-AUD-013-AC07` | `AWP-06-07` | **T065** | `NEW_TASK_REQUIRED` | Purchases, payables & unrecorded liabilities (Section 6) |
| AS-AUD-013 | `AS-AUD-013-AC08` | `AWP-06-08` | **T065** | `NEW_TASK_REQUIRED` | Purchases, payables & unrecorded liabilities (Section 6) |
| AS-AUD-013 | `AS-AUD-013-AC09` | — | **T065** | `NEW_TASK_REQUIRED` | Purchases, payables & unrecorded liabilities (Section 6) |
| AS-AUD-014 | `AS-AUD-014-AC01` | `AWP-07-01` | **T066** | `NEW_TASK_REQUIRED` | Fixed assets roll-forward and depreciation testing (Section 7) |
| AS-AUD-014 | `AS-AUD-014-AC02` | `AWP-07-02` | **T066** | `NEW_TASK_REQUIRED` | Fixed assets roll-forward and depreciation testing (Section 7) |
| AS-AUD-014 | `AS-AUD-014-AC03` | `AWP-07-03` | **T066** | `NEW_TASK_REQUIRED` | Fixed assets roll-forward and depreciation testing (Section 7) |
| AS-AUD-014 | `AS-AUD-014-AC04` | `AWP-07-04` | **T066** | `NEW_TASK_REQUIRED` | Fixed assets roll-forward and depreciation testing (Section 7) |
| AS-AUD-014 | `AS-AUD-014-AC05` | `AWP-07-05` | **T066** | `NEW_TASK_REQUIRED` | Fixed assets roll-forward and depreciation testing (Section 7) |
| AS-AUD-014 | `AS-AUD-014-AC06` | `AWP-07-06` | **T066** | `NEW_TASK_REQUIRED` | Fixed assets roll-forward and depreciation testing (Section 7) |
| AS-AUD-014 | `AS-AUD-014-AC07` | `AWP-07-07` | **T066** | `NEW_TASK_REQUIRED` | Fixed assets roll-forward and depreciation testing (Section 7) |
| AS-AUD-014 | `AS-AUD-014-AC08` | `AWP-07-08` | **T066** | `NEW_TASK_REQUIRED` | Fixed assets roll-forward and depreciation testing (Section 7) |
| AS-AUD-014 | `AS-AUD-014-AC09` | `AWP-07-09` | **T066** | `NEW_TASK_REQUIRED` | Fixed assets roll-forward and depreciation testing (Section 7) |
| AS-AUD-015 | `AS-AUD-015-AC01` | `AWP-08-01` | **T067** | `NEW_TASK_REQUIRED` | Expenses vouching, classification and cut-off (Section 8) |
| AS-AUD-015 | `AS-AUD-015-AC02` | `AWP-08-02` | **T067** | `NEW_TASK_REQUIRED` | Expenses vouching, classification and cut-off (Section 8) |
| AS-AUD-015 | `AS-AUD-015-AC03` | `AWP-08-03` | **T067** | `NEW_TASK_REQUIRED` | Expenses vouching, classification and cut-off (Section 8) |
| AS-AUD-015 | `AS-AUD-015-AC04` | `AWP-08-04` | **T067** | `NEW_TASK_REQUIRED` | Expenses vouching, classification and cut-off (Section 8) |
| AS-AUD-015 | `AS-AUD-015-AC05` | `AWP-08-05` | **T067** | `NEW_TASK_REQUIRED` | Expenses vouching, classification and cut-off (Section 8) |
| AS-AUD-015 | `AS-AUD-015-AC06` | `AWP-08-06` | **T067** | `NEW_TASK_REQUIRED` | Expenses vouching, classification and cut-off (Section 8) |
| AS-AUD-015 | `AS-AUD-015-AC07` | `AWP-08-07` | **T067** | `NEW_TASK_REQUIRED` | Expenses vouching, classification and cut-off (Section 8) |
| AS-AUD-015 | `AS-AUD-015-AC08` | `AWP-08-08` | **T067** | `NEW_TASK_REQUIRED` | Expenses vouching, classification and cut-off (Section 8) |
| AS-AUD-015 | `AS-AUD-015-AC09` | `AWP-08-09` | **T067** | `NEW_TASK_REQUIRED` | Expenses vouching, classification and cut-off (Section 8) |
| AS-AUD-016 | `AS-AUD-016-AC01` | `AWP-09-01` | **T068** | `NEW_TASK_REQUIRED` | Payroll audit workpapers and recalculation (Section 9) |
| AS-AUD-016 | `AS-AUD-016-AC02` | `AWP-09-02` | **T068** | `NEW_TASK_REQUIRED` | Payroll audit workpapers and recalculation (Section 9) |
| AS-AUD-016 | `AS-AUD-016-AC03` | `AWP-09-03` | **T068** | `NEW_TASK_REQUIRED` | Payroll audit workpapers and recalculation (Section 9) |
| AS-AUD-016 | `AS-AUD-016-AC04` | `AWP-09-04` | **T068** | `NEW_TASK_REQUIRED` | Payroll audit workpapers and recalculation (Section 9) |
| AS-AUD-016 | `AS-AUD-016-AC05` | `AWP-09-05` | **T068** | `NEW_TASK_REQUIRED` | Payroll audit workpapers and recalculation (Section 9) |
| AS-AUD-016 | `AS-AUD-016-AC06` | `AWP-09-06` | **T068** | `NEW_TASK_REQUIRED` | Payroll audit workpapers and recalculation (Section 9) |
| AS-AUD-016 | `AS-AUD-016-AC07` | `AWP-09-07` | **T068** | `NEW_TASK_REQUIRED` | Payroll audit workpapers and recalculation (Section 9) |
| AS-AUD-016 | `AS-AUD-016-AC08` | `AWP-09-08` | **T068** | `NEW_TASK_REQUIRED` | Payroll audit workpapers and recalculation (Section 9) |
| AS-AUD-016 | `AS-AUD-016-AC09` | — | **T068** | `NEW_TASK_REQUIRED` | Payroll audit workpapers and recalculation (Section 9) |
| AS-AUD-017 | `AS-AUD-017-AC01` | `AWP-10-01` | **T069** | `NEW_TASK_REQUIRED` | Loans, borrowings and covenant testing (Section 10) |
| AS-AUD-017 | `AS-AUD-017-AC02` | `AWP-10-02` | **T069** | `NEW_TASK_REQUIRED` | Loans, borrowings and covenant testing (Section 10) |
| AS-AUD-017 | `AS-AUD-017-AC03` | `AWP-10-03` | **T069** | `NEW_TASK_REQUIRED` | Loans, borrowings and covenant testing (Section 10) |
| AS-AUD-017 | `AS-AUD-017-AC04` | `AWP-10-04` | **T069** | `NEW_TASK_REQUIRED` | Loans, borrowings and covenant testing (Section 10) |
| AS-AUD-017 | `AS-AUD-017-AC05` | `AWP-10-05` | **T069** | `NEW_TASK_REQUIRED` | Loans, borrowings and covenant testing (Section 10) |
| AS-AUD-017 | `AS-AUD-017-AC06` | `AWP-10-06` | **T069** | `NEW_TASK_REQUIRED` | Loans, borrowings and covenant testing (Section 10) |
| AS-AUD-017 | `AS-AUD-017-AC07` | `AWP-10-07` | **T069** | `NEW_TASK_REQUIRED` | Loans, borrowings and covenant testing (Section 10) |
| AS-AUD-017 | `AS-AUD-017-AC08` | `AWP-10-08` | **T069** | `NEW_TASK_REQUIRED` | Loans, borrowings and covenant testing (Section 10) |
| AS-AUD-017 | `AS-AUD-017-AC09` | `AWP-10-09` | **T069** | `NEW_TASK_REQUIRED` | Loans, borrowings and covenant testing (Section 10) |
| AS-AUD-018 | `AS-AUD-018-AC01` | `AWP-11-01` | **T031** | `MERGE_EXISTING` | Consolidated into equity movement schedule task (Section 11) |
| AS-AUD-018 | `AS-AUD-018-AC02` | `AWP-11-02` | **T031** | `MERGE_EXISTING` | Consolidated into equity movement schedule task (Section 11) |
| AS-AUD-018 | `AS-AUD-018-AC03` | `AWP-11-03` | **T031** | `MERGE_EXISTING` | Consolidated into equity movement schedule task (Section 11) |
| AS-AUD-018 | `AS-AUD-018-AC04` | `AWP-11-04` | **T031** | `MERGE_EXISTING` | Consolidated into equity movement schedule task (Section 11) |
| AS-AUD-018 | `AS-AUD-018-AC05` | `AWP-11-05` | **T031** | `MERGE_EXISTING` | Consolidated into equity movement schedule task (Section 11) |
| AS-AUD-018 | `AS-AUD-018-AC06` | `AWP-11-06` | **T031** | `MERGE_EXISTING` | Consolidated into equity movement schedule task (Section 11) |
| AS-AUD-018 | `AS-AUD-018-AC07` | `AWP-11-07` | **T031** | `MERGE_EXISTING` | Consolidated into equity movement schedule task (Section 11) |
| AS-AUD-019 | `AS-AUD-019-AC01` | `AWP-12-01` | **T070** | `NEW_TASK_REQUIRED` | Related parties register and transaction testing (Section 12) |
| AS-AUD-019 | `AS-AUD-019-AC02` | `AWP-12-02` | **T070** | `NEW_TASK_REQUIRED` | Related parties register and transaction testing (Section 12) |
| AS-AUD-019 | `AS-AUD-019-AC03` | `AWP-12-03` | **T070** | `NEW_TASK_REQUIRED` | Related parties register and transaction testing (Section 12) |
| AS-AUD-019 | `AS-AUD-019-AC04` | `AWP-12-04` | **T070** | `NEW_TASK_REQUIRED` | Related parties register and transaction testing (Section 12) |
| AS-AUD-019 | `AS-AUD-019-AC05` | `AWP-12-05` | **T070** | `NEW_TASK_REQUIRED` | Related parties register and transaction testing (Section 12) |
| AS-AUD-019 | `AS-AUD-019-AC06` | `AWP-12-06` | **T070** | `NEW_TASK_REQUIRED` | Related parties register and transaction testing (Section 12) |
| AS-AUD-019 | `AS-AUD-019-AC07` | `AWP-12-07` | **T070** | `NEW_TASK_REQUIRED` | Related parties register and transaction testing (Section 12) |
| AS-AUD-019 | `AS-AUD-019-AC08` | — | **T070** | `NEW_TASK_REQUIRED` | Related parties register and transaction testing (Section 12) |
| AS-AUD-020 | `AS-AUD-020-AC01` | `AWP-13-01` | **T071** | `NEW_TASK_REQUIRED` | Tax & statutory liabilities audit workpapers (Section 13) |
| AS-AUD-020 | `AS-AUD-020-AC02` | `AWP-13-02` | **T071** | `NEW_TASK_REQUIRED` | Tax & statutory liabilities audit workpapers (Section 13) |
| AS-AUD-020 | `AS-AUD-020-AC03` | `AWP-13-03` | **T071** | `NEW_TASK_REQUIRED` | Tax & statutory liabilities audit workpapers (Section 13) |
| AS-AUD-020 | `AS-AUD-020-AC04` | `AWP-13-04` | **T071** | `NEW_TASK_REQUIRED` | Tax & statutory liabilities audit workpapers (Section 13) |
| AS-AUD-020 | `AS-AUD-020-AC05` | `AWP-13-05` | **T071** | `NEW_TASK_REQUIRED` | Tax & statutory liabilities audit workpapers (Section 13) |
| AS-AUD-020 | `AS-AUD-020-AC06` | `AWP-13-06` | **T071** | `NEW_TASK_REQUIRED` | Tax & statutory liabilities audit workpapers (Section 13) |
| AS-AUD-020 | `AS-AUD-020-AC07` | `AWP-13-07` | **T071** | `NEW_TASK_REQUIRED` | Tax & statutory liabilities audit workpapers (Section 13) |
| AS-AUD-021 | `AS-AUD-021-AC01` | `AWP-14-01` | **T072** | `NEW_TASK_REQUIRED` | Journal entries and fraud risk testing (Section 14) |
| AS-AUD-021 | `AS-AUD-021-AC02` | `AWP-14-02` | **T072** | `NEW_TASK_REQUIRED` | Journal entries and fraud risk testing (Section 14) |
| AS-AUD-021 | `AS-AUD-021-AC03` | `AWP-14-03` | **T072** | `NEW_TASK_REQUIRED` | Journal entries and fraud risk testing (Section 14) |
| AS-AUD-021 | `AS-AUD-021-AC04` | `AWP-14-04` | **T072** | `NEW_TASK_REQUIRED` | Journal entries and fraud risk testing (Section 14) |
| AS-AUD-021 | `AS-AUD-021-AC05` | `AWP-14-05` | **T072** | `NEW_TASK_REQUIRED` | Journal entries and fraud risk testing (Section 14) |
| AS-AUD-021 | `AS-AUD-021-AC06` | `AWP-14-06` | **T072** | `NEW_TASK_REQUIRED` | Journal entries and fraud risk testing (Section 14) |
| AS-AUD-021 | `AS-AUD-021-AC07` | `AWP-14-07` | **T072** | `NEW_TASK_REQUIRED` | Journal entries and fraud risk testing (Section 14) |
| AS-AUD-021 | `AS-AUD-021-AC08` | `AWP-14-08` | **T072** | `NEW_TASK_REQUIRED` | Journal entries and fraud risk testing (Section 14) |
| AS-AUD-021 | `AS-AUD-021-AC09` | — | **T072** | `NEW_TASK_REQUIRED` | Journal entries and fraud risk testing (Section 14) |
| AS-AUD-022 | `AS-AUD-022-AC01` | `AWP-15-01` | **T073** | `NEW_TASK_REQUIRED` | Substantive and final analytical review (Section 15) |
| AS-AUD-022 | `AS-AUD-022-AC02` | `AWP-15-02` | **T073** | `NEW_TASK_REQUIRED` | Substantive and final analytical review (Section 15) |
| AS-AUD-022 | `AS-AUD-022-AC03` | `AWP-15-03` | **T073** | `NEW_TASK_REQUIRED` | Substantive and final analytical review (Section 15) |
| AS-AUD-022 | `AS-AUD-022-AC04` | `AWP-15-04` | **T073** | `NEW_TASK_REQUIRED` | Substantive and final analytical review (Section 15) |
| AS-AUD-022 | `AS-AUD-022-AC05` | `AWP-15-05` | **T073** | `NEW_TASK_REQUIRED` | Substantive and final analytical review (Section 15) |
| AS-AUD-022 | `AS-AUD-022-AC06` | `AWP-15-06` | **T073** | `NEW_TASK_REQUIRED` | Substantive and final analytical review (Section 15) |
| AS-AUD-022 | `AS-AUD-022-AC07` | `AWP-15-07` | **T073** | `NEW_TASK_REQUIRED` | Substantive and final analytical review (Section 15) |
| AS-AUD-022 | `AS-AUD-022-AC08` | `AWP-15-08` | **T073** | `NEW_TASK_REQUIRED` | Substantive and final analytical review (Section 15) |
| AS-AUD-022 | `AS-AUD-022-AC09` | — | **T073** | `NEW_TASK_REQUIRED` | Substantive and final analytical review (Section 15) |
| AS-AUD-022 | `AS-AUD-022-AC10` | — | **T073** | `NEW_TASK_REQUIRED` | Substantive and final analytical review (Section 15) |
| AS-AUD-023 | `AS-AUD-023-AC01` | `AWP-16-01` | **T074** | `NEW_TASK_REQUIRED` | Going concern audit evaluation and forecast review (Section 16) |
| AS-AUD-023 | `AS-AUD-023-AC02` | `AWP-16-02` | **T074** | `NEW_TASK_REQUIRED` | Going concern audit evaluation and forecast review (Section 16) |
| AS-AUD-023 | `AS-AUD-023-AC03` | `AWP-16-03` | **T074** | `NEW_TASK_REQUIRED` | Going concern audit evaluation and forecast review (Section 16) |
| AS-AUD-023 | `AS-AUD-023-AC04` | `AWP-16-04` | **T074** | `NEW_TASK_REQUIRED` | Going concern audit evaluation and forecast review (Section 16) |
| AS-AUD-023 | `AS-AUD-023-AC05` | `AWP-16-05` | **T074** | `NEW_TASK_REQUIRED` | Going concern audit evaluation and forecast review (Section 16) |
| AS-AUD-023 | `AS-AUD-023-AC06` | `AWP-16-06` | **T074** | `NEW_TASK_REQUIRED` | Going concern audit evaluation and forecast review (Section 16) |
| AS-AUD-023 | `AS-AUD-023-AC07` | `AWP-16-07` | **T074** | `NEW_TASK_REQUIRED` | Going concern audit evaluation and forecast review (Section 16) |
| AS-AUD-023 | `AS-AUD-023-AC08` | `AWP-16-08` | **T074** | `NEW_TASK_REQUIRED` | Going concern audit evaluation and forecast review (Section 16) |
| AS-AUD-023 | `AS-AUD-023-AC09` | `AWP-16-09` | **T074** | `NEW_TASK_REQUIRED` | Going concern audit evaluation and forecast review (Section 16) |
| AS-AUD-023 | `AS-AUD-023-AC10` | — | **T074** | `NEW_TASK_REQUIRED` | Going concern audit evaluation and forecast review (Section 16) |
| AS-AUD-024 | `AS-AUD-024-AC01` | `AWP-17-01` | **T075** | `NEW_TASK_REQUIRED` | Subsequent events register and reviewed-through-date (Section 17) |
| AS-AUD-024 | `AS-AUD-024-AC02` | `AWP-17-02` | **T075** | `NEW_TASK_REQUIRED` | Subsequent events register and reviewed-through-date (Section 17) |
| AS-AUD-024 | `AS-AUD-024-AC03` | `AWP-17-03` | **T075** | `NEW_TASK_REQUIRED` | Subsequent events register and reviewed-through-date (Section 17) |
| AS-AUD-024 | `AS-AUD-024-AC04` | `AWP-17-04` | **T075** | `NEW_TASK_REQUIRED` | Subsequent events register and reviewed-through-date (Section 17) |
| AS-AUD-024 | `AS-AUD-024-AC05` | `AWP-17-05` | **T075** | `NEW_TASK_REQUIRED` | Subsequent events register and reviewed-through-date (Section 17) |
| AS-AUD-024 | `AS-AUD-024-AC06` | `AWP-17-06` | **T075** | `NEW_TASK_REQUIRED` | Subsequent events register and reviewed-through-date (Section 17) |
| AS-AUD-024 | `AS-AUD-024-AC07` | `AWP-17-07` | **T075** | `NEW_TASK_REQUIRED` | Subsequent events register and reviewed-through-date (Section 17) |
| AS-AUD-024 | `AS-AUD-024-AC08` | `AWP-17-08` | **T075** | `NEW_TASK_REQUIRED` | Subsequent events register and reviewed-through-date (Section 17) |
| AS-AUD-024 | `AS-AUD-024-AC09` | — | **T075** | `NEW_TASK_REQUIRED` | Subsequent events register and reviewed-through-date (Section 17) |
| AS-AUD-024 | `AS-AUD-024-AC10` | — | **T075** | `NEW_TASK_REQUIRED` | Subsequent events register and reviewed-through-date (Section 17) |
| AS-AUD-025 | `AS-AUD-025-AC01` | `AWP-18-01` | **T033** | `MERGE_EXISTING` | Consolidated into statement review and tie-out lifecycle |
| AS-AUD-025 | `AS-AUD-025-AC02` | `AWP-18-02` | **T033** | `MERGE_EXISTING` | Consolidated into statement review and tie-out lifecycle |
| AS-AUD-025 | `AS-AUD-025-AC03` | `AWP-18-03` | **T033** | `MERGE_EXISTING` | Consolidated into statement review and tie-out lifecycle |
| AS-AUD-025 | `AS-AUD-025-AC04` | `AWP-18-04` | **T032** | `MERGE_EXISTING` | Consolidated into disclosure notes applicability and checklist review |
| AS-AUD-025 | `AS-AUD-025-AC05` | `AWP-18-05` | **T033** | `MERGE_EXISTING` | Consolidated into statement review and tie-out lifecycle |
| AS-AUD-025 | `AS-AUD-025-AC06` | `AWP-18-06` | **T032** | `MERGE_EXISTING` | Consolidated into disclosure notes applicability and checklist review |
| AS-AUD-025 | `AS-AUD-025-AC07` | `AWP-18-07` | **T033** | `MERGE_EXISTING` | Consolidated into statement review and tie-out lifecycle |
| AS-AUD-025 | `AS-AUD-025-AC08` | `AWP-18-08` | **T032** | `MERGE_EXISTING` | Consolidated into disclosure notes applicability and checklist review |
| AS-AUD-025 | `AS-AUD-025-AC09` | `AWP-18-09` | **T032** | `MERGE_EXISTING` | Consolidated into disclosure notes applicability and checklist review |
| AS-AUD-025 | `AS-AUD-025-AC10` | — | **T032** | `MERGE_EXISTING` | Consolidated into disclosure notes applicability and checklist review |
| AS-AUD-025 | `AS-AUD-025-AC11` | — | **T032** | `MERGE_EXISTING` | Consolidated into disclosure notes applicability and checklist review |
| AS-AUD-026 | `AS-AUD-026-AC01` | `AWP-19-01` | **T022** | `MERGE_EXISTING` | Consolidated into adjustment drafts, decisions and SAD schedule |
| AS-AUD-026 | `AS-AUD-026-AC02` | `AWP-19-02` | **T022** | `MERGE_EXISTING` | Consolidated into adjustment drafts, decisions and SAD schedule |
| AS-AUD-026 | `AS-AUD-026-AC03` | `AWP-19-03` | **T022** | `MERGE_EXISTING` | Consolidated into adjustment drafts, decisions and SAD schedule |
| AS-AUD-026 | `AS-AUD-026-AC04` | `AWP-19-04` | **T022** | `MERGE_EXISTING` | Consolidated into adjustment drafts, decisions and SAD schedule |
| AS-AUD-026 | `AS-AUD-026-AC05` | `AWP-19-05` | **T022** | `MERGE_EXISTING` | Consolidated into adjustment drafts, decisions and SAD schedule |
| AS-AUD-026 | `AS-AUD-026-AC06` | `AWP-19-06` | **T022** | `MERGE_EXISTING` | Consolidated into adjustment drafts, decisions and SAD schedule |
| AS-AUD-026 | `AS-AUD-026-AC07` | `AWP-19-07` | **T023** | `MERGE_EXISTING` | Consolidated into reflection plans and final adjusted TB posting verification |
| AS-AUD-026 | `AS-AUD-026-AC08` | — | **T023** | `MERGE_EXISTING` | Consolidated into reflection treatment and double-counting prevention |
| AS-AUD-026 | `AS-AUD-026-AC09` | — | **T023** | `MERGE_EXISTING` | Consolidated into reflection plans, adjusted TB tie-out and partner sign-off |
| AS-AUD-026 | `AS-AUD-026-AC10` | — | **T023** | `MERGE_EXISTING` | Consolidated into reflection plans, adjusted TB tie-out and partner sign-off |
| AS-AUD-026 | `AS-AUD-026-AC11` | — | **T023** | `MERGE_EXISTING` | Consolidated into reflection plans, adjusted TB tie-out and partner sign-off |
| AS-AUD-026 | `AS-AUD-026-AC12` | — | **T022** | `MERGE_EXISTING` | Consolidated into uncorrected differences and reporting consequence evaluation |
| AS-AUD-026 | `AS-AUD-026-AC13` | — | **T022** | `MERGE_EXISTING` | Consolidated into sample projection and difference evaluation |
| AS-AUD-027 | `AS-AUD-027-AC01` | `AWP-20-01` | **T040** | `MERGE_EXISTING` | Consolidated into partner review, rep letter, opinion formulation & archive release |
| AS-AUD-027 | `AS-AUD-027-AC02` | `AWP-20-02` | **T040** | `MERGE_EXISTING` | Consolidated into partner review, rep letter, opinion formulation & archive release |
| AS-AUD-027 | `AS-AUD-027-AC03` | `AWP-20-03` | **T038** | `MERGE_EXISTING` | Consolidated into close readiness, program completion & ReviewPoint clearance |
| AS-AUD-027 | `AS-AUD-027-AC04` | `AWP-20-04` | **T038** | `MERGE_EXISTING` | Consolidated into close readiness, program completion & ReviewPoint clearance |
| AS-AUD-027 | `AS-AUD-027-AC05` | `AWP-20-05` | **T040** | `MERGE_EXISTING` | Consolidated into partner review, rep letter, opinion formulation & archive release |
| AS-AUD-027 | `AS-AUD-027-AC06` | `AWP-20-06` | **T037** | `MERGE_EXISTING` | Consolidated into package readiness and draft report link |
| AS-AUD-027 | `AS-AUD-027-AC07` | `AWP-20-07` | **T040** | `MERGE_EXISTING` | Consolidated into partner review, rep letter, opinion formulation & archive release |
| AS-AUD-027 | `AS-AUD-027-AC08` | `AWP-20-08` | **T037** | `MERGE_EXISTING` | Consolidated into package readiness and draft report link |
| AS-AUD-027 | `AS-AUD-027-AC09` | `AWP-20-09` | **T040** | `MERGE_EXISTING` | Consolidated into partner review, rep letter, opinion formulation & archive release |
| AS-AUD-027 | `AS-AUD-027-AC10` | `AWP-20-10` | **T040** | `MERGE_EXISTING` | Consolidated into partner review, rep letter, opinion formulation & archive release |
| AS-AUD-027 | `AS-AUD-027-AC11` | — | **T040** | `MERGE_EXISTING` | Consolidated into partner review, rep letter, opinion formulation & archive release |
| AS-AUD-027 | `AS-AUD-027-AC12` | — | **T040** | `MERGE_EXISTING` | Consolidated into partner review, rep letter, opinion formulation & archive release |
| AS-AUD-027 | `AS-AUD-027-AC13` | — | **T040** | `MERGE_EXISTING` | Consolidated into partner review, rep letter, opinion formulation & archive release |
| AS-AUD-027 | `AS-AUD-027-AC14` | — | **T040** | `BLOCKED_EXTERNAL` | Requires human partner sign-off and report authorization |
| AS-AUD-028 | `AS-AUD-028-AC01` | — | **T051** | `MERGE_EXISTING` | 20-section coverage verification consolidated into release reconciliation |
| AS-AUD-028 | `AS-AUD-028-AC02` | — | **T051** | `MERGE_EXISTING` | 165-procedure completion proof consolidated into release reconciliation |
| AS-AUD-028 | `AS-AUD-028-AC03` | — | **T048** | `MERGE_EXISTING` | Stale evidence & dependency invalidation consolidated into authority replay |
| AS-AUD-028 | `AS-AUD-028-AC04` | — | **T048** | `MERGE_EXISTING` | Concurrency & multi-user review consolidated into concurrency verification |
| AS-AUD-028 | `AS-AUD-028-AC05` | — | **T048** | `MERGE_EXISTING` | Role-based authorization & scope fences consolidated into authority replay |
| AS-AUD-028 | `AS-AUD-028-AC06` | — | **T049** | `MERGE_EXISTING` | Audit difference to TB to FS reconciliation consolidated into accounting journeys |
| AS-AUD-028 | `AS-AUD-028-AC07` | — | **T050** | `MERGE_EXISTING` | Bounded performance & large population handling consolidated into schema/output safety |
| AS-AUD-028 | `AS-AUD-028-AC08` | — | **T050** | `MERGE_EXISTING` | Exact archive reproducibility & checksum integrity consolidated into schema/output safety |
| AS-AUD-028 | `AS-AUD-028-AC09` | — | **T049** | `MERGE_EXISTING` | End-to-end entity audit journey consolidated into accounting journeys |
| AS-AUD-028 | `AS-AUD-028-AC10` | — | **T075** | `MERGE_EXISTING` | Final audit acceptance & verification sign-off consolidated into subsequent events & completion review |

## 3. Complete AWP source-procedure inventory (165 procedures)

| AWP ID | Section | Procedure Description | Primary Story | Primary AC | Canonical Task | Disposition |
|---|---|---|---|---|---|---|
| `AWP-01-01` | 1. Planning & Risk Assessment | Obtain company registration documents and basic company information. | AS-AUD-007 | `AS-AUD-007-AC01` | **T060** | `NEW_TASK_REQUIRED` |
| `AWP-01-02` | 1. Planning & Risk Assessment | Obtain prior-year audited financial statements and audit report. | AS-AUD-007 | `AS-AUD-007-AC02` | **T060** | `NEW_TASK_REQUIRED` |
| `AWP-01-03` | 1. Planning & Risk Assessment | Obtain current-year Trial Balance and draft financial statements. | AS-AUD-007 | `AS-AUD-007-AC03` | **T060** | `NEW_TASK_REQUIRED` |
| `AWP-01-04` | 1. Planning & Risk Assessment | Agree opening balances with prior-year audited balances. | AS-AUD-007 | `AS-AUD-007-AC04` | **T060** | `NEW_TASK_REQUIRED` |
| `AWP-01-05` | 1. Planning & Risk Assessment | Understand the nature of business, major revenue streams and accounting system. | AS-AUD-007 | `AS-AUD-007-AC05` | **T060** | `NEW_TASK_REQUIRED` |
| `AWP-01-06` | 1. Planning & Risk Assessment | Identify significant account balances and transaction classes. | AS-AUD-007 | `AS-AUD-007-AC06` | **T060** | `NEW_TASK_REQUIRED` |
| `AWP-01-07` | 1. Planning & Risk Assessment | Identify significant audit risks, fraud risks and areas involving management judgement. | AS-AUD-007 | `AS-AUD-007-AC07` | **T060** | `NEW_TASK_REQUIRED` |
| `AWP-01-08` | 1. Planning & Risk Assessment | Determine materiality and performance materiality. | AS-AUD-007 | `AS-AUD-007-AC08` | **T060** | `NEW_TASK_REQUIRED` |
| `AWP-01-09` | 1. Planning & Risk Assessment | Prepare the audit strategy and audit program. | AS-AUD-007 | `AS-AUD-007-AC09` | **T060** | `NEW_TASK_REQUIRED` |
| `AWP-02-01` | 2. Cash & Bank | Obtain bank reconciliation for all bank accounts at year-end. | AS-AUD-008 | `AS-AUD-008-AC01` | **T061** | `NEW_TASK_REQUIRED` |
| `AWP-02-02` | 2. Cash & Bank | Agree bank ledger balances with the Trial Balance. | AS-AUD-008 | `AS-AUD-008-AC02` | **T061** | `NEW_TASK_REQUIRED` |
| `AWP-02-03` | 2. Cash & Bank | Agree bank reconciliation balances with year-end bank statements. | AS-AUD-008 | `AS-AUD-008-AC03` | **T061** | `NEW_TASK_REQUIRED` |
| `AWP-02-04` | 2. Cash & Bank | Obtain direct bank confirmations and reconcile confirmed balances. | AS-AUD-008 | `AS-AUD-008-AC04` | **T061** | `NEW_TASK_REQUIRED` |
| `AWP-02-05` | 2. Cash & Bank | Check outstanding cheques, deposits and other reconciling items. | AS-AUD-008 | `AS-AUD-008-AC05` | **T061** | `NEW_TASK_REQUIRED` |
| `AWP-02-06` | 2. Cash & Bank | Investigate old or unusual outstanding items. | AS-AUD-008 | `AS-AUD-008-AC06` | **T061** | `NEW_TASK_REQUIRED` |
| `AWP-02-07` | 2. Cash & Bank | Test selected bank transactions to supporting documents. | AS-AUD-008 | `AS-AUD-008-AC07` | **T061** | `NEW_TASK_REQUIRED` |
| `AWP-02-08` | 2. Cash & Bank | Review subsequent bank statements for unusual transactions. | AS-AUD-008 | `AS-AUD-008-AC08` | **T061** | `NEW_TASK_REQUIRED` |
| `AWP-03-01` | 3. Trade Receivables | Obtain the year-end receivable ageing report. | AS-AUD-009 | `AS-AUD-009-AC01` | **T062** | `NEW_TASK_REQUIRED` |
| `AWP-03-02` | 3. Trade Receivables | Agree total receivables and ageing to the GL/TB. | AS-AUD-009 | `AS-AUD-009-AC02` | **T062** | `NEW_TASK_REQUIRED` |
| `AWP-03-03` | 3. Trade Receivables | Review significant and overdue customer balances. | AS-AUD-009 | `AS-AUD-009-AC03` | **T062** | `NEW_TASK_REQUIRED` |
| `AWP-03-04` | 3. Trade Receivables | Send customer balance confirmations and investigate differences. | AS-AUD-009 | `AS-AUD-009-AC04` | **T062** | `NEW_TASK_REQUIRED` |
| `AWP-03-05` | 3. Trade Receivables | Perform alternative procedures for non-confirmed balances. | AS-AUD-009 | `AS-AUD-009-AC05` | **T062** | `NEW_TASK_REQUIRED` |
| `AWP-03-06` | 3. Trade Receivables | Test selected sales invoices to delivery documents and ledger. | AS-AUD-009 | `AS-AUD-009-AC06` | **T062** | `NEW_TASK_REQUIRED` |
| `AWP-03-07` | 3. Trade Receivables | Check subsequent customer collections through bank statements. | AS-AUD-009 | `AS-AUD-009-AC07` | **T062** | `NEW_TASK_REQUIRED` |
| `AWP-03-08` | 3. Trade Receivables | Perform/recalculate ECL and assess adequacy of provision. | AS-AUD-010 | `AS-AUD-010-AC01` | **T062** | `NEW_TASK_REQUIRED` |
| `AWP-03-09` | 3. Trade Receivables | Test sales and receivable cut-off around year-end. | AS-AUD-009 | `AS-AUD-009-AC08` | **T062** | `NEW_TASK_REQUIRED` |
| `AWP-04-01` | 4. Inventory | Obtain the year-end inventory listing and agree it to the GL. | AS-AUD-011 | `AS-AUD-011-AC01` | **T063** | `NEW_TASK_REQUIRED` |
| `AWP-04-02` | 4. Inventory | Attend/observe physical inventory count where applicable. | AS-AUD-011 | `AS-AUD-011-AC02` | **T063** | `NEW_TASK_REQUIRED` |
| `AWP-04-03` | 4. Inventory | Perform auditor test counts and reconcile differences. | AS-AUD-011 | `AS-AUD-011-AC03` | **T063** | `NEW_TASK_REQUIRED` |
| `AWP-04-04` | 4. Inventory | Check inventory quantities against count sheets/final listing. | AS-AUD-011 | `AS-AUD-011-AC04` | **T063** | `NEW_TASK_REQUIRED` |
| `AWP-04-05` | 4. Inventory | Test inventory costs to purchase invoices or supporting records. | AS-AUD-011 | `AS-AUD-011-AC05` | **T063** | `NEW_TASK_REQUIRED` |
| `AWP-04-06` | 4. Inventory | Review slow-moving, damaged and obsolete inventory. | AS-AUD-011 | `AS-AUD-011-AC06` | **T063** | `NEW_TASK_REQUIRED` |
| `AWP-04-07` | 4. Inventory | Compare cost with NRV where applicable. | AS-AUD-011 | `AS-AUD-011-AC07` | **T063** | `NEW_TASK_REQUIRED` |
| `AWP-04-08` | 4. Inventory | Test purchases and goods received around year-end for cut-off. | AS-AUD-011 | `AS-AUD-011-AC08` | **T063** | `NEW_TASK_REQUIRED` |
| `AWP-05-01` | 5. Revenue / Sales | Obtain sales listing and reconcile total revenue to GL/TB. | AS-AUD-012 | `AS-AUD-012-AC01` | **T064** | `NEW_TASK_REQUIRED` |
| `AWP-05-02` | 5. Revenue / Sales | Perform analytical review of monthly and annual sales. | AS-AUD-012 | `AS-AUD-012-AC02` | **T064** | `NEW_TASK_REQUIRED` |
| `AWP-05-03` | 5. Revenue / Sales | Select sales samples based on value and risk. | AS-AUD-012 | `AS-AUD-012-AC03` | **T064** | `NEW_TASK_REQUIRED` |
| `AWP-05-04` | 5. Revenue / Sales | Check invoices to customer orders/delivery documents. | AS-AUD-012 | `AS-AUD-012-AC04` | **T064** | `NEW_TASK_REQUIRED` |
| `AWP-05-05` | 5. Revenue / Sales | Verify quantity, price, calculation and accounting entry. | AS-AUD-012 | `AS-AUD-012-AC05` | **T064** | `NEW_TASK_REQUIRED` |
| `AWP-05-06` | 5. Revenue / Sales | Check selected subsequent receipts where relevant. | AS-AUD-012 | `AS-AUD-012-AC06` | **T064** | `NEW_TASK_REQUIRED` |
| `AWP-05-07` | 5. Revenue / Sales | Review significant credit notes after year-end. | AS-AUD-012 | `AS-AUD-012-AC07` | **T064** | `NEW_TASK_REQUIRED` |
| `AWP-05-08` | 5. Revenue / Sales | Perform revenue cut-off testing before and after year-end. | AS-AUD-012 | `AS-AUD-012-AC08` | **T064** | `NEW_TASK_REQUIRED` |
| `AWP-06-01` | 6. Purchases & Trade Payables | Obtain supplier ageing and reconcile it to the GL/TB. | AS-AUD-013 | `AS-AUD-013-AC01` | **T065** | `NEW_TASK_REQUIRED` |
| `AWP-06-02` | 6. Purchases & Trade Payables | Review significant, old and unusual supplier balances. | AS-AUD-013 | `AS-AUD-013-AC02` | **T065** | `NEW_TASK_REQUIRED` |
| `AWP-06-03` | 6. Purchases & Trade Payables | Obtain supplier confirmations and investigate differences. | AS-AUD-013 | `AS-AUD-013-AC03` | **T065** | `NEW_TASK_REQUIRED` |
| `AWP-06-04` | 6. Purchases & Trade Payables | Compare supplier statements with the company's payable ledger. | AS-AUD-013 | `AS-AUD-013-AC04` | **T065** | `NEW_TASK_REQUIRED` |
| `AWP-06-05` | 6. Purchases & Trade Payables | Test selected purchases to invoices, GRNs and purchase orders. | AS-AUD-013 | `AS-AUD-013-AC05` | **T065** | `NEW_TASK_REQUIRED` |
| `AWP-06-06` | 6. Purchases & Trade Payables | Check subsequent payments to identify outstanding liabilities. | AS-AUD-013 | `AS-AUD-013-AC06` | **T065** | `NEW_TASK_REQUIRED` |
| `AWP-06-07` | 6. Purchases & Trade Payables | Perform search for unrecorded liabilities. | AS-AUD-013 | `AS-AUD-013-AC07` | **T065** | `NEW_TASK_REQUIRED` |
| `AWP-06-08` | 6. Purchases & Trade Payables | Test purchase and payable cut-off around year-end. | AS-AUD-013 | `AS-AUD-013-AC08` | **T065** | `NEW_TASK_REQUIRED` |
| `AWP-07-01` | 7. Fixed Assets | Obtain the fixed asset register and reconcile it to the GL. | AS-AUD-014 | `AS-AUD-014-AC01` | **T066** | `NEW_TASK_REQUIRED` |
| `AWP-07-02` | 7. Fixed Assets | Agree opening balances with prior-year audited balances. | AS-AUD-014 | `AS-AUD-014-AC02` | **T066** | `NEW_TASK_REQUIRED` |
| `AWP-07-03` | 7. Fixed Assets | Test additions to invoices, payment records and approvals. | AS-AUD-014 | `AS-AUD-014-AC03` | **T066** | `NEW_TASK_REQUIRED` |
| `AWP-07-04` | 7. Fixed Assets | Determine whether expenditure is correctly capitalized. | AS-AUD-014 | `AS-AUD-014-AC04` | **T066** | `NEW_TASK_REQUIRED` |
| `AWP-07-05` | 7. Fixed Assets | Physically verify significant additions/assets where appropriate. | AS-AUD-014 | `AS-AUD-014-AC05` | **T066** | `NEW_TASK_REQUIRED` |
| `AWP-07-06` | 7. Fixed Assets | Test disposals to disposal documents and sale proceeds. | AS-AUD-014 | `AS-AUD-014-AC06` | **T066** | `NEW_TASK_REQUIRED` |
| `AWP-07-07` | 7. Fixed Assets | Recalculate depreciation for selected assets. | AS-AUD-014 | `AS-AUD-014-AC07` | **T066** | `NEW_TASK_REQUIRED` |
| `AWP-07-08` | 7. Fixed Assets | Check useful lives and depreciation method. | AS-AUD-014 | `AS-AUD-014-AC08` | **T066** | `NEW_TASK_REQUIRED` |
| `AWP-07-09` | 7. Fixed Assets | Review assets for impairment, damage or obsolescence. | AS-AUD-014 | `AS-AUD-014-AC09` | **T066** | `NEW_TASK_REQUIRED` |
| `AWP-08-01` | 8. Expenses | Obtain detailed expense listing and reconcile to GL/TB. | AS-AUD-015 | `AS-AUD-015-AC01` | **T067** | `NEW_TASK_REQUIRED` |
| `AWP-08-02` | 8. Expenses | Perform analytical review against prior year and budget where available. | AS-AUD-015 | `AS-AUD-015-AC02` | **T067** | `NEW_TASK_REQUIRED` |
| `AWP-08-03` | 8. Expenses | Identify significant and unusual expense movements. | AS-AUD-015 | `AS-AUD-015-AC03` | **T067** | `NEW_TASK_REQUIRED` |
| `AWP-08-04` | 8. Expenses | Select samples based on value and risk. | AS-AUD-015 | `AS-AUD-015-AC04` | **T067** | `NEW_TASK_REQUIRED` |
| `AWP-08-05` | 8. Expenses | Check invoices and supporting documentation. | AS-AUD-015 | `AS-AUD-015-AC05` | **T067** | `NEW_TASK_REQUIRED` |
| `AWP-08-06` | 8. Expenses | Check management approval and payment evidence. | AS-AUD-015 | `AS-AUD-015-AC06` | **T067** | `NEW_TASK_REQUIRED` |
| `AWP-08-07` | 8. Expenses | Verify correct accounting classification. | AS-AUD-015 | `AS-AUD-015-AC07` | **T067** | `NEW_TASK_REQUIRED` |
| `AWP-08-08` | 8. Expenses | Check whether any capital expenditure has been incorrectly expensed. | AS-AUD-015 | `AS-AUD-015-AC08` | **T067** | `NEW_TASK_REQUIRED` |
| `AWP-08-09` | 8. Expenses | Perform expense cut-off testing. | AS-AUD-015 | `AS-AUD-015-AC09` | **T067** | `NEW_TASK_REQUIRED` |
| `AWP-09-01` | 9. Payroll | Obtain annual/monthly payroll summary and reconcile to GL. | AS-AUD-016 | `AS-AUD-016-AC01` | **T068** | `NEW_TASK_REQUIRED` |
| `AWP-09-02` | 9. Payroll | Select employees for detailed testing. | AS-AUD-016 | `AS-AUD-016-AC02` | **T068** | `NEW_TASK_REQUIRED` |
| `AWP-09-03` | 9. Payroll | Check employment contracts and salary details. | AS-AUD-016 | `AS-AUD-016-AC03` | **T068** | `NEW_TASK_REQUIRED` |
| `AWP-09-04` | 9. Payroll | Recalculate gross salary, allowances and deductions. | AS-AUD-016 | `AS-AUD-016-AC04` | **T068** | `NEW_TASK_REQUIRED` |
| `AWP-09-05` | 9. Payroll | Agree selected salary payments to bank statements. | AS-AUD-016 | `AS-AUD-016-AC05` | **T068** | `NEW_TASK_REQUIRED` |
| `AWP-09-06` | 9. Payroll | Test new employees and supporting employment documents. | AS-AUD-016 | `AS-AUD-016-AC06` | **T068** | `NEW_TASK_REQUIRED` |
| `AWP-09-07` | 9. Payroll | Check terminated employees and final payments. | AS-AUD-016 | `AS-AUD-016-AC07` | **T068** | `NEW_TASK_REQUIRED` |
| `AWP-09-08` | 9. Payroll | Review unusual changes in payroll or employee numbers. | AS-AUD-016 | `AS-AUD-016-AC08` | **T068** | `NEW_TASK_REQUIRED` |
| `AWP-10-01` | 10. Loans & Borrowings | Obtain loan/borrowing schedule and agree it to GL. | AS-AUD-017 | `AS-AUD-017-AC01` | **T069** | `NEW_TASK_REQUIRED` |
| `AWP-10-02` | 10. Loans & Borrowings | Agree opening balances to prior-year financial statements. | AS-AUD-017 | `AS-AUD-017-AC02` | **T069** | `NEW_TASK_REQUIRED` |
| `AWP-10-03` | 10. Loans & Borrowings | Obtain bank/financier confirmation. | AS-AUD-017 | `AS-AUD-017-AC03` | **T069** | `NEW_TASK_REQUIRED` |
| `AWP-10-04` | 10. Loans & Borrowings | Check new loans against agreements and bank receipts. | AS-AUD-017 | `AS-AUD-017-AC04` | **T069** | `NEW_TASK_REQUIRED` |
| `AWP-10-05` | 10. Loans & Borrowings | Check repayments against bank statements. | AS-AUD-017 | `AS-AUD-017-AC05` | **T069** | `NEW_TASK_REQUIRED` |
| `AWP-10-06` | 10. Loans & Borrowings | Recalculate interest expense and accrued interest. | AS-AUD-017 | `AS-AUD-017-AC06` | **T069** | `NEW_TASK_REQUIRED` |
| `AWP-10-07` | 10. Loans & Borrowings | Check current and non-current classification. | AS-AUD-017 | `AS-AUD-017-AC07` | **T069** | `NEW_TASK_REQUIRED` |
| `AWP-10-08` | 10. Loans & Borrowings | Review loan terms, security and covenant requirements where applicable. | AS-AUD-017 | `AS-AUD-017-AC08` | **T069** | `NEW_TASK_REQUIRED` |
| `AWP-10-09` | 10. Loans & Borrowings | Check subsequent repayments. | AS-AUD-017 | `AS-AUD-017-AC09` | **T069** | `NEW_TASK_REQUIRED` |
| `AWP-11-01` | 11. Equity / Share Capital | Obtain share capital and equity movement schedule. | AS-AUD-018 | `AS-AUD-018-AC01` | **T031** | `MERGE_EXISTING` |
| `AWP-11-02` | 11. Equity / Share Capital | Agree opening balances with prior-year audited FS. | AS-AUD-018 | `AS-AUD-018-AC02` | **T031** | `MERGE_EXISTING` |
| `AWP-11-03` | 11. Equity / Share Capital | Agree share capital to company/CR records. | AS-AUD-018 | `AS-AUD-018-AC03` | **T031** | `MERGE_EXISTING` |
| `AWP-11-04` | 11. Equity / Share Capital | Check additions, transfers or changes in shareholding. | AS-AUD-018 | `AS-AUD-018-AC04` | **T031** | `MERGE_EXISTING` |
| `AWP-11-05` | 11. Equity / Share Capital | Check dividend declarations and payments. | AS-AUD-018 | `AS-AUD-018-AC05` | **T031** | `MERGE_EXISTING` |
| `AWP-11-06` | 11. Equity / Share Capital | Reconcile retained earnings movement with profit/loss. | AS-AUD-018 | `AS-AUD-018-AC06` | **T031** | `MERGE_EXISTING` |
| `AWP-11-07` | 11. Equity / Share Capital | Agree closing equity balances to financial statements. | AS-AUD-018 | `AS-AUD-018-AC07` | **T031** | `MERGE_EXISTING` |
| `AWP-12-01` | 12. Related Parties | Obtain management's related-party listing. | AS-AUD-019 | `AS-AUD-019-AC01` | **T070** | `NEW_TASK_REQUIRED` |
| `AWP-12-02` | 12. Related Parties | Check directors, shareholders and key management records. | AS-AUD-019 | `AS-AUD-019-AC02` | **T070** | `NEW_TASK_REQUIRED` |
| `AWP-12-03` | 12. Related Parties | Review related-party transactions during the year. | AS-AUD-019 | `AS-AUD-019-AC03` | **T070** | `NEW_TASK_REQUIRED` |
| `AWP-12-04` | 12. Related Parties | Review significant related-party balances. | AS-AUD-019 | `AS-AUD-019-AC04` | **T070** | `NEW_TASK_REQUIRED` |
| `AWP-12-05` | 12. Related Parties | Confirm significant balances where appropriate. | AS-AUD-019 | `AS-AUD-019-AC05` | **T070** | `NEW_TASK_REQUIRED` |
| `AWP-12-06` | 12. Related Parties | Check whether transactions are properly recorded. | AS-AUD-019 | `AS-AUD-019-AC06` | **T070** | `NEW_TASK_REQUIRED` |
| `AWP-12-07` | 12. Related Parties | Verify required related-party disclosures in the financial statements. | AS-AUD-019 | `AS-AUD-019-AC07` | **T070** | `NEW_TASK_REQUIRED` |
| `AWP-13-01` | 13. Tax & Statutory Liabilities | Obtain tax returns and tax computations. | AS-AUD-020 | `AS-AUD-020-AC01` | **T071** | `NEW_TASK_REQUIRED` |
| `AWP-13-02` | 13. Tax & Statutory Liabilities | Reconcile tax balances with the GL/TB. | AS-AUD-020 | `AS-AUD-020-AC02` | **T071** | `NEW_TASK_REQUIRED` |
| `AWP-13-03` | 13. Tax & Statutory Liabilities | Check tax payments against bank statements. | AS-AUD-020 | `AS-AUD-020-AC03` | **T071** | `NEW_TASK_REQUIRED` |
| `AWP-13-04` | 13. Tax & Statutory Liabilities | Review outstanding tax liabilities and penalties. | AS-AUD-020 | `AS-AUD-020-AC04` | **T071** | `NEW_TASK_REQUIRED` |
| `AWP-13-05` | 13. Tax & Statutory Liabilities | Review correspondence with tax authorities. | AS-AUD-020 | `AS-AUD-020-AC05` | **T071** | `NEW_TASK_REQUIRED` |
| `AWP-13-06` | 13. Tax & Statutory Liabilities | Check tax provisions and current-year tax expense. | AS-AUD-020 | `AS-AUD-020-AC06` | **T071** | `NEW_TASK_REQUIRED` |
| `AWP-13-07` | 13. Tax & Statutory Liabilities | Check relevant tax disclosures in the financial statements. | AS-AUD-020 | `AS-AUD-020-AC07` | **T071** | `NEW_TASK_REQUIRED` |
| `AWP-14-01` | 14. Journal Entries & Fraud | Obtain journal entry listing for the audit period. | AS-AUD-021 | `AS-AUD-021-AC01` | **T072** | `NEW_TASK_REQUIRED` |
| `AWP-14-02` | 14. Journal Entries & Fraud | Identify unusual, manual and high-value journal entries. | AS-AUD-021 | `AS-AUD-021-AC02` | **T072** | `NEW_TASK_REQUIRED` |
| `AWP-14-03` | 14. Journal Entries & Fraud | Focus on journals posted close to year-end. | AS-AUD-021 | `AS-AUD-021-AC03` | **T072** | `NEW_TASK_REQUIRED` |
| `AWP-14-04` | 14. Journal Entries & Fraud | Select samples based on risk. | AS-AUD-021 | `AS-AUD-021-AC04` | **T072** | `NEW_TASK_REQUIRED` |
| `AWP-14-05` | 14. Journal Entries & Fraud | Check supporting documents and authorization. | AS-AUD-021 | `AS-AUD-021-AC05` | **T072** | `NEW_TASK_REQUIRED` |
| `AWP-14-06` | 14. Journal Entries & Fraud | Review unusual journals affecting revenue, expenses or reserves. | AS-AUD-021 | `AS-AUD-021-AC06` | **T072** | `NEW_TASK_REQUIRED` |
| `AWP-14-07` | 14. Journal Entries & Fraud | Check reversed or unusual post-year-end journals. | AS-AUD-021 | `AS-AUD-021-AC07` | **T072** | `NEW_TASK_REQUIRED` |
| `AWP-14-08` | 14. Journal Entries & Fraud | Document any fraud indicators or management override concerns. | AS-AUD-021 | `AS-AUD-021-AC08` | **T072** | `NEW_TASK_REQUIRED` |
| `AWP-15-01` | 15. Analytical Review | Compare current-year results with prior year. | AS-AUD-022 | `AS-AUD-022-AC01` | **T073** | `NEW_TASK_REQUIRED` |
| `AWP-15-02` | 15. Analytical Review | Analyse monthly revenue and expense trends. | AS-AUD-022 | `AS-AUD-022-AC02` | **T073** | `NEW_TASK_REQUIRED` |
| `AWP-15-03` | 15. Analytical Review | Compare gross profit and net profit margins. | AS-AUD-022 | `AS-AUD-022-AC03` | **T073** | `NEW_TASK_REQUIRED` |
| `AWP-15-04` | 15. Analytical Review | Analyse significant movements in major accounts. | AS-AUD-022 | `AS-AUD-022-AC04` | **T073** | `NEW_TASK_REQUIRED` |
| `AWP-15-05` | 15. Analytical Review | Calculate relevant ratios and key performance indicators. | AS-AUD-022 | `AS-AUD-022-AC05` | **T073** | `NEW_TASK_REQUIRED` |
| `AWP-15-06` | 15. Analytical Review | Review receivable, payable and inventory days where applicable. | AS-AUD-022 | `AS-AUD-022-AC06` | **T073** | `NEW_TASK_REQUIRED` |
| `AWP-15-07` | 15. Analytical Review | Investigate significant or unexpected fluctuations. | AS-AUD-022 | `AS-AUD-022-AC07` | **T073** | `NEW_TASK_REQUIRED` |
| `AWP-15-08` | 15. Analytical Review | Obtain management explanations and supporting evidence. | AS-AUD-022 | `AS-AUD-022-AC08` | **T073** | `NEW_TASK_REQUIRED` |
| `AWP-16-01` | 16. Going Concern | Obtain management's going-concern assessment. | AS-AUD-023 | `AS-AUD-023-AC01` | **T074** | `NEW_TASK_REQUIRED` |
| `AWP-16-02` | 16. Going Concern | Review current financial position and working capital. | AS-AUD-023 | `AS-AUD-023-AC02` | **T074** | `NEW_TASK_REQUIRED` |
| `AWP-16-03` | 16. Going Concern | Review cash flow forecasts. | AS-AUD-023 | `AS-AUD-023-AC03` | **T074** | `NEW_TASK_REQUIRED` |
| `AWP-16-04` | 16. Going Concern | Check expected cash inflows and major payments. | AS-AUD-023 | `AS-AUD-023-AC04` | **T074** | `NEW_TASK_REQUIRED` |
| `AWP-16-05` | 16. Going Concern | Review loan repayments and financing facilities. | AS-AUD-023 | `AS-AUD-023-AC05` | **T074** | `NEW_TASK_REQUIRED` |
| `AWP-16-06` | 16. Going Concern | Review losses, negative cash flows and overdue liabilities. | AS-AUD-023 | `AS-AUD-023-AC06` | **T074** | `NEW_TASK_REQUIRED` |
| `AWP-16-07` | 16. Going Concern | Assess significant assumptions used in forecasts. | AS-AUD-023 | `AS-AUD-023-AC07` | **T074** | `NEW_TASK_REQUIRED` |
| `AWP-16-08` | 16. Going Concern | Consider subsequent trading performance. | AS-AUD-023 | `AS-AUD-023-AC08` | **T074** | `NEW_TASK_REQUIRED` |
| `AWP-16-09` | 16. Going Concern | Document the auditor's conclusion. | AS-AUD-023 | `AS-AUD-023-AC09` | **T074** | `NEW_TASK_REQUIRED` |
| `AWP-17-01` | 17. Subsequent Events | Review post-year-end bank statements and transactions. | AS-AUD-024 | `AS-AUD-024-AC01` | **T075** | `NEW_TASK_REQUIRED` |
| `AWP-17-02` | 17. Subsequent Events | Review significant sales, purchases and payments after year-end. | AS-AUD-024 | `AS-AUD-024-AC02` | **T075** | `NEW_TASK_REQUIRED` |
| `AWP-17-03` | 17. Subsequent Events | Review board/management meeting minutes. | AS-AUD-024 | `AS-AUD-024-AC03` | **T075** | `NEW_TASK_REQUIRED` |
| `AWP-17-04` | 17. Subsequent Events | Check new loans, investments or major asset purchases. | AS-AUD-024 | `AS-AUD-024-AC04` | **T075** | `NEW_TASK_REQUIRED` |
| `AWP-17-05` | 17. Subsequent Events | Review litigation and significant legal developments. | AS-AUD-024 | `AS-AUD-024-AC05` | **T075** | `NEW_TASK_REQUIRED` |
| `AWP-17-06` | 17. Subsequent Events | Discuss significant events with management. | AS-AUD-024 | `AS-AUD-024-AC06` | **T075** | `NEW_TASK_REQUIRED` |
| `AWP-17-07` | 17. Subsequent Events | Determine whether events require adjustment or disclosure. | AS-AUD-024 | `AS-AUD-024-AC07` | **T075** | `NEW_TASK_REQUIRED` |
| `AWP-17-08` | 17. Subsequent Events | Ensure relevant events are reflected in the financial statements. | AS-AUD-024 | `AS-AUD-024-AC08` | **T075** | `NEW_TASK_REQUIRED` |
| `AWP-18-01` | 18. Financial Statements & Disclosures | Agree final financial statements to the audited Trial Balance. | AS-AUD-025 | `AS-AUD-025-AC01` | **T033** | `MERGE_EXISTING` |
| `AWP-18-02` | 18. Financial Statements & Disclosures | Check statement of financial position and profit/loss. | AS-AUD-025 | `AS-AUD-025-AC02` | **T033** | `MERGE_EXISTING` |
| `AWP-18-03` | 18. Financial Statements & Disclosures | Check cash flow statement and statement of changes in equity. | AS-AUD-025 | `AS-AUD-025-AC03` | **T033** | `MERGE_EXISTING` |
| `AWP-18-04` | 18. Financial Statements & Disclosures | Review accounting policies and significant estimates. | AS-AUD-025 | `AS-AUD-025-AC04` | **T032** | `MERGE_EXISTING` |
| `AWP-18-05` | 18. Financial Statements & Disclosures | Check comparative figures with prior-year audited FS. | AS-AUD-025 | `AS-AUD-025-AC05` | **T033** | `MERGE_EXISTING` |
| `AWP-18-06` | 18. Financial Statements & Disclosures | Check note disclosures and supporting schedules. | AS-AUD-025 | `AS-AUD-025-AC06` | **T032** | `MERGE_EXISTING` |
| `AWP-18-07` | 18. Financial Statements & Disclosures | Check related-party, tax and going-concern disclosures. | AS-AUD-025 | `AS-AUD-025-AC07` | **T033** | `MERGE_EXISTING` |
| `AWP-18-08` | 18. Financial Statements & Disclosures | Check subsequent-event disclosures. | AS-AUD-025 | `AS-AUD-025-AC08` | **T032** | `MERGE_EXISTING` |
| `AWP-18-09` | 18. Financial Statements & Disclosures | Perform final financial statement consistency and mathematical review. | AS-AUD-025 | `AS-AUD-025-AC09` | **T032** | `MERGE_EXISTING` |
| `AWP-19-01` | 19. Audit Differences & Adjustments | Record all identified audit differences. | AS-AUD-026 | `AS-AUD-026-AC01` | **T022** | `MERGE_EXISTING` |
| `AWP-19-02` | 19. Audit Differences & Adjustments | Obtain management's response to proposed adjustments. | AS-AUD-026 | `AS-AUD-026-AC02` | **T022** | `MERGE_EXISTING` |
| `AWP-19-03` | 19. Audit Differences & Adjustments | Recalculate the financial statement impact of each difference. | AS-AUD-026 | `AS-AUD-026-AC03` | **T022** | `MERGE_EXISTING` |
| `AWP-19-04` | 19. Audit Differences & Adjustments | Update the adjusted and unadjusted misstatement schedule. | AS-AUD-026 | `AS-AUD-026-AC04` | **T022** | `MERGE_EXISTING` |
| `AWP-19-05` | 19. Audit Differences & Adjustments | Compare total unadjusted differences with materiality. | AS-AUD-026 | `AS-AUD-026-AC05` | **T022** | `MERGE_EXISTING` |
| `AWP-19-06` | 19. Audit Differences & Adjustments | Assess whether remaining differences affect the audit conclusion. | AS-AUD-026 | `AS-AUD-026-AC06` | **T022** | `MERGE_EXISTING` |
| `AWP-19-07` | 19. Audit Differences & Adjustments | Ensure all agreed adjustments are posted and reflected in the final TB. | AS-AUD-026 | `AS-AUD-026-AC07` | **T023** | `MERGE_EXISTING` |
| `AWP-20-01` | 20. Final Completion & Audit Report | Ensure all audit sections are completed and cross-referenced. | AS-AUD-027 | `AS-AUD-027-AC01` | **T040** | `MERGE_EXISTING` |
| `AWP-20-02` | 20. Final Completion & Audit Report | Ensure all review points have been cleared. | AS-AUD-027 | `AS-AUD-027-AC02` | **T040** | `MERGE_EXISTING` |
| `AWP-20-03` | 20. Final Completion & Audit Report | Confirm all significant risks have been addressed. | AS-AUD-027 | `AS-AUD-027-AC03` | **T038** | `MERGE_EXISTING` |
| `AWP-20-04` | 20. Final Completion & Audit Report | Review audit differences and final materiality assessment. | AS-AUD-027 | `AS-AUD-027-AC04` | **T038** | `MERGE_EXISTING` |
| `AWP-20-05` | 20. Final Completion & Audit Report | Complete going-concern and subsequent-event procedures. | AS-AUD-027 | `AS-AUD-027-AC05` | **T040** | `MERGE_EXISTING` |
| `AWP-20-06` | 20. Final Completion & Audit Report | Complete financial statement disclosure checklist. | AS-AUD-027 | `AS-AUD-027-AC06` | **T037** | `MERGE_EXISTING` |
| `AWP-20-07` | 20. Final Completion & Audit Report | Obtain signed management representation letter. | AS-AUD-027 | `AS-AUD-027-AC07` | **T040** | `MERGE_EXISTING` |
| `AWP-20-08` | 20. Final Completion & Audit Report | Perform final analytical review. | AS-AUD-027 | `AS-AUD-027-AC08` | **T037** | `MERGE_EXISTING` |
| `AWP-20-09` | 20. Final Completion & Audit Report | Complete senior/manager/partner review. | AS-AUD-027 | `AS-AUD-027-AC09` | **T040** | `MERGE_EXISTING` |
| `AWP-20-10` | 20. Final Completion & Audit Report | Finalize the auditor's report and signed financial statements. | AS-AUD-027 | `AS-AUD-027-AC10` | **T040** | `MERGE_EXISTING` |
