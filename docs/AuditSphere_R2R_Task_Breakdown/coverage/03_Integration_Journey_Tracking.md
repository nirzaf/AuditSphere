# Integration Journey Tracking Ledger

[Master index](../00_INDEX.md)

| Status | Journey ID | Scenario | Expected outcome | Evidence |
|---|---|---|---|---|
| NOT_RUN | <a id="r2r-at-01"></a>R2R-AT-01 | New entity context → TB/GL → mapping → AJE → reconciliation → statements → package → close | Every displayed/exported amount reconciles to the same scope and exact inputs; no draft step is silently skipped. | |
| NOT_RUN | <a id="r2r-at-02"></a>R2R-AT-02 | Same account code and period label in two clients | No shared data, authority, cache, row, mapping, export or evidence leakage. | |
| NOT_RUN | <a id="r2r-at-03"></a>R2R-AT-03 | Existing browser circuit navigates to another unauthorized context | Prior content/IDs/counts/download links clear before new load; no full-page reload needed to enforce denial. | |
| NOT_RUN | <a id="r2r-at-04"></a>R2R-AT-04 | User grant/session revoked after opening approval dialog | Handler denies and UI clears protected content; no approval persists. | |
| NOT_RUN | <a id="r2r-at-05"></a>R2R-AT-05 | Two reviewers act on same expected revision | Unique/optimistic concurrency rule yields one applicable decision or explicit conflict, not duplicate contradictory approval. | |
| NOT_RUN | <a id="r2r-at-06"></a>R2R-AT-06 | Duplicate command, same OperationId/body | One mutation/result; retry reauthorizes. Changed body with same ID conflicts. | |
| NOT_RUN | <a id="r2r-at-07"></a>R2R-AT-07 | Import interrupted, duplicate/resumed/out-of-order chunk | Consistent staged state; no source seal before full expected counts/hash; prior accepted source remains. | |
| NOT_RUN | <a id="r2r-at-08"></a>R2R-AT-08 | Macro/formula/external-link/fake-XLSX/decompression attack | Bounded failure with safe issue list; no parser execution, network request or partial acceptance. | |
| NOT_RUN | <a id="r2r-at-09"></a>R2R-AT-09 | Missing opening data or GL-only account | Completeness stays incomplete; missing is not zero; UI and export agree. | |
| NOT_RUN | <a id="r2r-at-10"></a>R2R-AT-10 | New accepted AJE after initial plan | Adjustment-set membership changes; old statement/package cannot be newly approved as current. | |
| NOT_RUN | <a id="r2r-at-11"></a>R2R-AT-11 | Partial/rejected management AJE | No unbalanced subset applied; remainder visible for human evaluation; fresh balanced revision when needed. | |
| NOT_RUN | <a id="r2r-at-12"></a>R2R-AT-12 | Replacement TB includes a former adjustment | Fresh reflection decision; no double count; old result preserved. | |
| NOT_RUN | <a id="r2r-at-13"></a>R2R-AT-13 | Approved reconciliation supporting document replaced | Old proof/review historical; currentness stale and downstream finalization blocked. | |
| NOT_RUN | <a id="r2r-at-14"></a>R2R-AT-14 | Cash-flow noncash movement included by mistake | Validation identifies classification/bridge mismatch; no balancing cash plug. | |
| NOT_RUN | <a id="r2r-at-15"></a>R2R-AT-15 | Comparative mapping/source changes after current review | Current dependent statement stale; as-issued historical output unchanged. | |
| NOT_RUN | <a id="r2r-at-16"></a>R2R-AT-16 | Error restatement versus estimate change | Approved distinct period treatment, disclosure and lineage; no universal retrospective or current-P&L shortcut. | |
| NOT_RUN | <a id="r2r-at-17"></a>R2R-AT-17 | Required disclosure removed/added after build | Set-membership hash changes even when balances do not; prior review no longer authorizes current package. | |
| NOT_RUN | <a id="r2r-at-18"></a>R2R-AT-18 | XLSX succeeds, DOCX/PDF generation or artifact publication fails | Package not sealed/advanced; successful intermediate files explicitly nonpublished; retry deterministic. | |
| NOT_RUN | <a id="r2r-at-19"></a>R2R-AT-19 | Source acceptance races package rendering | Worker cannot publish against obsolete generation; no current success notification. | |
| NOT_RUN | <a id="r2r-at-20"></a>R2R-AT-20 | Artifact bytes tampered after rendering | Digest check blocks seal/review/download/release handoff as applicable; historical identity not rewritten to match tamper. | |
| NOT_RUN | <a id="r2r-at-21"></a>R2R-AT-21 | Closed period receives import/journal command | Denied; authorized amendment creates successor with fresh required decisions. | |
| NOT_RUN | <a id="r2r-at-22"></a>R2R-AT-22 | Group-only reviewer requests component private artifact | Group result allowed, source file denied; no sensitive filename/details leak. | |
| NOT_RUN | <a id="r2r-at-23"></a>R2R-AT-23 | Missing rate, rate direction reversed, unsupported ownership | No final figures asserted; clear bounded method/input requirement. | |
| NOT_RUN | <a id="r2r-at-24"></a>R2R-AT-24 | Intercompany mismatch or overlapping journal source inclusion | Unmatched difference visible; duplicate consumption rejected; no plug. | |
| NOT_RUN | <a id="r2r-at-25"></a>R2R-AT-25 | Component package replaced during group approval | Manifest mismatch blocks old run; explicit new pin/run required; old group result retained. | |
| NOT_RUN | <a id="r2r-at-26"></a>R2R-AT-26 | Entity close/group close sequencing | No mutual prerequisite deadlock; reviewed components feed group work without inventing approvals. | |
| NOT_RUN | <a id="r2r-at-27"></a>R2R-AT-27 | Upgrade prior schema with ambiguous/null context | Data preserved, unresolved links quarantined, no fabricated approval/currency/book. | |
| NOT_RUN | <a id="r2r-at-28"></a>R2R-AT-28 | Restore DB/artifacts and resume interrupted durable work | Exact manifest/digest reconciliation; stale leases fenced; no duplicate external effects or falsely completed operations. | |
| NOT_RUN | <a id="r2r-at-29"></a>R2R-AT-29 | Keyboard-only and responsive forms | Labels, field/summary errors, focus, line add/remove, dialog cancellation and dirty navigation usable without losing context. | |
| NOT_RUN | <a id="r2r-at-30"></a>R2R-AT-30 | Entity and group package delivered to existing release/archive contracts | Exact sealed/reviewed artifacts handed off; no source recomputation, Purview requirement or signature-provider side effect introduced. | |
