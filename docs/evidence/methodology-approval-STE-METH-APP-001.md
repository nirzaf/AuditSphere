# Methodology approval record

**Reference:** `STE-METH-APP-001`  
**Approver:** Mohamed Fazrin  
**Role:** Firm Methodology Owner  
**Approval date:** 2026-09-22

## Approved template versions

- Financial Statement Template v1.0
- Audit Program Template v1.0
- ECL Provision Matrix v1.0

## Approved accounting methods and framework

- IFRS
- Straight-line depreciation
- Provision Matrix ECL methodology
- External-books / audit reporting adjustment model

## Scope boundary

This record authorizes implementation and use of the named methods and template families. It does not approve advanced consolidation profiles for FX reserves, acquisitions/goodwill, NCI, ownership changes or disposals, nested groups, or asset-transfer/tax eliminations. Those profiles remain fail-closed pending a separate method-owner approval that names the method and fixture scope.

The repository renderer profiles still require an explicit version mapping from the named business templates to the exact controlled `financial-package-*` renderer versions before client-safe artifact approval is claimed.

## Repository profile mapping

The approved **Financial Statement Template v1.0** maps to these exact inactive renderer profiles:

| Business template | Renderer profile |
|---|---|
| Financial Statement Template v1.0 | `financial-package-xlsx-controlled.v1` |
| Financial Statement Template v1.0 | `financial-package-docx.v1` |
| Financial Statement Template v1.0 | `financial-package-pdf.v1` |

The Audit Program and ECL Provision Matrix approvals are methodology/template-family approvals; no corresponding financial-package renderer profile is enabled by this record.
