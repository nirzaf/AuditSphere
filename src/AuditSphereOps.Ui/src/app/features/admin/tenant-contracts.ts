import { arr, bool, guid, instant, nullable, obj, text } from '../../core/decode';

export const capabilityStatus = obj({ capability: text, displayName: text, permission: text, enabled: bool, state: text,
  lastVerifiedAt: nullable(instant), stale: bool, diagnosticCode: text });
export const tenantWorkspace = obj({ consentConfigured: bool, directoryConfigured: bool, simulation: bool,
  preparationConfigured: bool, configuredTenantId: nullable(guid),
  workspace: obj({ connection: obj({ draftId: nullable(guid), expectedTenantId: nullable(guid), draftState: nullable(text),
    connectionState: nullable(text), consentState: nullable(text), lastAttemptState: nullable(text), lastAttemptAt: nullable(instant),
    directoryCapabilityState: text, directoryLastCheckedAt: nullable(instant) }), capabilities: arr(capabilityStatus, 50),
    setupProgress: nullable(obj({ tenantRecorded: bool, siteUrlRecorded: bool, connectionPrepared: bool, consentEvidenceRecorded: bool,
      consentVerified: bool, selectedResourcesEvidenceRecorded: bool, clientTemplateApproved: bool, workspaceActivated: bool })),
    consentingAdministrator: nullable(obj({ tenantId: guid, objectId: guid, verifiedAt: nullable(instant) })), draftRevision: nullable(text) }) });
export const directoryCandidate = obj({ tenantId: guid, objectId: guid, displayName: text, userPrincipalName: text,
  accountEnabled: bool, userType: text });
export const directoryPage = obj({ users: arr(directoryCandidate, 25), nextPageToken: nullable(text) });

/** Only the server's fixed Microsoft consent endpoint or the Test/Development simulator may receive this redirect. */
export function consentDestination(value: unknown, tenant: string, simulation: boolean): string {
  if (!value || typeof value !== 'object' || !('redirect' in value) || typeof value.redirect !== 'string') throw new Error('Invalid consent destination');
  const url = new URL(value.redirect, location.origin);
  if (simulation) {
    if (url.origin !== location.origin || url.pathname !== '/auth/m365-consent/simulated-consent' || !url.searchParams.get('state')) throw new Error('Invalid simulator destination');
  } else if (url.protocol !== 'https:' || url.host !== 'login.microsoftonline.com' || url.username || url.password ||
    url.pathname.toLowerCase() !== `/${tenant.toLowerCase()}/v2.0/adminconsent` || !url.searchParams.get('state') || url.hash) throw new Error('Invalid Microsoft destination');
  return url.href;
}
