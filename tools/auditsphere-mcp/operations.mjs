// Admission is by exact method, OpenAPI template and supported request shape.
export const pbcRequest = "/api/ui/portal/requests/{id}";
export const pbcStart = pbcRequest + "/uploads";
export const pbcResume = pbcStart + "/{uploadId}/resume";
export const pbcChunk = "/api/pbc/uploads/{uploadId}/chunks/{chunkIndex}";
const workpaper = "/api/ui/audit/workpapers/{id}/commands";
const workpaperWorkspace = "/ui/app/audit/workpapers/:id";

const write = (body, workspace, recovery, limits) => ({ mode: "write", body, workspace, recovery, limits });
const download = (workspace, types, body) => ({ mode: "download", workspace, types, body });
const csv = (workspace, body) => download(workspace, ["text/csv"], body);
const pdf = "application/pdf";
const docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
const xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
const inspect = (workspace, body, maxBytes) => ({ mode: "inspect", workspace, form: body, maxBytes });
const reports = "/ui/app/finance/books";

export const direct = new Map([
  ["PUT /api/ui/me/profile", write("UpdateUserProfileRequest", "/ui/app/profile",
    { path: "/api/ui/me/profile", revision: "expectedRevision" },
    { position: { maxLength: 120 }, mobileNumber: { maxLength: 24 } })],
  ["POST /api/ui/accounting/clients/{clientId}/purchase-invoice-drafts", write("PurchaseDraftHttpInput",
    "/ui/app/accounting", { path: "/api/ui/accounting/clients/{clientId}/purchase-invoice-requests/{commandId}",
      key: "commandId", query: { kind: "DRAFT" } },
    { lines: { minItems: 1, maxItems: 99 }, voucherReference: { minLength: 1, maxLength: 100 },
      supplierInvoiceReference: { minLength: 1, maxLength: 200 }, evidenceReference: { minLength: 1, maxLength: 2000 } })],
  ["POST " + workpaper + "/prepare-save", { mode: "preview", body: "WorkpaperCommandRequest", workpaperSave: true,
    workspace: workpaperWorkspace }],
  ["POST " + workpaper + "/actions", { ...write("WorkpaperCommandRequest", workpaperWorkspace,
    { path: workpaper + "/receipts/{requestId}", key: "requestId", hash: "expectedRequestHash" }), workpaperSave: true }],
  ["POST " + pbcRequest + "/reply", write("PortalReplyInput", "/ui/portal/requests/:id",
    { path: pbcRequest + "/reply-receipts/{requestId}", key: "requestId" }, { body: { minLength: 1, maxLength: 4000 } })],
  ["POST /api/ui/portal/documents/{id}/comment", write("PortalDocumentCommentInput", "/ui/portal",
    { path: "/api/ui/portal/documents/{id}/comment-receipts/{requestId}", key: "requestId" }, { body: { minLength: 1, maxLength: 4000 } })],
  ["POST /api/ui/practice/time/entries/{id}/changes", write("TimeEntryChangeInput", "/ui/app/practice/time",
    { path: "/api/ui/practice/time/entries/{id}/changes/requests/{requestId}", key: "requestId" }, {
      kind: { const: "EDIT_UNAPPROVED" }, expectedRevision: { pattern: "^[1-9][0-9]{0,18}$" },
      workDate: { format: "date" }, startMinute: { type: "integer", minimum: 0, maximum: 1439 },
      durationMinutes: { type: "integer", minimum: 1, maximum: 1440 }, activity: { minLength: 1, maxLength: 20_000 },
      narrative: { maxLength: 2_000 }, reason: { type: "string", minLength: 1, maxLength: 2_000 }
    })],

  ["GET /api/pbc/uploads/{uploadId}/download", download("/ui/portal", ["*/*"])],
  ["GET /api/commercial/documents/{documentId}/download", download("/ui/app/practice/pipeline", [docx])],
  ["GET /api/deliverables/{deliverableId}/download", download("/ui/app", [pdf, docx])],
  ["GET /api/representation-scans/{scanId}/download", download("/ui/app", [pdf])],
  ["GET /api/deliverable-bundles/{bundleId}/download", download("/ui/app", ["application/zip"])],
  ["GET /api/ui/records/archives/{id}/export", download("/ui/app/records/archives/:id", ["application/json"])],
  ["GET /api/ui/consolidation/scopes/{scopeId}/deliverables/issued/download",
    download("/ui/app/consolidation/scopes/:id", ["application/json"])],
  ["GET /api/ui/consolidation/scopes/{scopeId}/deliverables/{deliverableId}/download",
    download("/ui/app/consolidation/scopes/:id", ["application/json"])],
  ["GET /api/ui/portal/branding/{profileId}/logo", download("/ui/portal", ["image/png"])],
  ["POST /api/ui/portal/accounting/packages/{id}/compositions/{compositionId}/pdf", download("/ui/portal/accounting/packages/:id", [pdf])],
  ["POST /api/ui/finance/journals/{id}/evidence", download("/ui/app/finance", ["application/octet-stream"])],
  ["POST /api/ui/portfolio/export", csv("/ui/app", "PortfolioExportInput")],
  ["POST /api/ui/datasets/{id}/source/export", { ...csv("/ui/app/accounting", "TrialBalanceExportInput"),
    limits: { revision: { pattern: "^[1-9][0-9]{0,18}$" } } }],
  ["POST /api/ui/accounting/packages/{id}/presentation/export", {
    ...download("/ui/app/accounting/packages/:id", [pdf, xlsx, docx], "StatementPresentationExportInput"),
    limits: { format: { type: "string", enum: ["pdf", "docx", "xlsx"] } }
  }],
  ["POST /api/ui/finance/books/trial-balance/export", csv(reports, "FirmTrialBalanceExportInput")],
  ["POST /api/ui/finance/receivables-aging/export", csv("/ui/app/finance/receivables-aging", "ReceivablesAgingExportInput")],

  ["POST /api/ui/engagements/{id}/tb-intake/preview", inspect("/ui/app/engagements/:id/tb-intake",
    "TrialBalanceUploadFileForm", 25 * 1024 * 1024)],
  ["POST /api/ui/engagements/{id}/tb-intake/reconcile", inspect("/ui/app/engagements/:id/tb-intake",
    "TrialBalanceUploadFileForm", 25 * 1024 * 1024)],
  ["POST /api/ui/engagements/{id}/general-ledger/preview", inspect("/ui/app/engagements/:id/general-ledger/upload",
    "GeneralLedgerUploadFileForm", 10_000_000)],
  ["POST /api/ui/engagements/{id}/general-ledger/reconcile", inspect("/ui/app/engagements/:id/general-ledger/upload",
    "GeneralLedgerUploadFileForm", 10_000_000)],

  ["POST " + pbcStart, { mode: "stage", body: "PortalUploadInput", workspace: "/ui/portal/requests/:id" }],
  ["POST " + pbcResume, { mode: "stage", body: "PortalUploadResumeInput", workspace: "/ui/portal/requests/:id" }],
  ["POST " + pbcChunk, { mode: "stage", stream: "Stream", workspace: "/ui/portal" }],
]);
