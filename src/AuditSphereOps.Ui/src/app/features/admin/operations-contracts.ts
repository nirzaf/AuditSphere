import { arr, bool, guid, instant, nullable, obj, str, text } from '../../core/decode';
import { capabilityStatus } from './tenant-contracts';

export const externalOperationResult = obj({ operationId: guid, state: text, message: text,
  boundUserId: nullable(guid), roleGrantId: nullable(guid), temporaryPassword: nullable(str(128)), correlationId: nullable(text) });
export const groupMembers = obj({ members: arr(obj({ objectId: guid, displayName: text, userPrincipalName: text }), 50), nextPageToken: nullable(str(2048)) });
export const groupMembershipPreview = obj({ managedGroupId: guid, groupName: text, groupObjectId: guid,
  userId: guid, userName: text, userObjectId: guid, tenantId: guid, existingMembership: bool, observedAt: instant });
export const groupChangeResult = obj({ operationId: nullable(guid), state: text, existingMembership: bool, message: text, correlationId: nullable(text) });
export const operationReview = obj({ operationId: guid, kind: text, state: text, tenantId: guid, target: text, objectId: nullable(guid),
  role: nullable(text), scopeKind: nullable(text), clientId: nullable(guid), engagementId: nullable(guid), managedGroupId: nullable(guid),
  reason: text, requestedByUserId: guid, correlationId: nullable(text), reconciliation: nullable(text) });
/** Presentation eligibility only. The Application service repeats the exact capability and actor check before dispatch. */
export function capabilityAvailable(capabilities: ReturnType<typeof capabilityStatus>[], capability: string): boolean {
  return capabilities.some(c => c.capability === capability && c.enabled && c.state === 'VERIFIED' && !c.stale);
}
export function recoverableOperation(state: string, kind: string): boolean {
  return state === 'UNKNOWN' || state === 'DISPATCHING' ||
    (['ACCEPTED', 'RECONCILED'].includes(state) && ['CREATE_TENANT_USER', 'INVITE_GUEST'].includes(kind));
}
