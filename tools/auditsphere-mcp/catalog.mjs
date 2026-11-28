import { direct } from "./operations.mjs";

const apiMethods = new Set(["get", "post", "put", "patch", "delete"]);
const safeSegment = /^[A-Za-z0-9][A-Za-z0-9._~-]{0,127}$/;
export const uuidPattern = "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$";
const uuid = new RegExp(uuidPattern);
export const isId = value => typeof value === "string" && uuid.test(value) &&
  value !== "00000000-0000-0000-0000-000000000000";
const schemaPrefix = "#/components/schemas/";

export function catalog(contract, routes) {
  const operations = new Map();
  for (const [path, item] of Object.entries(contract.paths)) {
    for (const [method, operation] of Object.entries(item)) {
      if (!apiMethods.has(method)) continue;
      const key = method.toUpperCase() + " " + path;
      const policy = direct.get(key);
      if (!path.startsWith("/api/ui/") && !policy) continue;
      const success = Object.entries(operation.responses ?? {}).filter(([status]) => /^2\d\d$/.test(status));
      const content = success.flatMap(([, response]) => Object.keys(response.content ?? {}));
      const json = success.length > 0 && success.every(([, response]) =>
        Object.keys(response.content ?? {}).length === 1 &&
        !!response.content?.["application/json"] &&
        response.content["application/json"].schema?.$ref !== schemaPrefix + "Stream");
      const session = operation.security?.length > 0 &&
        operation.security.every(requirement => Object.hasOwn(requirement, "sessionCookie"));
      const readable = !policy && method === "get" && session && json;
      const request = operation.requestBody?.content;
      let active;
      let bodySchema;
      const compatibleResponse = policy?.mode === "download"
        ? content.length === policy.types.length && policy.types.every(type => content.includes(type))
        : json;
      if (policy && session && compatibleResponse) {
        const mime = policy.form ? "multipart/form-data" : policy.stream ? "application/octet-stream" : "application/json";
        const shape = policy.form ?? policy.stream ?? policy.body;
        if (shape ? Object.keys(request ?? {}).length === 1 && request[mime]?.schema?.$ref === schemaPrefix + shape :
          !operation.requestBody) {
          try {
            bodySchema = policy.body ? requestSchema(contract, policy) : undefined;
            active = policy;
          } catch {
            // A changed request contract stays available for discovery and native use.
          }
        }
      }
      operations.set(key, {
        method: method.toUpperCase(), path, readable,
        mode: active?.mode ?? (readable ? "read" : "workspace"),
        summary: operation.summary ?? operation.description ?? null,
        parameters: operation.parameters ?? [],
        requestBody: operation.requestBody ?? null,
        responseTypes: [...new Set(content)],
        ...(active && { workspace: active.workspace, recovery: active.recovery, maxBytes: active.maxBytes }),
        ...(bodySchema && { bodySchema })
      });
    }
  }
  const workspaces = new Set([...routes.matchAll(/\bpath:\s*'((?:app|portal)(?:\/[A-Za-z0-9_:-]+)*)'/g)]
    .map(([, path]) => "/ui/" + path));
  return { operations, workspaces };
}

function requestSchema(contract, policy) {
  const body = boundedBody(contract, policy.body, policy.limits);
  if (!policy.workpaperSave) return body;
  // The shared native endpoint also accepts submit/discard. Only the draft
  // branch is available here, with the server's exact prepare-save hash.
  const save = body.properties.fields.properties.save.oneOf.find(candidate => candidate.type === "object");
  if (!save) throw new Error("Unsupported workpaper draft schema.");
  const properties = {
    requestId: body.properties.requestId,
    fields: { type: "object", additionalProperties: false, required: ["action", "save"],
      properties: { action: { const: "SAVE_DRAFT" }, save } }
  };
  if (policy.mode === "write") {
    properties.expectedRequestHash = { type: "string", pattern: "^[0-9a-f]{64}$" };
  }
  return { type: "object", properties, required: Object.keys(properties), additionalProperties: false };
}

