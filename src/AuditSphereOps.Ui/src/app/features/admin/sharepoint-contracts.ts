import { arr, bool, guid, instant, nullable, obj, oneOf, sha256, str, text, token } from '../../core/decode';

export const templatePurpose = oneOf('CLIENT_WORKSPACE', 'ENGAGEMENT_WORKSPACE');
export const folderTemplate = obj({ id: guid, purpose: templatePurpose, version: token, manifestJson: str(20000),
  manifestDigest: sha256, createdAt: instant, approvedAt: nullable(instant) });
export const selectedResources = obj({ probeConfigured: bool, workspace: obj({
  draft: nullable(obj({ id: guid, revision: token, state: text, tenantId: nullable(guid), siteUrl: nullable(str(2000)),
    siteId: nullable(str(2000)), driveId: nullable(str(2000)), rootFolderId: nullable(str(2000)),
    accessProfile: oneOf('APP_MEDIATED', 'DIRECT_STAFF_COLLABORATION'), connectionRevisionId: nullable(guid),
    connectionState: nullable(text), consentState: nullable(text) })),
  templates: arr(folderTemplate, 100), clientDefaultManifest: str(20000), engagementDefaultManifest: str(20000),
  clientSteManifest: str(20000), engagementSteManifest: str(20000),
  verification: nullable(obj({ state: text, diagnosticCode: text, observedAt: instant })) }) });

export function editableResource(state: string): boolean { return !['ACTIVE', 'SUSPENDED', 'BLOCKED'].includes(state); }
export function currentVerification(state: string, at: string, now = Date.now()): boolean {
  const observed = Date.parse(at); return state === 'VERIFIED' && Number.isFinite(observed) && observed <= now && observed >= now - 86400000;
}
