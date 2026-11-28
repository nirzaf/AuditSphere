import { mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { chromium } from "playwright-core";

export function chromeSession(origin, { headless = false } = {}) {
  let profile;
  let context;
  let apiPage;

  async function browser() {
    if (!context) {
      profile ??= await mkdtemp(join(tmpdir(), "auditsphere-mcp-"));
      context = await chromium.launchPersistentContext(profile, {
        channel: "chrome", headless, chromiumSandbox: true,
        acceptDownloads: false, serviceWorkers: "block", timeout: 15_000
      });
      context.on("close", () => { context = undefined; apiPage = undefined; });
    }
    return context;
  }

  return {
    async open(path) {
      const page = await (await browser()).newPage();
      await page.goto(origin + path, { waitUntil: "commit", timeout: 15_000 });
      await page.bringToFront();
    },
    async send(request) {
      if (!apiPage || apiPage.isClosed()) {
        apiPage = await (await browser()).newPage();
        await apiPage.goto(origin + "/health/live", { waitUntil: "domcontentloaded", timeout: 15_000 });
      }
      if (new URL(apiPage.url()).origin !== origin) return { code: "mcp.origin-changed" };
      try {
        return await apiPage.evaluate(browserRequest, { origin, ...request });
      } catch {
        // A navigation or tab close can interrupt evaluate after the request left.
        return { code: "mcp.transport", unknown: request.changesState };
      }
    },
    async close() {
      await context?.close().catch(() => {});
      if (profile) await rm(profile, { recursive: true, force: true }).catch(() => {});
      profile = undefined;
    }
  };
}

// Runs in the dedicated AuditSphere tab. Session cookies, XSRF tokens, and
// Chromium's Origin handling remain in the browser.
async function browserRequest({ origin, path, method, session, json, file, chunk, downloadTypes, maxBytes, changesState }) {
  if (location.origin !== origin || new URL(path, location.href).origin !== origin) {
    return { code: "mcp.origin-changed" };
  }
  const timeout = AbortSignal.timeout(file || chunk || downloadTypes ? 60_000 : 20_000);
  const request = { credentials: "same-origin", mode: "same-origin", redirect: "error", cache: "no-store", signal: timeout };
  const jsonType = /^(?:application\/json|application\/problem\+json)$/i;
  const mime = response => (response.headers.get("content-type") ?? "").split(";")[0].trim().toLowerCase();

  async function bounded(response, limit) {
    if (!response.body) return new Uint8Array();
    const reader = response.body.getReader();
    const chunks = [];
    let count = 0;
    try {
      for (;;) {
        const { value, done } = await reader.read();
        if (done) break;
        count += value.byteLength;
        if (count > limit) {
          await reader.cancel();
          return null;
        }
        chunks.push(value);
      }
    } finally {
      reader.releaseLock();
    }
    const bytes = new Uint8Array(count);
    let position = 0;
    for (const part of chunks) {
      bytes.set(part, position);
      position += part.byteLength;
    }
    return bytes;
  }

  async function checkSession() {
    const response = await fetch("/api/ui/session", { ...request, headers: { Accept: "application/json" } });
    if (!response.ok || !jsonType.test(mime(response))) {
      await response.body?.cancel();
      return { code: response.status === 401 ? "session.unavailable" : "mcp.session-check", status: response.status };
    }
    const bytes = await bounded(response, 8_192);
    let current;
    try { current = bytes && JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(bytes)); } catch { /* fail closed */ }
    if (!current || current.userId !== session.userId || current.firmId !== session.firmId ||
        current.generation !== session.generation) {
      return { code: "mcp.session-changed" };
    }
    return null;
  }

  function decodeBase64(data) {
    const text = atob(data);
    const result = new Uint8Array(text.length);
    for (let i = 0; i < text.length; i++) result[i] = text.charCodeAt(i);
    return result;
  }

  function base64(bytes) {
    if (typeof bytes.toBase64 === "function") return bytes.toBase64();
    const parts = [];
    for (let i = 0; i < bytes.length; i += 16_384) {
      parts.push(String.fromCharCode(...bytes.subarray(i, i + 16_384)));
    }
    return btoa(parts.join(""));
  }

  const headers = { Accept: downloadTypes?.join(", ") ?? "application/json" };
  let sent = false;
  try {
    if (session) {
      const problem = await checkSession();
      if (problem) return problem;
    }
    if (method !== "GET" && !chunk) {
      // Read only the Angular antiforgery cookie, never authentication tokens.
      const prefix = "XSRF-TOKEN=";
      const tokens = document.cookie.split(/;\s*/).filter(part => part.startsWith(prefix));
      if (tokens.length !== 1) return { code: "csrf.unavailable" };
      headers["X-XSRF-TOKEN"] = decodeURIComponent(tokens[0].slice(prefix.length));
      if (!headers["X-XSRF-TOKEN"]) return { code: "csrf.unavailable" };
    }

    let body;
    if (file) {
      body = new FormData();
      body.append("file", new Blob([decodeBase64(file.data)], { type: file.contentType }), file.name);
    } else if (chunk) {
      headers["Content-Type"] = "application/octet-stream";
      headers["X-Upload-Offset"] = String(chunk.offset);
      headers["X-Content-SHA256"] = chunk.sha256;
      headers["X-Pbc-Upload-Capability"] = chunk.capability;
      body = decodeBase64(chunk.data);
    } else if (json !== undefined) {
      headers["Content-Type"] = "application/json";
      body = json;
    }

    sent = true;
    const response = await fetch(path, { ...request, method, headers, body });
    const info = {
      status: response.status,
      correlationId: response.headers.get("X-Correlation-Id"),
      retryAfter: response.headers.get("Retry-After")
    };
    const contentType = mime(response);
    const decoder = new TextDecoder("utf-8", { fatal: true });
    if (!response.ok) {
      let code;
      if (jsonType.test(contentType)) {
        // Malformed or oversized errors still retain their HTTP status.
        try {
          const bytes = await bounded(response, 8_192);
          code = bytes && JSON.parse(decoder.decode(bytes))?.code;
        } catch { /* suppress raw provider or framework errors */ }
      } else {
        await response.body?.cancel();
      }
      return { ...info, code, unknown: changesState && response.status >= 500 };
    }
    if (downloadTypes) {
      // PBC downloads can declare arbitrary file types, but must be attachments.
      const attachment = /^attachment(?:;|$)/i.test(response.headers.get("Content-Disposition") ?? "");
      if (response.status !== 200 || !contentType || !(
        downloadTypes.includes(contentType) || (downloadTypes.includes("*/*") && attachment)
      )) {
        await response.body?.cancel();
        return { ...info, code: "mcp.unexpected-file" };
      }
      const bytes = await bounded(response, maxBytes);
      if (!bytes) return { ...info, code: "mcp.response-too-large" };
      const problem = await checkSession();
      if (problem) return problem;
      const etag = response.headers.get("ETag");
      return { ...info, contentType, data: base64(bytes),
        sha256: response.headers.get("X-Content-SHA256") ?? response.headers.get("X-Firm-Journal-Evidence-SHA256") ??
          response.headers.get("X-Statement-Presentation-SHA256") ??
          (/^"[0-9a-f]{64}"$/i.test(etag ?? "") ? etag.slice(1, -1) : null) };
    }
    if (!jsonType.test(contentType)) {
      await response.body?.cancel();
      return { ...info, code: "mcp.unexpected-response", unknown: changesState };
    }
    const bytes = await bounded(response, 65_536);
    if (!bytes) return { ...info, code: "mcp.response-too-large", unknown: changesState };
    const raw = decoder.decode(bytes);
    try { JSON.parse(raw); } catch { return { ...info, code: "mcp.unexpected-response", unknown: changesState }; }
    if (session) {
      const problem = await checkSession();
      if (problem) return { ...problem, unknown: changesState };
    }
    return { ...info, json: raw };
  } catch {
    return { code: "mcp.transport", unknown: Boolean(sent && changesState) };
  }
}
