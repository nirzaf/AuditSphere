import { arr, bool, guid, instant, nullable, obj, oneOf, sha256, text } from '../../core/decode';
export const accessLine = obj({ grantId: guid, role: text, scopeKind: oneOf('FIRM_WIDE', 'CLIENT', 'ENGAGEMENT', 'GROUP'), clientId: nullable(guid), engagementId: nullable(guid),
  groupId: nullable(guid), grantedAt: instant, expiresAt: nullable(instant), groupGrant: bool });
export const accessUser = obj({ userId: guid, displayName: text, microsoftIdentity: text, tenantId: text, objectId: text, accountType: text,
  directoryStatus: text, auditSphereStatus: text, access: arr(accessLine, 500), invitationId: nullable(guid), invitationStatus: text, lastMicrosoftVerification: nullable(instant),
  lastAuditSphereAccess: nullable(instant), createdBy: text, createdDate: instant });
const scope = obj({ id: guid, name: text, parentId: nullable(guid) });
export const accessGrantEvidence = obj({ grantId: guid, at: instant, action: text, priorRole: text, newRole: text, reason: nullable(text), actor: text, source: text });
export const accessWorkspace = obj({ users: arr(accessUser, 2000), disabledOrRevoked: arr(accessUser, 2000),
  operations: arr(obj({ id: guid, kind: text, state: text, target: text, reason: text, resultObjectId: nullable(text), correlationId: nullable(text), resultCode: nullable(text),
    reconciliation: nullable(text), requestedBy: text, createdAt: instant, updatedAt: instant }), 200),
  groups: arr(obj({ id: guid, displayName: text, groupObjectId: text, purpose: text, approvedAt: instant, approvedBy: text }), 2000),
  history: arr(obj({ at: instant, operation: text, target: text, change: text, reason: text, actor: text, result: text, microsoftOperation: nullable(text) }), 600),
  grantHistory: arr(accessGrantEvidence, 4000),
  clients: arr(scope, 2000), engagements: arr(scope, 5000), clientGroups: arr(scope, 2000) });
export const copyableInvitation = obj({ id: guid, recipientEmail: text, destinationPath: text, deliveryState: text, link: text });
export const invitationAssignment = obj({ userId: guid, roleGrantId: guid, invitationId: guid, destinationPath: text, deliveryState: text });
export const roleReview = obj({ review: obj({ userId: guid, userKind: text, currentAccess: arr(accessLine, 500), proposed: accessLine,
  addedCapabilities: arr(text, 100), removedCapabilities: arr(text, 100), scopeExpansion: bool, scopeReduction: bool,
  independenceImpact: arr(text, 100), warnings: arr(text, 100), blockingReasons: arr(text, 100) }), digest: sha256 });
