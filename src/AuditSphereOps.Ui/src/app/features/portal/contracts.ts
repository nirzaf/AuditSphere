import { arr, bool, date, DecodeError, Decoder, guid, instant, nat, nullable, obj, sha256, str, text } from '../../core/decode';
export const revision: Decoder<string> = (v, p) => {
  if (typeof v !== 'string' || !/^\d{1,19}$/.test(v)) throw new DecodeError(`${p}: expected exact unsigned integer string`);
  return v;
};
export const firstSignIn = obj({ completed: bool, identityPath: text, canComplete: bool, message: text });
const portalWorkspaceShape = obj({ firstSignIn, pendingOnboarding: bool, engagementCount: nat, hasMoreRequests: bool,
  requests: arr(obj({ id: guid, area: text, objective: text, periodStart: date, periodEnd: date, dueDate: date, state: text, delegated: bool,
    clarificationReason: nullable(text), clarificationReasonTruncated: bool, canWrite: bool }), 50),
  packages: arr(obj({ id: guid, framework: text, periodStart: date, periodEnd: date, currency: str(3) }), 100),
  asOfDateUtc: nullable(date), hasMorePackages: bool, packagePage: nat, packagePageSize: nat });
type PortalWorkspace = ReturnType<typeof portalWorkspaceShape>;
export type PortalRequestItem = PortalWorkspace['requests'][number];
export const portalWorkspace: Decoder<PortalWorkspace> = (value, path) => {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) throw new DecodeError(`${path}: expected object`);
  const raw = value as Record<string, unknown>;
  const requests = Array.isArray(raw['requests']) ? raw['requests'].map((item) => {
    if (typeof item !== 'object' || item === null || Array.isArray(item)) return item;
    const request = item as Record<string, unknown>;
    return {
      ...request,
      clarificationReason: Object.prototype.hasOwnProperty.call(request, 'clarificationReason') ? request['clarificationReason'] : null,
      clarificationReasonTruncated: Object.prototype.hasOwnProperty.call(request, 'clarificationReasonTruncated') ? request['clarificationReasonTruncated'] : false,
      canWrite: Object.prototype.hasOwnProperty.call(request, 'canWrite') ? request['canWrite'] : false,
    };
  }) : raw['requests'];
  return portalWorkspaceShape({
    ...raw,
    requests,
    asOfDateUtc: Object.prototype.hasOwnProperty.call(raw, 'asOfDateUtc') ? raw['asOfDateUtc'] : null,
    hasMorePackages: Object.prototype.hasOwnProperty.call(raw, 'hasMorePackages') ? raw['hasMorePackages'] : false,
    packagePage: Object.prototype.hasOwnProperty.call(raw, 'packagePage') ? raw['packagePage'] : 0,
    packagePageSize: Object.prototype.hasOwnProperty.call(raw, 'packagePageSize') ? raw['packagePageSize'] : 10,
  }, path);
};
const portalUpload = obj({ id: guid, fileName: text, receivedByteCount: revision, declaredByteCount: revision,
  state: text, transferState: nullable(text), sha256, createdAt: instant });
const portalConversation = obj({ id: guid, at: instant, speaker: text, body: text });
const portalRequestShape = obj({ id: guid, area: text, objective: text, instructions: text, requestedFormat: text, dueDate: date, state: text,
  revision, firstSignIn, canWrite: bool, canManageDelegations: bool, canDelegate: bool,
  delegations: arr(obj({ id: guid, delegateUserId: guid, delegateName: text, createdAt: instant }), 500),
  candidates: arr(obj({ userId: guid, name: text }), 500), uploads: arr(portalUpload, 50), conversation: arr(portalConversation, 50),
  clarificationReason: nullable(text), hasMoreUploads: bool, hasMoreConversation: bool });
export const portalRequest: Decoder<ReturnType<typeof portalRequestShape>> = (value, path) => {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) throw new DecodeError(`${path}: expected object`);
  const raw = value as Record<string, unknown>;
  return portalRequestShape({ ...raw,
    hasMoreUploads: Object.prototype.hasOwnProperty.call(raw, 'hasMoreUploads') ? raw['hasMoreUploads'] : false,
    hasMoreConversation: Object.prototype.hasOwnProperty.call(raw, 'hasMoreConversation') ? raw['hasMoreConversation'] : false,
  }, path);
};
export const portalRequestHistory = obj({ requestId: guid, uploadPage: nat, conversationPage: nat, pageSize: nat,
  uploads: arr(portalUpload, 50), hasMoreUploads: bool, conversation: arr(portalConversation, 50), hasMoreConversation: bool });

export function clientRequestStatusLabel(state: string): string {
  switch (state) {
    case 'SENT':
    case 'ACKNOWLEDGED':
    case 'PARTIALLY_RECEIVED':
      return 'Pending Upload';
    case 'RECEIVED':
    case 'UNDER_REVIEW':
    case 'RESUBMITTED':
      return 'Under Review';
    case 'ACCEPTED':
    case 'CLOSED':
      return 'Approved';
    case 'CLARIFICATION_REQUIRED':
      return 'Rejected / Re-upload Required';
    default:
      return state.replace(/_/g, ' ').toLowerCase();
  }
}
