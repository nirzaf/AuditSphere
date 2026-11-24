# AuditSphere MCP for Codex

This workstation service exposes AuditSphere’s OpenAPI operations and Angular workspaces to Codex. It uses the MCP stdio transport and the workstation’s installed Google Chrome. Each MCP process creates its own temporary Chrome profile for Microsoft sign-in. The process deletes that profile on normal shutdown.

## Install in Codex

Use a trusted checkout, Node.js 22 or later, and Google Chrome.

```bash
npm --prefix tools/auditsphere-mcp ci --ignore-scripts
npm --prefix tools/auditsphere-mcp test
codex mcp add auditsphere -- "$(command -v node)" \
  "$PWD/tools/auditsphere-mcp/server.mjs" https://audit.steaudit.com
codex mcp get auditsphere
```

Open a new Codex session after registration. For another AuditSphere installation, pass its HTTPS origin when registering the process. The tool caller cannot change that origin. Keep the checkout aligned with the app’s deployed API contract. To remove the registration, run `codex mcp remove auditsphere`.

The fast GitHub Actions workflow runs the catalogue and stdio tests in a separate job. These tests verify unsupported requests are rejected before Chrome starts. Microsoft sign-in and browser lifecycle checks use an installed Chrome session.

## Use from a Codex session

1. Ask Codex to call `open_sign_in` and complete Microsoft sign-in in the dedicated Chrome window. Account credentials and MFA stay in Chrome.
2. Use `find_features` to locate a documented operation or workspace. Use `describe_operation` to inspect the endpoint template, input parameters and request contract.
3. For a documented JSON GET, use `read_api` with the endpoint template and exact path or query parameters. For example, ask Codex to read `/api/ui/session` or find a specific client or engagement workspace.
4. For changes and approvals, use `open_workspace` to open the documented Angular screen. For example, `/ui/app/administration/users` opens Users & Access for a role assignment. Apply the change using the existing review and authorization flow. File operations also use the browser workspace.

## Access and deployment boundary

The MCP process has no network listener and requires no changes to Hostinger, Microsoft Entra registrations or AuditSphere privileges. API calls use the current browser session and retain the app’s tenant, role, scope, revocation and audit controls. The adapter sends no arbitrary URL, mutation, session cookie or browser token to Codex. Result data can include confidential information that the account is authorized to read. Use an approved Codex session for that data. Responses above 64 KiB require pagination or a browser workspace.
