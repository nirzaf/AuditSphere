# AuditSphere — Technical Spikes

**Status: PROPOSED.** A spike produces a written finding and, where warranted, an ADR and follow-up stories. It ships no production code. Each is time-boxed; stop at the box and report what is known.

---

## SPK-01 — Making the ISA 230 archive actually immutable

*Status (2026-10-10): archive authority is accepted by ADR-0009 under the owner's delegated project decision. The selected provider-side read-only-copy implementation and live permission readback remain open under STE-NXT-011/012; no live tenant evidence is claimed here.*

**Time box:** 3 days. **Blocks:** STE package 6 acceptance, gate P2 archive scope.

**Question.** After the local freeze, staff with Full Control on the client SharePoint site (ADR-0006) can still edit or delete files directly. Which mechanism makes the final archive tamper-evident or tamper-proof without Purview (ADR-0005)?

**Options to evaluate.**
1. At freeze, the client-sites worker breaks inheritance on `05_Final Signed Archive` and leaves only a read role for staff.
2. At freeze, copy the five-part bundle and manifest to a separate archive site or library where staff have read only.
3. Store issued evidence only in the existing independently controlled archive/release store; treat SharePoint as a working copy and say so in the UI.
4. Write-once object storage (immutability policy) for bundle and manifest bytes.

**For each:** required Graph/SharePoint permission (stay within the capability model), behaviour when a staff member is still assigned, how the application proves the state (`ExternalReadOnlyStates.OBSERVED`), cost, and what a regulator inspection export reads.

**Output.** Comparison table, recommendation, draft ADR, follow-up stories.

---

## SPK-02 — Recover the STE-GAP story definitions

*Status (2026-10-08): done. The ten definitions are committed verbatim, with a mapping table, in `auditsphere-execution-user-stories-ste-v21-gap-closure-current.md`. Their source is the owner's gap analysis; no definitions were invented.*

**Time box:** 2 hours. **Owner input required.**

**Question.** Commits `9e946d1`, `9c625a2` and `27449f0` cite STE-GAP-001 to STE-GAP-010 (and the code cites STE-GAP-009), but no file in the repository defines them. STE-GAP-004, -007 and -010 are described only as "external-tenant" or not at all.

**Output.** The ten definitions committed to `docs/execution/` (naming policy applies), each mapped to the verification table, so agents can see which are closed locally.

---

## SPK-03 — Normalized profit before tax

*Status (2026-10-08): methodology note written, `auditsphere-audit-report-normalized-pbt-spk-03-proposed.md`. It recommends no normalization now (Option A). The decision belongs to the owner (STE-NXT-010).*

**Time box:** 1 day. **Feeds:** STE-NXT-010.

**Question.** What does "normalized PBT" need to mean for STE (exclude one-off items, discontinued operations, owner remuneration adjustments?), who identifies the adjustments, and how are they evidenced so the benchmark stays reproducible from sealed data?

**Constraints.** The calculator must stay pure; adjustments must be recorded inputs with their own approval, never inferred.

**Output.** Short methodology note for the owner's decision, with one worked example using the specification's figures.

---

## SPK-04 — Splitting the execution ledger without losing evidence

*Status (2026-10-08): done. Finding in `auditsphere-execution-report-spk-04-ledger-split-current.md`. The release workflow does not read `status.json`. Wiki and out-of-repo consumers were not checked.*

**Time box:** 4 hours. **Feeds:** STE-NXT-002.

**Question.** Confirm the safe split of `docs/execution/status.json` and the current-slice file: which keys humans and agents read on resume, which are historical, and whether any external consumer (Wiki pages, scripts outside this repository, release workflow) reads the moved keys.

**Known so far.** Inside the repository only the docs validator touches `status.json`, and only to check it exists.

**Output.** Final key list for the slim file, archive naming, and a check that the release workflow (`.github/workflows/release.yml`) does not read it.
