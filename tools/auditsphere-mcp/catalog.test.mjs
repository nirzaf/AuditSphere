import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";
import { apiPath, catalog, originOf, searchCatalogue, workspacePath } from "./catalog.mjs";
import { direct } from "./operations.mjs";

const contract = JSON.parse(await readFile(new URL("../../contracts/auditsphere-openapi.json", import.meta.url), "utf8"));
const routes = await readFile(new URL("../../src/AuditSphereOps.Ui/src/app/app.routes.ts", import.meta.url), "utf8");
const entries = catalog(contract, routes);

test("the live contract offers discovery and safe reads without treating commands as reads", () => {
  assert.ok(searchCatalogue(entries, "administration access preview").operations.some(
    result => result.method === "POST" && !result.directRead));
  assert.equal(apiPath(entries.operations.get("GET /api/ui/session")), "/api/ui/session");
  assert.throws(() => apiPath(entries.operations.get("POST /api/ui/administration/access/preview")));
  assert.throws(() => apiPath(entries.operations.get("GET /api/ui/records/archives/{id}/export")));
  assert.throws(() => apiPath(entries.operations.get("GET /auth/sign-in")));
});

test("the direct route inventory is exact and gated operations require the workspace", () => {
  for (const [route, policy] of direct) {
    assert.equal(entries.operations.get(route)?.mode, policy.mode, route);
    assert.ok(entries.workspaces.has(policy.workspace), policy.workspace);
  }
  for (const key of [
    "POST /api/ui/administration/access/assign",
    "POST /api/ui/clients/{id}/acceptance/decision",
    "POST /api/ui/pbc-uploads/{id}/complete",
    "POST /api/ui/engagements/{id}/tb-intake/import",
    "POST /api/ui/engagements/{id}/general-ledger/import",
    "POST /api/ui/accounting/journals/{id}/instructions",
    "POST /api/ui/accounting/packages/{id}/presentation/pins/{revisionId}/artifacts",
    "POST /api/ui/audit/workpapers/{id}/draft",
    "POST /api/ui/audit/workpapers/{id}/submit"
  ]) assert.equal(entries.operations.get(key)?.mode, "workspace", key);
  assert.equal(entries.operations.get("GET /api/ui/consolidation/scopes/{scopeId}/deliverables/issued/download").readable,
    false);
  assert.equal(entries.operations.has("POST /api/setup/bootstrap"), false);

  const changed = structuredClone(contract);
  changed.paths["/api/ui/me/profile"].put.security = [];
  changed.paths["/api/ui/session"].get.security.push({});
  changed.paths["/api/ui/portal/requests/{id}/reply"].post.requestBody.content["application/json"].schema.$ref =
    "#/components/schemas/PortalAcknowledgementInput";
  delete changed.components.schemas.WorkpaperCommandFields.properties.save;
  const altered = catalog(changed, routes).operations;
  assert.equal(altered.get("PUT /api/ui/me/profile").mode, "workspace");
  assert.equal(altered.get("GET /api/ui/session").mode, "workspace");
  assert.equal(altered.get("POST /api/ui/portal/requests/{id}/reply").mode, "workspace");
  assert.equal(altered.get("POST /api/ui/audit/workpapers/{id}/commands/actions").mode, "workspace");
});

test("parameters stay in the chosen endpoint and query fields are explicit", () => {
  const lifecycle = entries.operations.get("GET /api/ui/engagements/{id}/lifecycle");
  const id = "12345678-1234-1234-1234-123456789abc";
  assert.equal(apiPath(lifecycle, { id }), "/api/ui/engagements/" + id + "/lifecycle");
  for (const invalid of ["..", "%2f", "https://example.test", "bad?x=1"]) {
    assert.throws(() => apiPath(lifecycle, { id: invalid }), /Invalid path/);
  }
  assert.throws(() => apiPath(lifecycle), /Missing required/);
  assert.throws(() => apiPath(lifecycle, { id }, { bypass: true }), /Unknown parameter/);
  const search = entries.operations.get("GET /api/ui/search");
  assert.equal(apiPath(search, {}, { term: "A&B" }), "/api/ui/search?term=A%26B");
});

