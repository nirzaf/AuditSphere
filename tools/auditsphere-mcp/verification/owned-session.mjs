// Optional browser check against an owned AuditSphere Test host and synthetic fixtures.
import assert from "node:assert/strict";
import { randomUUID } from "node:crypto";
import { mkdtemp, readFile, realpath, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { chromeSession } from "../browser.mjs";
import { catalog, isId } from "../catalog.mjs";
import { fileExchange, sha256 } from "../files.mjs";
import { pbcRequest } from "../operations.mjs";
import { toolHandler } from "../tools.mjs";

function ownedOrigin(value) {
  const url = new URL(value);
  if (url.protocol !== "http:" || url.hostname !== "127.0.0.1" || !url.port || value !== url.origin) {
    throw new Error("Use a test-owned loopback HTTP origin.");
  }
  return url.origin;
}

const clientOrigin = ownedOrigin(process.argv[2]);
const staffOrigin = ownedOrigin(process.argv[3]);
const requestId = process.argv[4];
const otherOrigin = ownedOrigin(process.argv[5]);
const workpaperId = process.argv[6];
const engagementId = process.argv[7];
const timeEntryId = process.argv[8];
assert.ok([requestId, workpaperId, engagementId, timeEntryId].every(isId) &&
  new Set([clientOrigin, staffOrigin, otherOrigin]).size === 3);

const contract = JSON.parse(await readFile(new URL("../../../contracts/auditsphere-openapi.json", import.meta.url), "utf8"));
const routes = await readFile(new URL("../../../src/AuditSphereOps.Ui/src/app/app.routes.ts", import.meta.url), "utf8");
const entries = catalog(contract, routes);
const directory = await mkdtemp(join(await realpath(tmpdir()), "auditsphere-mcp-owned-"));
const exchange = fileExchange(directory);
const browsers = [];

const read = (call, path, inputs = {}) => call("read_api", { path, ...inputs });
const unpack = response => JSON.parse(response.content[0].text);
function success(response, label) {
  assert.equal(response.isError, undefined, label + ": " + (response.isError ? JSON.stringify(unpack(response)) : ""));
  return unpack(response);
}

async function signIn(origin) {
  const browser = chromeSession(origin, { headless: true });
  browsers.push(browser);
  const call = toolHandler({ entries, origin, browser, exchange });
  assert.equal(unpack(await read(call, "/api/ui/session")).status, 401);
  await browser.open("/auth/sign-in?returnUrl=/health/live");
  const value = success(await read(call, "/api/ui/session"), "read current session");
  assert.equal(value.microsoftAccount, false, "only a synthetic Test session is allowed");
  const { userId, firmId, generation } = value;
  return { call, session: { userId, firmId, generation } };
}

try {
  const client = await signIn(clientOrigin);
  const staff = await signIn(staffOrigin);
  const other = await signIn(otherOrigin);
  assert.notEqual(client.session.userId, staff.session.userId);
  assert.notEqual(client.session.userId, other.session.userId);

  const profileInput = { path: "/api/ui/me/profile", session: client.session };
  const profile = success(await read(client.call, profileInput.path, { session: client.session }), "own profile");
  assert.equal(profile.userId, client.session.userId);
  const edit = { ...profileInput, method: "PUT", body: {
    position: "Synthetic MCP check", gender: null, dateOfBirth: null, mobileNumber: null, joinedDate: null,
    expectedRevision: String(profile.revision)
  } };
  const staleSession = { ...client.session, generation: (BigInt(client.session.generation) + 1n).toString() };
  assert.equal(unpack(await client.call("write_api", { ...edit, session: staleSession })).code, "mcp.session-changed");
  const edited = success(await client.call("write_api", edit), "update own profile");
  assert.equal(edited.value.position, edit.body.position);
  assert.equal(unpack(await client.call("write_api", edit)).status, 409);
  const savedProfile = success(await read(client.call, profileInput.path, { session: client.session }), "read saved profile");
  assert.equal(savedProfile.revision, edited.value.revision);
  assert.equal(savedProfile.userId, client.session.userId);

  const workpaper = "/api/ui/audit/workpapers/{id}/commands";
  const target = { pathParameters: { id: workpaperId }, session: staff.session };
  const state = success(await read(staff.call, workpaper + "/state", target), "read workpaper state");
  assert.equal(state.canSaveDraft, true);
  const draft = { requestId: randomUUID(), fields: { action: "SAVE_DRAFT", save: {
    expectedDraftRevision: state.workpaper.draft?.draftRevision ?? "0", baseWorkpaperRevision: state.workpaper.revision,
    baseInputGeneration: state.workpaper.inputGeneration, basePolicyGeneration: state.workpaper.policyGeneration,
    workPerformed: "Synthetic evidence inspected.", conclusion: "Synthetic draft only."
  } } };
  const intent = success(await staff.call("preview_api", { ...target, path: workpaper + "/prepare-save", body: draft }),
    "prepare exact workpaper draft save");
  assert.equal(intent.value.requestId, draft.requestId);
  const save = { ...target, method: "POST", path: workpaper + "/actions",
    body: { ...draft, expectedRequestHash: intent.value.requestHash } };
  assert.equal(unpack(await staff.call("write_api", { ...save, body: { ...save.body, fields: { action: "SUBMIT" } } })).code,
    "mcp.input");
  const savedDraft = success(await staff.call("write_api", save), "save an exact workpaper draft");
  assert.equal(savedDraft.value.action, "SAVE_DRAFT");
  const draftReceipt = success(await read(staff.call, workpaper + "/receipts/{requestId}", { ...target,
    pathParameters: { id: workpaperId, requestId: draft.requestId }, query: { requestHash: intent.value.requestHash } }),
  "read exact draft receipt");
  assert.equal(draftReceipt.receipt.id, savedDraft.value.id);

  const time = success(await read(staff.call, "/api/ui/practice/time", { session: staff.session }), "read own time");
  const entry = time.myEntries.find(item => item.id === timeEntryId);
  assert.equal(entry?.canEdit, true);
  const changes = "/api/ui/practice/time/entries/{id}/changes";
  const editTime = { method: "POST", path: changes, pathParameters: { id: timeEntryId }, session: staff.session,
    body: { requestId: randomUUID(), expectedRevision: entry.revision, kind: "EDIT_UNAPPROVED", workDate: entry.workDate,
      startMinute: 540, durationMinutes: 45, activity: "Synthetic fieldwork", narrative: "Reconciled minutes",
      reason: "Correct the draft duration." } };
  const timeResult = success(await staff.call("write_api", editTime), "edit an unapproved time entry");
  assert.equal(timeResult.value, timeEntryId);
  assert.deepEqual(success(await staff.call("write_api", editTime), "replay original time edit"), timeResult);
  assert.equal(unpack(await staff.call("write_api", { ...editTime,
    body: { ...editTime.body, durationMinutes: 46 } })).code, "idempotency.conflict");
  const timeReceipt = success(await read(staff.call, changes + "/requests/{requestId}", { session: staff.session,
    pathParameters: { id: timeEntryId, requestId: editTime.body.requestId } }), "read exact time receipt");
  assert.equal(timeReceipt.receipt.actorId, staff.session.userId);
  assert.equal(timeReceipt.receipt.resultStatus, "DRAFT");
  assert.equal(timeReceipt.receipt.after.durationMinutes, 45);

  const csvName = "synthetic-trial-balance.csv";
  const csv = Buffer.from([
    "PeriodCode,AccountCode,AccountName,NetClosingBalance,Currency,Entity,MappingCode",
    "FY26,1000,Synthetic cash,100.123456,QAR,SYN-ENTITY,",
    "FY26,3000,Synthetic equity,-100.123456,QAR,SYN-ENTITY,", ""
  ].join("\n"));
  await writeFile(join(directory, csvName), csv, { flag: "wx", mode: 0o600 });
  const inspect = { path: "/api/ui/engagements/{id}/tb-intake/preview", fileName: csvName, sha256: sha256(csv),
    session: staff.session, pathParameters: { id: engagementId } };
  const preview = success(await staff.call("inspect_file", inspect), "preview exact trial balance multipart bytes");
  assert.equal(preview.value.fileSha256, inspect.sha256);
  assert.equal(preview.value.canImport, true);
  assert.equal(preview.value.periods[0].datasetId, null);

  const pathParameters = { id: requestId };
  assert.equal(unpack(await read(other.call, pbcRequest, { pathParameters, session: other.session })).status, 403);
  const reply = { method: "POST", path: pbcRequest + "/reply", pathParameters, session: client.session,
    body: { requestId: randomUUID(), body: "Synthetic MCP reply" } };
  assert.equal(unpack(await other.call("write_api", { ...reply, session: other.session })).status, 403);
  const written = success(await client.call("write_api", reply), "save a scoped PBC reply");
  assert.deepEqual(success(await client.call("write_api", reply), "replay original reply"), written);
  const retained = success(await read(client.call, pbcRequest + "/reply-receipts/{requestId}", {
    pathParameters: { ...pathParameters, requestId: reply.body.requestId }, session: client.session
  }), "read original reply receipt");
  assert.deepEqual(retained, written.value);

  const fileName = "synthetic-mcp.txt";
  const bytes = Buffer.from("Synthetic PBC evidence for a disposable test session.\n");
  await writeFile(join(directory, fileName), bytes, { flag: "wx", mode: 0o600 });
  const upload = { requestId, fileName, sha256: sha256(bytes), session: client.session, contentType: "text/plain" };
  const staged = success(await client.call("stage_pbc_file", upload), "stage a scoped PBC file");
  assert.equal(staged.uploadState, "CHUNKING");
  assert.equal(staged.byteCount, bytes.length);
  const replay = success(await client.call("stage_pbc_file", upload), "reconcile a staged file");
  assert.equal(replay.uploadId, staged.uploadId);
  assert.equal(replay.newBytes, false);
  const request = success(await read(client.call, pbcRequest, { pathParameters, session: client.session }), "read staged upload");
  assert.equal(request.uploads.filter(item => item.id === staged.uploadId && item.state === "CHUNKING" &&
    item.sha256 === upload.sha256 && item.receivedByteCount === String(bytes.length)).length, 1);
  assert.equal(unpack(await client.call("write_api", {
    method: "POST", path: "/api/ui/pbc-uploads/{id}/complete", pathParameters: { id: staged.uploadId },
    session: client.session, body: { declaredSha256: upload.sha256 }
  })).code, "mcp.workspace-required");

  const exportArgs = { method: "POST", path: "/api/ui/portfolio/export", body: { search: null },
    fileName: "portfolio.csv", session: staff.session };
  assert.equal(unpack(await client.call("download_file", { ...exportArgs, session: client.session })).status, 403);
  const exportResult = success(await staff.call("download_file", exportArgs), "download authorized portfolio CSV");
  const downloaded = await readFile(join(directory, exportArgs.fileName));
  assert.equal(exportResult.contentType, "text/csv");
  assert.equal(exportResult.sha256, sha256(downloaded));
  assert.equal(exportResult.byteCount, downloaded.length);
  assert.equal(unpack(await staff.call("download_file", exportArgs)).code, "mcp.file-exists");
  console.log("PASS: owned Test cookies, scoped reads and denials, profile edit, workpaper and time draft edits, exact receipts, reply replay, multipart preview, PBC staging and authorized file export.");
} finally {
  await Promise.all(browsers.map(browser => browser.close()));
  await rm(directory, { recursive: true, force: true });
}
