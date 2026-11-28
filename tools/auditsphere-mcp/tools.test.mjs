import assert from "node:assert/strict";
import { lstat, mkdtemp, readFile, realpath, rm, symlink, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { catalog } from "./catalog.mjs";
import { fileExchange, sha256 } from "./files.mjs";
import { pbcRequest } from "./operations.mjs";
import { toolHandler } from "./tools.mjs";

const contract = JSON.parse(await readFile(new URL("../../contracts/auditsphere-openapi.json", import.meta.url), "utf8"));
const routes = await readFile(new URL("../../src/AuditSphereOps.Ui/src/app/app.routes.ts", import.meta.url), "utf8");
const entries = catalog(contract, routes);
const origin = "https://audit.example";
const userId = "12345678-1234-1234-1234-000000000001";
const firmId = "12345678-1234-1234-1234-000000000002";
const requestId = "12345678-1234-1234-1234-000000000003";
const replyId = "12345678-1234-1234-1234-000000000004";
const uploadId = "12345678-1234-1234-1234-000000000005";
const session = { userId, firmId, generation: "4" };
const unwrap = res => JSON.parse(res.content[0].text);
const json = value => ({ status: 200, json: JSON.stringify(value) });

test("a write preserves the exact request identity and returns a scoped readback after an unknown outcome", async () => {
  const calls = [];
  const browser = { send: async request => {
    calls.push(request);
    return { code: "mcp.transport", unknown: true };
  } };
  const call = toolHandler({ entries, browser, origin });
  const args = { method: "POST", path: pbcRequest + "/reply", session, pathParameters: { id: requestId },
    body: { requestId: replyId, body: "Synthetic reply" } };
  const invalid = await call("write_api", { ...args, body: { ...args.body, confirmed: true } });
  assert.equal(unwrap(invalid).code, "mcp.input");
  assert.equal(calls.length, 0);

  const response = unwrap(await call("write_api", args));
  assert.equal(response.outcome, "unknown");
  assert.equal(calls.length, 1);
  assert.deepEqual(JSON.parse(calls[0].json), args.body);
  assert.deepEqual(calls[0].session, session);
  assert.deepEqual(response.recovery, {
    tool: "read_api", arguments: { path: pbcRequest + "/reply-receipts/{requestId}", session,
      pathParameters: { id: requestId, requestId: replyId } }
  });
});

test("file transport verifies bytes and saves a private download exactly once", async t => {
  const directory = await mkdtemp(join(await realpath(tmpdir()), "auditsphere-files-test-"));
  t.after(() => rm(directory, { recursive: true, force: true }));
  const bytes = Buffer.from("Synthetic file.");
  const exchange = fileExchange(directory);
  await writeFile(join(directory, "source.csv"), bytes);
  await symlink(join(directory, "source.csv"), join(directory, "linked.csv"));
  const calls = [];
  const browser = { send: async request => {
    calls.push(request);
    return request.file ? json({ valid: true }) : {
      status: 200, contentType: "application/pdf", data: bytes.toString("base64"), sha256: sha256(bytes)
    };
  } };
  const call = toolHandler({ entries, browser, origin, exchange });
  const inspect = { path: "/api/ui/engagements/{id}/tb-intake/preview", pathParameters: { id: requestId },
    fileName: "source.csv", sha256: sha256(bytes), session };
  assert.equal(unwrap(await call("inspect_file", { ...inspect, fileName: "linked.csv" })).code, "mcp.file-unavailable");
  assert.equal(unwrap(await call("inspect_file", { ...inspect, sha256: "0".repeat(64) })).code, "mcp.file-changed");
  assert.equal(calls.length, 0);
  assert.deepEqual(unwrap(await call("inspect_file", inspect)), { valid: true });
  assert.deepEqual(Buffer.from(calls[0].file.data, "base64"), bytes);

  const download = { path: "/api/representation-scans/{scanId}/download", pathParameters: { scanId: requestId },
    fileName: "scan.pdf", session };
  assert.equal(unwrap(await call("download_file", { ...download, expectedSha256: "0".repeat(64) })).code, "mcp.file-integrity");
  assert.deepEqual(unwrap(await call("download_file", download)), {
    fileName: "scan.pdf", byteCount: bytes.length, contentType: "application/pdf", sha256: sha256(bytes)
  });
  assert.deepEqual(await readFile(join(directory, "scan.pdf")), bytes);
  assert.equal((await lstat(join(directory, "scan.pdf"))).mode & 0o777, 0o600);
  assert.equal(unwrap(await call("download_file", download)).code, "mcp.file-exists");
  assert.equal(calls.length, 3);
});

test("PBC staging reports partial progress after a lost chunk response, resumes by receipt and never completes", async () => {
  const bytes = Buffer.alloc(3 * 1024 * 1024, 7);
  const fileName = "trial.csv";
  const sha = sha256(bytes);
  const capability = "d".repeat(64);
  const uploads = [];
  const calls = [];
  let loseResponse = true;
  const path = "/api/ui/portal/requests/" + requestId;
  const browser = { send: async request => {
    calls.push(request);
    if (request.method === "GET") {
      assert.equal(request.path, path);
      return json({ id: requestId, canUpload: true, firstSignIn: { completed: true }, uploads });
    }
    if (request.path === path + "/uploads") {
      assert.deepEqual(JSON.parse(request.json), {
        fileName, byteCount: String(bytes.length), sha256: sha, contentType: "application/octet-stream"
      });
      uploads.push({ id: uploadId, state: "STARTED", fileName, sha256: sha, declaredByteCount: String(bytes.length),
        receivedByteCount: "0" });
      return json({ value: { uploadIntentId: uploadId, pbcRequestId: requestId, state: "STARTED", receivedByteCount: 0, capability } });
    }
    if (request.path === path + "/uploads/" + uploadId + "/resume") {
      return json({ value: { uploadIntentId: uploadId, pbcRequestId: requestId, state: "CHUNKING",
        receivedByteCount: uploads[0].receivedByteCount, nextChunkIndex: 1, capability } });
    }
    const index = Number(request.path.match(/\/chunks\/(\d+)$/)?.[1]);
    assert.ok(Number.isSafeInteger(index), request.path);
    const { offset, sha256: hash, capability: cap, data } = request.chunk;
    const content = Buffer.from(data, "base64");
    assert.equal(cap, capability);
    assert.equal(hash, sha256(content));
    assert.equal(String(offset), uploads[0].receivedByteCount);
    uploads[0].state = "CHUNKING";
    uploads[0].receivedByteCount = String(offset + content.length);
    if (loseResponse) {
      loseResponse = false;
      return { code: "mcp.transport", unknown: true };
    }
    return json({ uploadId, chunkIndex: index, offset, byteCount: content.length, state: "CHUNKING" });
  } };
  const call = toolHandler({ entries, browser, origin, exchange: { source: async () => bytes } });
  const args = { requestId, fileName, sha256: sha, session };
  const fail = unwrap(await call("stage_pbc_file", args));
  assert.equal(fail.outcome, "unknown");
  assert.equal(fail.recovery.uploadId, uploadId);
  assert.equal(calls.length, 3, "the transfer makes no retries after an unknown outcome");
  assert.equal(unwrap(await call("stage_pbc_file", args)).code, "mcp.upload-reconcile");

  const resumed = await call("stage_pbc_file", { ...args, uploadId });
  assert.equal(resumed.isError, undefined);
  assert.equal(unwrap(resumed).uploadState, "CHUNKING");
  assert.equal(unwrap(resumed).newBytes, true);
  assert.ok(!resumed.content[0].text.includes(capability));
  const recovered = unwrap(await call("stage_pbc_file", args));
  assert.equal(recovered.newBytes, false);
  assert.equal(uploads.length, 1);
  assert.equal(calls.filter(call => call.path.endsWith("/uploads")).length, 1);
  assert.ok(calls.every(call => !call.path.endsWith("/complete")));
});
