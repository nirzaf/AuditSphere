const apiMethods = new Set(["get", "post", "put", "patch", "delete"]);
const safeSegment = /^[A-Za-z0-9][A-Za-z0-9._~-]{0,127}$/;
const uuid = /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i;

export function catalog(contract, routes) {
  const operations = new Map();
  for (const [path, item] of Object.entries(contract.paths)) {
    if (!path.startsWith("/api/ui/")) continue;
    for (const [method, operation] of Object.entries(item)) {
      if (!apiMethods.has(method)) continue;
      const success = Object.entries(operation.responses ?? {}).filter(([status]) => /^2\d\d$/.test(status));
      const readable = method === "get" && success.length > 0 && success.every(
        ([, response]) => Object.keys(response.content ?? {}).length === 1 &&
          Object.hasOwn(response.content, "application/json")
      );
      operations.set(method.toUpperCase() + " " + path, {
        method: method.toUpperCase(), path, readable,
        summary: operation.summary ?? operation.description ?? null,
        parameters: operation.parameters ?? [],
        requestBody: operation.requestBody ?? null
      });
    }
  }
  // The root Angular route file uses literal workspace paths. Only known app and portal paths are exposed.
  const workspaces = new Set([...routes.matchAll(/\bpath:\s*'((?:app|portal)(?:\/[A-Za-z0-9_:-]+)*)'/g)]
    .map(([, path]) => "/ui/" + path));
  return { operations, workspaces };
}

export function originOf(raw) {
  const url = new URL(raw);
  if (url.protocol !== "https:" || url.username || url.password || url.pathname !== "/" ||
      url.search || url.hash) throw new Error("Configure a single HTTPS origin.");
  return url.origin;
}

function parameters(values, names, requiredNames) {
  if (values === null || typeof values !== "object" || Array.isArray(values)) {
    throw new Error("Parameters must be an object.");
  }
  if (Object.keys(values).some(key => !names.includes(key))) throw new Error("Unknown parameter.");
  if (requiredNames.some(key => !Object.hasOwn(values, key))) throw new Error("Missing required parameter.");
  return values;
}

function segment(value, format) {
  if (typeof value !== "string" || !safeSegment.test(value) || (format === "uuid" && !uuid.test(value))) {
    throw new Error("Invalid path parameter.");
  }
  return value;
}

export function apiPath(operation, pathParameters = {}, query = {}) {
  if (!operation?.readable) throw new Error("Only documented JSON GET operations can run through MCP.");
  const pathSpecs = operation.parameters.filter(parameter => parameter.in === "path");
  const pathKeys = [...operation.path.matchAll(/\{([^}]+)\}/g)].map(([, key]) => key);
  parameters(pathParameters, pathKeys, pathKeys);
  const path = operation.path.replace(/\{([^}]+)\}/g, (_, key) =>
    segment(pathParameters[key], pathSpecs.find(parameter => parameter.name === key)?.schema?.format));
  const specs = operation.parameters.filter(parameter => parameter.in === "query");
  parameters(query, specs.map(x => x.name), specs.filter(x => x.required).map(x => x.name));
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    const values = Array.isArray(value) ? value : [value];
    if (!values.length || values.length > 25 || values.some(item =>
      !["string", "boolean", "number"].includes(typeof item) ||
      (typeof item === "number" && !Number.isFinite(item)) || String(item).length > 512)) {
      throw new Error("Invalid or oversized query parameter.");
    }
    for (const item of values) search.append(key, String(item));
  }
  return path + (search.size ? "?" + search.toString() : "");
}

export function workspacePath(workspaces, template, values = {}) {
  if (!workspaces.has(template)) throw new Error("Select a documented workspace from the catalogue.");
  const keys = [...template.matchAll(/:([A-Za-z0-9_]+)/g)].map(([, key]) => key);
  parameters(values, keys, keys);
  return template.replace(/:([A-Za-z0-9_]+)/g, (_, key) => segment(values[key], /id$/i.test(key) ? "uuid" : undefined));
}

export function searchCatalogue({ operations, workspaces }, query = "", limit = 20) {
  const terms = query.toLowerCase().trim().split(/\s+/).filter(Boolean);
  const matches = value => terms.every(term => value.toLowerCase().includes(term));
  return {
    operations: [...operations.values()].filter(x => matches(x.method + " " + x.path + " " + (x.summary ?? "")))
      .slice(0, limit).map(({ method, path, readable }) => ({ method, path, directRead: readable })),
    workspaces: [...workspaces].filter(matches).slice(0, limit)
  };
}