function boundedBody(contract, name, limits = {}) {
  // Only explicitly admitted DTOs enter this supported subset of OpenAPI.
  function bounded(schema, depth = 0) {
    if (depth > 12 || !schema) throw new Error("Unsupported MCP request schema.");
    if (schema.$ref) {
      if (!schema.$ref.startsWith(schemaPrefix)) throw new Error("Unsupported MCP request reference.");
      return bounded(contract.components.schemas[schema.$ref.slice(schemaPrefix.length)], depth + 1);
    }
    const result = { ...schema };
    if (result.properties) {
      result.additionalProperties = false;
      result.properties = Object.fromEntries(Object.entries(result.properties)
        .map(([key, property]) => [key, { ...bounded(property, depth + 1), ...(depth === 0 && limits[key]) }]));
    }
    if (result.items) {
      result.items = bounded(result.items, depth + 1);
      result.maxItems = Math.min(result.maxItems ?? 100, 100);
    }
    if (result.format === "int64") {
      result.type = Array.isArray(result.type) && result.type.includes("null") ? ["null", "string"] : "string";
      result.maxLength = 20;
      result.pattern ??= "^-?(?:0|[1-9][0-9]*)$";
    }
    if (result.type === "string" || result.type?.includes("string")) result.maxLength ??= 16_384;
    if (result.format === "uuid") result.pattern = uuidPattern;
    for (const keyword of ["anyOf", "oneOf", "allOf"]) {
      if (result[keyword]) result[keyword] = result[keyword].map(candidate => bounded(candidate, depth + 1));
    }
    return result;
  }
  return bounded(contract.components.schemas[name]);
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
}

function segment(value, format) {
  if (format === "int32" && Number.isInteger(value) && value >= 0 && value <= 2_147_483_647) {
    return String(value);
  }
  if (typeof value !== "string" || !safeSegment.test(value) ||
      (format === "uuid" && !isId(value)) ||
      (format === "int32" && (!/^(0|[1-9]\d*)$/.test(value) || Number(value) > 2_147_483_647))) {
    throw new Error("Invalid path parameter.");
  }
  return value;
}

export function requestPath(operation, pathParameters = {}, query = {}) {
  const pathSpecs = operation.parameters.filter(parameter => parameter.in === "path");
  const pathKeys = [...operation.path.matchAll(/\{([^}]+)\}/g)].map(([, key]) => key);
  parameters(pathParameters, pathKeys, pathKeys);
  const path = operation.path.replace(/\{([^}]+)\}/g, (_, key) =>
    segment(pathParameters[key], pathSpecs.find(parameter => parameter.name === key)?.schema?.format));
  const specs = operation.parameters.filter(parameter => parameter.in === "query");
  parameters(query, specs.map(x => x.name), specs.filter(x => x.required).map(x => x.name));
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    const spec = specs.find(x => x.name === key).schema;
    const array = spec?.type === "array";
    const values = array ? value : [value];
    if (!Array.isArray(values) || !values.length || values.length > 25 ||
        values.some(item => !validQuery(item, array ? spec.items : spec))) {
      throw new Error("Invalid or oversized query parameter.");
    }
    for (const item of values) search.append(key, String(item));
  }
  if (search.toString().length > 8_192) throw new Error("Invalid or oversized query parameter.");
  return path + (search.size ? "?" + search.toString() : "");
}

function validQuery(value, schema) {
  const kind = Number.isInteger(value) ? "integer" : typeof value;
  if (!["string", "boolean", "integer", "number"].includes(kind) ||
      (typeof value === "number" && (!Number.isFinite(value) || (kind === "integer" && !Number.isSafeInteger(value)))) ||
      String(value).length > Math.min(512, schema?.maxLength ?? 512)) return false;
  const types = Array.isArray(schema?.type) ? schema.type : [schema?.type];
  if (schema?.type && !types.includes(kind) && !(kind === "integer" && types.includes("number"))) return false;
  if (schema?.format === "uuid" && !isId(value)) return false;
  if (schema?.enum && !schema.enum.includes(value)) return false;
  if (schema?.pattern && typeof value === "string" && !new RegExp(schema.pattern).test(value)) return false;
  return true;
}

export function apiPath(operation, pathParameters, query) {
  if (!operation?.readable) throw new Error("Only documented JSON GET operations can run through MCP.");
  return requestPath(operation, pathParameters, query);
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
      .slice(0, limit).map(({ method, path, readable, mode }) => ({ method, path, directRead: readable, mode })),
    workspaces: [...workspaces].filter(matches).slice(0, limit)
  };
}
