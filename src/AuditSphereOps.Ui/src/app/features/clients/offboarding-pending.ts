import { guidPattern } from '../../core/contracts';

export interface PendingOffboarding {
  clientId: string; requestKey: string; readinessDigest: string; archiveRevision: string;
  isArchived: boolean; overallState: string; salt: string; notesHash: string;
}

export function pendingOffboarding(raw: unknown, clientId: string): PendingOffboarding | null {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return null;
  const p = raw as Record<string, unknown>;
  return p['clientId'] === clientId && typeof p['requestKey'] === 'string' && guidPattern.test(p['requestKey']) &&
    typeof p['readinessDigest'] === 'string' && /^[a-f0-9]{64}$/.test(p['readinessDigest']) &&
    typeof p['archiveRevision'] === 'string' && /^(0|[1-9][0-9]*)$/.test(p['archiveRevision']) &&
    typeof p['isArchived'] === 'boolean' && typeof p['overallState'] === 'string' &&
    ['ACTION_REQUIRED', 'UNKNOWN', 'NO_FINDINGS'].includes(p['overallState']) &&
    typeof p['salt'] === 'string' && /^[a-f0-9]{32}$/.test(p['salt']) &&
    typeof p['notesHash'] === 'string' && /^[a-f0-9]{64}$/.test(p['notesHash']) ? p as unknown as PendingOffboarding : null;
}

export async function offboardingNotesHash(salt: string, notes: string): Promise<string> {
  const hash = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(salt + ':' + notes));
  return Array.from(new Uint8Array(hash), b => b.toString(16).padStart(2, '0')).join('');
}
