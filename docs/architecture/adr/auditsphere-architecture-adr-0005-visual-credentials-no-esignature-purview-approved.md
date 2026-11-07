# ADR-0005: Visual signature and seal credentials; no eSignature or Purview integration

**Status: APPROVED** (owner-delegated approval recorded 2026-10-10) · Date recorded: 2026-10-08 · Decider: repository owner (authority delegated 2026-10-10)

**Partly superseded (2026-10-10):** ADR-0016 adds certificate signing of the final report with a firm-held certificate. Only the "PKI signing" exclusion below is superseded; the visual credentials and the exclusion of eSignature providers and Purview stand.

## Context
Specification §4.1.4 and §4.4.1 call for the Partner's "digital signature" and the firm seal on letters and reports. The owner excluded eSignature providers (issue #19) and Purview records integration (issues #17, #18), recorded as gates P5 and P4 `OUT_OF_SCOPE` and as R2R-ADR-08.

## Decision
- The Partner registers a PNG signature (at most 512 KB, usable dimensions) and the firm registers versioned seal PNGs. Only the deciding Engagement Partner can embed them, into the current report version.
- Documents record the exact signature and seal identities and SHA-256 hashes. Management-signed letters are uploaded as PDFs and verified by a human Partner.
- These are **visual credentials**, never described as certificate-backed or legally binding digital signatures.

## Alternatives considered
- Integrate an eSignature provider or PKI signing: excluded by the owner.
- Purview retention labels for the archive: excluded by the owner.

## Consequences
- Legal acceptance of signatures and seal wording stays with the firm.
- Archive immutability cannot rely on Purview; see ADR-0006 and spike SPK-01.