test("origin and workspace navigation reject external and unauthorized paths", () => {
  assert.equal(originOf("https://audit.steaudit.com/"), "https://audit.steaudit.com");
  assert.throws(() => originOf("https://user:secret@example.test/"));
  assert.throws(() => originOf("http://example.test/"));
  const users = "/ui/app/administration/users";
  assert.equal(workspacePath(entries.workspaces, users), users);
  assert.throws(() => workspacePath(entries.workspaces, "/auth/sign-in"));
  assert.throws(() => workspacePath(entries.workspaces, "//example.test"));
});

test("the stdio server rejects unsupported requests before opening Chrome", { timeout: 10_000 }, async () => {
  const client = new Client({ name: "auditsphere-test", version: "1.0.0" });
  const transport = new StdioClientTransport({
    command: process.execPath,
    args: [fileURLToPath(new URL("server.mjs", import.meta.url)), "https://audit.example"],
    stderr: "pipe"
  });
  try {
    await client.connect(transport);
    assert.deepEqual((await client.listTools()).tools.map(tool => tool.name).sort(),
      ["describe_operation", "download_file", "find_features", "inspect_file", "open_sign_in", "open_workspace",
        "preview_api", "read_api", "stage_pbc_file", "write_api"]);
    const discovered = await client.callTool({ name: "find_features", arguments: { query: "administration access assign" } });
    assert.equal(discovered.isError, undefined);
    assert.ok(JSON.parse(discovered.content[0].text).operations.some(
      operation => operation.method === "POST" && !operation.directRead));

    const session = { userId: "12345678-1234-1234-1234-123456789abc",
      firmId: "12345678-1234-1234-1234-000000000003", generation: "4" };
    for (const [name, args, message] of [
      ["read_api", { path: "https://other.example/api/ui/session" }, /Only documented JSON GET/],
      ["read_api", { path: "/api/ui/administration/access/assign" }, /Only documented JSON GET/],
      ["read_api", { path: "/api/ui/session", query: { token: "unexpected" } }, /Unknown parameter/],
      ["open_workspace", { path: "/ui/app/administration/unknown" }, /Select a documented workspace/],
      ["write_api", { method: "POST", path: "/api/ui/administration/access/assign", session, body: { reviewed: true } },
        /Use the AuditSphere workspace/],
      ["write_api", { method: "POST", path: "/api/ui/audit/workpapers/{id}/commands/actions",
        pathParameters: { id: session.userId }, session, body: { requestId: session.firmId, fields: { action: "SUBMIT" },
          expectedRequestHash: "a".repeat(64), reviewed: true } }, /bodySchema/],
      ["write_api", { method: "PUT", path: "/api/ui/me/profile", session,
        body: { position: "Tester", gender: null, dateOfBirth: null, mobileNumber: null,
          joinedDate: null, expectedRevision: 1, reviewed: true } }, /bodySchema/],
      ["write_api", { method: "POST", path: "/api/ui/practice/time/entries/{id}/changes", session,
        pathParameters: { id: session.userId }, body: { requestId: session.firmId, expectedRevision: "1",
          kind: "CORRECT_APPROVED", workDate: "2026-11-20", startMinute: 540, durationMinutes: 60,
          activity: "Synthetic effort", narrative: null, reason: "Synthetic edit" } }, /bodySchema/],
      ["inspect_file", { path: "/api/ui/engagements/{id}/tb-intake/import", session, fileName: "input.csv", sha256: "a".repeat(64) },
        /Use the AuditSphere workspace/],
      ["download_file", { path: "https://other.example/file", session, fileName: "evidence.pdf" },
        /Choose a supported download/],
      ["stage_pbc_file", { requestId: session.userId, session, fileName: "../secret", sha256: "a".repeat(64) }, /input schema/],
      ["read_api", { path: "/api/ui/session", headers: { Cookie: "synthetic" } }, /input schema/]
    ]) {
      const denied = await client.callTool({ name, arguments: args });
      assert.equal(denied.isError, true);
      assert.match(JSON.parse(denied.content[0].text).error, message);
    }
  } finally {
    await client.close();
  }
});
