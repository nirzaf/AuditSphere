import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { apiPath, catalog, originOf, searchCatalogue, workspacePath } from "./catalog.mjs";

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
