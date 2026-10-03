import { arr, bool, date, DecodeError, Decoder, guid, instant, nat, nullable, obj, sha256, str, text } from '../../core/decode';
export const revision: Decoder<string> = (v, p) => {
  if (typeof v !== 'string' || !/^\d{1,19}$/.test(v)) throw new DecodeError(`${p}: expected exact unsigned integer string`);
  return v;
};
export const firstSignIn = obj({ completed: bool, identityPath: text, canComplete: bool, message: text });
export const portalWorkspace = obj({ firstSignIn, pendingOnboarding: bool, engagementCount: nat, hasMoreRequests: bool,
  requests: arr(obj({ id: guid, area: text, objective: text, periodStart: date, periodEnd: date, dueDate: date, state: text, delegated: bool }), 50),
  packages: arr(obj({ id: guid, framework: text, periodStart: date, periodEnd: date, currency: str(3) }), 100) });
export const portalRequest = obj({ id: guid, area: text, objective: text, instructions: text, requestedFormat: text, dueDate: date, state: text,
  revision, firstSignIn, canWrite: bool, canDelegate: bool,
  delegations: arr(obj({ id: guid, delegateUserId: guid, delegateName: text, createdAt: instant }), 500),
  candidates: arr(obj({ userId: guid, name: text }), 500),
  uploads: arr(obj({ id: guid, fileName: text, receivedByteCount: revision, declaredByteCount: revision, state: text, transferState: nullable(text), sha256, createdAt: instant }), 500),
  conversation: arr(obj({ id: guid, at: instant, speaker: text, body: text }), 2000) });
