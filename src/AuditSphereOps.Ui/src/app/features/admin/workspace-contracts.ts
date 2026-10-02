import { arr, bool, guid, instant, nat, nullable, obj, oneOf, sha256, str, text } from '../../core/decode';

export const workspacePage = obj({ providerConfigured: bool, workspace: obj({ page: nat, hasMore: bool,
  clients: arr(obj({ clientId: guid, clientName: text, workspaceState: text, lastErrorCode: nullable(text), lastVerifiedAt: nullable(instant),
    engagements: arr(obj({ engagementId: guid, label: text, bindingState: text, testedAt: nullable(instant) }), 25) }), 25) }) });
export const workspaceReview = obj({ targetId: guid, kind: oneOf('CLIENT', 'ENGAGEMENT'), name: text, currentState: text,
  reviewToken: sha256, eligible: bool, requiredAction: text, tenantId: nullable(guid), siteId: nullable(str(2000)),
  driveId: nullable(str(2000)), rootFolderId: nullable(str(2000)), clientTemplateId: nullable(guid), engagementTemplateId: nullable(guid) });
export const workspaceResult = obj({ workspaceOrBindingId: guid, state: text, message: text, diagnosticCode: nullable(text) });
export const clientSites = arr(obj({ clientId: guid, clientName: text, state: text, membershipState: text, url: nullable(str(2000)),
  verifiedMembers: nat, verifiedAt: nullable(instant), membershipVerifiedAt: nullable(instant), operationState: nullable(text), requiredAction: text }), 100);

/** Only a verified site may be opened; never render signed URLs or arbitrary URI schemes as links. */
export function siteLink(state: string, value: string | null): string | null {
  if(state !== 'READY' || !value) return null;
  try { const url = new URL(value); return url.protocol === 'https:' && url.hostname.endsWith('.sharepoint.com') &&
    !url.username && !url.password && !url.search && !url.hash && (!url.port || url.port === '443') ? url.href : null; }
  catch { return null; }
}
